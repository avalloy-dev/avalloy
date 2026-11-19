using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

[TestClass]
public class FileLoggerProviderScopeTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup() =>
        _dir = Path.Combine(Path.GetTempPath(), "alloy-log-tests-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [TestMethod]
    public void Scoped_entries_carry_the_flattened_scope_prefix()
    {
        var path = Path.Combine(_dir, "test.log");
        using (var factory = new LoggerFactory())
        {
            factory.AddProvider(new FileLoggerProvider(path));
            var logger = factory.CreateLogger("cat");
            using (logger.BeginScope("outer"))
            using (logger.BeginScope("inner"))
            {
                logger.LogInformation("scoped message");
            }

            logger.LogInformation("plain message");
        }

        var text = File.ReadAllText(path);
        text.Should().Contain("INFO [cat] outer => inner · scoped message");
        // No scope → untouched classic format, no separator.
        text.Should().Contain("INFO [cat] plain message");
        text.Should().NotContain("· plain message");
    }
}
