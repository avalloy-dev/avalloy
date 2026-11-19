using Avalloy.Preferences;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

[TestClass]
public class SharedDirectoryTests
{
    [TestMethod]
    public void Group_writable_directory_is_shared()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix modes"); return; }
        var dir = Directory.CreateTempSubdirectory("avalloy-group-shared-");
        try
        {
            File.SetUnixFileMode(dir.FullName, (UnixFileMode)Convert.ToInt32("0770", 8));
            Assert.IsTrue(new FileSetWatcher.PhysicalFileSystem().IsSharedDirectory(dir.FullName));
        }
        finally { dir.Delete(); }
    }

    [TestMethod]
    public void Own_directory_becomes_shared_when_other_users_can_write()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix modes"); return; }
        var dir = Directory.CreateTempSubdirectory("avalloy-shared-");
        try
        {
            File.SetUnixFileMode(dir.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var fs = new FileSetWatcher.PhysicalFileSystem();
            Assert.IsFalse(fs.IsSharedDirectory(dir.FullName));
            File.SetUnixFileMode(dir.FullName, (UnixFileMode)Convert.ToInt32("1777", 8));
            Assert.IsTrue(fs.IsSharedDirectory(dir.FullName));
        }
        finally { dir.Delete(); }
    }

    [TestMethod]
    public void Another_users_directory_is_shared_even_without_other_write()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        { Assert.Inconclusive("Needs non-root Unix user"); return; }
        Assert.IsFalse(File.GetUnixFileMode("/").HasFlag(UnixFileMode.OtherWrite));
        Assert.IsTrue(new FileSetWatcher.PhysicalFileSystem().IsSharedDirectory("/"));
    }

    [TestMethod]
    public void Windows_directories_are_not_shared()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows only"); return; }
        Assert.IsFalse(new FileSetWatcher.PhysicalFileSystem().IsSharedDirectory(Path.GetTempPath()));
    }

    [TestMethod]
    public void Missing_directory_is_not_shared()
    {
        Assert.IsFalse(new FileSetWatcher.PhysicalFileSystem().IsSharedDirectory(
            Path.Combine(Path.GetTempPath(), "avalloy-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
