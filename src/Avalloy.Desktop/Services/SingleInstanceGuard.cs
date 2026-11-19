using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Avalloy.Services;

/// <summary>
/// Cross-platform single-instance guard. The first process to <see cref="Acquire"/>
/// a given id becomes the <em>primary</em> and holds an exclusive lock for its
/// lifetime; any later process fails to acquire, signals the primary to come to the
/// foreground, and exits. Ownership is an exclusive lock file (released by the OS on
/// process exit, so a crash never leaves a stale lock); the "come forward" signal
/// travels over a named pipe the primary listens on.
/// <para>
/// Both the lock and the pipe are namespaced per user, so two users on the same
/// machine each get their own instance. Backs a close-to-background UX:
/// relaunching the app (or its launcher) re-shows the hidden window instead of
/// starting a duplicate that would double-run background workers.
/// </para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _pipeName;
    private FileStream? _lock;
    private CancellationTokenSource? _cts;
    private Action? _onActivate;

    private SingleInstanceGuard(string pipeName, FileStream lockFile)
    {
        _pipeName = pipeName;
        _lock = lockFile;
    }

    /// <summary>Try to become the primary instance. Returns the guard when this
    /// process is primary (caller continues launching and should later call
    /// <see cref="StartListening"/>); returns <c>null</c> when another instance
    /// already owns the id — in that case the running instance has been signalled
    /// to activate and this process should exit immediately.</summary>
    public static SingleInstanceGuard? Acquire(string appId)
    {
        var key = $"{Sanitize(appId)}.{Sanitize(Environment.UserName)}";
        var lockPath = Path.Combine(Path.GetTempPath(), key + ".instance.lock");
        var pipeName = key + ".activate";

        FileStream lockFile;
        try
        {
            // FileShare.None → only one process holds this at a time. The handle
            // (and its lock) is released by the OS when the owner exits or crashes,
            // so the next launch can acquire cleanly with no stale-lock cleanup.
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Someone else holds the lock → we're a secondary. Wake the primary.
            TrySignal(pipeName);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            TrySignal(pipeName);
            return null;
        }

        return new SingleInstanceGuard(pipeName, lockFile);
    }

    /// <summary>
    /// Whether the id is currently free, WITHOUT becoming the primary and without signalling anyone. Purely a
    /// probe: it takes the lock for an instant and releases it.
    ///
    /// <para>Exists for relaunch-after-quit flows. A replacement process started while the outgoing one still
    /// holds the lock loses the race, signals a primary that is mid-shutdown (so no window comes forward), and
    /// exits — the app simply never reappears. A waiter can poll this to launch only once the coast is clear.
    /// <see cref="Acquire"/> is unsuitable for polling: it signals the primary on every failure.</para>
    ///
    /// <para>Inherently a moment-in-time answer — the caller must still handle losing a subsequent race.</para>
    /// </summary>
    public static bool IsAvailable(string appId)
    {
        var key = $"{Sanitize(appId)}.{Sanitize(Environment.UserName)}";
        var lockPath = Path.Combine(Path.GetTempPath(), key + ".instance.lock");

        try
        {
            using var probe = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;           // held by a live instance
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Begin listening for activation signals from later launches. Idempotent.
    /// <paramref name="onActivate"/> is raised on a background thread — marshal to the
    /// UI thread yourself.</summary>
    public void StartListening(Action onActivate)
    {
        if (_cts is not null) return; // already listening
        _onActivate = onActivate;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                // Drain the one-byte ping; the connection itself is the signal.
                var buf = new byte[1];
                _ = await server.ReadAsync(buf, ct).ConfigureAwait(false);
                _onActivate?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Transient pipe error — pause briefly so we don't spin hot.
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    private static void TrySignal(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(2000);
            client.WriteByte(1);
            client.Flush();
        }
        catch { /* best-effort: the primary may still be starting its listener */ }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _lock?.Dispose(); } catch { }
        _lock = null;
    }
}
