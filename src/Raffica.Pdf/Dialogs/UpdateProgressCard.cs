using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose after a failed update.</summary>
internal enum UpdateProgressChoice
{
    /// <summary>Close the message.</summary>
    Close,

    /// <summary>Try the update again.</summary>
    TryAgain,

    /// <summary>Open the download page.</summary>
    OpenDownloadPage,
}

/// <summary>
/// The card of a running update: the four steps Download, Check, Install and Restart, the current one in bold with its progress (the megabytes
/// received while downloading, else "In progress"), the finished ones marked Done, and Cancel while a download or the check of the file can still be stopped. After a
/// failure it shows what went wrong in the app's words (<see cref="UpdateWording"/>) with Try again (where it can help), Open download page (where the
/// user solves it by hand) and Close. The owner calls <see cref="Apply"/> with every new state; the card never decides anything itself.
/// </summary>
internal sealed class UpdateProgressCard
{
    private readonly Action _cancel;
    private readonly Action<UpdateProgressChoice> _finished;
    private readonly TextBlock[] _labels;
    private readonly TextBlock[] _status;
    private readonly Grid _bar;
    private readonly Border _track;
    private readonly TextBlock _failure;
    private readonly Button _cancelButton;
    private readonly Button _retry;
    private readonly Button _page;
    private readonly Button _close;
    private bool _done;

    /// <param name="cancel">Called when the user cancels a download or check (the owner stops it).</param>
    /// <param name="finished">Called once when the user closes a failure message, asks to try again or to open the download page.</param>
    public UpdateProgressCard(Action cancel, Action<UpdateProgressChoice> finished)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        ArgumentNullException.ThrowIfNull(finished);
        _cancel = cancel;
        _finished = finished;

        string[] steps = [PdfStrings.UpdateStepDownload, PdfStrings.UpdateStepCheck, PdfStrings.UpdateStepInstall, PdfStrings.UpdateStepRestart];
        var rows = new StackPanel { Name = "PART_Steps", Spacing = 6, MinWidth = 320 };
        _labels = new TextBlock[steps.Length];
        _status = new TextBlock[steps.Length];
        for (var i = 0; i < steps.Length; i++)
        {
            _labels[i] = new TextBlock { Name = "PART_Step" + i, Text = steps[i], Opacity = 0.6 };
            _status[i] = new TextBlock { Name = "PART_Status" + i, HorizontalAlignment = HorizontalAlignment.Right };
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Children.Add(_labels[i]);
            Grid.SetColumn(_status[i], 1);
            row.Children.Add(_status[i]);
            rows.Children.Add(row);
        }

