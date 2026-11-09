using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Redaction;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

/// <summary>The redact pane is 256 px wide (field code 7): in German no text of a check box, button or toggle may be wider than its control or cut off (07-UI-11).</summary>
public sealed class RedactPaneGermanTests : IDisposable
{
    private const double PaneWidth = 256;

    private readonly CultureInfo _pdf = PdfStrings.Culture;
    private readonly CultureInfo _shell = ShellStrings.Culture;

    public void Dispose()
    {
        PdfStrings.Culture = _pdf;
        ShellStrings.Culture = _shell;
    }

    private static Window Host(RedactPane pane)
    {
        var window = new Window { Width = PaneWidth, Height = 6000, Content = pane };
        window.Show();
        ViewFixture.Pump();
        window.UpdateLayout();
        return window;
    }

    // The scrolling parts of the pane clip what is below their height; the check looks at every row, so they are let grow here.
    private static void Unclip(Window window)
    {
        foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Name is "PART_LookHost" or "PART_FindHost"))
        {
            scroll.MaxHeight = double.PositiveInfinity;
        }

        foreach (var list in window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Name == "PART_FindList"))
        {
            list.MaxHeight = double.PositiveInfinity;
        }

        ViewFixture.Pump();
        window.UpdateLayout();
    }

    private static IEnumerable<string> Offenders(Window window)
    {
        // The text of a hit row or of a mark row is the document's own (an e-mail address, an IBAN): a word wider than the pane has no break (the hit row wraps it
        // in the middle, the mark row ends in an ellipsis on purpose). That is data, not a label; the rows are held to the pane below.
        var data = window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Name is "PART_FindList" or "PART_List")
            .SelectMany(l => l.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToHashSet();
        foreach (var clipped in LabelFit.FindClipped(window))
        {
            if (!data.Any(t => clipped.StartsWith(t + " (", StringComparison.Ordinal)))
            {
                yield return clipped;
            }
        }

        // The controls themselves stay inside the pane, and a check box, button or toggle that wraps its label grows in height instead of in width.
        foreach (var control in window.GetVisualDescendants().OfType<ContentControl>().Where(c => c is Button or CheckBox or ToggleButton && c.IsEffectivelyVisible))
        {
            if (control.TranslatePoint(default, window) is { } origin && origin.X + control.Bounds.Width > PaneWidth + 0.5)
            {
                yield return $"{control.Name ?? control.GetType().Name} is {origin.X + control.Bounds.Width} wide from the left edge";
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task NoTextOfTheLookRowsOrTheFindPanelIsWiderThanItsControl(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        PdfStrings.Culture = culture;
        ShellStrings.Culture = culture;
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var pane = new RedactPane(f.Session, f.View);
        using var window = new Disposer(Host(pane));
        pane.LookToggle.IsChecked = true;
        pane.ToggleFind();
        var panel = pane.FindPanel!;
        ViewFixture.Pump();

        // A pattern search with hits, failed checksums and pictures: every button and box of the panel is on screen.
        panel.PatternsToggle.IsChecked = true;
        Assert.True(await panel.SearchAsync());
        panel.NotSearchedToggle.IsChecked = true;
        pane.ImageBox.SelectedIndex = (int)RedactionImageTreatment.Blur;
        pane.RasterPageBox.IsChecked = true;
        pane.OverlayBox.IsEnabled = true;
        ViewFixture.Pump();
        window.Value.UpdateLayout();

        Assert.True(panel.AddFailedButton.IsVisible);
        Unclip(window.Value);
        var offenders = Offenders(window.Value).ToList();
        Assert.True(offenders.Count == 0, string.Join(" | ", offenders));

        // With marks: the counter, the list of the marks (the document's own words are data again), the contrast note and the Apply button.
        panel.MarkAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Unclip(window.Value);
        Assert.True(pane.ApplyButton.IsVisible);
        offenders = Offenders(window.Value).ToList();
        Assert.True(offenders.Count == 0, string.Join(" | ", offenders));

        // The term mode with its two boxes.
        panel.PatternsToggle.IsChecked = false;
        ViewFixture.Pump();
        window.Value.UpdateLayout();
        offenders = Offenders(window.Value).ToList();
        Assert.True(offenders.Count == 0, string.Join(" | ", offenders));
    }

    private sealed class Disposer(Window window) : IDisposable
    {
        public Window Value { get; } = window;

        public void Dispose() => Value.Close();
    }
}
