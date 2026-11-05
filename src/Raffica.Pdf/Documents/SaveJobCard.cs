using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Documents;

/// <summary>
/// The card of a running save: the document, a thin progress bar and Cancel. Like the card of a print job it sits in the notification slot of
/// the frame and does not block the window. Only the colours of the shell are used.
/// </summary>
internal sealed class SaveJobCard : Border
{
    private readonly TextBlock _title;
    private readonly Grid _bar;
    private readonly Button _cancel;
    private readonly Action _cancelRequested;
    private double _fraction;

    /// <param name="documentName">The name of the document being saved.</param>
    /// <param name="cancel">Called once when the user cancels; the card then says that it is cancelling.</param>
    public SaveJobCard(string documentName, Action cancel)
    {
        ArgumentNullException.ThrowIfNull(documentName);
        ArgumentNullException.ThrowIfNull(cancel);
        _cancelRequested = cancel;
        Name = "PART_SaveJob";
        Padding = new Thickness(16, 8);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));

        _title = new TextBlock { Name = "PART_Title", Text = PdfStrings.Fill(PdfStrings.SaveJobTitleFormat, documentName), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
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

        var layout = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(GridLength.Auto) } };
        layout.Children.Add(new StackPanel { Children = { _title, track } });
        Grid.SetColumn(_cancel, 1);
        layout.Children.Add(_cancel);
        Child = layout;
        AutomationProperties.SetName(this, _title.Text);
    }

    /// <summary>The first line: "Saving plan.pdf".</summary>
    public string Title => _title.Text ?? string.Empty;

    /// <summary>How far the save is, 0 to 1.</summary>
    public double Fraction => _fraction;

    /// <summary>Shows how far the save is (0 to 1). Call on the UI thread.</summary>
    public void Report(double fraction)
    {
        if (!_cancel.IsEnabled)
        {
            return;
        }

        _fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        _bar.ColumnDefinitions[0].Width = new GridLength(_fraction, GridUnitType.Star);
        _bar.ColumnDefinitions[1].Width = new GridLength(1 - _fraction, GridUnitType.Star);
    }

    /// <summary>Presses Cancel (what a click does).</summary>
    public void RequestCancel()
    {
        if (!_cancel.IsEnabled)
        {
            return;
        }

        _cancel.IsEnabled = false;
        _title.Text = PdfStrings.PrintJobCancelling;
        _cancelRequested();
    }
}
