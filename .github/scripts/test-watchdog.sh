#!/usr/bin/env bash
# Run the CI test command under a watchdog that ends it itself, rather than trusting the runner to.
#
#   test-watchdog.sh <deadline-seconds> <out-dir> -- <command...>
#
# Why: every macOS hang since 2026-11-01 ran past the Test step's own timeout-minutes AND ignored the
# job's cancel (timeout or superseded run) for the full 5-minute grace, so GitHub force-completed the job
# with no log at all. actions/runner ends a timed-out step by signalling its shell and completes it ~5s
# later even if grandchildren survive, so a merely hung test would have been stopped; a runner that does
# not do even that has stopped responding. Whatever the cause, the step has to end on its own and leave
# its evidence on disk while it still can, so the upload step that follows has something to send.
#
# What it leaves in <out-dir>:
#   test-output.log   the command's output, also streamed to the job log
#   watch.log         a resource sample every 10s (load, memory, disk, process count, top processes)
#   stopped-ps.txt    the process table when it had to stop the run
#   sample-*.txt      macOS `sample` stacks of each test process still alive at that point
#
# Exit status: the command's own, or 124 when the watchdog stopped it.
#
# bash 3.2-compatible on purpose (macOS /bin/bash).
set -uo pipefail

deadline=$1; out=$2; shift 2
[ "${1:-}" = "--" ] && shift
mkdir -p "$out"

os=$(uname -s)
interval=10
min_disk_mb=1024     # stop before the disk is full, while there is still room to write the evidence
min_mem_pct=8        # macOS: kern.memorystatus_level, the percentage of memory still free

disk_free_mb() { df -Pm . 2>/dev/null | awk 'NR==2 {print $4}'; }
mem_free_pct() {
  case "$os" in
    Darwin) sysctl -n kern.memorystatus_level 2>/dev/null ;;
    Linux)  awk '/MemTotal/ {t=$2} /MemAvailable/ {a=$2} END {if (t) printf "%d", a*100/t}' /proc/meminfo ;;
    *)      echo "" ;;
  esac
}
nprocs() { ps -A -o pid= 2>/dev/null | wc -l | tr -d ' '; }
load() { uptime 2>/dev/null | sed 's/.*load averages*: *//'; }

sample_line() {
  echo "$(date -u +%H:%M:%S) +${1}s load=[$(load)] mem_free=$(mem_free_pct)% disk_free=$(disk_free_mb)MB procs=$(nprocs)"
}

sample_full() {
  sample_line "$1"
  ps -A -o pid,ppid,stat,etime,rss,%cpu,command 2>/dev/null | sort -k5 -n -r | head -8 | cut -c1-200 | sed 's/^/    /'
}

# Test processes by executable name (the MTP apphost, Avalloy.<Name>.Tests), inside the group or out of it.
# The executable, not the command line: a shell whose arguments merely mention a test project is not one.
test_pids() { ps -A -o pid=,comm= 2>/dev/null | awk '{ n = $2; sub(/.*\//, "", n) } n ~ /^Avalloy\.[A-Za-z]+\.Tests(\.exe)?$/ { print $1 }'; }

stop_run() {
  local why=$1
  echo "::error title=Test run stopped by the watchdog::${why}. The tests still running are listed above (hang dump), and the process table, stacks and resource samples are in the test-output artifact."
  echo "== watchdog: ${why}; stopping the test run =="
  ps -A -o pid,ppid,pgid,stat,etime,rss,%cpu,command > "$out/stopped-ps.txt" 2>&1
  pids=" $(test_pids | tr '\n' ' ') "
  awk -v g="$group" -v p="$pids" 'NR == 1 || $3 == g || index(p, " " $1 " ")' "$out/stopped-ps.txt" | cut -c1-240
  if [ "$os" = Darwin ]; then
    samplers=""
    for pid in $(test_pids); do
      /usr/bin/sample "$pid" 2 -file "$out/sample-$pid.txt" > /dev/null 2>&1 &
      samplers="$samplers $!"
    done
    [ -n "$samplers" ] && wait $samplers 2>/dev/null
  fi
  case "$os" in
    MINGW*|MSYS*|CYGWIN*)
      taskkill //F //T //FI "IMAGENAME eq Avalloy.*" > /dev/null 2>&1
      kill -KILL "$group" 2>/dev/null ;;
    *)
      kill -TERM -- "-$group" 2>/dev/null
      sleep 5
      kill -KILL -- "-$group" 2>/dev/null
      # A test process that left the group (setsid, a relaunch) would hold the step's output open.
      for pid in $(test_pids); do kill -KILL "$pid" 2>/dev/null; done ;;
  esac
}

# Job control gives the command its own process group, so one signal reaches every process it started.
set -m
"$@" 2>&1 | tee "$out/test-output.log" &
tee_pid=$!
set +m
group=$(ps -o pgid= -p "$tee_pid" 2>/dev/null | tr -d ' ')
[ -n "$group" ] || group=$tee_pid

start=$SECONDS
next=0
stopped=""
: > "$out/watch.log"
# tee outlives the command while anything still holds its output open, which is itself worth stopping.
while kill -0 "$tee_pid" 2>/dev/null; do
  elapsed=$((SECONDS - start))
  if [ "$elapsed" -ge "$next" ]; then
    sample_full "$elapsed" >> "$out/watch.log"
    # A short line in the job log every 30s, so even a live view of a stuck run says how far it got.
    [ $((next % 30)) -eq 0 ] && [ "$elapsed" -gt 0 ] && echo "== watchdog $(sample_line "$elapsed")"
    next=$((next + interval))
  fi
  disk=$(disk_free_mb); mem=$(mem_free_pct)
  if [ "$elapsed" -ge "$deadline" ]; then stopped="still running after ${deadline}s"
  elif [ -n "$disk" ] && [ "$disk" -lt "$min_disk_mb" ]; then stopped="disk nearly full (${disk}MB free)"
  elif [ -n "$mem" ] && [ "$mem" -lt "$min_mem_pct" ]; then stopped="memory nearly exhausted (${mem}% free)"
  fi
  if [ -n "$stopped" ]; then
    sample_full "$elapsed" >> "$out/watch.log"
    stop_run "$stopped"
    break
  fi
  sleep 1
done

wait %1 2>/dev/null
status=$?
echo "== watchdog: finished after $((SECONDS - start))s; peak samples in watch.log"

# A module that hit its hang dump lists the tests still running in a *_hang.log beside the dump (under
# <out-dir>, where CI points --results-directory); make them an annotation too, which GitHub keeps even
# when it keeps nothing else of the job.
running=$(find "$out" -name '*_hang.log' -exec cat {} + 2>/dev/null | grep -E '^\[[0-9:.]+\] ' | sort -u | head -20 | tr '\n' ' ')
[ -n "$running" ] && echo "::error title=Hang dump::Tests still running when a module's hang dump fired: ${running}"

[ -n "$stopped" ] && exit 124
exit "$status"
