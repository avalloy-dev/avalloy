using AwesomeAssertions;
using Avalloy.Controls.Splash;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Controls.Splash.Tests;

/// <summary>
/// The splash's build-vitals line. The channel comes from the ENTRY assembly's DebuggableAttribute
/// (the app's configuration, not the configuration this library was packaged with) and the timestamp
/// from the assembly file's write time — so under any real host both parts resolve. The window itself
/// needs a headless dispatcher and is exercised by the Starter demo instead.
/// </summary>
[TestClass]
public class SplashBuildInfoTests
{
    [TestMethod]
    public void Compute_names_a_channel_and_never_throws()
    {
        var info = AlloySplashWindow.ComputeBuildInfo();

        info.Should().MatchRegex("^(Debug|Release)",
            "the channel must lead the line even when no assembly path resolves");
    }

    [TestMethod]
    public void Compute_carries_the_build_timestamp_when_the_host_has_a_real_path()
    {
        // The test host is a real on-disk executable, so the full "channel · built <when>" form applies.
        AlloySplashWindow.ComputeBuildInfo().Should().Contain("· built ",
            "a resolvable entry-assembly path must yield the timestamp half");
    }
}
