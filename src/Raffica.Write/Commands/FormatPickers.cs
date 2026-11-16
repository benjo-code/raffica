using System.Globalization;
using Raffica.Shell.Frame;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

namespace Raffica.Write.Commands;

/// <summary>
/// The pickers of the Home tab (font, size, colour, highlight, spacing, line spacing and the two styles): what each shows (the value of the selection, or "Mixed"), the lazy list it opens
/// with and what a choice runs. Nothing is read when the ribbon is built: the lists are asked when a list opens (the installed fonts, the styles of the file) and the value comes from the
/// selection format the window already holds. A choice calls <paramref name="run"/> with the command id and the value (<c>WriteWindow.RunCommand(id, payload)</c>, which validates it).
/// </summary>
/// <param name="format">The format of the selection now (null when there is none to show).</param>
/// <param name="document">The model of the active document (null without one).</param>
/// <param name="fonts">The font catalog of the active document (null without one).</param>
/// <param name="run">Runs a value command with its payload; false when it was refused.</param>
internal sealed class FormatPickers(Func<SelectionFormat?> format, Func<WordDocument?> document, Func<FontCatalog?> fonts, Func<string, object?, bool> run)
{
    /// <summary>The sizes of the size list, which are also the steps of Grow font and Shrink font (Word's list).</summary>
    public static IReadOnlyList<float> SizeSteps { get; } = [8f, 9f, 10f, 10.5f, 11f, 12f, 14f, 16f, 18f, 20f, 22f, 24f, 26f, 28f, 36f, 48f, 72f];

    private static readonly float[] s_spacings = [0f, 3f, 6f, 8f, 10f, 12f, 18f, 24f, 36f, 48f];

    private const float LargestSize = 1638f;

    /// <summary>The picker of a command, or null when the command is no picker.</summary>
    public PickerDefinition? For(string commandId) => Counted(Create(commandId));

    /// <summary>How many times a list was asked (a list is read when it opens, never before; tests pin that).</summary>
    internal int ListReads { get; private set; }

    private PickerDefinition? Counted(PickerDefinition? picker)
    {
        if (picker is null)
        {
            return null;
        }

        return picker with
        {
            Items = () =>
            {
                ListReads++;
                return picker.Items();
            },
        };
    }

    private PickerDefinition? Create(string commandId) => commandId switch
    {
        WriteCommandIds.Font => new PickerDefinition(
            () => fonts() is { } catalog ? FontPickerSource.Families(catalog) : [],
            id => run(WriteCommandIds.Font, id),
            () => Read(f => f.Font.Value),
            () => Read(f => f.Font.Mixed) is true,
            Width: 150),
        WriteCommandIds.Size => new PickerDefinition(
            () => [.. SizeSteps.Select(s => new PickerItem(PointsId(s), PointsId(s)))],
            text => ChooseNumber(WriteCommandIds.Size, text),
            () => Read(f => PointsId(f.Size.Value)),
            () => Read(f => f.Size.Mixed) is true,
            Editable: true,
            Width: 56,
            MaxLength: 4),
        WriteCommandIds.Color => new PickerDefinition(
            ColorChoices.TextItems,
            id => run(WriteCommandIds.Color, id),
            () => Read(f => ColorChoices.TextIdOf(f.Color.Value)),
            () => Read(f => f.Color.Mixed) is true,
            Width: 110),
        WriteCommandIds.Highlight => new PickerDefinition(
            ColorChoices.HighlightItems,
            id => run(WriteCommandIds.Highlight, id),
            () => Read(f => ColorChoices.HighlightIdOf(f.Highlight.Value)),
            () => Read(f => f.Highlight.Mixed) is true,
            Width: 130),
        WriteCommandIds.SpaceBefore => Spacing(WriteCommandIds.SpaceBefore, f => f.SpaceBefore),
        WriteCommandIds.SpaceAfter => Spacing(WriteCommandIds.SpaceAfter, f => f.SpaceAfter),
        WriteCommandIds.LineSpacing => new PickerDefinition(
            LineItems,
            id => ChooseLine(id),
            () => Read(f => LineId(f.LineSpacing.Value)),
            () => Read(f => f.LineSpacing.Mixed) is true,
            () => Read(f => LineLabel(f.LineSpacing.Value)),
            Width: 130),
        WriteCommandIds.ParagraphStyle => new PickerDefinition(
            () => document() is { } model ? StylePickerSource.Paragraph(model) : [],
            id => run(WriteCommandIds.ParagraphStyle, id),
            () => Read(f => f.ParagraphStyle.Value),
            () => Read(f => f.ParagraphStyle.Mixed) is true,
            () => Read(f => StyleLabel(f.ParagraphStyle.Value)),
            Width: 160),
        WriteCommandIds.CharacterStyle => new PickerDefinition(
            () => [new PickerItem(string.Empty, WriteStrings.Get("StyleNone")), .. document() is { } model ? StylePickerSource.Character(model) : []],
            id => run(WriteCommandIds.CharacterStyle, id.Length == 0 ? null : id),
            () => Read(f => f.CharacterStyle.Value ?? string.Empty),
            () => Read(f => f.CharacterStyle.Mixed) is true,
            () => Read(f => f.CharacterStyle.Value is null ? WriteStrings.Get("StyleNone") : StyleLabel(f.CharacterStyle.Value)),
            Width: 140),
        _ => null,
    };

