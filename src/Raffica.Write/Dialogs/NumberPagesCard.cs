using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Number pages from here: where the count starts (0 to 99999, or "continue" from the section before), the format (1 2 3, i ii iii, I II III, a b c, A B C) and an optional text before the number in the footer
/// ("A-" gives "A-1"). The card says when the command starts a new page at the caret ("This starts a new page here") and which later sections follow the new numbering. The default is the one-click answer
/// of the picker of the spec: start at 1, 1 2 3, no prefix. A wrong entry shows a hint and keeps the card open; Enter confirms, Esc cancels.
/// </summary>
internal static class NumberPagesCard
{
    private static readonly PageNumberFormat[] s_formats = [PageNumberFormat.Arabic, PageNumberFormat.LowerRoman, PageNumberFormat.UpperRoman, PageNumberFormat.LowerLetter, PageNumberFormat.UpperLetter];

    /// <param name="context">The facts of the caret's place and whether the command starts a new page there.</param>
    /// <param name="done">The numbering chosen, or null when the user cancelled.</param>
    public static Dialog Create(NumberPagesContext context, Action<PageNumbering?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);

        var children = new List<Control>();
        if (context.StartsNewPage)
        {
            children.Add(new TextBlock { Name = "PART_PageStartLine", Text = WriteStrings.NumPagesPageStartLine, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        }

        var start = new TextBox { Name = "PART_Start", Text = "1", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(start, WriteStrings.NumPagesStartLabel);
        var keep = new CheckBox { Name = "PART_Continue", Content = WriteStrings.NumPagesContinue, IsChecked = false };
        keep.IsCheckedChanged += (_, _) => start.IsEnabled = keep.IsChecked != true;
        children.Add(Field(WriteStrings.NumPagesStartLabel, start));
        children.Add(keep);

        var format = new SelectBox { Name = "PART_Format", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = FormatWords(), SelectedIndex = 0 };
        AutomationProperties.SetName(format, WriteStrings.NumPagesFormatLabel);
        children.Add(Field(WriteStrings.NumPagesFormatLabel, format));

        var prefix = new TextBox { Name = "PART_Prefix", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(prefix, WriteStrings.NumPagesPrefixLabel);
        children.Add(Field(WriteStrings.NumPagesPrefixLabel, prefix));
        var prefixHint = new TextBlock { Name = "PART_PrefixHint", Text = WriteStrings.NumPagesPrefixHint, TextWrapping = TextWrapping.Wrap };
        prefixHint.Classes.Add("secondary");
        children.Add(prefixHint);

        if (FollowingLines(context.Facts) is { Count: > 0 } following)
        {
            var lines = new StackPanel { Name = "PART_Following", Spacing = 2 };
            foreach (var line in following)
            {
                lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
            }

            children.Add(lines);
        }

        var hint = new TextBlock { Name = "PART_Hint", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        ValidationHint.Mark(hint);
        children.Add(hint);

        var ok = new Button { Name = "PART_Ok", Content = WriteStrings.NumPagesOk };
        ok.Classes.Add("primary");
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        var body = new StackPanel { Spacing = 10, MinWidth = 340 };
        body.Children.AddRange(children);
        var dialog = new Dialog { Title = WriteStrings.NumPagesTitle, Content = body, Footer = CardFooter.Build(ok, null, cancel) };

        var finished = false;
        void Finish(PageNumbering? answer)
        {
            if (!finished)
            {
                finished = true;
                done(answer);
            }
        }

        void Confirm()
        {
            var problem = TryRead(start.Text, keep.IsChecked == true, format.SelectedIndex, prefix.Text, out var numbering);
            if (problem is null)
            {
                Finish(numbering);
                return;
            }

            hint.Text = problem == Problem.Start ? WriteStrings.NumPagesInvalidStart : WriteStrings.NumPagesInvalidPrefix;
            hint.IsVisible = true;
            var field = problem == Problem.Start ? start : prefix;
            field.Focus();
            field.SelectAll();
        }

        void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        }

        start.KeyDown += OnKey;
        prefix.KeyDown += OnKey;
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => Finish(null);
        dialog.CancelRequested += (_, _) => Finish(null);
        dialog.AttachedToVisualTree += (_, _) => start.SelectAll();
        return dialog;
    }

    private enum Problem
    {
        Start,
        Prefix,
    }

    // Reads the entries; null when they are right (numbering is then set), else the field that is not.
    private static Problem? TryRead(string? startText, bool keepCount, int formatIndex, string? prefix, out PageNumbering? numbering)
    {
        numbering = null;
        int? first = null;
        if (!keepCount)
        {
            if (!int.TryParse(startText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > PageNumbering.MaximumStart)
            {
                return Problem.Start;
            }

            first = value;
        }

        var text = prefix ?? string.Empty;
        if (!PageNumbering.IsValidPrefix(text))
        {
            return Problem.Prefix;
        }

        numbering = new PageNumbering(first, s_formats[Math.Clamp(formatIndex, 0, s_formats.Length - 1)], text);
        return null;
    }

    private static string[] FormatWords() =>
    [
        WriteStrings.NumPagesFormatArabic, WriteStrings.NumPagesFormatLowerRoman, WriteStrings.NumPagesFormatUpperRoman, WriteStrings.NumPagesFormatLowerLetter, WriteStrings.NumPagesFormatUpperLetter,
    ];

    private static StackPanel Field(string label, Control field) => new() { Spacing = 4, Children = { new TextBlock { Text = label }, field } };

    // The later sections that take the new numbering: the new section starts at the caret's block (or is the caret's own section when the block starts it), so the sections after it are the following ones;
    // those that set their own numbering or footer keep it and the card says how many.
    private static List<string> FollowingLines(SectionFacts facts)
    {
        var lines = new List<string>();
        if (facts.FollowingSections <= 0)
        {
            return lines;
        }

        var first = (facts.AtSectionStart ? facts.Section : facts.Section + 1) + 2;
        var numbers = SectionNumberList.Describe(Enumerable.Range(first, facts.FollowingSections));
        lines.Add(WriteStrings.Fill(facts.FollowingSections == 1 ? WriteStrings.NumPagesFollowOne : WriteStrings.NumPagesFollowMany, numbers));
        if (facts.FollowingWithOwnSetting > 0)
        {
            lines.Add(facts.FollowingWithOwnSetting == 1 ? WriteStrings.NumPagesOwnOne : WriteStrings.Fill(WriteStrings.NumPagesOwnMany, facts.FollowingWithOwnSetting));
        }

        return lines;
    }
}
