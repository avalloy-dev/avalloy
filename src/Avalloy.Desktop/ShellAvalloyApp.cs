using Avalloy.Services;
using Avalloy.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace Avalloy;

/// <summary>
/// <see cref="AvalloyApp"/> with the batteries in: registers the themed dialog service and
/// maps the <see cref="ResolveThemeService"/> hook onto the engine-agnostic
/// <see cref="AvalloyApp.ResolveThemeController"/> seam. An app that wants Avalloy's own theme
/// engine derives from this; an app bringing another engine derives from <see cref="AvalloyApp"/>
/// and answers <c>ResolveThemeController</c> itself.
/// </summary>
public abstract class ShellAvalloyApp : AvalloyApp
{
    /// <summary>Register the themed <see cref="DialogService"/> (which uses
    /// <c>ConfirmationDialog</c>). Registered via
    /// the unconditional built-in hook so apps get it whether or not their
    /// <see cref="ConfigureServices"/> calls <c>base</c> (matching the pre-split
    /// behavior, where Core registered it unconditionally); an app can still replace
    /// it with its own <c>AddSingleton&lt;IDialogService&gt;</c>.</summary>
    protected override void ConfigureBuiltInServices(IServiceCollection services)
    {
        base.ConfigureBuiltInServices(services);
        services.AddSingleton<IDialogService, DialogService>();
    }

    /// <summary>Override to supply the app's <see cref="ThemeService"/> so Avalloy
    /// owns the theme-apply lifecycle. Default <c>null</c> means no theming.
    /// Typically <c>Services.GetRequiredService&lt;ThemeService&gt;()</c>.</summary>
    protected virtual ThemeService? ResolveThemeService() => null;

    /// <summary>Bridge the <see cref="ResolveThemeService"/> hook onto Avalloy.Base's
    /// engine-agnostic seam.</summary>
    protected sealed override IThemeController? ResolveThemeController() => ResolveThemeService();

}
