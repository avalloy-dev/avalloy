using Avalonia.Platform.Storage;

namespace Avalloy.Services;

/// <summary>Default <see cref="IFolderPicker"/> — the Avalonia storage-provider
/// folder picker parented to <see cref="ShellHost.MainWindow"/>.</summary>
public sealed class FolderPicker(ShellHost host) : IFolderPicker
{
    public async Task<string?> PickAsync(string? suggestedStartPath, string title)
    {
        if (host.MainWindow is not { StorageProvider: { } storage }) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        };
        if (!string.IsNullOrEmpty(suggestedStartPath))
        {
            try
            {
                var seedUri = new Uri(suggestedStartPath);
                options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(seedUri);
            }
            catch { /* picker still opens at its default root if seed fails */ }
        }

        var picked = await storage.OpenFolderPickerAsync(options);
        if (picked is null || picked.Count == 0) return null;
        var folder = picked[0];
        var local = folder.Path?.LocalPath;
        return string.IsNullOrEmpty(local) ? null : local;
    }
}
