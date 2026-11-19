using System.ComponentModel;

namespace Avalloy.Preferences;

/// <summary>
/// Contract that apps implement on their root preferences POCO so
/// <see cref="PreferencesService{T}"/> can hook mutation tracking without
/// knowing the section layout. Implementations subscribe/unsubscribe the
/// supplied handler from every <see cref="INotifyPropertyChanged"/> section
/// the model exposes (top-level properties typically backed by Community
/// Toolkit MVVM's <c>ObservableObject</c>).
/// </summary>
public interface IPreferencesModel
{
    /// <summary>Wire the handler to every nested
    /// <see cref="INotifyPropertyChanged"/> section. Called after load and
    /// reload. Must be idempotent-safe with the matching
    /// <see cref="UnregisterMutationHandler"/> call.</summary>
    void RegisterMutationHandler(PropertyChangedEventHandler handler);

    /// <summary>Unwire the handler. Called before the model is replaced on
    /// reload so the old instance's events don't keep firing.</summary>
    void UnregisterMutationHandler(PropertyChangedEventHandler handler);
}
