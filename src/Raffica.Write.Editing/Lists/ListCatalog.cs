using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Finds and adds the numbering definitions the list commands use. A plain class that holds only the ids of the <c>w:num</c> it made
/// itself (so a toggled-off list is reused, not added again). A definition or instance is never removed, and nothing here reads the
/// document before the first call.
/// </summary>
public sealed class ListCatalog
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly WordDocument _document;
    private readonly HashSet<int> _created = [];

    public ListCatalog(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    /// <summary>
    /// The document's own list of this kind. The list the List Bullet or List Number style points at wins (even though Word links such
    /// a definition to that style with <c>w:pStyle</c>; definitions linked to the Heading1 to Heading9 styles are never picked),
    /// otherwise the first one by position that is linked to no style and whose level 0 is of the kind. 0 when there is none.
    /// </summary>
    public int FindList(ListKind kind)
    {
        var numbering = _document.Numbering;
        if (numbering is null)
        {
            return 0;
        }

        if (StylePointedAt(kind) is { } pointed
            && numbering.FindInstance(pointed) is { } styled
            && LevelZeroKind(numbering, styled) == kind)
        {
            return pointed;
        }

        foreach (var instance in InPartOrder(numbering.Instances))
        {
            if (instance.NumId > 0
                && FindAbstract(numbering, instance) is { } abstractNumbering
                && !IsLinkedToStyles(abstractNumbering)
                && LevelZeroKind(abstractNumbering) == kind)
            {
                return instance.NumId;
            }
        }

        return 0;
    }

    /// <summary>The document's own list of this kind, else Write's definition with a new <c>w:num</c>.</summary>
    public int EnsureList(ListKind kind)
    {
        var found = FindList(kind);
        if (found != 0)
        {
            return found;
        }

        var numbering = Required();
        var id = NextAbstractId(numbering);
        var added = kind == ListKind.Bullet ? WriteListDefinitions.Bullet(id) : WriteListDefinitions.Numbered(id);
        numbering.Abstracts.Add(added);
        return AddInstance(numbering, id, track: true);
    }

    /// <summary>The kind of <paramref name="level"/> of the list <paramref name="numId"/> (a level override of the <c>w:num</c> wins, a level the definition lacks answers with level 0); null when the list or its format does not exist, or the format is none.</summary>
    public ListKind? KindOf(int numId, int level)
    {
        if (_document.Numbering is not { } numbering || numbering.FindInstance(numId) is not { } instance)
        {
            return null;
        }

        var format = instance.Overrides.FirstOrDefault(o => o.Level == level && o.Definition is not null)?.Definition?.Format;
        if (format is null && FindAbstract(numbering, instance) is { } abstractNumbering)
        {
            format = (abstractNumbering.Levels.FirstOrDefault(l => l.Level == level) ?? abstractNumbering.Levels.FirstOrDefault(l => l.Level == 0))?.Format;
        }

        return format switch
        {
            null or "none" => null,
            "bullet" => ListKind.Bullet,
            _ => ListKind.Numbered,
        };
    }

    /// <summary>A <c>w:num</c> for a fresh run of the kind: an unreferenced one this catalog made on the same definition, else a new one.</summary>
    public int NewRun(ListKind kind)
    {
        var baseId = EnsureList(kind);
        var numbering = Required();
        if (numbering.FindInstance(baseId)?.AbstractNumId is not { } abstractId)
        {
            return baseId;
        }

        foreach (var instance in InPartOrder(numbering.Instances))
        {
            if (_created.Contains(instance.NumId)
                && instance.AbstractNumId == abstractId
                && instance.Overrides.Count == 0
                && !IsReferenced(instance.NumId))
            {
                return instance.NumId;
            }
        }

        return AddInstance(numbering, abstractId, track: true);
    }

    /// <summary>A new <c>w:num</c> on the same definition with a <c>w:startOverride</c> of 1 at <paramref name="level"/>.</summary>
    public int Restart(int numId, int level)
    {
        var numbering = Required();
        var abstractId = numbering.FindInstance(numId)?.AbstractNumId
            ?? throw new ArgumentException("The list does not exist.", nameof(numId));
        var id = AddInstance(numbering, abstractId, track: false);
        numbering.FindInstance(id)!.Overrides.Add(new LevelOverride { Level = level, StartOverride = 1 });
        return id;
    }

    /// <summary>True when a paragraph of the body (tables and block content controls included), a note, a header or footer, or a style points at the list. It does not look into row-level content controls, drawing text boxes or comments.</summary>
    public bool IsReferenced(int numId)
    {
        if (numId <= 0)
        {
            return false;
        }

        if (_document.Styles is { } styles && styles.Styles.Any(s => s.ParagraphProperties.NumberingId == numId))
        {
            return true;
        }

        if (BlocksReference(_document.Body.Blocks, numId))
        {
            return true;
        }

        foreach (var notes in new[] { _document.Footnotes, _document.Endnotes })
        {
            if (notes is not null && notes.Notes.Any(n => BlocksReference(n.Content.Blocks, numId)))
            {
                return true;
            }
        }

        return _document.HeadersFooters.Values.Any(p => BlocksReference(p.Content.Blocks, numId));
    }

    /// <summary>
    /// The list of the headings: the instance of a definition already linked to Heading1 to Heading9, else of a multilevel numbered
    /// definition with nine levels that is linked to no style, else Write's heading definition with a new <c>w:num</c>.
    /// </summary>
    /// <param name="styleLevels">
    /// The zero-based levels whose heading style the file defines (null: all nine). A definition counts as linked when these levels link to Heading1 to
    /// Heading9, and a definition made here links only these levels: a <c>w:pStyle</c> that names a style the file does not have is never written.
    /// </param>
    public int HeadingList(IReadOnlyCollection<int>? styleLevels = null)
    {
        var numbering = Required();
        foreach (var instance in InPartOrder(numbering.Instances))
        {
            if (instance.NumId > 0 && FindAbstract(numbering, instance) is { } linked && IsHeadingLinked(linked, styleLevels))
            {
                return instance.NumId;
            }
        }

        foreach (var instance in InPartOrder(numbering.Instances))
        {
            if (instance.NumId > 0
                && FindAbstract(numbering, instance) is { } candidate
                && IsMultilevel(candidate)
                && candidate.Levels.Count >= WriteListDefinitions.LevelCount
                && !IsLinkedToStyles(candidate)
                && LevelZeroKind(candidate) == ListKind.Numbered)
            {
                return instance.NumId;
            }
        }

        var id = NextAbstractId(numbering);
        var added = WriteListDefinitions.Heading(id);
        if (styleLevels is not null)
        {
            foreach (var level in added.Levels.Where(l => !styleLevels.Contains(l.Level)))
            {
                var link = level.KeptChildren.Where(k => k.Content.Element.Name == W + "pStyle").ToList();
                foreach (var kept in link)
                {
                    level.KeptChildren.Remove(kept);
                }
            }
        }

        numbering.Abstracts.Add(added);
        return AddInstance(numbering, id, track: true);
    }

    private static int NextAbstractId(NumberingDefinitions numbering) =>
        numbering.Abstracts.Count == 0 ? 0 : numbering.Abstracts.Max(a => a.AbstractNumId) + 1;

    private static IEnumerable<NumberingInstance> InPartOrder(IEnumerable<NumberingInstance> instances) =>
        instances.OrderBy(i => i.Position);

    private static AbstractNumbering? FindAbstract(NumberingDefinitions numbering, NumberingInstance instance) =>
        instance.AbstractNumId is { } id ? numbering.FindAbstract(id) : null;

    private static ListKind? LevelZeroKind(NumberingDefinitions numbering, NumberingInstance instance) =>
        FindAbstract(numbering, instance) is { } abstractNumbering ? LevelZeroKind(abstractNumbering) : null;

    private static ListKind? LevelZeroKind(AbstractNumbering abstractNumbering)
    {
        var level = abstractNumbering.Levels.FirstOrDefault(l => l.Level == 0);
        return level?.Format switch
        {
            null or "none" => null,
            "bullet" => ListKind.Bullet,
            _ => ListKind.Numbered,
        };
    }

    private static string? LevelStyle(NumberingLevel level)
    {
        foreach (var kept in level.KeptChildren)
        {
            if (kept.Content.Element.Name == W + "pStyle")
            {
                return (string?)kept.Content.Element.Attribute(W + "val");
            }
        }

        return null;
    }

    private static bool IsLinkedToStyles(AbstractNumbering abstractNumbering) =>
        abstractNumbering.Levels.Any(l => LevelStyle(l) is not null);

    private static bool IsHeadingLinked(AbstractNumbering abstractNumbering, IReadOnlyCollection<int>? styleLevels)
    {
        for (var level = 0; level < WriteListDefinitions.LevelCount; level++)
        {
            if (styleLevels is not null && !styleLevels.Contains(level))
            {
                continue;
            }

            var item = abstractNumbering.Levels.FirstOrDefault(l => l.Level == level);
            if (item is null || LevelStyle(item) != "Heading" + (level + 1).ToString(CultureInfo.InvariantCulture))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMultilevel(AbstractNumbering abstractNumbering) =>
        abstractNumbering.KeptChildren.Any(k =>
            k.Content.Element.Name == W + "multiLevelType"
            && (string?)k.Content.Element.Attribute(W + "val") == "multilevel");

    // A document without a numbering part gets an empty one; the DOCX layer writes it (part, relationship and content type) on save.
    private NumberingDefinitions Required() =>
        _document.Numbering ??= new NumberingDefinitions(new OoxmlRoot(W + "numbering", [new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName)]));

    private int AddInstance(NumberingDefinitions numbering, int abstractNumId, bool track)
    {
        var id = numbering.Instances.Count == 0 ? 1 : Math.Max(1, numbering.Instances.Max(i => i.NumId) + 1);
        numbering.Instances.Add(new NumberingInstance { NumId = id, AbstractNumId = abstractNumId });
        if (track)
        {
            _created.Add(id);
        }

        return id;
    }

    private int? StylePointedAt(ListKind kind)
    {
        var (id, name) = kind == ListKind.Bullet ? ("ListBullet", "List Bullet") : ("ListNumber", "List Number");
        var style = _document.Styles?.Styles.FirstOrDefault(s =>
            s.Type == "paragraph"
            && (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));
        return style?.ParagraphProperties.NumberingId is > 0 and var numId ? numId : null;
    }

    private static bool BlocksReference(IEnumerable<Block> blocks, int numId)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph when paragraph.Properties.NumberingId == numId:
                    return true;
                case Table table when RowsReference(table.Rows, numId):
                    return true;
                case ContentControlBlock control when BlocksReference(control.Content, numId):
                    return true;
            }
        }

        return false;
    }

    private static bool RowsReference(IEnumerable<RowItem> items, int numId)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row when RowsReference(row.Items, numId):
                    return true;
                case TableCell cell when BlocksReference(cell.Blocks, numId):
                    return true;
            }
        }

        return false;
    }
}
