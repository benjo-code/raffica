using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>The size of a new table: the payload of Insert table (the grid picker and the card hand it over).</summary>
internal sealed record TableSize(int Columns, int Rows);

/// <summary>
/// The values of the table commands that take a payload (<c>table.insert</c>, <c>table.borders</c>, <c>table.shading</c>, <c>table.properties</c>): what is a value, and what it means. A payload that is not a value
/// of the command is refused (false) and changes nothing, never an exception: the colours are the fixed list of the Home tab (<see cref="ColorChoices.TextColors"/>) only, since the editor takes any colour.
/// </summary>
internal static class TablePayloads
{
    /// <summary>The border presets the picker offers, by id. <c>outside</c> draws the outline of the range and leaves the lines inside as they are; <c>box</c> draws the outline and turns the lines inside the range off.</summary>
    public static IReadOnlyList<string> BorderPresets { get; } = ["all", "outside", "box", "none", "top", "bottom", "left", "right", "insideH", "insideV"];

    /// <summary>The id of the shading item that removes the shading.</summary>
    public const string NoShading = "none";

    /// <summary>The widths of a border line the card offers, in eighths of a point (a quarter of a point to six points).</summary>
    public static IReadOnlyList<int> BorderWidths { get; } = [2, 4, 6, 8, 12, 18, 24, 36, 48];

    /// <summary>Whether <paramref name="payload"/> is a value of the command (null is none of them: every payload command needs its value).</summary>
    public static bool IsValid(string id, object? payload) => id switch
    {
        WriteCommandIds.TableInsert => payload is TableSize { Columns: >= 1 and <= TableRules.MaxColumns, Rows: >= 1 and <= TableRules.MaxRows },
        WriteCommandIds.TableBorders => payload switch
        {
            string preset => BorderPresets.Contains(preset),
            BorderSpec spec => IsValid(spec),
            _ => false,
        },
        WriteCommandIds.TableShading => payload is string color && (color == NoShading || ColorChoices.TextColors.Any(c => c.Id == color)),
        WriteCommandIds.TableProperties => payload is TableProperties { IsEmpty: false },
        _ => payload is null,
    };

    /// <summary>A border spec the card made: valid for the editor, a style, a colour of the fixed list (or automatic) and a width the card offers.</summary>
    public static bool IsValid(BorderSpec spec)
    {
        if (!spec.IsValid || (spec.Set != BorderSides.None && !BorderWidths.Contains(spec.WidthEighths)))
        {
            return false;
        }

        return spec.Color is not { } color || ColorChoices.TextColors.Any(c => (c.Argb & 0xFFFFFF) == (color & 0xFFFFFF));
    }

    /// <summary>The spec of a preset id (see <see cref="BorderPresets"/>); null for an id that is none.</summary>
    public static BorderSpec? Preset(string id) => id switch
    {
        "all" => BorderSpec.All(),
        "outside" => BorderSpec.Outside(),
        "box" => BorderSpec.Box(),
        "none" => BorderSpec.None(),
        "top" => BorderSpec.Sides(BorderSides.Top),
        "bottom" => BorderSpec.Sides(BorderSides.Bottom),
        "left" => BorderSpec.Sides(BorderSides.Left),
        "right" => BorderSpec.Sides(BorderSides.Right),
        "insideH" => BorderSpec.Sides(BorderSides.InsideHorizontal),
        "insideV" => BorderSpec.Sides(BorderSides.InsideVertical),
        _ => null,
    };

    /// <summary>The colour of a shading id as the editor takes it (0xAARRGGBB), or null for <see cref="NoShading"/>; false for an id that is none of the list.</summary>
    public static bool TryShading(string id, out uint? argb)
    {
        argb = null;
        if (id == NoShading)
        {
            return true;
        }

        if (ColorChoices.TextColors.FirstOrDefault(c => c.Id == id) is { } choice)
        {
            argb = choice.Argb;
            return true;
        }

        return false;
    }
}
