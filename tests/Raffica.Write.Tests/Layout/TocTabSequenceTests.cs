using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;
using static Raffica.Write.Tests.Support.TocDocuments;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// Random sequences of changes to a table of contents (milestone 4.5b): numbers, level texts, numbering on and off, titles, levels and the size of the entry styles, one to three of them between two refreshes, over entry styles
/// with and without tab stops and hanging indents of their own, with and without a stop of the user's. After every refresh every short entry ends its page number at the right edge on Write's page, and a second refresh changes
/// nothing. Five cases are not asked for the right edge:
/// <list type="bullet">
/// <item>two stops of the entry style (its left tab stops and its hanging indent) behind the number: the second takes the tab before the page number, in Word too, and Write does not take stops from a style;</item>
/// <item>a stop of the entry style behind the title of an entry without a number, for the same reason;</item>
/// <item>a stop of the entry's own, Write's or Word's, at Word's place for its number behind a single left tab stop of the entry style: Word writes that stop in its own list of such a document and draws the page number of a short entry at it,
/// so Write keeps it, also one it wrote for larger letters or a longer number that stands there now. Nothing else of this kind is excused: a stop that is not at that place must go, and so must every one behind a hanging indent that takes
/// the number on a first line at the margin, where Word writes none (every hanging indent of these sequences starts its first line there);</item>
/// <item>a stop of the user's that Write keeps and that takes the tab before the page number: in front of a style stop, beside the stop of the number, or behind the title of an entry without a number. A stop that ever stood behind one of the
/// user's in the same entry counts as one of them from then on: once the number or its letters change, which of the two is the number's cannot be told;</item>
/// <item>an entry that is not short: its number ends more than 250 points into the line.</item>
/// </list>
/// </summary>
public sealed class TocTabSequenceTests
{
    // The text area of A4 with 2.5 cm margins: in points, and the right stop of the page number in twips.
    private const float TextWidth = 453.6f;
    private const int RightStop = 9072;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly string[] s_texts = ["%1.", "Chapter %1:", "(%1)", "Appendix %1", "%1", string.Empty, "Chapter %1.%1.%1:", "%1 -"];
    private static readonly string[] s_formats = ["decimal", "upperRoman", "upperLetter"];
    private static readonly int[] s_starts = [1, 2, 8, 18, 100];
    private static readonly int?[] s_sizes = [null, 16, 22, 40, 72, 96];
    private static readonly int[] s_stylePositions = [100, 300, 600, 1320, 2500];

