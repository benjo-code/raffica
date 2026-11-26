using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a <c>TOC</c> instruction asks for, as the refresh and the insert read it: the levels of the headings (<c>\o "a-b"</c>, all nine with only <c>\u</c>), whether outline levels count (<c>\u</c>), the caption label of a list
/// of figures or tables (<c>\c "label"</c>, which replaces the levels), whether entries are links (<c>\h</c>) and where page numbers are left out (<c>\n</c>, with or without a range of levels). Another switch or a value Write cannot read
/// is not a specification: the field is then not updated (<see cref="TryParse"/>).
/// </summary>
internal sealed record TocSpec(int FirstLevel, int LastLevel, bool Outline, string? Label, bool Links, bool NoPages, (int From, int To)? NoPagesLevels)
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

        if (parsed.Has("c"))
        {
            return parsed.ValueOf("c") is { } label && CaptionLabels.IsUsable(label) ? new TocSpec(1, 1, false, label, links, noPages, noPagesLevels) : null;
        }

        if (parsed.ValueOf("o") is { } levels)
        {
            return Range(levels) is { } range ? new TocSpec(range.From, range.To, outline, null, links, noPages, noPagesLevels) : null;
        }

        return outline ? new TocSpec(1, 9, true, null, links, noPages, noPagesLevels) : null;
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

    /// <summary>The instruction a new table of contents gets (<c>TOC \o "1-3" \h \z \u</c>, with <c>\n</c> for no page numbers, or <c>TOC \h \z \c "label"</c> for a list).</summary>
    public static string InstructionFor(TocOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pages = options.PageNumbers ? string.Empty : @" \n";
        return options.Label is { } label
            ? "TOC" + pages + @" \h \z \c """ + label + "\""
            : "TOC \\o \"" + options.FirstLevel.ToString(CultureInfo.InvariantCulture) + "-" + options.LastLevel.ToString(CultureInfo.InvariantCulture) + "\"" + pages + @" \h \z \u";
    }
}

/// <summary>One line of a table of contents: the paragraph it stands for, its level, the number label (empty when there is none) and the text.</summary>
internal sealed record TocEntry(Paragraph Source, int Level, string Label, string Text);

/// <summary>
/// Reads the entries of a table of contents from the body and makes the paragraphs of one, in Word's own shape: every entry is a paragraph of the style <c>TOC1</c> to <c>TOC9</c> (<c>TableofFigures</c> for a list) that holds a
/// <c>w:hyperlink</c> to a hidden <c>_Toc</c> bookmark on the heading, with the number label, a tab, the text, a tab and a <c>PAGEREF</c> field. The rules of an entry: a heading is a paragraph of the style <c>Heading1</c> to
/// <c>Heading9</c> (by id, never by name), with <c>\u</c> also a paragraph with an outline level (its own or its style's); its text is the visible text with line breaks and tabs read as spaces, hidden text and tracked deletions left out;
/// an entry with no text left is left out; the label is the number label the host shows, and a heading without one has none; a heading in a table or a content control counts; the entries of a table of contents
/// are not headings of another.
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
            }

            var text = VisibleText(paragraph);
            if (text.Length == 0)
            {
                continue;
            }

            entries.Add(new TocEntry(paragraph, level, label, text));
        }

        return entries;
    }

    private static bool HoldsSequence(Paragraph paragraph, string label) =>
        FieldRefresh.HasFieldMarkup(paragraph)
        && FieldView.InParagraph(paragraph).Any(f => f.Parent is null && f.Support.Kind == FieldKind.Seq && FieldRefresh.IsUsable(f) && string.Equals(f.Instruction.Argument, label, StringComparison.OrdinalIgnoreCase));

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
    /// plain text) and <paramref name="pageOf"/> the page text of an entry's bookmark. <paramref name="tabs"/> is the tab stop element written into every paragraph (null for none).
    /// </summary>
    public static List<Paragraph> Entries(WordDocument document, TocSpec spec, IReadOnlyList<TocEntry> entries, Func<TocEntry, string?> bookmarkOf, Func<TocEntry, string?, string> pageOf, XElement? tabs)
    {
        var paragraphs = new List<Paragraph>(Math.Max(1, entries.Count));
        if (entries.Count == 0)
        {
            var sentence = new Paragraph();
            sentence.Properties.StyleId = StyleIdFor(spec.FirstLevel, spec.IsFigureList);
            if (tabs is not null)
            {
                sentence.Properties.Set(W + "tabs", new XElement(tabs));
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
            if (tabs is not null)
            {
                paragraph.Properties.Set(W + "tabs", new XElement(tabs));
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
                target.Add(TabRun());
            }

            target.Add(TextRun(entry.Text));
            if (bookmark is not null && spec.ShowsPage(entry.Level))
            {
                target.Add(TabRun());
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
