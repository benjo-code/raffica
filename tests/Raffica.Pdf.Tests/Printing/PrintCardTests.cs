using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Printing;
using Raffica.Pdf.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Printing;

public sealed class PrintCardTests
{
    private static readonly PrinterInfo[] Two =
    [
        new("Office", "Second floor", false),
        new("CUPS-PDF", null, true),
    ];

    private static PrintCardInput Input(Func<string, Task<bool>>? duplex = null, PrintSettings? defaults = null) =>
        new(PageCount: 10, CurrentPage: 3, DocumentName: "plan.pdf", Printers: Two, Defaults: defaults ?? new PrintSettings(), SupportsDuplex: duplex ?? (_ => Task.FromResult(false)));

    private static (PrintHost Host, Dialog Dialog) Show(PrintCardInput input, List<PrintSettings?> results)
    {
        var host = new PrintHost();
        var dialog = PrintCard.Create(input, results.Add);
        host.Host.Show(dialog);
        PrintHost.Pump();
        return (host, dialog);
    }

    private static void Click(Dialog dialog, string name) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void RangeAndCopiesAreValidated()
    {
        var results = new List<PrintSettings?>();
        var (host, dialog) = Show(Input(), results);
        using var _ = host;
        var pages = PrintHost.Find<SegmentedControl>(dialog, "PART_Pages");
        var range = PrintHost.Find<TextBox>(dialog, "PART_Range");
        var copies = PrintHost.Find<TextBox>(dialog, "PART_Copies");
        var hint = PrintHost.Find<TextBlock>(dialog, "PART_Hint");

        // The range counts only when the choice is "Range"; the field is off otherwise.
        Assert.False(range.IsEnabled);
        Assert.False(hint.IsVisible);
        pages.SelectedIndex = 2;
        PrintHost.Pump();
        Assert.True(range.IsEnabled);

        foreach (var wrong in new[] { "", "  ", "0", "11", "5-3", "abc", "1-", "1,,2", "99999999999" })
        {
            range.Text = wrong;
            Click(dialog, "PART_Print");
            Assert.Empty(results);
            Assert.True(hint.IsVisible, $"range '{wrong}' must show the hint");
            Assert.Contains("10", hint.Text, StringComparison.Ordinal);
        }

        range.Text = "1-3, 5";
        foreach (var wrong in new[] { "", "0", "1000", "-1", "abc", "2.5", "99999999999" })
        {
            copies.Text = wrong;
            Click(dialog, "PART_Print");
            Assert.Empty(results);
            Assert.True(hint.IsVisible, $"copies '{wrong}' must show the hint");
        }

        copies.Text = " 2 ";
        Click(dialog, "PART_Print");

        var settings = Assert.Single(results)!;
        Assert.Equal(PrintPageChoice.Range, settings.Pages);
        Assert.Equal("1-3, 5", settings.Range);
        Assert.Equal(2, settings.Copies);
        Assert.Equal("CUPS-PDF", settings.Printer);
        host.Host.Close();
    }

    [AvaloniaFact]
    public void TheChoicesGoIntoTheSettings()
    {
        var results = new List<PrintSettings?>();
        var (host, dialog) = Show(Input(duplex: _ => Task.FromResult(true), defaults: new PrintSettings { Printer = "Office", Scaling = PrintScaling.ActualSize }), results);
        using var _ = host;
        var printer = PrintHost.Find<SelectBox>(dialog, "PART_Printer");
        Assert.Equal(0, printer.SelectedIndex);

        PrintHost.Find<SegmentedControl>(dialog, "PART_Pages").SelectedIndex = 1;
        PrintHost.Find<CheckBox>(dialog, "PART_Collate").IsChecked = false;
        PrintHost.Find<SelectBox>(dialog, "PART_Orientation").SelectedIndex = 2;
        PrintHost.Pump();
        PrintHost.Find<SelectBox>(dialog, "PART_Duplex").SelectedIndex = 2;
        Click(dialog, "PART_Print");

        var settings = Assert.Single(results)!;
        Assert.Equal("Office", settings.Printer);
        Assert.Equal(PrintPageChoice.CurrentPage, settings.Pages);
        Assert.False(settings.Collate);
        Assert.Equal(PrintScaling.ActualSize, settings.Scaling);
        Assert.Equal(PrintOrientation.Landscape, settings.Orientation);
        Assert.Equal(PrintDuplex.LongEdge, settings.Duplex);
        host.Host.Close();
    }

    [AvaloniaFact]
    public async Task DuplexRowHiddenWhenUnsupported()
    {
        var results = new List<PrintSettings?>();
        var supported = new Dictionary<string, TaskCompletionSource<bool>>
        {
            ["Office"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["CUPS-PDF"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var asked = new List<string>();
        var (host, dialog) = Show(Input(name =>
        {
            asked.Add(name);
            return supported[name].Task;
        }), results);
        using var _ = host;
        var row = PrintHost.Find<Control>(dialog, "PART_DuplexRow");
        var printer = PrintHost.Find<SelectBox>(dialog, "PART_Printer");

        // The default printer is asked; until the answer is in (and when it is no), the row is not there.
        Assert.Equal(["CUPS-PDF"], asked);
        Assert.False(row.IsVisible);
        supported["CUPS-PDF"].SetResult(false);
        await PrintHost.UntilAsync(() => !row.IsVisible);
        Assert.False(row.IsVisible);

        // Another printer that can do it shows the row; going back hides it and forgets the choice.
        printer.SelectedIndex = 0;
        supported["Office"].SetResult(true);
        await PrintHost.UntilAsync(() => row.IsVisible);
        PrintHost.Find<SelectBox>(dialog, "PART_Duplex").SelectedIndex = 3;
        printer.SelectedIndex = 1;
        await PrintHost.UntilAsync(() => !row.IsVisible);
        Click(dialog, "PART_Print");

        Assert.Equal(PrintDuplex.Default, Assert.Single(results)!.Duplex);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(PrintHost.Find<SelectBox>(dialog, "PART_Duplex"))));
        host.Host.Close();
    }

    [AvaloniaFact]
    public void EnterPrintsEscCancels()
    {
        var results = new List<PrintSettings?>();
        var (host, dialog) = Show(Input(), results);
        using var _ = host;
        var copies = PrintHost.Find<TextBox>(dialog, "PART_Copies");

        // Enter in a field prints with what the card shows.
        copies.Focus();
        copies.Text = "3";
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        host.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        var settings = Assert.Single(results)!;
        Assert.Equal(3, settings.Copies);

        // Enter with a wrong value keeps the card open and says why.
        copies.Text = "0";
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        host.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Single(results);
        Assert.True(PrintHost.Find<TextBlock>(dialog, "PART_Hint").IsVisible);

        // Esc and the Cancel button answer with null.
        host.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(2, results.Count);
        Assert.Null(results[1]);
        Click(dialog, "PART_Cancel");
        Assert.Equal(3, results.Count);
        Assert.Null(results[2]);
        host.Host.Close();
    }

    [AvaloniaFact]
    public void EveryControlHasANameForTheScreenReader()
    {
        var (host, dialog) = Show(Input(), []);
        using var _ = host;
        var controls = dialog.GetVisualDescendants().OfType<Control>().Where(c => c is TextBox or SelectBox or SegmentedControl or CheckBox).ToList();

        Assert.NotEmpty(controls);
        Assert.All(controls, c => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)), c.Name));
        host.Host.Close();
    }
}
