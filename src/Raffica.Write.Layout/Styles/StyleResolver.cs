using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// Turns the styles, defaults and direct formatting of a document into plain values the layout reads. Order: document defaults, the
/// table style of a cell, the <c>basedOn</c> chain of the paragraph style (a cycle stops at the repeat), numbering indents, direct
/// formatting; for runs the character style comes after the paragraph's. A style id that does not exist falls back to the default
/// style. The result for a style is cached, so the chain is walked once per style and never per run. Not thread-safe: one layout
/// uses one resolver at a time.
/// </summary>
public sealed class StyleResolver
{
    private readonly Dictionary<string, Style> _byId = new(StringComparer.Ordinal);
    private readonly Style? _defaultParagraph;
    private readonly Style? _defaultTable;
    private readonly List<System.Xml.Linq.XElement> _defaultParagraphProperties = [];
    private readonly RunProps _defaultRun = new();
    private readonly NumberingDefinitions? _numbering;
    private readonly DocumentSettings? _settings;
    private readonly Dictionary<string, ResolvedParagraph> _paragraphs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunProps?> _characterLayers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TableStyleData> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<(int NumId, int Level), NumberingLevel?> _levels = [];

    public StyleResolver(StyleSheet? styles, DocumentSettings? settings, NumberingDefinitions? numbering = null)
    {
        _settings = settings;
        _numbering = numbering;
        if (styles is null)
        {
            return;
        }

        foreach (var style in styles.Styles)
        {
            if (style.Id.Length > 0)
            {
                _byId.TryAdd(style.Id, style);
            }

            if (style.IsDefault)
            {
                if (style.Type is "paragraph")
                {
                    _defaultParagraph ??= style;
                }
                else if (style.Type is "table")
                {
                    _defaultTable ??= style;
                }
            }
        }

        if (styles.Defaults is { } defaults)
        {
            _defaultRun.Overlay(defaults.Element(Ooxml.W + "rPrDefault")?.Element(Ooxml.W + "rPr")?.Elements() ?? []);
            _defaultParagraphProperties.AddRange(defaults.Element(Ooxml.W + "pPrDefault")?.Element(Ooxml.W + "pPr")?.Elements() ?? []);
        }
    }

    /// <summary>True when the document asks for automatic hyphenation.</summary>
    public bool AutoHyphenation => _settings?.AutoHyphenation ?? false;

    public ResolvedParagraph ForParagraph(Paragraph paragraph) => ForParagraph(paragraph, null);

    /// <summary>Resolves a paragraph inside a table cell: the table style's paragraph and run properties come before the paragraph's own style.</summary>
    public ResolvedParagraph ForParagraph(Paragraph paragraph, ResolvedCellFormat? cell)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var properties = paragraph.Properties;
        var styleId = properties.StyleId;
        var direct = HasDirect(properties, "pStyle");
        var cache = cell?.Layer.Paragraphs ?? _paragraphs;
        var key = styleId ?? string.Empty;
        if (!direct && cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = Build(styleId, direct ? properties : null, cell?.Layer);
        if (!direct)
        {
            cache[key] = resolved;
        }

        return resolved;
    }

    public ResolvedRun ForRun(ResolvedParagraph owner, Run run)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(run);
        var properties = run.Properties;
        var styleId = properties.StyleId;
        if (HasDirect(properties, "rStyle"))
        {
            return Compose(owner.BaseRun, styleId, properties.Children, paragraphMark: false);
        }