    /// <summary>
    /// Grow font and Shrink font: the next size of <see cref="SizeSteps"/> above or below the size shown (the size at the start of a mixed selection), as the target size in points for the size command.
    /// Above 72 pt the step is 10 pt and the size never goes beyond 1638 pt; below 8 pt it is 1 pt and never below 1 pt.
    /// </summary>
    public static float StepSize(float current, bool up)
    {
        if (!float.IsFinite(current) || current <= 0)
        {
            current = 11f;
        }

        if (up)
        {
            foreach (var step in SizeSteps)
            {
                if (step > current)
                {
                    return step;
                }
            }

            return MathF.Min(LargestSize, MathF.Floor(current / 10f) * 10f + 10f);
        }

        for (var i = SizeSteps.Count - 1; i >= 0; i--)
        {
            if (SizeSteps[i] < current)
            {
                return SizeSteps[i];
            }
        }

        return MathF.Max(1f, MathF.Ceiling(current) - 1f);
    }

    /// <summary>The action of Grow font or Shrink font: runs the size command with <see cref="StepSize"/> of the size shown.</summary>
    public Action StepAction(string commandId, bool up) => () => run(commandId, StepSize(Read(f => f.Size.Value), up));

    private T? Read<T>(Func<SelectionFormat, T> read) => format() is { } f ? read(f) : default;

    private PickerDefinition Spacing(string commandId, Func<SelectionFormat, Mixable<float>> pick) => new(
        () => [.. s_spacings.Select(s => new PickerItem(PointsId(s), WriteStrings.Fill(WriteStrings.Get("PointsFormat"), s)))],
        text => ChooseNumber(commandId, text),
        () => Read(f => PointsId(pick(f).Value)),
        () => Read(f => pick(f).Mixed) is true,
        () => Read(f => WriteStrings.Fill(WriteStrings.Get("PointsFormat"), pick(f).Value)),
        Width: 90);

    // A number typed or chosen: the value command validates the range (a refusal changes nothing).
    private void ChooseNumber(string commandId, string text)
    {
        if (float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            run(commandId, value);
        }
    }

    private void ChooseLine(string id)
    {
        var parts = id.Split(':');
        if (parts.Length == 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            var rule = parts[0] switch
            {
                "auto" => LineRuleKind.Auto,
                "exact" => LineRuleKind.Exact,
                "atLeast" => LineRuleKind.AtLeast,
                _ => (LineRuleKind?)null,
            };
            if (rule is { } kind)
            {
                run(WriteCommandIds.LineSpacing, new LineSpacingChoice(value, kind));
            }
        }
    }

    private static IReadOnlyList<PickerItem> LineItems() =>
    [
        new PickerItem("auto:1", WriteStrings.Get("LineSingle")),
        new PickerItem("auto:1.15", Number(1.15f)),
        new PickerItem("auto:1.5", Number(1.5f)),
        new PickerItem("auto:2", WriteStrings.Get("LineDouble")),
        new PickerItem("atLeast:12", WriteStrings.Fill(WriteStrings.Get("LineAtLeastFormat"), 12f)),
        new PickerItem("exact:12", WriteStrings.Fill(WriteStrings.Get("LineExactFormat"), 12f)),
    ];

    private static string LineId((LineSpacingRule Rule, float Value) line) => line.Rule switch
    {
        LineSpacingRule.Exact => "exact:" + PointsId(line.Value),
        LineSpacingRule.AtLeast => "atLeast:" + PointsId(line.Value),
        _ => "auto:" + PointsId(line.Value),
    };

    private static string LineLabel((LineSpacingRule Rule, float Value) line) => line.Rule switch
    {
        LineSpacingRule.Exact => WriteStrings.Fill(WriteStrings.Get("LineExactFormat"), line.Value),
        LineSpacingRule.AtLeast => WriteStrings.Fill(WriteStrings.Get("LineAtLeastFormat"), line.Value),
        _ => Number(line.Value),
    };

    private string StyleLabel(string? id) => id is null ? string.Empty : document()?.Styles?.Find(id)?.Name ?? id;

    private static string Number(float value) => value.ToString("0.##", WriteStrings.Culture);

    /// <summary>The text of a number of points as an item id: no trailing zeros, invariant.</summary>
    internal static string PointsId(float points) => points.ToString("0.##", CultureInfo.InvariantCulture);
}
