namespace Avalloy;

/// <summary>
/// Where one app keeps its files. Computed in one place so that every package agrees, rather than each
/// recomputing the platform conventions (and drifting) next to the code that writes the file.
///
/// <para>The conventions are the ones Avalloy has always used, and changing any of them MOVES existing
/// users' files: preferences in the roaming app-data folder, data in the local one, logs where each
/// OS's users already look. Use <c>with</c> to relocate one folder rather than inventing another scheme.</para>
/// </summary>
/// <param name="AppName">The app's folder name, e.g. <c>"Contoso"</c>. Every default path ends in it.</param>
/// <param name="ConfigDir">Preferences and other user-edited settings, under .NET's
/// <see cref="Environment.SpecialFolder.ApplicationData"/>: <c>%APPDATA%\{app}</c> on Windows,
/// <c>~/Library/Application Support/{app}</c> on macOS (the same folder as <paramref name="DataDir"/>
/// there), <c>~/.config/{app}</c> on Linux.</param>
/// <param name="DataDir">App-managed data (icon packs, caches, file-backed secrets), under .NET's
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/>: <c>%LOCALAPPDATA%\{app}</c> on Windows,
/// <c>~/Library/Application Support/{app}</c> on macOS, <c>~/.local/share/{app}</c> on Linux.</param>
/// <param name="LogsDir">Log files: <c>~/Library/Logs/{app}</c> on macOS, where Console.app and users
/// already look; <c>{DataDir}/logs</c> elsewhere.</param>
public sealed record AppPaths(string AppName, string ConfigDir, string DataDir, string LogsDir)
{
    /// <summary>The platform-default paths for <paramref name="appName"/>.</summary>
    public static AppPaths ForApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("An app name is required.", nameof(appName));
        }

        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
        var logs = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", appName)
            : Path.Combine(data, "logs");
        return new AppPaths(appName, config, data, logs);
    }

    /// <summary>
    /// The folder Avalloy's own packages share when no app has said otherwise (<c>"Avalloy"</c>): file-backed
    /// secrets, the VFS cache, the login-shell PATH cache. Those are namespaced inside it — secrets by service
    /// name — and have lived there since before this type existed, so it stays their default.
    /// </summary>
    public static AppPaths Shared { get; } = ForApp("Avalloy");
}