        return owner.CachedRun(styleId ?? string.Empty, () => Compose(owner.BaseRun, styleId, [], paragraphMark: false));
    }

    /// <summary>The formatting of the paragraph mark (and so of an empty paragraph's line): the paragraph's run base, the mark's character style and its direct properties.</summary>
    public ResolvedRun ForParagraphMark(ResolvedParagraph owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var mark = owner.MarkRunProperties;
        var styleId = mark?.Element(Ooxml.W + "rStyle") is { } style ? Ooxml.Val(style) : null;
        return Compose(owner.BaseRun, styleId, mark?.Elements() ?? [], paragraphMark: true);
    }

    public ResolvedTable ForTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var styleId = table.Properties.StyleId;
        var key = styleId ?? string.Empty;
        if (!_tables.TryGetValue(key, out var data))
        {
            data = new TableStyleData(Chain(styleId, _defaultTable));
            _tables[key] = data;
        }

        return new ResolvedTable(data, table.Properties);
    }

    private static bool HasDirect(PropertyBag bag, string styleName)
    {
        var style = Ooxml.W + styleName;
        foreach (var child in bag.Children)
        {
            if (child.Name != style)
            {
                return true;
            }
        }

        return false;
    }

    private ResolvedParagraph Build(string? styleId, PropertyBag? direct, CellLayer? cell)
    {
        var props = new ParaProps();
        props.Overlay(_defaultParagraphProperties);
        var run = _defaultRun.Clone();
        if (cell is not null)
        {
            props.Overlay(cell.Paragraph);
            run.CombineStyleLayer(cell.Run);
        }

        var chain = Chain(styleId, _defaultParagraph);
        var layer = new RunProps();
        foreach (var style in chain)
        {
            props.Overlay(style.ParagraphProperties.Children);
            layer.Overlay(style.RunProperties.Children);
        }

        run.CombineStyleLayer(layer);
        if (direct is not null)
        {
            props.OverlayNumbering(direct.Children);
        }

        ApplyNumberingLevel(props);
        if (direct is not null)
        {
            props.Overlay(direct.Children);
        }

        return new ResolvedParagraph(props, run, styleId ?? _defaultParagraph?.Id, direct?.Get(Ooxml.W + "rPr"));
    }

    private void ApplyNumberingLevel(ParaProps props)
    {
        if (props.NumId is not > 0)
        {
            return;
        }

        if (FindLevel(props.NumId.Value, props.Ilvl ?? 0) is { } level)
        {
            props.Overlay(level.ParagraphProperties.Children);
        }
    }

    private NumberingLevel? FindLevel(int numId, int level)
    {
        if (_numbering is null)
        {
            return null;
        }

        if (!_levels.TryGetValue((numId, level), out var found))
        {
            found = LookupLevel(_numbering, numId, level);
            _levels[(numId, level)] = found;
        }

        return found;
    }

    internal static NumberingLevel? LookupLevel(NumberingDefinitions numbering, int numId, int level)
    {
        var instance = numbering.FindInstance(numId);
        if (instance is null)
        {
            return null;
        }

        foreach (var over in instance.Overrides)
        {
            if (over.Level == level && over.Definition is { } definition)
            {
                return definition;
            }
        }

        if (instance.AbstractNumId is not { } abstractId || numbering.FindAbstract(abstractId) is not { } abstractNumbering)
        {
            return null;
        }

        foreach (var candidate in abstractNumbering.Levels)
        {
            if (candidate.Level == level)
            {
                return candidate;
            }
        }

        return null;
    }

    private ResolvedRun Compose(RunProps baseRun, string? characterStyle, IEnumerable<System.Xml.Linq.XElement> direct, bool paragraphMark)
    {
        var run = baseRun.Clone();
        if (characterStyle is not null && CharacterLayer(characterStyle) is { } layer)
        {
            run.CombineStyleLayer(layer);
        }

        run.Overlay(direct);
        return run.ToResolved(paragraphMark);
    }

    private RunProps? CharacterLayer(string styleId)
    {
        if (_characterLayers.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        RunProps? layer = null;
        if (_byId.ContainsKey(styleId))
        {
            layer = new RunProps();
            foreach (var style in Chain(styleId, null))
            {
                layer.Overlay(style.RunProperties.Children);
            }
        }

        _characterLayers[styleId] = layer;
        return layer;
    }

    /// <summary>The style and the styles it is based on, base first. A cycle ends at the first style seen twice; an unknown id gives the fallback's chain.</summary>
    private List<Style> Chain(string? id, Style? fallback)
    {
        var current = id is not null && _byId.TryGetValue(id, out var found) ? found : fallback;
        var chain = new List<Style>();
        var seen = new HashSet<Style>(ReferenceEqualityComparer.Instance);
        while (current is not null && seen.Add(current))
        {
            chain.Add(current);
            current = current.BasedOn is { } parent && _byId.TryGetValue(parent, out var next) ? next : null;
        }

        chain.Reverse();
        return chain;
    }
}
