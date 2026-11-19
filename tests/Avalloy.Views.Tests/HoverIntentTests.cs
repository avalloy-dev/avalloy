using Avalloy.AttachedProperties;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// <see cref="HoverIntent"/> on a fake clock: the dwell, the leave grace, a stop racing a tick already posted, and
/// the attached behavior driven by real headless pointer moves. Every wait is the clock advanced by hand, then the
/// dispatcher drained, because a tick reaches the UI thread as posted work.
/// </summary>
[TestClass]
public class HoverIntentTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    private static void Advance(FakeTimeProvider time, int ms)
    {
        time.Advance(TimeSpan.FromMilliseconds(ms));
        Dispatcher.UIThread.RunJobs();
    }

    [TestMethod]
    public Task It_Reveals_Only_Once_The_Pointer_Has_Dwelt() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var revealed = false;
        var intent = new HoverIntent(r => revealed = r, time: time);

        intent.Enter();
        Advance(time, HoverIntent.DwellMs - 1);
        revealed.Should().BeFalse("the dwell has not run out");

        Advance(time, 1);
        revealed.Should().BeTrue();
        intent.IsRevealed.Should().BeTrue();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Pass_Across_The_Zone_Reveals_Nothing() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var calls = new List<bool>();
        var intent = new HoverIntent(calls.Add, time: time);

        intent.Enter();
        Advance(time, 100);
        intent.Leave();
        Advance(time, 1000);

        calls.Should().Equal(false);
        intent.IsRevealed.Should().BeFalse();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Leave_After_The_Clock_Fired_But_Before_The_Tick_Ran_Still_Wins() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var revealed = false;
        var intent = new HoverIntent(r => revealed = r, time: time);

        intent.Enter();
        time.Advance(TimeSpan.FromMilliseconds(HoverIntent.DwellMs));   // the tick is posted, not yet run
        intent.Leave();
        Dispatcher.UIThread.RunJobs();

        revealed.Should().BeFalse("the stale tick must be dropped");
    }, CancellationToken.None);

    [TestMethod]
    public Task Without_A_Grace_A_Leave_Hides_At_Once() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var revealed = false;
        var intent = new HoverIntent(r => revealed = r, time: time);

        intent.Enter();
        Advance(time, HoverIntent.DwellMs);
        intent.Leave();

        revealed.Should().BeFalse();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Grace_Keeps_It_Up_While_The_Pointer_Crosses_Onto_The_Affordance() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var revealed = false;
        var intent = new HoverIntent(r => revealed = r, leaveGraceMs: 400, time: time);

        intent.Enter();
        Advance(time, HoverIntent.DwellMs);
        intent.Leave();                       // off the chip...
        Advance(time, 300);
        revealed.Should().BeTrue("the grace has not run out");

        intent.Enter();                       // ...and onto its button
        Advance(time, 1000);
        revealed.Should().BeTrue("the pointer is on the affordance, so the grace was cancelled");

        intent.Leave();
        Advance(time, 399);
        revealed.Should().BeTrue();
        Advance(time, 1);
        revealed.Should().BeFalse("the grace ran out with the pointer on neither");
    }, CancellationToken.None);

    [TestMethod]
    public Task Hide_Ignores_The_Grace_And_The_Next_Enter_Earns_The_Dwell_Again() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var revealed = false;
        var intent = new HoverIntent(r => revealed = r, leaveGraceMs: 400, time: time);

        intent.Enter();
        Advance(time, HoverIntent.DwellMs);
        intent.Hide();
        revealed.Should().BeFalse();

        intent.Enter();
        Advance(time, HoverIntent.DwellMs - 1);
        revealed.Should().BeFalse();
        Advance(time, 1);
        revealed.Should().BeTrue();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Attached_Behavior_Adds_Its_Class_After_A_Dwell_And_Drops_It_On_Leave() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var previous = HoverIntent.Time;
        HoverIntent.Time = time;
        try
        {
            var zone = new Border
            {
                Width = 100, Height = 40, Background = Avalonia.Media.Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            HoverIntent.SetRevealClass(zone, "reveal");
            var window = new Window { Width = 300, Height = 200, Content = zone };
            window.Show();
            HeadlessSession.Settle(window);

            window.MouseMove(new Point(20, 20));
            Advance(time, HoverIntent.DwellMs - 1);
            zone.Classes.Should().NotContain("reveal");
            Advance(time, 1);
            zone.Classes.Should().Contain("reveal");

            window.MouseMove(new Point(250, 150));
            zone.Classes.Should().NotContain("reveal");
            window.Close();
        }
        finally { HoverIntent.Time = previous; }
    }, CancellationToken.None);

    [TestMethod]
    public Task Scrolling_Under_A_Still_Pointer_Rearms_The_Dwell() => Session.Dispatch(() =>
    {
        var time = new FakeTimeProvider();
        var previous = HoverIntent.Time;
        HoverIntent.Time = time;
        try
        {
            var row = new Border { Height = 40, Background = Avalonia.Media.Brushes.Transparent };
            HoverIntent.SetRevealClass(row, "reveal");
            var scroller = new ScrollViewer
            {
                Height = 100,
                Content = new StackPanel { Children = { row, new Border { Height = 1000 } } },
            };
            HoverIntent.SetRearmOnScroll(scroller, true);
            var window = new Window { Width = 300, Height = 100, Content = scroller };
            window.Show();
            HeadlessSession.Settle(window);

            window.MouseMove(new Point(20, 20));
            Advance(time, HoverIntent.DwellMs);
            row.Classes.Should().Contain("reveal");

            scroller.Offset = new Vector(0, 10);
            HeadlessSession.Settle(window);
            row.Classes.Should().NotContain("reveal", "the content moved under the pointer");

            Advance(time, HoverIntent.DwellMs);
            row.Classes.Should().Contain("reveal", "the pointer dwelt again once the motion settled");
            window.Close();
        }
        finally { HoverIntent.Time = previous; }
    }, CancellationToken.None);
}