        _track = new Border { Name = "PART_Track", Height = 4, CornerRadius = new CornerRadius(2), ClipToBounds = true, Margin = new Thickness(0, 6, 0, 0) };
        _track.Bind(Border.BackgroundProperty, _track.GetResourceObservable("Raffica.Brush.Rule"));
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable("Raffica.Brush.Ink"));
        _bar = new Grid { ColumnDefinitions = { new ColumnDefinition(0, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Star) } };
        _bar.Children.Add(fill);
        _track.Child = _bar;

        _failure = new TextBlock { Name = "PART_Failure", TextWrapping = TextWrapping.Wrap, MaxWidth = 440, IsVisible = false };

        _cancelButton = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        _cancelButton.Classes.Add("boxed");
        _retry = new Button { Name = "PART_Retry", Content = PdfStrings.UpdateTryAgain, IsVisible = false };
        _retry.Classes.Add("primary");
        _page = new Button { Name = "PART_Page", Content = PdfStrings.UpdateOpenPage, IsVisible = false };
        _page.Classes.Add("boxed");
        _close = new Button { Name = "PART_Dismiss", Content = ShellStrings.Close, IsVisible = false };
        _close.Classes.Add("boxed");

        Dialog = new Dialog
        {
            Title = PdfStrings.UpdateProgressTitle,
            Content = new StackPanel { Spacing = 8, Children = { rows, _track, _failure } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _cancelButton, _close, _page, _retry } },
        };

        _cancelButton.Click += (_, _) => RequestCancel();
        _retry.Click += (_, _) => Finish(UpdateProgressChoice.TryAgain);
        _page.Click += (_, _) => Finish(UpdateProgressChoice.OpenDownloadPage);
        _close.Click += (_, _) => Finish(UpdateProgressChoice.Close);

        // Esc cancels what can be cancelled and closes a failure; the later steps cannot be stopped, so Esc then does nothing.
        Dialog.CancelRequested += (_, _) =>
        {
            if (_failure.IsVisible)
            {
                Finish(UpdateProgressChoice.Close);
            }
            else
            {
                RequestCancel();
            }
        };
        Apply(UpdateState.None, null, canRetry: false, offersDownloadPage: false);
    }

    /// <summary>The dialog to show in the host.</summary>
    public Dialog Dialog { get; }

    /// <summary>Gets the step shown as current (0 Download, 1 Check, 2 Install, 3 Restart), or -1 while a failure is shown.</summary>
    public int CurrentStep { get; private set; }

    /// <summary>Shows a state: the step it is in, the bytes while downloading, or the failure.</summary>
    /// <param name="state">The state of the update flow.</param>
    /// <param name="failureMessage">The text the installer gave for a failure, or <see langword="null"/> for the sentence of the failure code.</param>
    /// <param name="canRetry">Whether Try again can help after a failure.</param>
    /// <param name="offersDownloadPage">Whether the failure is one the user solves with the download page.</param>
    public void Apply(UpdateState state, string? failureMessage, bool canRetry, bool offersDownloadPage)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_done)
        {
            return;
        }

        if (state.Stage == UpdateStage.Failed)
        {
            ShowFailure(UpdateWording.ForFailure(state, failureMessage), canRetry, offersDownloadPage);
            return;
        }

        _failure.IsVisible = false;
        _track.IsVisible = true;
        _retry.IsVisible = _page.IsVisible = _close.IsVisible = false;
        _cancelButton.IsVisible = true;
        var step = state.Stage switch
        {
            UpdateStage.Verifying => 1,
            UpdateStage.Ready or UpdateStage.Installing => 2,
            UpdateStage.Restarting => 3,
            _ => 0,
        };
        CurrentStep = step;
        _cancelButton.IsEnabled = state.Stage is UpdateStage.None or UpdateStage.Available or UpdateStage.Downloading or UpdateStage.Verifying;
        for (var i = 0; i < _labels.Length; i++)
        {
            _labels[i].FontWeight = i == step ? FontWeight.SemiBold : FontWeight.Normal;
            _labels[i].Opacity = i <= step ? 1 : 0.6;
            // The state of every step is a word next to it (Done, In progress), so the progress never rests on weight or colour alone.
            _status[i].Text = i < step ? PdfStrings.UpdateStepDone : i == step ? PdfStrings.UpdateStepCurrent : string.Empty;
        }

        if (step == 0 && state.File is { Size: > 0 } file)
        {
            _status[0].Text = PdfStrings.Fill(PdfStrings.UpdateProgressBytesFormat, UpdateWording.Megabytes(state.BytesReceived), UpdateWording.Megabytes(file.Size));
            SetFraction((double)state.BytesReceived / file.Size);
        }
        else
        {
            SetFraction(step == 0 ? 0 : 1);
        }
    }

    /// <summary>Presses Cancel (what a click does) while a download or check can still be stopped.</summary>
    public void RequestCancel()
    {
        if (_failure.IsVisible || !_cancelButton.IsEnabled)
        {
            return;
        }

        _cancel();
    }

    private void ShowFailure(string text, bool canRetry, bool offersDownloadPage)
    {
        CurrentStep = -1;
        _failure.Text = text;
        _failure.IsVisible = true;
        _track.IsVisible = false;
        _cancelButton.IsVisible = false;
        _retry.IsVisible = canRetry;
        _page.IsVisible = offersDownloadPage;
        _close.IsVisible = true;
    }

    private void SetFraction(double fraction)
    {
        var clamped = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        _bar.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Star);
        _bar.ColumnDefinitions[1].Width = new GridLength(1 - clamped, GridUnitType.Star);
    }

    private void Finish(UpdateProgressChoice choice)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _finished(choice);
    }
}
