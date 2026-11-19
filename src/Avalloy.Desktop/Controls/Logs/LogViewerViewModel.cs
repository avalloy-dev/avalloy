using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalloy.Services;
using Microsoft.Extensions.Logging;

namespace Avalloy.Controls.Logs;

/// <summary>One display row in the <see cref="LogViewer"/>. Display strings and
/// per-level flags are precomputed once so the item template binds without
/// converters, and the level flags drive style classes.</summary>
public sealed class LogEntryRow(LogEntry entry)
{
    public LogEntry Entry { get; } = entry;

    public string Time { get; } = entry.Timestamp.ToString("HH:mm:ss.fff");
    public string LevelBadge { get; } = entry.Level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "···",
    };
    public string Category => Entry.Category;
    public string Message => Entry.Message;
    public string? Scopes => Entry.Scopes;
    public bool HasScopes => Entry.Scopes is not null;
    public string? ExceptionText => Entry.ExceptionText;
    public bool HasException => Entry.ExceptionText is not null;

    public bool IsTrace { get; } = entry.Level == LogLevel.Trace;
    public bool IsDebug { get; } = entry.Level == LogLevel.Debug;
    public bool IsInfo { get; } = entry.Level == LogLevel.Information;
    public bool IsWarn { get; } = entry.Level == LogLevel.Warning;
    public bool IsError { get; } = entry.Level == LogLevel.Error;
    public bool IsCritical { get; } = entry.Level == LogLevel.Critical;

    /// <summary>Single line in the rolling-file format, for copy-to-clipboard.</summary>
    public string ToClipboardLine()
    {
        var sb = new StringBuilder()
            .Append(Entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(' ').Append(LevelBadge)
            .Append(" [").Append(Entry.Category).Append("] ");
        if (Entry.Scopes is { } scopes) sb.Append(scopes).Append(" · ");
        sb.Append(Entry.Message);
        if (Entry.ExceptionText is { } ex)
            sb.Append(Environment.NewLine).Append("    ")
              .Append(ex.Replace(Environment.NewLine, Environment.NewLine + "    "));
        return sb.ToString();
    }
}

/// <summary>One of the five minimum-level filter chips.</summary>
public sealed partial class LevelChip(string label, LogLevel level) : ObservableObject
{
    public string Label { get; } = label;
    public LogLevel Level { get; } = level;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// Filtering/tail logic behind <see cref="LogViewer"/>. Avalonia-free (the
/// <see cref="LogBuffer"/>'s marshalling seam keeps it headless-testable): it
/// mirrors the buffer from a snapshot plus <see cref="LogBuffer.Flushed"/>
/// batches, deduped by <see cref="LogEntry.Sequence"/>, and projects the
/// filtered view into <see cref="Visible"/>.
/// </summary>
public sealed partial class LogViewerViewModel : ObservableObject, IDisposable
{
    /// <summary>Sentinel first entry of <see cref="Categories"/>.</summary>
    public const string AllCategories = "All categories";

    private readonly LogBuffer _buffer;
    private readonly List<LogEntryRow> _rows = new();
    private long _lastSequence;
    private long _rolledBaseline;
    private bool _disposed;

    public LogViewerViewModel() : this(AlloyLogging.Buffer)
    {
    }

    public LogViewerViewModel(LogBuffer buffer)
    {
        _buffer = buffer;
        LevelChips =
        [
            new LevelChip("Trace", LogLevel.Trace),
            new LevelChip("Debug", LogLevel.Debug),
            new LevelChip("Info", LogLevel.Information),
            new LevelChip("Warn", LogLevel.Warning),
            new LevelChip("Error", LogLevel.Error),
        ];
        foreach (var entry in _buffer.Snapshot())
            Mirror(entry);
        _buffer.Flushed += OnFlushed;
        SyncChips();
        Refilter();
    }

    /// <summary>The filtered rows the list binds to, ordered per <see cref="NewestFirst"/>.</summary>
    public ObservableCollection<LogEntryRow> Visible { get; } = new();

    /// <summary>Distinct categories observed, headed by <see cref="AllCategories"/>.</summary>
    public ObservableCollection<string> Categories { get; } = new() { AllCategories };

    /// <summary>Minimum-level floor chips; clicking one via <see cref="SetMinLevelCommand"/>
    /// marks it and everything above it active.</summary>
    public IReadOnlyList<LevelChip> LevelChips { get; }

    public string? LogFilePath => AlloyLogging.LogFilePath;
    public bool HasLogFile => AlloyLogging.LogFilePath is not null;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedCategory = AllCategories;

    [ObservableProperty]
    private LogLevel _minLevel = LogLevel.Trace;

    [ObservableProperty]
    private bool _paused;

