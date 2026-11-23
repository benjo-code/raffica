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
using Raffica.Write.Model;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Page setup: paper size (A4, Letter, A3, A5, Legal or custom), orientation, margins (Normal, Narrow, Moderate, Wide or custom), columns (the count, the spacing and a line between) and where it applies (this
/// section, the whole document or from here on). Lengths are typed in centimetres or inches by the region rule of the page presets. Only the parts the user changed are written. Custom width and height are the two
/// sides of the upright page (the orientation turns it). A section whose columns have different widths is made equal by any column write, so the card offers only the count for it and says so. "From here on"
/// starts a new page (new columns start on the same page) and turns "different first page" off for the new section; the card says so. A wrong entry shows a hint and keeps the card open; nothing changed
/// closes the card with no answer; Enter confirms, Esc cancels.
/// </summary>
internal static class PageSetupCard
{
    // Twips of the smallest and largest page side, the room the text keeps and the narrowest column the card accepts.
    internal const int MinimumSide = 720;
    internal const int MaximumSide = 31680;
    internal const int MinimumText = 720;
    internal const int MinimumColumn = 360;
    internal const int MaximumColumns = 45;
    internal const int MaximumColumnSpace = 14400;
    private const double TwipsPerCentimetre = 1440 / 2.54;

    private static readonly string[] s_papers = ["A4", "Letter", "A3", "A5", "Legal"];
    private static readonly string[] s_marginPresets = ["Normal", "Narrow", "Moderate", "Wide"];
    private static readonly ApplyScope[] s_scopes = [ApplyScope.ThisSection, ApplyScope.WholeDocument, ApplyScope.FromHere];

    /// <param name="context">The section's setup and what the card needs to know about the document.</param>
    /// <param name="done">The change and where it applies, or null when the user cancelled or changed nothing.</param>
    public static Dialog Create(PageSetupContext context, Action<PageSetupRequest?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);
        var current = context.Current;
        var unit = context.Inches ? WriteStrings.PsUnitInch : WriteStrings.PsUnitCm;

        // Paper.
        var upright = (Width: Math.Min(current.WidthTwips, current.HeightTwips), Height: Math.Max(current.WidthTwips, current.HeightTwips));
        var size = Select("PART_Size", WriteStrings.PsSizeLabel, [.. s_papers, WriteStrings.PsCustom]);
        var width = new Length("PART_Width", WriteStrings.PsWidthFormat, unit, context.Inches);
        var height = new Length("PART_Height", WriteStrings.PsHeightFormat, unit, context.Inches);
        width.Set(upright.Width);
        height.Set(upright.Height);
        size.SelectedIndex = PaperIndexOf(upright);
        var orientation = Select("PART_Orientation", WriteStrings.PsOrientationLabel, [WriteStrings.OrientationPortrait, WriteStrings.OrientationLandscape]);
        orientation.SelectedIndex = current.Landscape ? 1 : 0;

        // Margins.
        var margins = Select("PART_Margins", WriteStrings.PsMarginsLabel, [.. s_marginPresets.Select(MarginName), WriteStrings.PsCustom]);
        var top = new Length("PART_Top", WriteStrings.PsTopFormat, unit, context.Inches);
        var bottom = new Length("PART_Bottom", WriteStrings.PsBottomFormat, unit, context.Inches);
        var left = new Length("PART_Left", WriteStrings.PsLeftFormat, unit, context.Inches);
        var right = new Length("PART_Right", WriteStrings.PsRightFormat, unit, context.Inches);
        top.Set(current.TopTwips);
        bottom.Set(current.BottomTwips);
        left.Set(current.LeftTwips);
        right.Set(current.RightTwips);
        margins.SelectedIndex = MarginIndexOf(current, context.Inches);

        // Columns.
        var count = new TextBox { Name = "PART_ColumnCount", Text = current.Columns.ToString(CultureInfo.InvariantCulture), MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(count, WriteStrings.PsColumnCount);
        var spacing = new Length("PART_ColumnSpacing", WriteStrings.PsColumnSpacingFormat, unit, context.Inches);
        spacing.Set(current.ColumnSpaceTwips);
        var line = new CheckBox { Name = "PART_ColumnLine", Content = WriteStrings.PsColumnLine, IsChecked = current.ColumnLine };
        var unequal = new TextBlock { Name = "PART_ColumnsNote", Text = WriteStrings.PsColumnsUnequal, TextWrapping = TextWrapping.Wrap, IsVisible = context.ColumnsUnequal };
        unequal.Classes.Add("secondary");
        spacing.Box.IsEnabled = !context.ColumnsUnequal;
        line.IsEnabled = !context.ColumnsUnequal;

        // Where it applies.
        var apply = Select("PART_Apply", WriteStrings.PsApplyLabel, [WriteStrings.PsApplyThis, WriteStrings.PsApplyWhole, WriteStrings.PsApplyFrom]);
        apply.SelectedIndex = Array.IndexOf(s_scopes, context.DefaultScope);
        var fromNote = new TextBlock { Name = "PART_FromHereNote", Text = WriteStrings.PsFromHereNote, TextWrapping = TextWrapping.Wrap, IsVisible = context.DefaultScope == ApplyScope.FromHere };
        fromNote.Classes.Add("secondary");

        var hint = new TextBlock { Name = "PART_Hint", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        ValidationHint.Mark(hint);

        // A preset fills its fields and locks them; Custom unlocks them (kept as they are).
        void SyncSize()
        {
            var custom = size.SelectedIndex == s_papers.Length;
            if (!custom)
            {
                var (w, h) = PagePresets.Size(s_papers[size.SelectedIndex]);
                width.Set(w);
                height.Set(h);
            }

            width.Box.IsEnabled = custom;
            height.Box.IsEnabled = custom;
        }

        void SyncMargins()
        {
            var custom = margins.SelectedIndex == s_marginPresets.Length;
            if (!custom)
            {
                var preset = PagePresets.Margins(s_marginPresets[margins.SelectedIndex], context.Inches);
                top.Set(preset.Top);
                bottom.Set(preset.Bottom);
                left.Set(preset.Left);
                right.Set(preset.Right);
            }

            foreach (var field in new[] { top, bottom, left, right })
            {
                field.Box.IsEnabled = custom;
            }
        }

        SyncSize();
        SyncMargins();
        size.SelectionChanged += (_, _) => SyncSize();
        margins.SelectionChanged += (_, _) => SyncMargins();
        apply.SelectionChanged += (_, _) => fromNote.IsVisible = apply.SelectedIndex == 2;

        var sizeGrid = TwoColumns(width.Panel, height.Panel);
        var marginGrid = TwoColumns(top.Panel, bottom.Panel, left.Panel, right.Panel);
        var body = new StackPanel { Spacing = 10, MinWidth = 420 };
        body.Children.Add(Field(WriteStrings.PsSizeLabel, size));
        body.Children.Add(sizeGrid);
        body.Children.Add(Field(WriteStrings.PsOrientationLabel, orientation));
        body.Children.Add(Field(WriteStrings.PsMarginsLabel, margins));
        body.Children.Add(marginGrid);
        body.Children.Add(new TextBlock { Text = WriteStrings.PsColumnsLabel, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 4, 0, 0) });
        body.Children.Add(Field(WriteStrings.PsColumnCount, count));
        body.Children.Add(spacing.Panel);
        body.Children.Add(line);
        body.Children.Add(unequal);
        body.Children.Add(Field(WriteStrings.PsApplyLabel, apply));
        body.Children.Add(fromNote);
        body.Children.Add(hint);

        var ok = new Button { Name = "PART_Ok", Content = WriteStrings.PsOk };
        ok.Classes.Add("primary");
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MaxHeight = 560, Content = body };
        var dialog = new Dialog { Title = WriteStrings.PageSetupTitle, Content = scroll, Footer = CardFooter.Build(ok, null, cancel) };

        var finished = false;
        void Finish(PageSetupRequest? answer)
        {
            if (!finished)
            {
                finished = true;
                done(answer);
            }
        }

        void Confirm()
        {
            var read = Read();
            if (read.Problem is { } problem)
            {
                hint.Text = problem.Text;
                hint.IsVisible = true;
                problem.Focus?.Focus();
                (problem.Focus as TextBox)?.SelectAll();
                return;
            }

            Finish(read.Request);
        }

        // Reads every field and makes the change: only the parts that differ from the section are written.
        (PageSetupRequest? Request, Problem? Problem) Read()
        {
            var scope = s_scopes[Math.Clamp(apply.SelectedIndex, 0, s_scopes.Length - 1)];
            int w, h;
            if (size.SelectedIndex == s_papers.Length)
            {
                if (!width.TryRead(out var rawW) || !height.TryRead(out var rawH))
                {
                    return (null, Invalid(!width.TryRead(out _) ? width : height));
                }

                (w, h) = (Math.Min(rawW, rawH), Math.Max(rawW, rawH));
                if (w < MinimumSide || h > MaximumSide)
                {
                    return (null, OutOfRange(w < MinimumSide ? width : height));
                }
            }
            else
            {
                (w, h) = PagePresets.Size(s_papers[size.SelectedIndex]);
            }

            var landscape = orientation.SelectedIndex == 1;
            var pageWidth = landscape ? h : w;
            var pageHeight = landscape ? w : h;
            var edges = new Dictionary<Length, int>();
            foreach (var field in new[] { top, bottom, left, right })
            {
                if (!field.TryRead(out var twips))
                {
                    return (null, Invalid(field));
                }

                if (twips < 0 || twips > Math.Max(pageWidth, pageHeight))
                {
                    return (null, OutOfRange(field));
                }

                edges[field] = twips;
            }

            if (edges[left] + edges[right] > pageWidth - MinimumText)
            {
                return (null, new Problem(WriteStrings.PsNoRoomForText, left.Box));
            }

            if (edges[top] + edges[bottom] > pageHeight - MinimumText)
            {
                return (null, new Problem(WriteStrings.PsNoRoomForText, top.Box));
            }

            var columns = current.Columns;
            var space = current.ColumnSpaceTwips;
            var separator = current.ColumnLine;
            if (!int.TryParse(count.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out columns) || columns < 1 || columns > MaximumColumns)
            {
                return (null, new Problem(WriteStrings.Fill(WriteStrings.PsOutOfRangeFormat, WriteStrings.PsColumnCount), count));
            }

            var columnsChanged = columns != current.Columns;
            if (!context.ColumnsUnequal)
            {
                if (!spacing.TryRead(out space))
                {
                    return (null, Invalid(spacing));
                }

                if (space < 0 || space > MaximumColumnSpace)
                {
                    return (null, OutOfRange(spacing));
                }

                separator = line.IsChecked == true;
                columnsChanged |= space != current.ColumnSpaceTwips || separator != current.ColumnLine;
            }
            else
            {
                space = current.ColumnSpaceTwips;
                separator = current.ColumnLine;
            }

            if (columnsChanged && (long)(columns - 1) * space + (long)columns * MinimumColumn > pageWidth - edges[left] - edges[right])
            {
                return (null, new Problem(WriteStrings.PsColumnsDoNotFit, count));
            }

            var parts = PageSetupParts.None;
            var value = current with { TopTwips = edges[top], BottomTwips = edges[bottom], LeftTwips = edges[left], RightTwips = edges[right], Columns = columns, ColumnSpaceTwips = space, ColumnLine = separator };
            if ((w, h) != upright)
            {
                parts |= PageSetupParts.Size;
                value = value with { WidthTwips = pageWidth, HeightTwips = pageHeight, Landscape = landscape };
            }

            if (landscape != current.Landscape)
            {
                parts |= PageSetupParts.Orientation;
                if ((parts & PageSetupParts.Size) == 0)
                {
                    value = value with { Landscape = landscape, WidthTwips = current.HeightTwips, HeightTwips = current.WidthTwips };
                }
            }

            if (edges[top] != current.TopTwips || edges[bottom] != current.BottomTwips || edges[left] != current.LeftTwips || edges[right] != current.RightTwips)
            {
                parts |= PageSetupParts.Margins;
            }

            if (columnsChanged)
            {
                parts |= PageSetupParts.Columns;
            }

            return (parts == PageSetupParts.None ? null : new PageSetupRequest(new PageSetupChange(value, parts), scope), null);
        }

        void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        }

        foreach (var box in new[] { width.Box, height.Box, top.Box, bottom.Box, left.Box, right.Box, count, spacing.Box })
        {
            box.KeyDown += OnKey;
        }

        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => Finish(null);
        dialog.CancelRequested += (_, _) => Finish(null);
        return dialog;
    }

    private sealed record Problem(string Text, Control? Focus);

    private static Problem Invalid(Length field) => new(WriteStrings.Fill(WriteStrings.PsInvalidValueFormat, field.Label), field.Box);

    private static Problem OutOfRange(Length field) => new(WriteStrings.Fill(WriteStrings.PsOutOfRangeFormat, field.Label), field.Box);

    private static int PaperIndexOf((int Width, int Height) upright)
    {
        for (var i = 0; i < s_papers.Length; i++)
        {
            var (w, h) = PagePresets.Size(s_papers[i]);
            if (Math.Abs(w - upright.Width) <= 1 && Math.Abs(h - upright.Height) <= 1)
            {
                return i;
            }
        }

        return s_papers.Length;
    }

    private static int MarginIndexOf(SectionPageSetup setup, bool inches)
    {
        for (var i = 0; i < s_marginPresets.Length; i++)
        {
            var m = PagePresets.Margins(s_marginPresets[i], inches);
            if (m.Top == setup.TopTwips && m.Bottom == setup.BottomTwips && m.Left == setup.LeftTwips && m.Right == setup.RightTwips)
            {
                return i;
            }
        }

        return s_marginPresets.Length;
    }

    private static string MarginName(string preset) => preset switch
    {
        "Normal" => WriteStrings.MarginNormal,
        "Narrow" => WriteStrings.MarginNarrow,
        "Moderate" => WriteStrings.MarginModerate,
        _ => WriteStrings.MarginWide,
    };

    private static SelectBox Select(string name, string label, string[] items)
    {
        var box = new SelectBox { Name = name, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = items };
        AutomationProperties.SetName(box, label);
        return box;
    }

    private static StackPanel Field(string label, Control field) => new() { Spacing = 4, Children = { new TextBlock { Text = label }, field } };

    private static Grid TwoColumns(params Control[] cells)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8, ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Star) } };
        for (var i = 0; i < cells.Length; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Grid.SetRow(cells[i], i / 2);
            Grid.SetColumn(cells[i], i % 2);
            grid.Children.Add(cells[i]);
        }

        return grid;
    }

    /// <summary>A length field: shows twips in centimetres or inches, and keeps the exact twips while the text is as it was shown (a preset's 1417 twips read "2.5" and stay 1417).</summary>
    internal sealed class Length
    {
        private readonly bool _inches;
        private int _twips;
        private string _shown = string.Empty;

        public Length(string name, string labelFormat, string unit, bool inches)
        {
            _inches = inches;
            Label = WriteStrings.Fill(labelFormat, unit);
            Box = new TextBox { Name = name, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(Box, Label);
            Panel = Field(Label, Box);
        }

        public string Label { get; }

        public TextBox Box { get; }

        public StackPanel Panel { get; }

        public void Set(int twips)
        {
            _twips = twips;
            _shown = Format(twips, _inches);
            Box.Text = _shown;
        }

        public bool TryRead(out int twips)
        {
            var text = Box.Text?.Trim() ?? string.Empty;
            if (text == _shown)
            {
                twips = _twips;
                return true;
            }

            return TryParse(text, _inches, out twips);
        }

        /// <summary>The twips as text in the unit and the language of the UI, at most two decimals.</summary>
        internal static string Format(int twips, bool inches) =>
            (twips / (inches ? 1440.0 : TwipsPerCentimetre)).ToString("0.##", WriteStrings.Culture);

        /// <summary>A number with a point or a comma and an optional unit (cm, in, the inch sign); null for anything else. A value out of the int range is refused.</summary>
        internal static bool TryParse(string text, bool inches, out int twips)
        {
            twips = 0;
            var trimmed = text.Trim();
            var unit = inches ? 1440.0 : TwipsPerCentimetre;
            foreach (var (suffix, factor) in new[] { ("cm", TwipsPerCentimetre), ("in", 1440.0), ("\"", 1440.0) })
            {
                if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed[..^suffix.Length].TrimEnd();
                    unit = factor;
                    break;
                }
            }

            if (trimmed.Length == 0 || trimmed.Length > 12 || !trimmed.All(c => char.IsAsciiDigit(c) || c is '.' or ','))
            {
                return false;
            }

            if (!double.TryParse(trimmed.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            {
                return false;
            }

            var result = Math.Round(value * unit);
            if (result > int.MaxValue / 2)
            {
                return false;
            }

            twips = (int)result;
            return true;
        }
    }
}
