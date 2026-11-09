using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Printing;

/// <summary>What the print dialog of the app needs.</summary>
/// <param name="PageCount">The pages of the document.</param>
/// <param name="CurrentPage">The page being looked at, 0-based.</param>
/// <param name="DocumentName">The name of the file.</param>
/// <param name="Printers">The printers of the system, at least one.</param>
/// <param name="Defaults">What the dialog starts with (the printer used last, the scaling of the settings).</param>
/// <param name="SupportsDuplex">Asks whether a printer prints on both sides (may take a moment; the dialog does not wait for it).</param>
internal sealed record PrintCardInput(
    int PageCount,
    int CurrentPage,
    string DocumentName,
    IReadOnlyList<PrinterInfo> Printers,
    PrintSettings Defaults,
    Func<string, Task<bool>> SupportsDuplex);

/// <summary>
/// The print dialog for systems without one of their own (Linux): printer, pages, copies, collate, scaling, two-sided (only for a printer
/// that can) and orientation. Print is the primary button; Enter prints, Esc cancels. A wrong range or number of copies keeps the card open
/// and says what is wrong. The page numbers are the ones the user sees, counted from 1.
/// </summary>
internal static class PrintCard
{
    private const int RangeIndex = 2;

    private static readonly PrintDuplex[] Duplexes = [PrintDuplex.Default, PrintDuplex.Off, PrintDuplex.LongEdge, PrintDuplex.ShortEdge];
    private static readonly PrintOrientation[] Orientations = [PrintOrientation.Default, PrintOrientation.Portrait, PrintOrientation.Landscape];
    private static readonly PrintPageChoice[] Choices = [PrintPageChoice.All, PrintPageChoice.CurrentPage, PrintPageChoice.Range];

