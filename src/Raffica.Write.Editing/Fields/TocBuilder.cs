using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a <c>TOC</c> instruction asks for, as the refresh and the insert read it: the levels of the headings (<c>\o "a-b"</c>, all nine with only <c>\u</c>), whether outline levels count (<c>\u</c>), the caption label of a list
/// of figures or tables (<c>\c "label"</c>, which replaces the levels), whether entries are links (<c>\h</c>), where page numbers are left out (<c>\n</c>, with or without a range of levels) and what stands before a page number
/// (<paramref name="Separator"/>: a tab, or what <c>\p</c> names). Another switch or a value Write cannot read is not a specification: the field is then not updated (<see cref="TryParse"/>).
/// </summary>
internal sealed record TocSpec(int FirstLevel, int LastLevel, bool Outline, string? Label, bool Links, bool NoPages, (int From, int To)? NoPagesLevels, string Separator = "\t")
{
    /// <summary>True for a list of figures or tables (<c>\c</c>).</summary>
    public bool IsFigureList => Label is not null;

    /// <summary>Whether an entry of that level shows a page number.</summary>
    public bool ShowsPage(int level) => !NoPages && (NoPagesLevels is not { } range || level < range.From || level > range.To);

    /// <summary>Whether any level shows a page number.</summary>
    public bool ShowsAnyPage => !NoPages && !(NoPagesLevels is { } range && range.From <= FirstLevel && range.To >= LastLevel);

    /// <summary>Whether an entry needs a bookmark on its target: for the link or for the page number.</summary>
    public bool NeedsBookmark(int level) => Links || ShowsPage(level);

    /// <summary>The specification of the instruction, or null when it is not a <c>TOC</c> Write updates.</summary>
    public static TocSpec? TryParse(string instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var parsed = FieldInstruction.Parse(instruction);
        if (parsed.Name != "TOC" || !FieldSupport.Of(parsed).IsSupported)
        {
            return null;
        }

        var outline = parsed.Has("u");
        var links = parsed.Has("h");
        var noPages = false;
        (int From, int To)? noPagesLevels = null;
        if (parsed.Has("n"))
        {
            if (parsed.ValueOf("n") is { } value)
            {
                if (Range(value) is not { } range)
                {
                    return null;
                }

                noPagesLevels = range;
            }
            else
            {
                noPages = true;
            }
        }

        var separator = "\t";
        if (parsed.Has("p"))
        {
            if (SeparatorOf(parsed.ValueOf("p")) is not { } named)
            {
                return null;
            }

            separator = named;
        }

        if (parsed.Has("c"))
        {
            return parsed.ValueOf("c") is { } label && CaptionLabels.IsUsable(label) ? new TocSpec(1, 1, false, label, links, noPages, noPagesLevels, separator) : null;
        }

        if (parsed.ValueOf("o") is { } levels)
        {
            return Range(levels) is { } range ? new TocSpec(range.From, range.To, outline, null, links, noPages, noPagesLevels, separator) : null;
        }

        return outline ? new TocSpec(1, 9, true, null, links, noPages, noPagesLevels, separator) : null;
    }

    // What Word puts before a page number for the value of \p: its first character, a space for an empty value, a tab without a value. A control character other than a tab is not one Write writes.
    private static string? SeparatorOf(string? value)
    {
        if (value is null)
        {
            return "\t";
        }

        if (value.Length == 0)
        {
            return " ";
        }

        return Rune.TryGetRuneAt(value, 0, out var first) && (!Rune.IsControl(first) || first.Value == '\t') ? first.ToString() : null;
    }

    // "a-b" or "a", levels 1 to 9, first not above last.
    private static (int From, int To)? Range(string text)
    {
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var from = text;
        var to = text;
        if (dash >= 0)
        {
            from = text[..dash];
            to = text[(dash + 1)..];
        }

        if (!int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var a) || !int.TryParse(to, NumberStyles.None, CultureInfo.InvariantCulture, out var b) || a < 1 || b > 9 || a > b)
        {
            return null;
        }

