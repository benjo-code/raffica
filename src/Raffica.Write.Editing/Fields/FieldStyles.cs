using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What the fields ask the style sheet, answered as Word 16 answers it (<c>Docs/write-word-check.md</c>, "Fields, captions and tables of contents (milestone 4.5b)"). This is the one definition of these rules for the field code.
/// <para>
/// Word knows its built-in styles by their <b>name</b>, never by their id (a German file calls Heading 1 <c>berschrift1</c> and names it <c>heading 1</c>): the built-in style of a name is a paragraph style (a <c>w:type</c> that is
/// absent counts) that is not a custom one (<c>w:customStyle</c>) and whose <c>w:name</c> is exactly that name in any case (a name with a list of aliases is another name). The style of a paragraph is the paragraph style its
/// <c>w:pStyle</c> names, else the default paragraph style. A <b>heading style</b> is the built-in <c>heading 1</c> to <c>heading 9</c>. The <b>style outline level</b> of a paragraph is decided by the first style of its chain
/// (the style, then <c>w:basedOn</c>) that has an own <c>w:outlineLvl</c> (body text is no level) or is a heading style (its number). The entry styles of a table of contents are the built-in <c>toc 1</c> to <c>toc 9</c> and
/// <c>table of figures</c>, the caption style is the built-in <c>caption</c>; where the file has none, the one Write makes takes an id no style has.
/// </para>
/// <para>One object serves one operation or pass: it reads the style sheet when first asked and keeps what it read (never one look-up in the list per paragraph). Styles change between operations, so it is not kept across them.</para>
/// </summary>
internal sealed class FieldStyles(WordDocument document)
{
    private const int MaxChain = 32;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XName s_customStyle = W + "customStyle";

    private Dictionary<string, Style>? _byId;
    private Dictionary<string, string>? _builtIns;
    private Style? _default;
    private readonly Dictionary<Style, int?> _levels = new(ReferenceEqualityComparer.Instance);

    /// <summary>The first style with that id, as <see cref="StyleSheet.Find"/> answers, or null.</summary>
    public Style? Find(string id)
    {
        Read();
        return _byId!.GetValueOrDefault(id);
    }

    /// <summary>The level (1 to 9) of the heading style of the paragraph, or null when its style is not a heading style.</summary>
    public int? HeadingLevel(Paragraph paragraph) => HeadingOf(StyleOf(paragraph));

    /// <summary>The outline level (1 to 9) the style of the paragraph gives it, or null for body text. A <c>w:outlineLvl</c> of the paragraph itself is not looked at.</summary>
    public int? StyleOutlineLevel(Paragraph paragraph)
    {
        if (StyleOf(paragraph) is not { } own)
        {
            return null;
        }

        if (!_levels.TryGetValue(own, out var level))
        {
            var seen = 0;
            for (var style = own; style is not null && seen < MaxChain; seen++)
            {
                if (style.ParagraphProperties.Get(W + "outlineLvl") is { } element)
                {
                    level = ReadOutline(element);
                    break;
                }

                if (HeadingOf(style) is { } heading)
                {
                    level = heading;
                    break;
                }

                style = style.BasedOn is { } parent ? Find(parent) : null;
            }

            _levels[own] = level;
        }

        return level;
    }

    /// <summary>
    /// The level a table of contents over headings lists the paragraph at, or null: a heading style gives its own level (a <c>w:outlineLvl</c> of the paragraph is ignored); else, with <paramref name="outline"/> (the <c>\u</c> switch), the
    /// <c>w:outlineLvl</c> of the paragraph when it has one (body text is no level, whatever its style says); else the style outline level.
    /// </summary>
    public int? ListLevel(Paragraph paragraph, bool outline)
    {
        if (HeadingLevel(paragraph) is { } heading)
        {
            return heading;
        }

        return outline && paragraph.Properties.Get(W + "outlineLvl") is { } own ? ReadOutline(own) : StyleOutlineLevel(paragraph);
    }

    /// <summary>Whether some table of contents could list the paragraph: it has a heading style, a style outline level or an outline level of its own.</summary>
    public bool CanBeListed(Paragraph paragraph) => ListLevel(paragraph, outline: true) is not null || ListLevel(paragraph, outline: false) is not null;

