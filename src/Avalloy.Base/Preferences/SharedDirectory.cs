using System.Runtime.InteropServices;

namespace Avalloy.Preferences;

/// <summary>Whether users other than this one can create entries in a directory.</summary>
/// <remarks>
/// <para>A watcher on such a directory can be poked by them: on a case-sensitive disk, creating and
/// removing a sibling named like a watched subdirectory but for case rebuilds every watcher (see
/// event filtering). So <see cref="WatchPlan"/> does not watch one only to see a
/// config directory inside it replaced.</para>
/// <para>On Unix a directory is shared when another user owns it or its group or anyone else may write to it, as
/// <c>/tmp</c> is both. The owner comes from the runtime's internal <c>SystemNative_Stat</c> shim, as
/// the native runtime uses it, and is believed only when the same
/// record reports a directory; otherwise the mode bits alone decide. A directory that cannot be
/// looked at is not shared, which keeps watching as it was.</para>
/// <para>On Windows the rule does not apply: directories there are case-insensitive unless a user
/// opts one in, so nobody without access to the watched subdirectory can raise an event under its
/// name.</para>
/// </remarks>
internal static class SharedDirectory
{
    public static bool IsShared(string directory)
    {
        if (OperatingSystem.IsWindows())
            return false;
        try
        {
            if ((File.GetUnixFileMode(directory) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        return OwnerIsSomeoneElse(directory);
    }

    private const int TypeMask = 0xF000, Dir = 0x4000;

    // The status record starts flags, mode, uid; the buffer is larger than the record.
    private static bool OwnerIsSomeoneElse(string directory)
    {
        try
        {
            var status = new byte[256];
            if (SystemNativeStat(directory, status) != 0 || (BitConverter.ToInt32(status, 4) & TypeMask) != Dir)
                return false;
            return BitConverter.ToUInt32(status, 8) != SystemNativeGetEUid();
        }
        catch (Exception) { return false; }   // DllNotFound, EntryPointNotFound under AOT, or anything else
    }

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_Stat", SetLastError = true)]
    private static extern int SystemNativeStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] status);

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetEUid")]
    private static extern uint SystemNativeGetEUid();
}
