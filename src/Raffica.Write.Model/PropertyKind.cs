namespace Raffica.Write.Model;

/// <summary>Which <c>*Pr</c> element a <see cref="PropertyBag"/> holds the children of; it decides the schema order and the typed accessors.</summary>
public enum PropertyKind
{
    Paragraph,
    Run,
    Table,
    TableRow,
    TableCell,
    Section,
    Style,
    ContentControl,
}
