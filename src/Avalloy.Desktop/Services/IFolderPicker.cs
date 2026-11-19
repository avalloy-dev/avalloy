namespace Avalloy.Services;

/// <summary>OS folder picker. Resolves the picked folder's disk path, or null if
/// the user cancelled. Parented to the current shell window via
/// <see cref="ShellHost"/>; returns null when no window is available.</summary>
public interface IFolderPicker
{
    Task<string?> PickAsync(string? suggestedStartPath, string title);
}
