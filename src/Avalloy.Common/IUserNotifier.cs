namespace Avalloy;

/// <summary>
/// How a package that has no UI of its own tells the user something. Three levels, because that is what
/// such a package has to say: something is happening, it worked, it didn't.
///
/// <para>In an Avalloy app this is the toast service — <c>AvalloyApp</c> registers <c>ToastService</c> as
/// the implementation — so a package consuming it reaches the user without referencing Avalloy.Desktop.
/// A headless host supplies its own (a console line, a log entry, nothing at all).</para>
///
/// <para>Implementations must not throw and must be callable from any thread: callers report from
/// background work, including in the window before the app has a UI to show anything in.</para>
/// </summary>
public interface IUserNotifier
{
    void Info(string title, string message);
    void Success(string title, string message);
    void Error(string title, string message);
}
