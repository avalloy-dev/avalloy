using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

/// <summary>
/// <see cref="AlloyLogging.LogFilePath"/> is published only once <see cref="AlloyLogging.Factory"/> is, so a caller
/// that waits for the path can log on that cue and the line reaches the file. Published the other way round (the path
/// while the logging providers were registered, the factory after the whole service container was built), a caller
/// keyed on the path logged into the NullLogger: an app's UI hang watchdog lost its "armed" line that way.
/// </summary>
[TestClass]
[DoNotParallelize]   // AlloyLogging is process-wide
public class AlloyLoggingPublishTests
{
    private ILoggerFactory _factory = null!;
    private string? _path;

    [TestInitialize]
    public void Setup() => (_factory, _path) = (AlloyLogging.Factory, AlloyLogging.LogFilePath);

    [TestCleanup]
    public void Cleanup()
    {
        AlloyLogging.AfterFactoryPublished = null;
        AlloyLogging.Factory = _factory;
        AlloyLogging.LogFilePath = _path;
    }

    [TestMethod]
    public void The_factory_is_live_before_the_log_path_appears()
    {
        AlloyLogging.Factory = NullLoggerFactory.Instance;
        AlloyLogging.LogFilePath = null;
        using var factory = new LoggerFactory();
        string? pathWhenFactoryLanded = "unset";
        ILoggerFactory? factoryWhenFactoryLanded = null;
        AlloyLogging.AfterFactoryPublished = () =>
        {
            pathWhenFactoryLanded = AlloyLogging.LogFilePath;
            factoryWhenFactoryLanded = AlloyLogging.Factory;
        };

        AlloyLogging.Publish(factory, "/tmp/app.log");

        factoryWhenFactoryLanded.Should().BeSameAs(factory, "the factory is published first");
        pathWhenFactoryLanded.Should().BeNull("the path must not be visible before the factory is live");
        AlloyLogging.LogFilePath.Should().Be("/tmp/app.log");
        AlloyLogging.Factory.Should().BeSameAs(factory);
    }

    [TestMethod]
    public void No_file_sink_publishes_the_factory_and_no_path()
    {
        AlloyLogging.LogFilePath = "/stale.log";
        using var factory = new LoggerFactory();

        AlloyLogging.Publish(factory, null);

        AlloyLogging.Factory.Should().BeSameAs(factory);
        AlloyLogging.LogFilePath.Should().BeNull("a sink that couldn't be opened has no path to report");
    }
}
