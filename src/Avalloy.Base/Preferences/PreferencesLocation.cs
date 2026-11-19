namespace Avalloy.Preferences;

/// <summary>
/// Resolves the directory where <c>preferences.json</c> + <c>presets/</c> live.
/// Resolution order:
/// <list type="number">
///   <item><c>--prefs-dir &lt;path&gt;</c> command-line flag</item>
///   <item>The env var passed in <see cref="Resolve"/></item>
///   <item>A <c>.location</c> bootstrap file at the platform default location
///         whose contents are an absolute path to the real preferences directory</item>
///   <item>The platform default itself (<c>%APPDATA%\{AppFolderName}</c> /
///         <c>~/Library/Application Support/{AppFolderName}</c>)</item>
/// </list>
/// The pointer is intentionally never stored inside <c>preferences.json</c> —
/// that would be circular.
/// </summary>
public static class PreferencesLocation
{
    public const string CliFlag = "--prefs-dir";
    public const string BootstrapFileName = ".location";

    /// <summary>
    /// Resolve the preferences directory. Pure function, no side effects.
    /// </summary>
    /// <param name="args">Process command-line args (excluding the executable name).</param>
    /// <param name="appFolderName">App-specific folder name appended to the
    /// platform default app-data root (e.g. "Avalloy" → "%APPDATA%\Avalloy").</param>
    /// <param name="envVar">Environment variable to consult before the
    /// bootstrap file. Pass null to skip the env-var lookup.</param>
    /// <param name="env">Environment-variable lookup (defaults to the real
    /// environment; tests pass a fake).</param>
    /// <param name="platformDefault">Platform default directory (defaults to
    /// the real one; tests pass a fake).</param>
    /// <param name="fileExists">File-exists check (tests inject).</param>
    /// <param name="readAllText">File-read function (tests inject).</param>
    public static string Resolve(
        string[] args,
        string appFolderName,
        string? envVar = null,
        Func<string, string?>? env = null,
        string? platformDefault = null,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        platformDefault ??= GetPlatformDefault(appFolderName);
        fileExists ??= File.Exists;
        readAllText ??= File.ReadAllText;

        // 1. CLI flag
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == CliFlag)
            {
                var v = args[i + 1];
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }

        // 2. Environment variable
        if (!string.IsNullOrWhiteSpace(envVar))
        {
            var envValue = env(envVar);
            if (!string.IsNullOrWhiteSpace(envValue)) return envValue;
        }

        // 3. Bootstrap file at the platform default location
        var bootstrap = Path.Combine(platformDefault, BootstrapFileName);
        if (fileExists(bootstrap))
        {
            var pointed = readAllText(bootstrap).Trim();
            if (!string.IsNullOrWhiteSpace(pointed)) return pointed;
        }

        // 4. Platform default itself
        return platformDefault;
    }

    /// <summary>The platform default preferences directory: <see cref="AppPaths.ConfigDir"/>.</summary>
    public static string GetPlatformDefault(string appFolderName) => AppPaths.ForApp(appFolderName).ConfigDir;
}
