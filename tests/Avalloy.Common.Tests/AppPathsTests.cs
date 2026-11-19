using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Abstractions.Tests;

/// <summary>
/// <see cref="AppPaths"/> replaced five separate computations of the same folders, spread over Core,
/// Secrets and FileSystem. Each was a promise to every existing install about where its files are, so
/// the expectations below are those computations copied VERBATIM from before the change — a drift in
/// any of them would silently strand users' preferences, logs, icon packs or secrets.
/// </summary>
[TestClass]
public class AppPathsTests
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [TestMethod]
    public void ConfigDir_Is_Where_Preferences_Always_Lived()
        // PreferencesLocation.GetPlatformDefault, before.
        => AppPaths.ForApp("Contoso").ConfigDir.Should().Be(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Contoso"));

    [TestMethod]
    public void DataDir_Is_Where_Icon_Packs_Always_Lived()
        // AvalloyIconPacks.DefaultOptions' UserPacksRoot, before, minus its "iconpacks" leaf.
        => AppPaths.ForApp("Contoso").DataDir.Should().Be(Path.Combine(Local, "Contoso"));

    [TestMethod]
    public void LogsDir_Is_Where_Logs_Always_Lived()
    {
        // FileLoggerProvider.DefaultDirectory and AppRelaunch's watcher log, before (the same formula twice).
        var expected = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Contoso")
            : Path.Combine(Local, "Contoso", "logs");

        AppPaths.ForApp("Contoso").LogsDir.Should().Be(expected);
    }

    [TestMethod]
    public void Shared_Is_The_Avalloy_Folder_Secrets_And_The_Vfs_Cache_Already_Use()
    {
        // UserOnlyFileStore / WindowsDpapiStore / VfsMaterializer all hard-coded LocalAppData/Avalloy.
        AppPaths.Shared.DataDir.Should().Be(Path.Combine(Local, "Avalloy"));
        AppPaths.Shared.Should().Be(AppPaths.ForApp("Avalloy"));
    }

    [TestMethod]
    public void AppName_Is_Carried_Through()
        => AppPaths.ForApp("Contoso").AppName.Should().Be("Contoso");

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void A_Blank_App_Name_Is_Refused(string name)
    {
        // A blank name would resolve every folder to the shared OS root itself.
        var act = () => AppPaths.ForApp(name);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void One_Folder_Can_Be_Relocated_Without_Moving_The_Others()
    {
        var paths = AppPaths.ForApp("Contoso") with { LogsDir = "/var/log/contoso" };

        paths.LogsDir.Should().Be("/var/log/contoso");
        paths.DataDir.Should().Be(AppPaths.ForApp("Contoso").DataDir);
    }
}
