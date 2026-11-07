using Avalonia;
using Avalonia.Controls;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Platform.Windows;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Updating;

/// <summary>
/// The window of the Windows update helper (<c>--finish-update</c>): the progress card of the update in a window of its own, in the language and theme of the app (the shell
/// theme is applied to the application). It follows <see cref="WindowsUpdateHelper"/>: the steps while it waits, verifies and installs, and after a failure the worded error
/// with "Try again" and "Close". A card answers once, so "Try again" shows a fresh card. The window never closes while the setup program runs; closing it while the helper
/// verifies cancels the update (the installed version starts again), and closing it after a failure is "Close".
/// </summary>
internal sealed class UpdateHelperWindow : Window
{
    private const double DefaultWidth = 520;
    private const double DefaultHeight = 360;

    private readonly WindowsUpdateHelper _helper;
    private readonly DialogHost _host = new() { Content = new Border() };
    private UpdateProgressCard _card;

    public UpdateHelperWindow(WindowsUpdateHelper helper)
    {
        ArgumentNullException.ThrowIfNull(helper);
        _helper = helper;
        Title = PdfStrings.UpdateProgressTitle;
        CanResize = false;
        Width = helper.Request?.Width is > 0 and var width ? width : DefaultWidth;
        Height = helper.Request?.Height is > 0 and var height ? height : DefaultHeight;
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Page"));
        Bind(ForegroundProperty, this.GetResourceObservable("Raffica.Brush.Text.Primary"));
        Content = _host;

        // Where the window of the app was, so it takes its place (the request holds physical pixels); otherwise the middle of the screen.
        if (helper.Request is { Left: { } left, Top: { } top })
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(left, top);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _card = NewCard();
        _helper.Changed += OnChanged;
        Opened += OnOpened;
        Closing += OnClosing;
    }

    /// <summary>Gets the card that is shown now (a new one after "Try again").</summary>
    internal UpdateProgressCard Card => _card;

    /// <summary>Gets the host the card is shown in.</summary>
    internal DialogHost Host => _host;

    private UpdateProgressCard NewCard()
    {
        var card = new UpdateProgressCard(_helper.CancelInstall, OnChoice);
        card.Apply(_helper.State, _helper.FailureMessage, _helper.CanRetry, offersDownloadPage: false);
        _host.Show(card.Dialog);
        return card;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;

        // RunAsync never throws; it ends in the state of the helper, which the card shows.
        _ = _helper.RunAsync();
    }

    private void OnChanged() => _card.Apply(_helper.State, _helper.FailureMessage, _helper.CanRetry, offersDownloadPage: false);

    private void OnChoice(UpdateProgressChoice choice)
    {
        if (choice == UpdateProgressChoice.TryAgain)
        {
            // The answered card goes, a fresh one follows the next attempt.
            _host.Close();
            _card = NewCard();
            _ = _helper.RetryAsync();
            return;
        }

        _helper.Dismiss();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        switch (_helper.State.Stage)
        {
            case UpdateStage.Installing:
                // The setup program runs: it is not cut off by closing a window.
                e.Cancel = true;
                break;
            case UpdateStage.Verifying:
                _helper.CancelInstall();
                break;
            case UpdateStage.Failed:
                _helper.Dismiss();
                break;
        }
    }
}
