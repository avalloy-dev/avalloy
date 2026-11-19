using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>Open the OS file manager at a file or folder — the "reveal log file"
/// affordance. Best-effort: every call is wrapped in a catch-all because a reveal
/// button must never be the reason the app dies.</summary>
public static class FileReveal
{
    /// <summary>Show <paramref name="path"/> selected in Finder / Explorer. On
    /// Linux (no portable "select" verb) the containing folder is opened.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path } });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
            }
            else if (Path.GetDirectoryName(path) is { } dir)
            {
                OpenFolder(dir);
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    /// <summary>Open <paramref name="dir"/> in the OS file manager.</summary>
    public static void OpenFolder(string dir)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { dir } });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { dir } });
            else
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { dir } });
        }
        catch
        {
            // Best-effort only.
        }
    }
}
