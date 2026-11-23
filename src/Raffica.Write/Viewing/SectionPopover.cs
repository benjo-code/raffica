using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Write.Panes;

namespace Raffica.Write.Viewing;

/// <summary>
/// The content of the popover a section marker opens: the section's title and the lines of its row (where it starts, how its pages are numbered, its page setup), the switches of the row (header, footer, first
/// page), Number from here and Remove marker with what the merge changes when the two setups differ. The words are the pane row's (<see cref="SectionsRowWording"/>) and every control asks the same request the
/// pane's row asks (<see cref="SectionRowAction"/>, run by the window), so the two can never disagree; the pane keeps its own controls because they live as long as the pane and refocus through its rebuilds,
/// while the popover is made when it opens from the row as the document is then and closes before it acts.
/// </summary>
internal static class SectionPopover
{
    /// <summary>The name of the Remove marker button of the popover (tests).</summary>
    internal const string RemoveName = "PART_Chip_Remove";

    /// <summary>The name of the Number from here button of the popover (tests).</summary>
    internal const string NumberName = "PART_Chip_Number";

    /// <summary>The name of the header switch of the popover (tests).</summary>
    internal const string HeaderName = "PART_Chip_Header";

    /// <summary>The name of the footer switch of the popover (tests).</summary>
    internal const string FooterName = "PART_Chip_Footer";

    /// <summary>The name of the first-page switch of the popover (tests).</summary>
    internal const string FirstName = "PART_Chip_First";

    /// <summary>The width of the popover in DIPs (the pane's 256 px column, so the words of the row wrap the same way).</summary>
    internal const double Width = 256;

    /// <summary>Builds the content for a row. <paramref name="blocked"/> is why nothing can change now (null when something can); <paramref name="run"/> runs a request and <paramref name="close"/> closes the popover before it does.</summary>
    internal static StackPanel Build(SectionRow row, SectionRow previous, string? blocked, Func<SectionRowAction, bool> run, Action close)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(close);
        var panel = new StackPanel { Spacing = 4, Width = Width };
        AutomationProperties.SetName(panel, WriteStrings.Fill(WriteStrings.SectionPopoverNameFormat, row.Index + 1));
        panel.Children.Add(Text("sections-title", SectionsRowWording.Title(row)));
        panel.Children.Add(Text("sections-line", SectionsRowWording.Start(row)));
        panel.Children.Add(Text("sections-line", SectionsRowWording.Numbers(row)));
        panel.Children.Add(Text("sections-line", SectionsRowWording.Setup(row)));
        panel.Children.Add(Toggle(row, HeaderName, SectionsRowWording.Header(row), row.HeaderOwn, SectionRowKind.OwnHeader, blocked, run, close));
        panel.Children.Add(Toggle(row, FooterName, SectionsRowWording.Footer(row), row.FooterOwn, SectionRowKind.OwnFooter, blocked, run, close));
        panel.Children.Add(Toggle(row, FirstName, WriteStrings.SectionFirstPage, row.FirstPageDifferent, SectionRowKind.FirstPage, blocked, run, close));

        var number = Command(row, NumberName, WriteStrings.SectionNumberFrom, blocked ?? (row.CanNumber ? null : WriteStrings.SectionNumberNoParagraph));
        number.Click += (_, _) => Act(row, SectionRowKind.NumberFrom, true, run, close);
        panel.Children.Add(number);

        var remove = Command(row, RemoveName, WriteStrings.SectionRemoveMarker, blocked);
        remove.Click += (_, _) => Act(row, SectionRowKind.RemoveMarker, true, run, close);
        panel.Children.Add(remove);
        var tip = SectionsRowWording.RemoveTip(row, previous);
        if (row.DiffersFrom(previous))
        {
            // What the merge changes is on the page before the button is pressed, as in the pane.
            panel.Children.Add(Text("sections-warn", tip));
        }

        AutomationProperties.SetItemStatus(remove, tip);
        return panel;
    }

    private static CheckBox Toggle(SectionRow row, string name, string words, bool on, SectionRowKind kind, string? blocked, Func<SectionRowAction, bool> run, Action close)
    {
        var box = new CheckBox { Name = name, Content = words, IsChecked = on };
        AutomationProperties.SetName(box, SectionsRowWording.ButtonName(words, row));
        Rule(box, blocked);
        box.Click += (_, _) => Act(row, kind, !on, run, close);
        return box;
    }

    private static Button Command(SectionRow row, string name, string words, string? disabledBecause)
    {
        var button = new Button { Name = name, Content = words, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Classes.Add("boxed");
        button.Classes.Add("wrap");
        AutomationProperties.SetName(button, SectionsRowWording.ButtonName(words, row));
        Rule(button, disabledBecause);
        return button;
    }

    // A disabled control says why on the mouse and to assistive technology, as in the pane.
    private static void Rule(Control control, string? why)
    {
        control.IsEnabled = why is null;
        ToolTip.SetTip(control, why);
        ToolTip.SetShowOnDisabled(control, true);
        AutomationProperties.SetHelpText(control, why ?? string.Empty);
    }

    // The popover closes first (what it shows is about to be out of date), then the request runs; a request that no longer fits the document is refused and says so (the window's note).
    private static void Act(SectionRow row, SectionRowKind kind, bool on, Func<SectionRowAction, bool> run, Action close)
    {
        close();
        _ = run(new SectionRowAction(kind, row.Index, row.Identity, on));
    }

    private static TextBlock Text(string style, string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add(style);
        AutomationProperties.SetName(block, text);
        return block;
    }
}
