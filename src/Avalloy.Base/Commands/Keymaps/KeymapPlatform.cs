namespace Avalloy.Commands.Keymaps;

/// <summary>Platform conventions for key labels and Option composition.</summary>
public enum KeymapPlatform { MacOS, Linux, Windows }

/// <summary>The current platform.</summary>
public static class KeymapPlatforms
{
    public static KeymapPlatform Current => OperatingSystem.IsMacOS() ? KeymapPlatform.MacOS
        : OperatingSystem.IsWindows() ? KeymapPlatform.Windows : KeymapPlatform.Linux;
}
