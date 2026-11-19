using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Avalloy.Services;

/// <summary>
/// Minimal rolling-file <see cref="ILoggerProvider"/>. Exists because a packaged
/// desktop app launched from Finder/Explorer has no console: the console + debug
/// providers write into the void, so a crash leaves the user holding an OS crash
/// report with an unsymbolicated JIT stack and no exception text -- a real SIGABRT
/// on macOS was diagnosed that way. With this provider the same crash leaves a readable log.
/// </summary>
/// <remarks>
/// <para>
/// Writes are <b>synchronous and flushed per entry</b>, on purpose. A background
/// queue would drop exactly the entry that matters — the last one before
/// <c>abort()</c>. Volume is low (Information and above), so the cost is noise.
/// </para>
/// <para>
/// Every write is wrapped in a catch-all: a logger that throws while the app is
/// already failing turns a diagnosable crash into a mystery. IO errors mean we
/// lose the line, nothing more.
/// </para>
/// <para>
/// The file is opened <b>append-only at the OS level</b> and each entry goes out as one
/// write, so several writers on one file (a second copy of the app on the same data, two
/// providers in one process) each land every entry whole. <c>FileMode.Append</c> is not
/// that: it seeks to the end once at open, and each writer then wrote at its own offset,
/// over the other's newer bytes.
/// </para>
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _keepFiles;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    private AppendOnlyFile? _file;
    private bool _disposed;

    /// <summary>The file currently being written, for "reveal log" affordances.</summary>
    public string FilePath => _path;

    /// <param name="path">Full path of the active log file. Its directory is created if missing.</param>
    /// <param name="maxBytes">Roll over once the active file exceeds this size.</param>
    /// <param name="keepFiles">How many rolled files to retain alongside the active one.</param>
    public FileLoggerProvider(string path, long maxBytes = 8L * 1024 * 1024, int keepFiles = 5)
    {
        _path = path;
        _maxBytes = maxBytes;
        _keepFiles = keepFiles;
        Open();
        WriteBanner();
    }

    /// <summary>Conventional log location for <paramref name="appName"/>: <c>~/Library/Logs/{app}</c>
    /// on macOS (where users and Console.app already look), <c>{LocalApplicationData}/{app}/logs</c>
    /// elsewhere. The same as <see cref="AppPaths.LogsDir"/>.</summary>
    public static string DefaultDirectory(string appName) => AppPaths.ForApp(appName).LogsDir;

    /// <summary>Provider writing to <c>{DefaultDirectory(appName)}/{appName}.log</c>. Returns
    /// null if the log file can't be opened (read-only volume, permissions) — the caller just
    /// skips the provider rather than failing startup over logging.</summary>
    public static FileLoggerProvider? TryCreate(string appName) => TryCreate(AppPaths.ForApp(appName));

    /// <summary>Provider writing to <c>{paths.LogsDir}/{paths.AppName}.log</c>; null if it can't be
    /// opened, as <see cref="TryCreate(string)"/>.</summary>
    public static FileLoggerProvider? TryCreate(AppPaths paths)
    {
        try
        {
            return new FileLoggerProvider(Path.Combine(paths.LogsDir, paths.AppName + ".log"));
        }
        catch
        {
            return null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Called by <see cref="LoggerFactory"/> so scopes flow across every
    /// provider that opts in via <see cref="ISupportExternalScope"/>.</summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _file?.Dispose();
            _file = null;
        }
    }

    private void Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _file = AppendOnlyFile.Open(_path);
    }

    private void WriteBanner()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly()?.GetName();
        Write(
            $"=== launch pid {Environment.ProcessId} · {asm?.Name} {asm?.Version} · " +
            $"{RuntimeInformation.OSDescription} · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} ===");
    }

    /// <summary>Roll when the active file outgrows the cap: {app}.log → {app}.1.log → … and the
    /// oldest falls off the end.</summary>
    private void RollIfNeeded()
    {
        if (_file?.Length < _maxBytes)
            return;

        _file?.Dispose();
        _file = null;

        var dir = Path.GetDirectoryName(_path)!;
        var name = Path.GetFileNameWithoutExtension(_path);
        var ext = Path.GetExtension(_path);
        for (var i = _keepFiles; i >= 1; i--)
        {
            var older = Path.Combine(dir, $"{name}.{i}{ext}");
            var newer = i == 1 ? _path : Path.Combine(dir, $"{name}.{i - 1}{ext}");
            if (!File.Exists(newer))
                continue;
            File.Delete(older);
            File.Move(newer, older);
        }

        Open();
    }

    internal void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            try
            {
                RollIfNeeded();
                // One buffer per entry, so another writer can only land before or after it.
                _file?.Append(Utf8.GetBytes(line + Environment.NewLine));
            }
            catch
            {
                // Losing a log line must never be the reason the app dies.
            }
        }
    }

    /// <summary>
    /// A file every write of which lands at its CURRENT end, whoever else is writing it.
    /// Unix: <c>O_APPEND</c> on the descriptor, and a plain <c>write(2)</c> — .NET's own
    /// writes are <c>pwrite</c> at an offset it tracks, which is the bug being fixed.
    /// Windows: a handle with <c>FILE_APPEND_DATA</c> and no <c>FILE_WRITE_DATA</c>, for which
    /// the OS ignores the write offset.
    /// </summary>
    private sealed class AppendOnlyFile : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly FileStream? _windowsStream;

        private AppendOnlyFile(SafeFileHandle handle, FileStream? windowsStream)
        {
            _handle = handle;
            _windowsStream = windowsStream;
        }

        public long Length => RandomAccess.GetLength(_handle);

        public static AppendOnlyFile Open(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                var stream = new FileInfo(path).Create(
                    FileMode.OpenOrCreate,
                    FileSystemRights.AppendData | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1, // unbuffered; unlike FileStream's constructor, Create rejects 0
                    FileOptions.None,
                    fileSecurity: null);
                return new AppendOnlyFile(stream.SafeFileHandle, stream);
            }

            // .NET creates it; open(2) then never sees O_CREAT, so its variadic mode argument
            // (passed on the stack on Apple arm64, where a P/Invoke can't put it) is never read.
            File.OpenHandle(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite).Dispose();
            var fd = Unix.open(path, Unix.O_WRONLY | Unix.O_APPEND | Unix.O_CLOEXEC);
            if (fd < 0)
                throw new IOException($"open({path}) failed: errno {Marshal.GetLastPInvokeError()}");
            return new AppendOnlyFile(new SafeFileHandle(fd, ownsHandle: true), null);
        }

        public void Append(byte[] bytes)
        {
            if (_windowsStream is not null)
                _windowsStream.Write(bytes);
            else
                Unix.WriteAll(_handle, bytes);
        }

        public void Dispose()
        {
            if (_windowsStream is not null)
                _windowsStream.Dispose();
            else
                _handle.Dispose();
        }
    }

    private static unsafe class Unix
    {
        public const int O_WRONLY = 0x1;
        private const int EINTR = 4;

        // Linux spells these differently from macOS and the BSDs.
        public static readonly int O_APPEND = OperatingSystem.IsLinux() ? 0x400 : 0x8;
        public static readonly int O_CLOEXEC = OperatingSystem.IsLinux() ? 0x80000 : 0x1000000;

        [DllImport("libc", SetLastError = true)]
        public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern nint write(int fd, byte* buf, nint count);

        public static void WriteAll(SafeFileHandle handle, byte[] bytes)
        {
            var added = false;
            handle.DangerousAddRef(ref added);
            try
            {
                var fd = (int)handle.DangerousGetHandle();
                fixed (byte* p = bytes)
                {
                    var done = 0;
                    while (done < bytes.Length)
                    {
                        var n = write(fd, p + done, bytes.Length - done);
                        if (n < 0)
                        {
                            var errno = Marshal.GetLastPInvokeError();
                            if (errno == EINTR)
                                continue;
                            throw new IOException($"write failed: errno {errno}");
                        }

                        done += (int)n;
                    }
                }
            }
            finally
            {
                if (added)
                    handle.DangerousRelease();
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var sb = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(' ')
                .Append(Abbreviate(logLevel))
                .Append(" [")
                .Append(category)
                .Append("] ");

            if (LogScopeFormatter.Flatten(provider._scopes) is { } scopes)
                sb.Append(scopes).Append(" · ");

            sb.Append(formatter(state, exception));

            // Exceptions go on their own lines, indented, so a stack stays readable and
            // grep for the timestamp still finds the entry head.
            if (exception is not null)
                sb.Append(Environment.NewLine).Append(Indent(exception.ToString()));

            provider.Write(sb.ToString());
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => "NONE",
        };

        private static string Indent(string text) =>
            "    " + text.Replace(Environment.NewLine, Environment.NewLine + "    ");
    }
}