    [ObservableProperty]
    private bool _newestFirst = true;

    [ObservableProperty]
    private string _status = "";

    partial void OnSearchTextChanged(string value) => Refilter();

    partial void OnSelectedCategoryChanged(string value) => Refilter();

    partial void OnMinLevelChanged(LogLevel value)
    {
        SyncChips();
        Refilter();
    }

    partial void OnPausedChanged(bool value)
    {
        if (!value) Refilter(); // catch up on what accumulated while paused
        else UpdateStatus();
    }

    partial void OnNewestFirstChanged(bool value) => Refilter();

    [RelayCommand]
    private void SetMinLevel(LevelChip chip) =>
        MinLevel = MinLevel == chip.Level ? LogLevel.Trace : chip.Level; // re-click lowers the floor back out

    [RelayCommand]
    private void Clear()
    {
        _buffer.Clear();
        _rows.Clear();
        Visible.Clear();
        _rolledBaseline = _buffer.TotalAdded; // cleared entries aren't "rolled off"
        UpdateStatus();
    }

    [RelayCommand]
    private void RevealLogFile()
    {
        if (AlloyLogging.LogFilePath is { } path) FileReveal.Reveal(path);
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        if (AlloyLogging.LogFilePath is { } path && Path.GetDirectoryName(path) is { } dir)
            FileReveal.OpenFolder(dir);
    }

    /// <summary>Emit one entry per level + an exception — the "is everything
    /// wired?" self-test, mirrored from the demo's LogProbe command.</summary>
    [RelayCommand]
    private void Probe()
    {
        var log = AlloyLogging.CreateLogger("Avalloy.LogProbe");
        log.LogTrace("Log probe: Trace");
        log.LogDebug("Log probe: Debug");
        log.LogInformation("Log probe: Information at {Time:O}", DateTimeOffset.Now);
        log.LogWarning("Log probe: Warning with structured arg {Count}", 42);
        try
        {
            throw new InvalidOperationException("Log probe synthetic exception");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Log probe: Error with exception payload");
        }
    }

    /// <summary>Clipboard text for the given rows (or the whole filtered view when
    /// none are selected). The clipboard call itself lives in the control's
    /// code-behind — it needs a visual for <c>TopLevel.GetTopLevel</c>.</summary>
    public string CopyText(IReadOnlyList<LogEntryRow>? selected = null)
    {
        var rows = selected is { Count: > 0 } ? selected : Visible.ToList();
        return string.Join(Environment.NewLine, rows.Select(r => r.ToClipboardLine()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buffer.Flushed -= OnFlushed;
    }

    private void OnFlushed(IReadOnlyList<LogEntry> batch)
    {
        var refilterNeeded = false;
        foreach (var entry in batch)
        {
            if (entry.Sequence <= _lastSequence) continue; // snapshot already covered it
            var row = Mirror(entry);
            if (Paused || !Matches(row)) continue;
            if (NewestFirst) Visible.Insert(0, row);
            else Visible.Add(row);
            if (Visible.Count > _buffer.Capacity) refilterNeeded = true;
        }

        // Trimming the far end of Visible entry-by-entry is O(n²) in a flood;
        // just rebuild once the cap is crossed.
        if (refilterNeeded) Refilter();
        else UpdateStatus();
    }

    private LogEntryRow Mirror(LogEntry entry)
    {
        _lastSequence = entry.Sequence;
        var row = new LogEntryRow(entry);
        _rows.Add(row);
        if (_rows.Count > _buffer.Capacity)
            _rows.RemoveAt(0);
        if (!Categories.Contains(entry.Category))
            Categories.Add(entry.Category);
        return row;
    }

    private bool Matches(LogEntryRow row)
    {
        if (row.Entry.Level < MinLevel) return false;
        if (SelectedCategory != AllCategories && row.Category != SelectedCategory) return false;
        if (SearchText is not { Length: > 0 } search) return true;
        return row.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (row.ExceptionText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void Refilter()
    {
        Visible.Clear();
        IEnumerable<LogEntryRow> rows = _rows.Where(Matches);
        if (NewestFirst) rows = rows.Reverse();
        foreach (var row in rows)
            Visible.Add(row);
        UpdateStatus();
    }

    private void SyncChips()
    {
        foreach (var chip in LevelChips)
            chip.IsActive = chip.Level >= MinLevel;
    }

    private void UpdateStatus()
    {
        var status = $"{_rows.Count:N0} retained · {Visible.Count:N0} shown";
        var dropped = _buffer.TotalAdded - _buffer.Count - _rolledBaseline;
        if (dropped > 0) status += $" · {dropped:N0} rolled off";
        if (Paused) status += " · paused";
        Status = status;
    }
}
