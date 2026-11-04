using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Printing;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Printing;

/// <summary>
/// The card of a running print job: the document, "Page 2 of 4", a thin progress bar and Cancel. It does not block the window (it sits in the
/// notification slot of the frame, not in a dialog), so the user can go on reading. Only the colours of the shell are used.
/// </summary>
internal sealed class PrintJobCard : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _status;
    private readonly Grid _bar;
    private readonly Button _cancel;
    private readonly Action _cancelRequested;
    private double _fraction;

    /// <param name="documentName">The name of the document being printed.</param>
    /// <param name="cancel">Called once when the user cancels; the card then says that it is cancelling.</param>
    public PrintJobCard(string documentName, Action cancel)
    {
        ArgumentNullException.ThrowIfNull(documentName);
        ArgumentNullException.ThrowIfNull(cancel);
        _cancelRequested = cancel;
        Name = "PART_PrintJob";
        Padding = new Thickness(16, 8);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));

        _title = new TextBlock { Name = "PART_Title", Text = PdfStrings.Fill(PdfStrings.PrintJobTitleFormat, documentName), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _status = new TextBlock { Name = "PART_Status", Text = PdfStrings.PrintJobPreparing, FontSize = 12 };
        _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("Raffica.Brush.Text.Secondary"));

        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), ClipToBounds = true, Margin = new Thickness(0, 6, 0, 0) };
        track.Bind(BackgroundProperty, track.GetResourceObservable("Raffica.Brush.Rule"));
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.Bind(BackgroundProperty, fill.GetResourceObservable("Raffica.Brush.Ink"));
        _bar = new Grid { ColumnDefinitions = { new ColumnDefinition(0, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Star) } };
        _bar.Children.Add(fill);
        track.Child = _bar;

        _cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        _cancel.Classes.Add("boxed");
        AutomationProperties.SetName(_cancel, ShellStrings.Cancel);
        _cancel.Click += (_, _) => RequestCancel();

        var text = new StackPanel { Children = { _title, _status, track } };
        var layout = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(GridLength.Auto) } };
        layout.Children.Add(text);
        Grid.SetColumn(_cancel, 1);
        layout.Children.Add(_cancel);
        Child = layout;
        AutomationProperties.SetName(this, _title.Text);
    }

    /// <summary>The first line: "Printing plan.pdf".</summary>
    public string Title => _title.Text ?? string.Empty;

    /// <summary>The second line: "Page 2 of 4", "Preparing the job" or "Cancelling".</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>How far the job is, 0 to 1.</summary>
    public double Fraction => _fraction;

    /// <summary>Shows how far the job is. Call on the UI thread.</summary>
    public void Report(PrintProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!_cancel.IsEnabled)
        {
            // After Cancel the line stays "Cancelling" until the job ends.
            return;
        }

        _fraction = progress.Total > 0 ? Math.Clamp((double)progress.Done / progress.Total, 0, 1) : 0;
        _bar.ColumnDefinitions[0].Width = new GridLength(_fraction, GridUnitType.Star);
        _bar.ColumnDefinitions[1].Width = new GridLength(1 - _fraction, GridUnitType.Star);
        _status.Text = PdfStrings.Fill(PdfStrings.PrintJobProgressFormat, Math.Min(progress.Done, progress.Total), progress.Total);
        AutomationProperties.SetName(_bar, _status.Text);
    }

    private void RequestCancel()
    {
        if (!_cancel.IsEnabled)
        {
            return;
        }

        _cancel.IsEnabled = false;
        _status.Text = PdfStrings.PrintJobCancelling;
        _cancelRequested();
    }
}