        return (a, b);
    }

    /// <summary>
    /// The instruction a new table of contents gets, with the switches in Word's order: <c>TOC \o "1-3" \h \z \u</c>, with <c>\n</c> for no page numbers and <c>\p " "</c> for page numbers that are not right-aligned, or
    /// <c>TOC \h \z \c "label"</c> for a list, with the same two switches.
    /// </summary>
    public static string InstructionFor(TocOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pages = (options.PageNumbers ? string.Empty : @" \n") + (options.RightAlign ? string.Empty : @" \p "" """);
        return options.Label is { } label
            ? "TOC" + pages + @" \h \z \c """ + label + "\""
            : "TOC \\o \"" + options.FirstLevel.ToString(CultureInfo.InvariantCulture) + "-" + options.LastLevel.ToString(CultureInfo.InvariantCulture) + "\"" + pages + @" \h \z \u";
    }
}

/// <summary>
/// One line of a table of contents: the paragraph it stands for, its level, the number label (empty when there is none), the text (empty only after a label) and whether a tab follows the label (<paramref name="LabelTab"/>; a list
/// level can have a space or nothing there, which the entry shows as a space).
/// </summary>
internal sealed record TocEntry(Paragraph Source, int Level, string Label, string Text, bool LabelTab = false)
{
    /// <summary>What the entry says before its page number: the label, what follows it and the text; the label alone without a text, the text alone without a label.</summary>
    public string Line => Label.Length == 0 ? Text : Text.Length == 0 ? Label : Label + (LabelTab ? "\t" : " ") + Text;

    /// <summary>True when the entry has a tab after its label, so that a tab stop must take the text behind the number.</summary>
    public bool HasLabelTab => LabelTab && Label.Length > 0 && Text.Length > 0;
}

