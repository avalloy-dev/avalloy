using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Services.Tests;

/// <summary>
/// Two writers on one log file — a second copy of an app on the same data, or two providers
/// in one process — must each land every entry whole. Opening with <c>FileMode.Append</c>
/// only seeks to the end once, so each writer kept its own offset and wrote over the other's
/// newer bytes: the second writer's banner and lines vanished and the first's were torn.
/// </summary>
[TestClass]
public class FileLoggerProviderSharedFileTests
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
    public void Two_providers_on_one_file_keep_every_line_whole()
    {
        var path = Path.Combine(_dir, "shared.log");
        using (var first = new FileLoggerProvider(path))
        using (var second = new FileLoggerProvider(path))
        {
            var a = first.CreateLogger("first");
            var b = second.CreateLogger("second");
            for (var i = 1; i <= 3; i++)
            {
                a.LogInformation("FIRST line {N}", i);
                b.LogInformation("SECOND line {N}", i);
            }
        }

        var lines = File.ReadAllLines(path);
        lines.Count(l => l.StartsWith("=== launch pid ", StringComparison.Ordinal)).Should().Be(2,
            "each provider's banner survives");
        string[] expected =
        [
            "INFO [first] FIRST line 1", "INFO [second] SECOND line 1",
            "INFO [first] FIRST line 2", "INFO [second] SECOND line 2",
            "INFO [first] FIRST line 3", "INFO [second] SECOND line 3",
        ];
        var entries = lines.Where(l => !l.StartsWith("=== ", StringComparison.Ordinal))
            .Select(l => l[24..]) // drop the "yyyy-MM-dd HH:mm:ss.fff " timestamp
            .ToArray();
        entries.Should().Equal(expected, "every entry lands whole, in the order it was written");
    }

    [TestMethod]
    public void A_writer_appends_after_bytes_another_wrote_since_it_opened()
    {
        var path = Path.Combine(_dir, "shared.log");
        using (var provider = new FileLoggerProvider(path))
        {
            // Another writer appends behind this provider's back. It shares the file as a second
            // logger would: File.AppendAllText allows only readers, which Windows refuses while
            // the provider holds the file open for writing.
            using (var other = new StreamWriter(new FileStream(
                       path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)))
            {
                other.WriteLine("written elsewhere");
            }

            provider.CreateLogger("cat").LogInformation("after");
        }

        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(3);
        lines[1].Should().Be("written elsewhere");
        lines[2].Should().EndWith("INFO [cat] after");
    }
}
