using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
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
/// sides of the upright page (the orientation turns it). A column write makes the columns of every section it reaches equal, so the card offers only the count for a caret section whose columns have different
/// widths and says how many sections in the chosen scope lose their custom widths. "From here on" starts a new page (new columns start on the same page) and turns "different first page" off for the new section; the
/// card says so. A wrong entry shows a hint and keeps the card open; nothing changed closes the card with no answer; Enter confirms, Esc cancels.
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

    // Two page sides this close are the same size (the tolerance of the paper match).
    private const int SideTolerance = 1;
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
        return new Form(context, CardParts.Once(done)).Dialog;
    }

    private sealed record Problem(string Text, Control? Focus);

    // The fields of the card and what they say; made once, read when the user confirms.
    private sealed class Form
    {
        private readonly PageSetupContext _context;
        private readonly SectionPageSetup _current;
        private readonly (int Width, int Height) _upright;
        private readonly SelectBox _size;
        private readonly SelectBox _orientation;
        private readonly SelectBox _margins;
        private readonly SelectBox _apply;
        private readonly Length _width;
        private readonly Length _height;
        private readonly Length _top;
        private readonly Length _bottom;
        private readonly Length _left;
        private readonly Length _right;
        private readonly Length _spacing;
        private readonly TextBox _count;
        private readonly CheckBox _line;
        private readonly TextBlock _unequalNote;
        private readonly TextBlock _equalizeNote;
        private readonly TextBlock _fromNote;
        private readonly TextBlock _hint = CardParts.Hint();

        public Form(PageSetupContext context, Action<PageSetupRequest?> finish)
        {
            _context = context;
            _current = context.Current;
            var unit = context.Inches ? WriteStrings.PsUnitInch : WriteStrings.PsUnitCm;
            _upright = (Math.Min(_current.WidthTwips, _current.HeightTwips), Math.Max(_current.WidthTwips, _current.HeightTwips));

            _size = CardParts.Select("PART_Size", WriteStrings.PsSizeLabel, [.. s_papers, WriteStrings.PsCustom]);
            _width = new Length("PART_Width", WriteStrings.PsWidthFormat, unit, context.Inches);
            _height = new Length("PART_Height", WriteStrings.PsHeightFormat, unit, context.Inches);
            _orientation = CardParts.Select("PART_Orientation", WriteStrings.PsOrientationLabel, [WriteStrings.OrientationPortrait, WriteStrings.OrientationLandscape]);
            _margins = CardParts.Select("PART_Margins", WriteStrings.PsMarginsLabel, [.. s_marginPresets.Select(MarginName), WriteStrings.PsCustom]);
            _top = new Length("PART_Top", WriteStrings.PsTopFormat, unit, context.Inches);
            _bottom = new Length("PART_Bottom", WriteStrings.PsBottomFormat, unit, context.Inches);
            _left = new Length("PART_Left", WriteStrings.PsLeftFormat, unit, context.Inches);
            _right = new Length("PART_Right", WriteStrings.PsRightFormat, unit, context.Inches);
            _count = CardParts.Text("PART_ColumnCount", WriteStrings.PsColumnCount, _current.Columns.ToString(CultureInfo.InvariantCulture), 96);
            _spacing = new Length("PART_ColumnSpacing", WriteStrings.PsColumnSpacingFormat, unit, context.Inches);
            _line = new CheckBox { Name = "PART_ColumnLine", Content = WriteStrings.PsColumnLine, IsChecked = _current.ColumnLine };
            _unequalNote = Note("PART_ColumnsNote", WriteStrings.PsColumnsUnequal, context.ColumnsUnequal);
            _equalizeNote = Note("PART_EqualizeNote", string.Empty, false);
            _apply = CardParts.Select("PART_Apply", WriteStrings.PsApplyLabel, [WriteStrings.PsApplyThis, WriteStrings.PsApplyWhole, WriteStrings.PsApplyFrom]);
            _fromNote = Note("PART_FromHereNote", WriteStrings.PsFromHereNote, context.DefaultScope == ApplyScope.FromHere);

            Fill();
            Dialog = Build(finish);
        }

        public Dialog Dialog { get; }

        // The starting values: the section's own, with the matching preset chosen (or Custom).
        private void Fill()
        {
            _width.Set(_upright.Width);
            _height.Set(_upright.Height);
            _size.SelectedIndex = PaperIndexOf(_upright);
            _orientation.SelectedIndex = _current.Landscape ? 1 : 0;
            _top.Set(_current.TopTwips);
            _bottom.Set(_current.BottomTwips);
            _left.Set(_current.LeftTwips);
            _right.Set(_current.RightTwips);
            _margins.SelectedIndex = MarginIndexOf(_current, _context.Inches);
            _spacing.Set(_current.ColumnSpaceTwips);
            _spacing.Box.IsEnabled = !_context.ColumnsUnequal;
            _line.IsEnabled = !_context.ColumnsUnequal;
            _apply.SelectedIndex = Array.IndexOf(s_scopes, _context.DefaultScope);
        }

        private Dialog Build(Action<PageSetupRequest?> finish)
        {
            // A preset fills its fields and locks them; Custom unlocks them (kept as they are).
            SyncSize();
            SyncMargins();
            _size.SelectionChanged += (_, _) => SyncSize();
            _margins.SelectionChanged += (_, _) => SyncMargins();
            _apply.SelectionChanged += (_, _) =>
            {
                _fromNote.IsVisible = Scope() == ApplyScope.FromHere;
                UpdateEqualizeNote();
            };
            _count.TextChanged += (_, _) => UpdateEqualizeNote();
            _spacing.Box.TextChanged += (_, _) => UpdateEqualizeNote();
            _line.IsCheckedChanged += (_, _) => UpdateEqualizeNote();
            UpdateEqualizeNote();

            var body = new StackPanel { Spacing = 10, MinWidth = 420 };
            body.Children.Add(CardParts.Field(WriteStrings.PsSizeLabel, _size));
            body.Children.Add(TwoColumns(_width.Panel, _height.Panel));
            body.Children.Add(CardParts.Field(WriteStrings.PsOrientationLabel, _orientation));
            body.Children.Add(CardParts.Field(WriteStrings.PsMarginsLabel, _margins));
            body.Children.Add(TwoColumns(_top.Panel, _bottom.Panel, _left.Panel, _right.Panel));
            body.Children.Add(new TextBlock { Text = WriteStrings.PsColumnsLabel, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 4, 0, 0) });
            body.Children.Add(CardParts.Field(WriteStrings.PsColumnCount, _count));
            body.Children.Add(_spacing.Panel);
            body.Children.Add(_line);
            body.Children.Add(_unequalNote);
            body.Children.Add(_equalizeNote);
            body.Children.Add(CardParts.Field(WriteStrings.PsApplyLabel, _apply));
            body.Children.Add(_fromNote);
            body.Children.Add(_hint);

            var (ok, cancel) = CardParts.Buttons(WriteStrings.PsOk, ShellStrings.Cancel);
            var scroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MaxHeight = 560, Content = body };
            var dialog = new Dialog { Title = WriteStrings.PageSetupTitle, Content = scroll, Footer = CardFooter.Build(ok, null, cancel) };

            void Confirm()
            {
                var (request, problem) = Read();
                if (problem is not null)
                {
                    _hint.Text = problem.Text;
                    _hint.IsVisible = true;
                    problem.Focus?.Focus();
                    (problem.Focus as TextBox)?.SelectAll();
                    return;
                }

                finish(request);
            }

            CardParts.ConfirmOnEnter(Confirm, [_width.Box, _height.Box, _top.Box, _bottom.Box, _left.Box, _right.Box, _count, _spacing.Box]);
            ok.Click += (_, _) => Confirm();
            cancel.Click += (_, _) => finish(null);
            dialog.CancelRequested += (_, _) => finish(null);
            return dialog;
        }

        private ApplyScope Scope() => s_scopes[Math.Clamp(_apply.SelectedIndex, 0, s_scopes.Length - 1)];

        private void SyncSize()
        {
            var custom = _size.SelectedIndex == s_papers.Length;
            if (!custom)
            {
                var (w, h) = PagePresets.Size(s_papers[_size.SelectedIndex]);
                _width.Set(w);
                _height.Set(h);
            }

            _width.Box.IsEnabled = custom;
            _height.Box.IsEnabled = custom;
        }

        private void SyncMargins()
        {
            var custom = _margins.SelectedIndex == s_marginPresets.Length;
            if (!custom)
            {
                var preset = PagePresets.Margins(s_marginPresets[_margins.SelectedIndex], _context.Inches);
                _top.Set(preset.Top);
                _bottom.Set(preset.Bottom);
                _left.Set(preset.Left);
                _right.Set(preset.Right);
            }

            foreach (var field in new[] { _top, _bottom, _left, _right })
            {
                field.Box.IsEnabled = custom;
            }
        }

        // Whether the column fields, as they stand, would write the columns part (an unreadable entry says no: the hint comes when the user confirms).
        private bool ColumnsWouldChange()
        {
            if (!int.TryParse(_count.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var columns))
            {
                return false;
            }

            if (columns != _current.Columns)
            {
                return true;
            }

            return !_context.ColumnsUnequal && _spacing.TryRead(out var space) && (space != _current.ColumnSpaceTwips || (_line.IsChecked == true) != _current.ColumnLine);
        }

        // A column write makes the columns of every section it reaches equal: the card says how many lose their own widths in the chosen scope.
        private void UpdateEqualizeNote()
        {
            var affected = ColumnsWouldChange() ? _context.UnequalTargets(Scope()) : 0;
            _equalizeNote.Text = affected switch
            {
                0 => string.Empty,
                1 => WriteStrings.PsEqualizeOne,
                _ => WriteStrings.Fill(WriteStrings.PsEqualizeMany, affected),
            };
            _equalizeNote.IsVisible = affected > 0;
        }

        // Reads every field and makes the change: only the parts that differ from the section are written.
        private (PageSetupRequest? Request, Problem? Problem) Read()
        {
            if (ReadSize(out var side, out var problem) is false)
            {
                return (null, problem);
            }

            var landscape = _orientation.SelectedIndex == 1;
            var (pageWidth, pageHeight) = landscape ? (side.Height, side.Width) : (side.Width, side.Height);
            if (ReadMargins(pageWidth, pageHeight, out var edges, out problem) is false)
            {
                return (null, problem);
            }

            if (ReadColumns(pageWidth - edges.Left - edges.Right, edges.Changed, out var columns, out problem) is false)
            {
                return (null, problem);
            }

            return (Compose(side, landscape, edges, columns), null);
        }

        private bool ReadSize(out (int Width, int Height) side, out Problem? problem)
        {
            problem = null;
            if (_size.SelectedIndex != s_papers.Length)
            {
                side = PagePresets.Size(s_papers[_size.SelectedIndex]);
                return true;
            }

            side = default;
            if (!_width.TryRead(out var rawW))
            {
                problem = Invalid(_width);
                return false;
            }

            if (!_height.TryRead(out var rawH))
            {
                problem = Invalid(_height);
                return false;
            }

            side = (Math.Min(rawW, rawH), Math.Max(rawW, rawH));
            if (side.Width < MinimumSide || side.Height > MaximumSide)
            {
                problem = OutOfRange(side.Width < MinimumSide ? _width : _height);
                return false;
            }

            return true;
        }

        // The margins are read always (an unreadable entry is a wrong entry) but checked against the page only when they change: a file whose margins already leave no room still allows another change.
        private bool ReadMargins(int pageWidth, int pageHeight, out (int Top, int Bottom, int Left, int Right, bool Changed) edges, out Problem? problem)
        {
            problem = null;
            edges = default;
            var values = new Dictionary<Length, int>();
            foreach (var field in new[] { _top, _bottom, _left, _right })
            {
                if (!field.TryRead(out var twips))
                {
                    problem = Invalid(field);
                    return false;
                }

                values[field] = twips;
            }

            edges = (values[_top], values[_bottom], values[_left], values[_right], false);
            edges.Changed = edges.Top != _current.TopTwips || edges.Bottom != _current.BottomTwips || edges.Left != _current.LeftTwips || edges.Right != _current.RightTwips;
            if (!edges.Changed)
            {
                return true;
            }

            foreach (var (field, twips) in values)
            {
                if (twips < 0 || twips > Math.Max(pageWidth, pageHeight))
                {
                    problem = OutOfRange(field);
                    return false;
                }
            }

            if (edges.Left + edges.Right > pageWidth - MinimumText)
            {
                problem = new Problem(WriteStrings.PsNoRoomForText, _left.Box);
                return false;
            }

            if (edges.Top + edges.Bottom > pageHeight - MinimumText)
            {
                problem = new Problem(WriteStrings.PsNoRoomForText, _top.Box);
                return false;
            }

            return true;
        }

        private bool ReadColumns(int textWidth, bool marginsChanged, out (int Count, int Space, bool Line, bool Changed) columns, out Problem? problem)
        {
            problem = null;
            columns = (_current.Columns, _current.ColumnSpaceTwips, _current.ColumnLine, false);
            if (!int.TryParse(_count.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || count > MaximumColumns)
            {
                problem = new Problem(WriteStrings.PsColumnCountInvalid, _count);
                return false;
            }

            var space = _current.ColumnSpaceTwips;
            var line = _current.ColumnLine;
            var changed = count != _current.Columns;
            if (!_context.ColumnsUnequal)
            {
                if (!_spacing.TryRead(out space))
                {
                    problem = Invalid(_spacing);
                    return false;
                }

                if (space < 0 || space > MaximumColumnSpace)
                {
                    problem = OutOfRange(_spacing);
                    return false;
                }

                line = _line.IsChecked == true;
                changed |= space != _current.ColumnSpaceTwips || line != _current.ColumnLine;
            }

            if ((changed || marginsChanged) && count > 1 && (long)(count - 1) * space + (long)count * MinimumColumn > textWidth)
            {
                problem = new Problem(WriteStrings.PsColumnsDoNotFit, _count);
                return false;
            }

            columns = (count, space, line, changed);
            return true;
        }

        // The change: only what differs from the section is a part; null when nothing does.
        private PageSetupRequest? Compose((int Width, int Height) side, bool landscape, (int Top, int Bottom, int Left, int Right, bool Changed) edges, (int Count, int Space, bool Line, bool Changed) columns)
        {
            var parts = PageSetupParts.None;
            var value = _current with { TopTwips = edges.Top, BottomTwips = edges.Bottom, LeftTwips = edges.Left, RightTwips = edges.Right, Columns = columns.Count, ColumnSpaceTwips = columns.Space, ColumnLine = columns.Line };
            var sizeChanged = Math.Abs(side.Width - _upright.Width) > SideTolerance || Math.Abs(side.Height - _upright.Height) > SideTolerance;
            if (sizeChanged)
            {
                parts |= PageSetupParts.Size;
                value = value with { WidthTwips = landscape ? side.Height : side.Width, HeightTwips = landscape ? side.Width : side.Height, Landscape = landscape };
            }

            if (landscape != _current.Landscape)
            {
                parts |= PageSetupParts.Orientation;
                if (!sizeChanged)
                {
                    value = value with { Landscape = landscape, WidthTwips = _current.HeightTwips, HeightTwips = _current.WidthTwips };
                }
            }

            if (edges.Changed)
            {
                parts |= PageSetupParts.Margins;
            }

            if (columns.Changed)
            {
                parts |= PageSetupParts.Columns;
            }

            return parts == PageSetupParts.None ? null : new PageSetupRequest(new PageSetupChange(value, parts), Scope());
        }
    }

    private static TextBlock Note(string name, string text, bool visible)
    {
        var note = new TextBlock { Name = name, Text = text, TextWrapping = TextWrapping.Wrap, IsVisible = visible };
        note.Classes.Add("secondary");
        return note;
    }

    private static Problem Invalid(Length field) => new(WriteStrings.Fill(WriteStrings.PsInvalidValueFormat, field.Label), field.Box);

    private static Problem OutOfRange(Length field) => new(WriteStrings.Fill(WriteStrings.PsOutOfRangeFormat, field.Label), field.Box);

    private static int PaperIndexOf((int Width, int Height) upright)
    {
        for (var i = 0; i < s_papers.Length; i++)
        {
            var (w, h) = PagePresets.Size(s_papers[i]);
            if (Math.Abs(w - upright.Width) <= SideTolerance && Math.Abs(h - upright.Height) <= SideTolerance)
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
            Box = CardParts.Text(name, Label, null, 96);
            Box.HorizontalAlignment = HorizontalAlignment.Stretch;
            Panel = CardParts.Field(Label, Box);
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

        /// <summary>A number with a point or a comma and an optional unit (cm, in, the inch sign); false for anything else. A value out of the int range is refused.</summary>
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