    /// <summary>The id of the entry style of that level (the built-in <c>toc 1</c> to <c>toc 9</c>) or of a list of figures (<c>table of figures</c>), or null when the document has none.</summary>
    public string? ExistingEntryStyleId(int level, bool figureList) => BuiltIn(EntryName(level, figureList));

    /// <summary>The id the entries of that level (or of a list of figures) get: the one of the entry style, else the id Write would give the style it makes.</summary>
    public string EntryStyleId(int level, bool figureList) =>
        ExistingEntryStyleId(level, figureList) ?? FreeId(figureList ? "TableofFigures" : "TOC" + level.ToString(CultureInfo.InvariantCulture));

    /// <summary>The id of the caption style (the built-in <c>caption</c>), or null when the document has none.</summary>
    public string? CaptionStyleId => BuiltIn("caption");

    /// <summary>The built-in style <c>heading</c> and the level, or null when the document has none.</summary>
    public Style? HeadingStyle(int level) => BuiltIn("heading " + level.ToString(CultureInfo.InvariantCulture)) is { } id ? Find(id) : null;

    /// <summary>The name of the entry style of that level or of a list of figures.</summary>
    public static string EntryName(int level, bool figureList) => figureList ? "table of figures" : "toc " + level.ToString(CultureInfo.InvariantCulture);

    /// <summary>The id a style Write makes for a field is based on: the style <c>Normal</c> by id, else the default paragraph style (<c>Standard</c> in a German file), else none.</summary>
    public string? BaseStyleId => Find("Normal") is { } normal && IsParagraphStyle(normal) ? normal.Id : _default?.Id is { Length: > 0 } id ? id : null;

    /// <summary><paramref name="id"/>, or when a style has it the first of <paramref name="id"/> followed by 1, 2, ... that none has.</summary>
    public string FreeId(string id)
    {
        var free = id;
        for (var n = 1; Find(free) is not null; n++)
        {
            free = id + n.ToString(CultureInfo.InvariantCulture);
        }

        return free;
    }

    /// <summary>Adds a style Write made to the style sheet of the document and to what this object knows.</summary>
    public void Add(Style style)
    {
        Read();
        document.Styles!.Styles.Add(style);
        Note(style);
        _levels.Clear();
    }

    // The paragraph style the pStyle of the paragraph names, else the default paragraph style.
    private Style? StyleOf(Paragraph paragraph)
    {
        Read();
        return paragraph.Properties.StyleId is { } id && _byId!.TryGetValue(id, out var style) && IsParagraphStyle(style) ? style : _default;
    }

    private string? BuiltIn(string name)
    {
        Read();
        return _builtIns!.GetValueOrDefault(name);
    }

    private void Read()
    {
        if (_byId is not null)
        {
            return;
        }

        _byId = new Dictionary<string, Style>(StringComparer.Ordinal);
        _builtIns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in document.Styles?.Styles ?? [])
        {
            Note(style);
        }
    }

    // The first style of an id, of a built-in name and of the default mark counts (Word reads two built-in styles of one name as the first).
    private void Note(Style style)
    {
        _byId!.TryAdd(style.Id, style);
        if (!IsParagraphStyle(style))
        {
            return;
        }

        if (style.IsDefault)
        {
            _default ??= style;
        }

        if (style.Id.Length > 0 && style.Name is { } name && !IsCustom(style))
        {
            _builtIns!.TryAdd(name, style.Id);
        }
    }

    private static bool IsParagraphStyle(Style style) => style.Type is null || string.Equals(style.Type, "paragraph", StringComparison.OrdinalIgnoreCase);

    private static bool IsCustom(Style style) => style.UnknownAttributes.Any(a => a.Name == s_customStyle && a.Value is "1" or "true" or "on");

    // The cheap tests of the name first: this runs for every paragraph.
    private static int? HeadingOf(Style? style) =>
        style is { Name: { Length: 9 } name } && name[8] is >= '1' and <= '9' && name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) && IsParagraphStyle(style) && !IsCustom(style) ? name[8] - '0' : null;

    // 0 to 8 are the levels 1 to 9; 9 (body text) and anything else give none.
    private static int? ReadOutline(XElement element) =>
        int.TryParse((string?)element.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 8 ? value + 1 : null;
}