    /// <param name="input">What to show.</param>
    /// <param name="done">The choices, or null when the user cancelled.</param>
    public static Dialog Create(PrintCardInput input, Action<PrintSettings?> done)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Printers.Count, 1);
        var defaults = input.Defaults.Normalized();

        var printer = new SelectBox
        {
            Name = "PART_Printer",
            ItemsSource = input.Printers.Select(p => string.IsNullOrWhiteSpace(p.Description) ? p.Name : $"{p.Name} ({p.Description})").ToArray(),
            SelectedIndex = InitialPrinter(input.Printers, defaults.Printer),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(printer, PdfStrings.PrintCardPrinter);

        var pages = new SegmentedControl
        {
            Name = "PART_Pages",
            ItemsSource = new[] { PdfStrings.PrintPagesAll, PdfStrings.PrintPagesCurrent, PdfStrings.PrintPagesRange },
            SelectedIndex = Math.Max(0, Array.IndexOf(Choices, defaults.Pages)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(pages, PdfStrings.PrintCardPages);
        var range = new TextBox { Name = "PART_Range", Text = defaults.Range, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(range, PdfStrings.PrintRangeLabel);
        range.IsEnabled = pages.SelectedIndex == RangeIndex;
        pages.SelectionChanged += (_, _) => range.IsEnabled = pages.SelectedIndex == RangeIndex;

        var copies = new TextBox { Name = "PART_Copies", Text = defaults.Copies.ToString(CultureInfo.InvariantCulture), MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(copies, PdfStrings.PrintCardCopies);
        var collate = new CheckBox { Name = "PART_Collate", Content = PdfStrings.PrintCardCollate, IsChecked = defaults.Collate };
        AutomationProperties.SetName(collate, PdfStrings.PrintCardCollate);

        var scaling = new SegmentedControl
        {
            Name = "PART_Scaling",
            ItemsSource = new[] { PdfStrings.PrintScalingFit, PdfStrings.PrintScalingActual },
            SelectedIndex = defaults.Scaling == PrintScaling.ActualSize ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(scaling, PdfStrings.PrintCardScaling);

        var duplex = new SelectBox
        {
            Name = "PART_Duplex",
            ItemsSource = new[] { PdfStrings.PrintDuplexDefault, PdfStrings.PrintDuplexOff, PdfStrings.PrintDuplexLong, PdfStrings.PrintDuplexShort },
            SelectedIndex = Math.Max(0, Array.IndexOf(Duplexes, defaults.Duplex)),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 240,
        };
        AutomationProperties.SetName(duplex, PdfStrings.PrintCardDuplex);
        var duplexRow = Row(PdfStrings.PrintCardDuplex, duplex);
        duplexRow.Name = "PART_DuplexRow";
        duplexRow.IsVisible = false;

        var orientation = new SelectBox
        {
            Name = "PART_Orientation",
            ItemsSource = new[] { PdfStrings.PrintOrientationDefault, PdfStrings.PrintOrientationPortrait, PdfStrings.PrintOrientationLandscape },
            SelectedIndex = Math.Max(0, Array.IndexOf(Orientations, defaults.Orientation)),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 240,
        };
        AutomationProperties.SetName(orientation, PdfStrings.PrintCardOrientation);

        var hint = new TextBlock { Name = "PART_Hint", IsVisible = false, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Danger"));

        var name = new TextBlock { Text = input.DocumentName, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var body = new StackPanel
        {
            Spacing = 10,
            MinWidth = 420,
            Children =
            {
                name,
                Row(PdfStrings.PrintCardPrinter, printer),
                Row(PdfStrings.PrintCardPages, new StackPanel { Spacing = 6, Children = { pages, range } }),
                Row(PdfStrings.PrintCardCopies, new StackPanel { Spacing = 6, Children = { copies, collate } }),
                Row(PdfStrings.PrintCardScaling, scaling),
                duplexRow,
                Row(PdfStrings.PrintCardOrientation, orientation),
                hint,
            },
        };

        var print = new Button { Name = "PART_Print", Content = PdfStrings.CmdPrint };
        print.Classes.Add("primary");
        AutomationProperties.SetName(print, PdfStrings.CmdPrint);
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        AutomationProperties.SetName(cancel, ShellStrings.Cancel);
        var dialog = new Dialog
        {
            Title = PdfStrings.PrintCardTitle,
            Content = body,
            Footer = CardFooter.Build(print, null, cancel),
        };

        // Whether the printer can print on both sides is asked in the background: the row appears when the answer is yes. A late answer for
        // a printer that is no longer chosen is dropped.
        var duplexAsked = 0;
        async Task UpdateDuplexAsync()
        {
            var token = ++duplexAsked;
            duplexRow.IsVisible = false;
            duplex.SelectedIndex = 0;
            var supported = false;
            try
            {
                supported = await input.SupportsDuplex(input.Printers[Math.Max(0, printer.SelectedIndex)].Name);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A printer that cannot be asked is treated as one-sided.
            }

            if (token == duplexAsked)
            {
                duplexRow.IsVisible = supported;
            }
        }

        printer.SelectionChanged += (_, _) => _ = UpdateDuplexAsync();
        _ = UpdateDuplexAsync();

        void Confirm()
        {
            var choice = Choices[Math.Max(0, pages.SelectedIndex)];
            var rangeText = (range.Text ?? string.Empty).Trim();
            if (choice == PrintPageChoice.Range && !IsValidRange(rangeText, input.PageCount))
            {
                Show(hint, PdfStrings.Fill(PdfStrings.PrintRangeInvalidFormat, input.PageCount), range);
                return;
            }

            if (!int.TryParse(copies.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < PrintSettings.MinCopies || count > PrintSettings.MaxCopies)
            {
                Show(hint, PdfStrings.PrintCopiesInvalid, copies);
                return;
            }

            done(new PrintSettings
            {
                Printer = input.Printers[Math.Max(0, printer.SelectedIndex)].Name,
                Pages = choice,
                Range = choice == PrintPageChoice.Range ? rangeText : string.Empty,
                Copies = count,
                Collate = collate.IsChecked == true,
                Scaling = scaling.SelectedIndex == 1 ? PrintScaling.ActualSize : PrintScaling.FitToPaper,
                Duplex = duplexRow.IsVisible ? Duplexes[Math.Max(0, duplex.SelectedIndex)] : PrintDuplex.Default,
                Orientation = Orientations[Math.Max(0, orientation.SelectedIndex)],
            });
        }

        print.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => done(null);
        dialog.CancelRequested += (_, _) => done(null);

        // Enter anywhere that does not take the key itself (a field, a segmented choice) prints; a button or an open list keeps its own Enter.
        dialog.KeyDown += (_, e) =>
        {
            if (!e.Handled && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        };
        return dialog;
    }

    internal static bool IsValidRange(string text, int pageCount)
    {
        try
        {
            return PageRange.Parse(text, pageCount).Count > 0;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int InitialPrinter(IReadOnlyList<PrinterInfo> printers, string wanted)
    {
        for (var i = 0; i < printers.Count; i++)
        {
            if (wanted.Length > 0 && string.Equals(printers[i].Name, wanted, StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (var i = 0; i < printers.Count; i++)
        {
            if (printers[i].IsDefault)
            {
                return i;
            }
        }

        return 0;
    }

    private static void Show(TextBlock hint, string text, Control focus)
    {
        hint.Text = text;
        hint.IsVisible = true;
        focus.Focus();
    }

    private static Grid Row(string label, Control control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Raffica.Brush.Text.Secondary"));
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(120, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star) } };
        row.Children.Add(text);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }
}
