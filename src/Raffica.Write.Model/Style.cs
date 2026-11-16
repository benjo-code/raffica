namespace Raffica.Write.Model;

/// <summary>One <c>w:style</c>. The kind and the references are plain strings, so a value this code does not know is kept as it is; everything else the style holds stays a kept child.</summary>
public sealed class Style : OrderedElement
{
    /// <summary><c>w:styleId</c>; empty when the attribute was absent.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>w:type</c> (<c>paragraph</c>, <c>character</c>, <c>table</c>, <c>numbering</c>, or any other text); null when absent.</summary>
    public string? Type { get; set; }

    public string? Name { get; set; }

    public string? BasedOn { get; set; }

    public string? Next { get; set; }

    public string? Link { get; set; }

    /// <summary>The <c>w:default</c> attribute text exactly as read (or null), so a save writes the same text.</summary>
    public string? DefaultAttribute { get; set; }

    /// <summary><c>w:default</c> on (<c>1</c>, <c>true</c> or <c>on</c>). Setting true keeps a spelling already read and otherwise writes <c>1</c>.</summary>
    public bool IsDefault
    {
        get => DefaultAttribute is "1" or "true" or "on";
        set
        {
            if (!value)
            {
                DefaultAttribute = null;
            }
            else if (!IsDefault)
            {
                DefaultAttribute = "1";
            }
        }
    }

    public PropertyBag ParagraphProperties { get; } = new(PropertyKind.Paragraph);

    public PropertyBag RunProperties { get; } = new(PropertyKind.Run);

    public PropertyBag TableProperties { get; } = new(PropertyKind.Table);

    /// <summary>A deep copy: every string, the three property bags, the kept children and the unknown attributes are new objects with the same content, so the copy shares nothing with the original and serializes equal.</summary>
    public Style Clone()
    {
        var copy = new Style
        {
            Id = Id,
            Type = Type,
            Name = Name,
            BasedOn = BasedOn,
            Next = Next,
            Link = Link,
            DefaultAttribute = DefaultAttribute,
            Position = Position,
        };
        ParagraphProperties.CopyStateTo(copy.ParagraphProperties);
        RunProperties.CopyStateTo(copy.RunProperties);
        TableProperties.CopyStateTo(copy.TableProperties);
        foreach (var kept in KeptChildren)
        {
            copy.KeptChildren.Add(new KeptChild(kept.Position, new UnknownContent(kept.Content.Element)));
        }

        foreach (var position in ChildPositions)
        {
            copy.ChildPositions[position.Key] = position.Value;
        }

        CopyAttributesTo(copy);
        return copy;
    }
}