    // No multiple of a level indent and no place Write takes: a left stop here is the user's.
    private static readonly int[] s_userPositions = [311, 1011, 2011, 3011, 5011];

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(500)]
    public void EveryShortEntryKeepsItsPageNumberAtTheRightEdgeThroughAnySequenceOfChanges(int firstSeed)
    {
        var failures = new List<string>();
        var asserted = 0;
        var excluded = 0;
        for (var seed = firstSeed; seed < firstSeed + 100; seed++)
        {
            var run = new Sequence(seed);
            using var file = run.File;
            for (var step = 0; step < 10; step++)
            {
                run.Change();
                run.Refresh();
                var again = run.Refresh();
                if (again != 0)
                {
                    failures.Add($"seed {seed} step {step}: a second refresh changed {again} blocks\n    {run.Log}");
                    break;
                }

                if (run.Check(ref asserted, ref excluded) is { } wrong)
                {
                    failures.Add($"seed {seed} step {step}: {wrong}\n    {run.Log}");
                    break;
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of 100 sequences failed:\n{string.Join('\n', failures.Take(6))}");

        // The exclusions must not swallow the test.
        Assert.True(asserted > excluded, $"{asserted} entries checked, {excluded} excluded");
    }

    private sealed class Sequence
    {
        private readonly Random _random;
        private readonly TextEditor _editor;
        private readonly FakeFieldHost _host;
        private readonly List<string> _log = [];
        // By the place of the entry in the list (headings are never added or moved): the stops of the user, and those that stood behind one of them.
        private readonly HashSet<(int Entry, int Position)> _user = [];

        public Sequence(int seed)
        {
            _random = new Random(seed);
            (File, _editor) = Numbered([(null, "Contents."), ("Heading1", "A"), ("Heading2", "B"), ("Heading1", "C")]);
            _host = new FakeFieldHost(Model);
            var at = (Paragraph)Model.Body.Blocks[0];
            Assert.NotNull(_editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), _host));
            ShapeTheStyles();
            Refresh();
        }

        public DocxFile File { get; }

        public string Log => string.Join("; ", _log);

        private WordDocument Model => File.Model;

        public int Refresh() => _editor.RefreshFields(_host, RefreshScope.OrderBased).Content.Count;

        // One to three changes between two refreshes, as a pause finds them.
        public void Change()
        {
            var count = _random.Next(4) == 0 ? _random.Next(2, 4) : 1;
            for (var i = 0; i < count; i++)
            {
                ChangeOnce();
            }

            _log.Add("|");
        }

        // Null when every entry that can have its page number at the right edge has it.
        public string? Check(ref int asserted, ref int excluded)
        {
            var layout = Finish(DocumentLayout.Create(Model, Fonts, new LayoutOptions()));
            var margin = layout.Caret(new TextPosition(StoryRef.Body, (Paragraph)Model.Body.Blocks[0], 0), 0)!.Value.X;
            foreach (var (entry, index) in Entries(Model).Select((e, i) => (e, i)))
            {
                var text = ParagraphProjection.Build(entry, ProjectionHost.Plain).Text;
                var first = text.IndexOf('\t', StringComparison.Ordinal);
                var last = text.LastIndexOf('\t');
                if (first < 0)
                {
                    return $"the entry '{text}' has no tab";
                }

                var numbered = first != last;

                // Where the number ends (the title, for an entry without one): the width does not depend on the tab stops.
                var anchor = (At(layout, entry, first).X - margin) * 20f;
                if (anchor > 5000)
                {
                    // Not a short entry: the number leaves no room for the title and the page.
                    excluded++;
                    continue;
                }

                if (Impossible(entry, index, numbered, anchor))
                {
                    excluded++;
                    continue;
                }

                asserted++;
                var start = At(layout, entry, 0);
                var end = At(layout, entry, text.Length);
                if (start.Top != end.Top || Math.Abs(end.X - margin - TextWidth) >= 1f)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"'{text.Replace('\t', '|')}' [{entry.Properties.StyleId}] direct ({TabText(entry)}), style ({StyleText(entry)}): the page number ends at {end.X - margin:F1} points{(start.Top == end.Top ? string.Empty : " on another line")}");
                }
            }

            return null;
        }

        private bool Impossible(Paragraph entry, int index, bool numbered, float anchor)
        {
            // As the layout takes a stop: more than 0.01 points behind the text.
            bool Behind(int position) => position > anchor + 0.2f && position < RightStop;
            var style = Model.Styles!.Find(entry.Properties.StyleId!)!;
            var design = (style.ParagraphProperties.Get(W + "tabs")?.Elements(W + "tab") ?? [])
                .Where(t => (string)t.Attribute(W + "val")! == "left").Select(t => (int)t.Attribute(W + "pos")!).ToList();
            var tabStops = design.ToList();
            if (style.ParagraphProperties.Get(W + "ind") is { } indent && (int?)indent.Attribute(W + "hanging") > 0)
            {
                design.Add((int)indent.Attribute(W + "left")!);
            }

            design = [.. design.Distinct().Where(Behind)];
            var direct = Tabs(entry).Where(t => t.Kind == "left" && Behind(t.Position)).Select(t => t.Position).ToList();
            var user = direct.Where(position => _user.Contains((index, position))).ToList();

            // The same for a stop a refresh wrote behind one of the user's.
            if (user.Count > 0)
            {
                foreach (var position in direct.Where(position => position > user.Min()))
                {
                    _user.Add((index, position));
                }
            }

            if (!numbered)
            {
                return design.Count > 0 || user.Count > 0;
            }

            // Two stops of the style; Word's own stop behind its single tab stop; the user's stop in front of a style stop; the user's stop beside the stop of the number.
            return design.Count >= 2
                || (design.Count == 1 && tabStops.Contains(design[0]) && direct.Any(position => position > design[0] && AtWordsPlace(position, anchor)))
                || user.Any(u => design.Any(d => d > u))
                || (design.Count == 0 && user.Count > 0 && direct.Count >= 2);
        }

        // Where Word writes the stop behind a number that ends at the anchor (the rule found in Word, Docs/write-word-check.md): the left indent of the first of TOC1 to TOC9 more than 12 points behind it (11 points per level without the
        // style), else that place; a few twips either way, as the two measure a text a little differently.
        private bool AtWordsPlace(int position, float anchor) => new[] { -20f, 0f, 20f }.Any(by =>
        {
            var behind = anchor + by + 240f;
            var indent = Enumerable.Range(1, 9).Select(level => Model.Styles!.Find("TOC" + level.ToString(CultureInfo.InvariantCulture)) is { } style
                    ? (int?)style.ParagraphProperties.Get(W + "ind")?.Attribute(W + "left") ?? 0
                    : (level - 1) * 220)
                .Where(left => left > behind).Select(left => (int?)left).FirstOrDefault();
            return Math.Abs(position - (indent ?? behind)) <= 21f;
        });

        private string StyleText(Paragraph entry)
        {
            var style = Model.Styles!.Find(entry.Properties.StyleId!)!;
            var tabs = style.ParagraphProperties.Get(W + "tabs")?.Elements(W + "tab").Select(t => $"{(string?)t.Attribute(W + "val")}:{(string?)t.Attribute(W + "pos")}") ?? [];
            var hanging = (int?)style.ParagraphProperties.Get(W + "ind")?.Attribute(W + "hanging");
            return string.Join(' ', tabs) + (hanging is null ? string.Empty : $" hanging:{hanging}") + $" size:{(string?)style.RunProperties.Get(W + "sz")?.Attribute(W + "val")}";
        }

        private int Pick(int[] values) => values[_random.Next(values.Length)];

        // The entry styles get no stops, a left stop, a left and a right one, a hanging indent or two left stops; the list starts as an insert over such styles leaves it.
        private void ShapeTheStyles()
        {
            var shape = _random.Next(5);
            var rightInTheStyle = shape == 2 && _random.Next(2) == 0;
            foreach (var id in new[] { "TOC1", "TOC2" })
            {
                var style = Model.Styles!.Find(id)!;
                var position = Pick(s_stylePositions);
                if (shape is 1 or 2 or 4)
                {
                    var tabs = new XElement(W + "tabs", Stop("left", position));
                    if (shape == 4)
                    {
                        tabs.Add(Stop("left", position + 700));
                    }

                    if (shape == 2)
                    {
                        tabs.Add(Stop("right", RightStop));
                    }

                    style.ParagraphProperties.Set(W + "tabs", tabs);
                }
                else if (shape == 3)
                {
                    style.ParagraphProperties.Set(W + "ind", new XElement(W + "ind", new XAttribute(W + "left", position), new XAttribute(W + "hanging", position)));
                }

                _log.Add($"{id} shape {shape} at {position}");
            }

            // Entries as a fresh insert over these styles has them: no stop of their number where one of the style serves.
            if (shape != 0)
            {
                foreach (var entry in Entries(Model))
                {
                    if (rightInTheStyle)
                    {
                        entry.Properties.Remove(W + "tabs");
                    }
                    else
                    {
                        entry.Properties.Set(W + "tabs", new XElement(W + "tabs", Stop("right", RightStop)));
                    }
                }
            }

            Model.Edits.Raise();
        }

        private static XElement Stop(string kind, int position) => new(W + "tab", new XAttribute(W + "val", kind), new XAttribute(W + "pos", position));

        private void ChangeOnce()
        {
            var headings = Model.Body.Blocks.OfType<Paragraph>().Where(p => p.Properties.StyleId is "Heading1" or "Heading2").ToList();
            var heading = headings[_random.Next(headings.Count)];
            var length = ParagraphProjection.Build(heading, ProjectionHost.Plain).Text.Length;
            switch (_random.Next(9))
            {
                case 0 or 1:
                    var level = _random.Next(3) == 0 ? 1 : 0;
                    var text = s_texts[_random.Next(s_texts.Length)];
                    var from = Pick(s_starts);
                    var format = s_formats[_random.Next(s_formats.Length)];
                    foreach (var l in Model.Numbering!.Abstracts.SelectMany(a => a.Levels).Where(l => l.Level == level && l.Text is not null))
                    {
                        l.Text = level == 1 && text.Length > 0 ? text.Replace("%1", "%1.%2", StringComparison.Ordinal) : text;
                        l.Start = from;
                        l.Format = format;
                    }

                    Model.Edits.Raise();
                    _log.Add($"level {level + 1} '{text}' from {from} {format}");
                    break;
                case 2:
                    if (length < 4)
                    {
                        _editor.InsertText(Caret(new TextPosition(StoryRef.Body, heading, length)), "x");
                    }
                    else
                    {
                        _editor.Delete(new TextRange(new TextPosition(StoryRef.Body, heading, 1), new TextPosition(StoryRef.Body, heading, length)));
                    }

                    _log.Add("rename");
                    break;
                case 3:
                    var to = heading.Properties.StyleId == "Heading1" ? "Heading2" : "Heading1";
                    _editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, heading, 0)), to);
                    _log.Add("heading to " + to);
                    break;
                case 4:
                    var on = _random.Next(2) == 0;
                    _editor.SetHeadingNumbering(on, new TextPosition(StoryRef.Body, heading, 0));
                    _log.Add("numbering " + on);
                    break;
                case 5 or 6:
                    var id = _random.Next(3) == 0 ? "TOC2" : "TOC1";
                    var size = s_sizes[_random.Next(s_sizes.Length)];
                    var run = Model.Styles!.Find(id)!.RunProperties;
                    if (size is { } half)
                    {
                        run.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", half)));
                    }
                    else
                    {
                        run.Remove(W + "sz");
                    }

                    Model.Edits.Raise();
                    _log.Add($"{id} size {size?.ToString(CultureInfo.InvariantCulture) ?? "of the style"}");
                    break;
                case 7:
                    // A stop of the user's in one entry, behind or in front of its number.
                    var entries = Entries(Model);
                    if (entries.Count == 0 || _random.Next(3) != 0)
                    {
                        break;
                    }

                    var chosen = _random.Next(entries.Count);
                    var entry = entries[chosen];
                    var at = Pick(s_userPositions);
                    var stops = entry.Properties.Get(W + "tabs") is { } had ? new XElement(had) : new XElement(W + "tabs");
                    if (stops.Elements(W + "tab").All(t => (int)t.Attribute(W + "pos")! != at))
                    {
                        var behind = stops.Elements(W + "tab").FirstOrDefault(t => (int)t.Attribute(W + "pos")! > at);
                        if (behind is null)
                        {
                            stops.Add(Stop("left", at));
                        }
                        else
                        {
                            behind.AddBeforeSelf(Stop("left", at));
                        }

                        entry.Properties.Set(W + "tabs", stops);
                        _user.Add((chosen, at));

                        // A stop behind the new one counts as the user's from now on: once the number or its letters change, which of the two is the stop of the number cannot be told.
                        foreach (var tab in Tabs(entry).Where(t => t.Kind == "left" && t.Position > at))
                        {
                            _user.Add((chosen, tab.Position));
                        }

                        Model.Edits.Raise();
                        _log.Add($"user stop {at} in '{ParagraphProjection.Build(entry, ProjectionHost.Plain).Text.Replace('\t', '|')}'");
                    }

                    break;
                default:
                    var renamed = headings[0];
                    _editor.InsertText(Caret(new TextPosition(StoryRef.Body, renamed, 0)), _random.Next(2) == 0 ? "D" : string.Empty);
                    break;
            }
        }

        private static CaretBox At(DocumentLayout layout, Paragraph paragraph, int offset)
        {
            var box = layout.Caret(new TextPosition(StoryRef.Body, paragraph, offset), 0);
            Assert.NotNull(box);
            return box!.Value;
        }
    }
}
