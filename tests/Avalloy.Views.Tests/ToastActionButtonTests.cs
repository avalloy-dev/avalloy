using Avalloy.Services;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Views.Tests;

/// <summary>
/// A toast's action buttons answer it: pressing one runs its action and closes the card it sits in. Without the
/// close a persistent toast (Duration &lt;= 0) never goes, and a button that did its work reads as one that did
/// nothing. The content is hosted in a real <see cref="NotificationCard"/>, as WindowNotificationManager hosts it,
/// so the button has to find its card the way it does in the app.
/// </summary>
[TestClass]
public class ToastActionButtonTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Get();

    private static (Window Window, NotificationCard Card, Button Button) Host(Toast toast, string label)
    {
        var card = new NotificationCard { Content = ToastService.BuildActionContent(toast) };
        var window = new Window { Content = card, Width = 400, Height = 200 };
        window.Show();
        HeadlessSession.Settle(window);
        var button = card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
        return (window, card, button);
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Toast Persistent(Action onAction, Action onSecondary) =>
        new("Delete the 3 old backups?", null, ToastSeverity.Info, TimeSpan.Zero,
            "Delete", onAction, "Keep", onSecondary);

    [TestMethod]
    public Task The_Action_Button_Runs_Once_And_Closes_The_Card() => Session.Dispatch(() =>
    {
        int actions = 0, secondaries = 0;
        var (window, card, button) = Host(Persistent(() => actions++, () => secondaries++), "Delete");

        card.IsClosing.Should().BeFalse("nothing has been pressed yet");
        Press(button);
        card.IsClosing.Should().BeTrue("an answered toast has to go, even a persistent one");

        Press(button);
        actions.Should().Be(1, "a second press while the card closes must not run the action again");
        secondaries.Should().Be(0);
        window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task The_Secondary_Button_Runs_Once_And_Closes_The_Card() => Session.Dispatch(() =>
    {
        int actions = 0, secondaries = 0;
        var (window, card, button) = Host(Persistent(() => actions++, () => secondaries++), "Keep");

        Press(button);
        card.IsClosing.Should().BeTrue();

        Press(button);
        Press(card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Delete")));
        secondaries.Should().Be(1);
        actions.Should().Be(0, "the toast was already answered by Keep");
        window.Close();
    }, CancellationToken.None);

    [TestMethod]
    public Task A_Throwing_Action_Still_Closes_The_Card() => Session.Dispatch(() =>
    {
        var (window, card, button) = Host(
            Persistent(() => throw new InvalidOperationException("free failed"), () => { }), "Delete");

        var press = () => Press(button);
        press.Should().Throw<InvalidOperationException>("the failure is the action's to report, not to swallow");
        card.IsClosing.Should().BeTrue("a failed action must not leave its toast on screen");
        window.Close();
    }, CancellationToken.None);
}
