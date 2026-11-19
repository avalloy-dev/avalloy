using AwesomeAssertions;
using Avalloy.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Desktop.Tests;

/// <summary>
/// <see cref="ToastService"/> as the app's <see cref="IUserNotifier"/>. It is three delegating lines, but it is
/// the ONLY wiring between UI-less packages (Avalloy.Updates' update checks) and anything the user sees — a
/// mis-mapped level would ship an update failure as a cheerful green toast, and those packages' suites would stay
/// green because they hold a fake.
/// <para>What can be asserted without a dispatcher is that each level survives an unattached service, which
/// buffers instead of throwing. The rendering itself belongs to <see cref="ToastService"/>.</para>
/// </summary>
[TestClass]
public class ToastUserNotifierTests
{
    [TestMethod]
    public void Every_Level_Survives_An_Unattached_Toast_Service()
    {
        // No Attach: exactly the startup window in which a silent update check can complete.
        IUserNotifier notifier = new ToastService();

        var act = () =>
        {
            notifier.Info("Updating Contoso", "Downloading 2.0.0…");
            notifier.Success("Update ready", "Restarting to finish the update…");
            notifier.Error("Update failed", "no network");
        };

        act.Should().NotThrow(
            "an update check that runs before the shell window exists must not take the app down with it");
    }
}