/// <summary>
/// Reads the entries of a table of contents from the body and makes the paragraphs of one, in Word's own shape: every entry is a paragraph of the style <c>TOC1</c> to <c>TOC9</c> (<c>TableofFigures</c> for a list) that holds a
/// <c>w:hyperlink</c> to a hidden <c>_Toc</c> bookmark on the heading, with the number label, what follows it (a tab or a space), the text, the separator before the page number (a tab unless <c>\p</c> names another) and a
/// <c>PAGEREF</c> field. The rules of an entry: a heading is a paragraph of the style <c>Heading1</c> to <c>Heading9</c> (by id, never by name), with <c>\u</c> also a paragraph with an outline level (its own or its style's); its text
/// is the visible text with line breaks and tabs read as spaces, hidden text and tracked deletions left out; the label is the number label the host shows, and a heading without one has none; a heading with no text left is listed as its
/// label alone, as Word does (see <see cref="IsListedWithoutText"/>), and left out without one; a heading in a table or a content control counts; the entries of a table of contents are not headings of another. The tab stops of the
/// entries are <see cref="TocTabs"/>.
/// </summary>
internal static class TocBuilder
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The style id of the entries of a list of figures or tables.</summary>
    public const string FigureStyleId = "TableofFigures";

    /// <summary>The id of the style of the entries of that level (<c>TOC1</c> to <c>TOC9</c>) or of a list.</summary>
    public static string StyleIdFor(int level, bool figureList) => figureList ? FigureStyleId : "TOC" + level.ToString(CultureInfo.InvariantCulture);

    /// <summary>The entries of the specification, in document order.</summary>
    public static List<TocEntry> Collect(WordDocument document, IFieldHost host, TocSpec spec, HashSet<Paragraph> excluded)
    {
        var entries = new List<TocEntry>();
        foreach (var paragraph in BookmarkOperations.ParagraphsOf(document.Body.Blocks))
        {
            if (excluded.Contains(paragraph))
            {
                continue;
            }

            int level;
            string label;
            var tab = false;
            if (spec.Label is { } caption)
            {
                if (!HoldsSequence(paragraph, caption))
                {
                    continue;
                }

                level = 1;
                label = string.Empty;
            }
            else
            {
                if (LevelOf(document, paragraph, spec.Outline) is not { } found || found < spec.FirstLevel || found > spec.LastLevel)
                {
                    continue;
                }

                level = found;
                label = host.NumberLabel(paragraph) ?? string.Empty;
                tab = label.Length > 0 && host.LabelEndsInTab(paragraph);
            }

            var text = VisibleText(paragraph);
            if (text.Length == 0 && (label.Length == 0 || !IsListedWithoutText(paragraph)))
            {
                continue;
            }

            entries.Add(new TocEntry(paragraph, level, label, text, tab));
        }

        return entries;
    }

    /// <summary>
    /// Whether Word lists a numbered heading that has no visible text, as its number: an empty one, one with white space, tabs, line or column breaks, note or comment references, bookmarks, hidden or deleted text. Word leaves out a heading
    /// whose paragraph mark is hidden, and one that holds a page break and no text. A picture, a symbol, a non-breaking hyphen or another object Word lists with a copy of it, which Write does not make, so such a heading stays left out
    /// (a recorded difference); so does one whose paragraph is a tracked deletion.
    /// </summary>
    internal static bool IsListedWithoutText(Paragraph paragraph)
    {
        var mark = paragraph.Properties.Get(W + "rPr");
        return mark?.Element(W + "del") is null && !IsOn(mark?.Element(W + "vanish")) && Blank(paragraph.Inlines);
    }

    private static bool IsOn(XElement? toggle) => toggle is not null && (string?)toggle.Attribute(W + "val") is not ("0" or "false" or "off");

    // True when the shown inlines hold nothing Word would copy into an entry and no page break.
    private static bool Blank(IEnumerable<Inline> inlines) => inlines.All(inline => inline switch
    {
        Run run => run.Properties.GetFlag(W + "vanish") || run.Content.All(content => content switch
        {
            TextContent or TabContent or FootnoteReference or EndnoteReference or CommentReference or FieldChar or InstructionText or DeletedText => true,
            BreakContent b => b.Kind != BreakKind.Page,
            UnknownRunContent unknown => unknown.Content.Element.Name.LocalName is "cr" or "ptab" or "lastRenderedPageBreak" or "annotationRef",
            _ => false,
        }),
        Hyperlink link => Blank(link.Inlines),
        SimpleField simple => Blank(simple.Inlines),
        ContentControlInline control => Blank(control.Inlines),
        TrackedChange change => change.Kind is TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom || Blank(change.Inlines),
        BookmarkStart or BookmarkEnd or CommentRangeStart or CommentRangeEnd => true,
        UnknownInline unknown => unknown.Content.Element.Name.LocalName is "proofErr" or "permStart" or "permEnd" or "moveFromRangeStart" or "moveFromRangeEnd" or "moveToRangeStart" or "moveToRangeEnd",
        _ => false,
    });

    // A caption Word lists: a sequence field of the label that it counts (also one in a tracked insertion), not a hidden one (\h without a number format).
    private static bool HoldsSequence(Paragraph paragraph, string label) =>
        FieldRefresh.HasFieldMarkup(paragraph)
        && FieldView.InParagraph(paragraph).Any(f => f.Parent is null && SeqCounter.Counts(f) && (!f.Instruction.Has("h") || f.Instruction.Has("*")) && string.Equals(f.Instruction.Argument, label, StringComparison.OrdinalIgnoreCase));

    /// <summary>The level of a heading paragraph: its <c>Heading1</c> to <c>Heading9</c> style id, else (<paramref name="outline"/>) its outline level, else null.</summary>
    public static int? LevelOf(WordDocument document, Paragraph paragraph, bool outline) =>
        FieldRefresh.HeadingLevel(paragraph) ?? (outline ? OutlineLevel(document, paragraph) : null);

    /// <summary>The outline level (1 to 9) of the paragraph: its own <c>w:outlineLvl</c>, else the one of its style or the styles it is based on; level 10 (body text) and none give null.</summary>
    public static int? OutlineLevel(WordDocument document, Paragraph paragraph)
    {
        if (ReadOutline(paragraph.Properties.Get(W + "outlineLvl")) is { } own)
        {
            return own;
        }

        var seen = 0;
        for (var style = paragraph.Properties.StyleId is { } id ? document.Styles?.Find(id) : null; style is not null && seen < 32; seen++)
        {
            if (style.ParagraphProperties.Get(W + "outlineLvl") is { } element)
            {
                return ReadOutline(element);
            }

            style = style.BasedOn is { } parent ? document.Styles?.Find(parent) : null;
        }

        return null;
    }

    private static int? ReadOutline(XElement? element) =>
        element is not null && int.TryParse((string?)element.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 8 ? value + 1 : null;

    /// <summary>The visible text of a paragraph as an entry shows it: trimmed, a tab or a line break read as one space, hidden text, instruction text and tracked deletions left out, field results kept.</summary>
    public static string VisibleText(Paragraph paragraph)
    {
        var text = new StringBuilder();
        Append(text, paragraph.Inlines);
        return text.ToString().Trim();
    }

    private static void Append(StringBuilder text, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run when !run.Properties.GetFlag(W + "vanish"):
                    foreach (var content in run.Content)
                    {
                        switch (content)
                        {
                            case TextContent t:
                                text.Append(t.Text);
                                break;
                            case TabContent or BreakContent:
                                text.Append(' ');
                                break;
                            default:
                                break;
                        }
                    }

                    break;
                case Hyperlink link:
                    Append(text, link.Inlines);
                    break;
                case SimpleField simple:
                    Append(text, simple.Inlines);
                    break;
                case ContentControlInline control:
                    Append(text, control.Inlines);
                    break;
                case TrackedChange change when change.Kind is not (TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom):
                    Append(text, change.Inlines);
                    break;
                default:
                    break;
            }
        }
    }

    // ---- the paragraphs ----------------------------------------------------------------------------------------------------------------------

    /// <summary>The sentence an empty table of contents shows, in the language of the document (German for <c>de</c>, English otherwise).</summary>
    public static string EmptySentence(WordDocument document) =>
        CaptionLabels.DocumentLanguage(document) == "de" ? "Es wurden keine Einträge für das Inhaltsverzeichnis gefunden." : "No table of contents entries found.";

    /// <summary>A right-aligned tab stop at <paramref name="position"/> twips with the leader, as Word writes it into the paragraphs of a table of contents.</summary>
    public static XElement RightTab(int position, TocLeader leader)
    {
        var tab = new XElement(W + "tab", new XAttribute(W + "val", "right"));
        var name = leader switch
        {
            TocLeader.Dots => "dot",
            TocLeader.Hyphens => "hyphen",
            TocLeader.Underscore => "underscore",
            _ => null,
        };
        if (name is not null)
        {
            tab.Add(new XAttribute(W + "leader", name));
        }

        tab.Add(new XAttribute(W + "pos", position.ToString(CultureInfo.InvariantCulture)));
        return new XElement(W + "tabs", tab);
    }

    /// <summary>The width of the text area of the section that holds the top-level block, in twips (the position of the right tab stop).</summary>
    public static int TextWidthTwips(WordDocument document, int topLevelIndex)
    {
        var sections = SectionList.Of(document);
        var info = sections.FirstOrDefault(s => topLevelIndex >= s.FirstBlock && topLevelIndex <= s.LastBlock) ?? (sections.Count > 0 ? sections[^1] : null);
        var setup = SectionPageSetup.Read(info?.Properties);
        return Math.Max(720, setup.WidthTwips - setup.LeftTwips - setup.RightTwips);
    }

    /// <summary>
    /// The paragraphs of the entries (the sentence of an empty list when there are none), detached and without the marks of the field. <paramref name="bookmarkOf"/> gives the hidden bookmark of an entry (null: none can be made, the entry is
    /// plain text) and <paramref name="pageOf"/> the page text of an entry's bookmark. <paramref name="tabsOf"/> gives the tab stops of a paragraph that has its style (the entry is null for the sentence): a new element, or null for none.
    /// </summary>
    public static List<Paragraph> Entries(WordDocument document, TocSpec spec, IReadOnlyList<TocEntry> entries, Func<TocEntry, string?> bookmarkOf, Func<TocEntry, string?, string> pageOf, Func<TocEntry?, Paragraph, XElement?> tabsOf)
    {
        var paragraphs = new List<Paragraph>(Math.Max(1, entries.Count));
        if (entries.Count == 0)
        {
            var sentence = new Paragraph();
            sentence.Properties.StyleId = StyleIdFor(spec.FirstLevel, spec.IsFigureList);
            if (tabsOf(null, sentence) is { } own)
            {
                sentence.Properties.Set(W + "tabs", own);
            }

            sentence.Inlines.Add(TextRun(EmptySentence(document)));
            paragraphs.Add(sentence);
            return paragraphs;
        }

        foreach (var entry in entries)
        {
            var bookmark = spec.NeedsBookmark(entry.Level) ? bookmarkOf(entry) : null;
            var paragraph = new Paragraph();
            paragraph.Properties.StyleId = StyleIdFor(entry.Level, spec.IsFigureList);
            if (tabsOf(entry, paragraph) is { } tabs)
            {
                paragraph.Properties.Set(W + "tabs", tabs);
            }

            IList<Inline> target = paragraph.Inlines;
            if (spec.Links && bookmark is not null)
            {
                var link = new Hyperlink { Anchor = bookmark };
                link.UnknownAttributes.Add(new XAttribute(W + "history", "1"));
                paragraph.Inlines.Add(link);
                target = link.Inlines;
            }

            if (entry.Label.Length > 0)
            {
                target.Add(TextRun(entry.Label));
                if (entry.Text.Length > 0)
                {
                    target.Add(entry.LabelTab ? TabRun() : TextRun(" "));
                }
            }

            if (entry.Text.Length > 0)
            {
                target.Add(TextRun(entry.Text));
            }

            if (bookmark is not null && spec.ShowsPage(entry.Level))
            {
                target.Add(spec.Separator == "\t" ? TabRun() : TextRun(spec.Separator));
                foreach (var inline in FieldWriter.Make("PAGEREF " + bookmark + (spec.Links ? @" \h" : string.Empty), pageOf(entry, bookmark), null))
                {
                    target.Add(inline);
                }
            }

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    private static Run TextRun(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Run TabRun()
    {
        var run = new Run();
        run.Content.Add(new TabContent());
        return run;
    }

    /// <summary>
    /// Adds the paragraph styles the entries of the specification use when the document lacks them (<c>TOC1</c> to <c>TOC9</c> for the levels, <c>TableofFigures</c> for a list): spacing after, an indent per level.
    /// Returns the change a step records (old and new are equal clones: the styles are made in place) or null when every style exists or the document has no style sheet.
    /// </summary>
    public static StyleChange? EnsureStyles(WordDocument document, TocSpec spec)
    {
        if (document.Styles is not { } sheet)
        {
            return null;
        }

        var made = new List<Style>();
        var levels = spec.IsFigureList ? [1] : Enumerable.Range(spec.FirstLevel, spec.LastLevel - spec.FirstLevel + 1);
        foreach (var level in levels)
        {
            var id = StyleIdFor(level, spec.IsFigureList);
            if (sheet.Find(id) is not null)
            {
                continue;
            }

            var normal = sheet.Find("Normal") is not null;
            var style = new Style
            {
                Id = id,
                Type = "paragraph",
                Name = spec.IsFigureList ? "table of figures" : "toc " + level.ToString(CultureInfo.InvariantCulture),
                BasedOn = normal ? "Normal" : null,
                Next = normal ? "Normal" : null,
            };
            style.ParagraphProperties.Set(W + "spacing", new XElement(W + "spacing", new XAttribute(W + "after", "100")));
            if (!spec.IsFigureList && level > 1)
            {
                style.ParagraphProperties.Set(W + "ind", new XElement(W + "ind", new XAttribute(W + "left", ((level - 1) * 220).ToString(CultureInfo.InvariantCulture))));
            }

            style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "uiPriority", new XAttribute(W + "val", "39")))));
            style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "unhideWhenUsed"))));
            sheet.Styles.Add(style);
            made.Add(style);
        }

        return made.Count == 0
            ? null
            : new StyleChange(made[0].Clone(), made[0].Clone()) { Companions = [.. made.Skip(1).Select(s => new StyleChange(s.Clone(), s.Clone()))] };
    }
    // ---- what an entry paragraph says (the comparison of a new one with an old one) ---------------------------------------------------------------

    /// <summary>
    /// What a paragraph of a table of contents says, for telling whether a refresh changes it: its style, its tab stops, its field marks and instructions, its link targets and its text. The formatting of runs is left out on
    /// purpose, so an entry that already says the right thing keeps the formatting a file gave it.
    /// </summary>
    public static string Signature(Paragraph paragraph)
    {
        var text = new StringBuilder();
        text.Append(paragraph.Properties.StyleId).Append('|').Append(paragraph.Properties.Get(W + "tabs")?.ToString(SaveOptions.DisableFormatting)).Append('|');
        Sign(text, paragraph.Inlines);
        return text.ToString();
    }

    private static void Sign(StringBuilder text, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        switch (content)
                        {
                            case TextContent t:
                                text.Append(t.Text);
                                break;
                            case TabContent:
                                text.Append('\t');
                                break;
                            case InstructionText i:
                                text.Append('{').Append(i.Text.Trim()).Append('}');
                                break;
                            case FieldChar f:
                                text.Append('<').Append((int)f.Kind).Append('>');
                                break;
                            default:
                                text.Append('?');
                                break;
                        }
                    }

                    break;
                case Hyperlink link:
                    text.Append("[#").Append(link.Anchor).Append('|').Append(link.RelationshipId).Append(':');
                    Sign(text, link.Inlines);
                    text.Append(']');
                    break;
                case BookmarkStart start:
                    text.Append("(bs:").Append(start.Name).Append(')');
                    break;
                case BookmarkEnd:
                    text.Append("(be)");
                    break;
                default:
                    text.Append('~').Append(inline.GetType().Name);
                    break;
            }
        }
    }
}
