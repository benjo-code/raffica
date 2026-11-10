using Avalonia.Controls;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Shell.Frame;

namespace Raffica.Pdf;

// What the macOS menu bar asks of the window (MacMenuBinder). Nothing here runs before the menu asks.
internal sealed partial class PdfWindow : IMenuWindow
{
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    /// <summary>Raised when the ribbon was asked for its states again (the menu bar reads them again).</summary>
    public event Action? StatesChanged;

    public CommandDefinition? CommandOf(string id) => _commandsById.GetValueOrDefault(id);

    public TextBox? FocusedTextBox => FocusManager?.GetFocusedElement() as TextBox;

    public bool DialogOpen => Host.IsOpen;

    public IReadOnlyList<RecentFile> RecentFiles => Start.Recent;

    public void OpenRecent(RecentFile file) => _ = GuardAsync(() => OpenRecentAsync(file));

    public void ShowAbout() => _ = GuardAsync(ShowAboutAsync);

    public void Minimize() => WindowState = WindowState.Minimized;

    public void ToggleWindowZoom() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void BringAllToFront() => Activate();

    public bool IsFullScreen
    {
        get => WindowState == WindowState.FullScreen;
        set
        {
            if (value == IsFullScreen)
            {
                return;
            }

            if (value)
            {
                _stateBeforeFullScreen = WindowState;
                WindowState = WindowState.FullScreen;
            }
            else
            {
                WindowState = _stateBeforeFullScreen == WindowState.FullScreen ? WindowState.Normal : _stateBeforeFullScreen;
            }
        }
    }
}
