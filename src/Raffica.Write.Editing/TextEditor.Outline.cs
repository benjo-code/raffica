using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Why promote or demote is not available for a range of body blocks (<see cref="OutlineShiftReason.None"/> when it is). The commands word each member in both languages.</summary>
public enum OutlineShiftReason
{
    /// <summary>The command is available.</summary>
    None,

    /// <summary>A paragraph has an outline level that comes from neither a <c>HeadingN</c> style nor its own <c>w:outlineLvl</c> (a custom style, say), or the style it would move to is not in the file.</summary>
    NotAHeadingStyle,

    /// <summary>A paragraph would leave heading levels 1 to 9.</summary>
    OutOfRange,

    /// <summary>No paragraph of the range has an outline level.</summary>
    NothingToShift,
}

/// <summary>The answer of <see cref="TextEditor.ReadOutlineShift"/>: whether the shift is possible and, when it is not, why.</summary>
public readonly record struct OutlineShiftFacts(bool Allowed, OutlineShiftReason Reason);

public sealed partial class TextEditor
{
    private const int MaxHeadingLevel = 8;

    /// <summary>
    /// Moves every paragraph of the body blocks <paramref name="firstBlock"/> to <paramref name="lastBlock"/> that has an outline level (tables and block content controls inside them included)
    /// by <paramref name="delta"/> levels (-1 promotes, +1 demotes; any other value throws). The level comes from the paragraph's own <c>w:outlineLvl</c> (which wins when the style has one too, and is rewritten)
    /// or from a <c>HeadingN</c> style (the style id becomes <c>Heading(N+delta)</c>); any other source of a level, a style the file lacks, or a result outside heading levels 1 to 9 makes the whole call
    /// return null before anything changes. Paragraphs without a level stay as they are. One <see cref="BlockChange"/>, one history step; this is a paragraph change, never a style change.
    /// </summary>
    public EditResult? ShiftOutlineLevels(int firstBlock, int lastBlock, int delta)
    {
        ValidateShift(delta);
        var revision = History.BeginOperation();
        var top = _document.Body.Blocks;
        if (firstBlock < 0 || lastBlock >= top.Count || firstBlock > lastBlock)
        {
            return null;
        }

        var plan = PlanOutlineShift(top, firstBlock, lastBlock, delta, out _);
        if (plan is null || plan.Count == 0)
        {
            return null;
        }

        var anchorIndex = -1;
        for (var i = firstBlock; i <= lastBlock && anchorIndex < 0; i++)
        {
            if (top[i] is Paragraph)
            {
                anchorIndex = i;
            }
        }

        var first = firstBlock;
        var last = lastBlock;
        if (anchorIndex < 0)
        {
            // The nearest top-level paragraph before the range, else after it: the clone and the BlockChange widen only as far as that.
            for (var i = firstBlock - 1; i >= 0 && anchorIndex < 0; i--)
            {
                if (top[i] is Paragraph)
                {
                    anchorIndex = i;
                }
            }

            for (var i = lastBlock + 1; i < top.Count && anchorIndex < 0; i++)
            {
                if (top[i] is Paragraph)
                {
                    anchorIndex = i;
                }
            }

            if (anchorIndex < 0)
            {
                return null;
            }

            first = Math.Min(first, anchorIndex);
            last = Math.Max(last, anchorIndex);
        }

        var anchor = (Paragraph)top[anchorIndex];
        var span = new Span(StoryRef.Body, top, first, anchor, 0, last, anchor, 0);
        return CommitParagraphEdit(span, revision, p =>
        {
            if (plan.TryGetValue(p, out var action))
            {
                action(p);
            }
        }, new EditHistory.Point(anchorIndex, 0));
    }

    /// <summary>
    /// Whether <see cref="ShiftOutlineLevels"/> would shift the body blocks <paramref name="firstBlock"/> to <paramref name="lastBlock"/> by <paramref name="delta"/>. Pure: nothing changes and no history
    /// operation begins. A range of more than <see cref="MaxFactBlocks"/> blocks is not read and answers permissively (allowed); the operation then decides.
    /// </summary>
    public OutlineShiftFacts ReadOutlineShift(int firstBlock, int lastBlock, int delta)
    {
        ValidateShift(delta);
        var top = _document.Body.Blocks;
        if (firstBlock < 0 || lastBlock >= top.Count || firstBlock > lastBlock)
        {
            return new OutlineShiftFacts(false, OutlineShiftReason.NothingToShift);
        }

        if (lastBlock - firstBlock + 1 > MaxFactBlocks)
        {
            return new OutlineShiftFacts(true, OutlineShiftReason.None);
        }

        var plan = PlanOutlineShift(top, firstBlock, lastBlock, delta, out var reason);
        return plan is { Count: > 0 } ? new OutlineShiftFacts(true, OutlineShiftReason.None) : new OutlineShiftFacts(false, reason);
    }

    // The change to make per paragraph, or null (with the reason) when any paragraph refuses. Nothing is changed here.
    private Dictionary<Paragraph, Action<Paragraph>>? PlanOutlineShift(IList<Block> top, int firstBlock, int lastBlock, int delta, out OutlineShiftReason reason)
    {
        var paragraphs = new List<Paragraph>();
        for (var i = firstBlock; i <= lastBlock; i++)
        {
            VisitWholeBlock(top[i], paragraphs.Add, 0);
        }

        var plan = new Dictionary<Paragraph, Action<Paragraph>>(ReferenceEqualityComparer.Instance);
        var badSource = false;
        var outOfRange = false;
        foreach (var paragraph in paragraphs)
        {
            var level = OutlineLevelOf(paragraph);
            if (level is null)
            {
                continue;
            }

            var target = (int)Math.Clamp((long)level.Value + delta, -1, MaxHeadingLevel + 1);
            var direct = paragraph.Properties.Get(W + "outlineLvl");
            var styleLevel = HeadingLevelOf(paragraph.Properties.StyleId);
            if (direct is null && styleLevel != level.Value + 1)
            {
                badSource = true;
                continue;
            }

            if (target is < 0 or > MaxHeadingLevel)
            {
                outOfRange = true;
                continue;
            }

            if (direct is not null)
            {
                var value = target.ToString(CultureInfo.InvariantCulture);
                plan[paragraph] = p =>
                {
                    var rewritten = new XElement(direct);
                    rewritten.SetAttributeValue(W + "val", value);
                    p.Properties.Set(W + "outlineLvl", rewritten);
                };
            }
            else
            {
                var styleId = "Heading" + (target + 1).ToString(CultureInfo.InvariantCulture);
                if (_document.Styles?.Find(styleId) is null)
                {
                    badSource = true;
                    continue;
                }

                plan[paragraph] = p => p.Properties.StyleId = styleId;
            }
        }

        if (badSource || outOfRange)
        {
            reason = badSource ? OutlineShiftReason.NotAHeadingStyle : OutlineShiftReason.OutOfRange;
            return null;
        }

        reason = plan.Count == 0 ? OutlineShiftReason.NothingToShift : OutlineShiftReason.None;
        return plan;
    }

    // The outline level (0 to 8) of a paragraph after every style layer; without a format resolver only its own w:outlineLvl and a HeadingN style id are known.
    private int? OutlineLevelOf(Paragraph paragraph)
    {
        if (_format is not null)
        {
            return _format.ForParagraph(StoryRef.Body, paragraph).OutlineLevel is { } level and >= 0 and <= MaxHeadingLevel ? level : null;
        }

        if (paragraph.Properties.Get(W + "outlineLvl")?.Attribute(W + "val") is { } attribute
            && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
        {
            return direct is >= 0 and <= MaxHeadingLevel ? direct : null;
        }

        return HeadingLevelOf(paragraph.Properties.StyleId) is { } heading ? heading - 1 : null;
    }

    private static void ValidateShift(int delta)
    {
        if (delta is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "A shift is -1 (promote) or +1 (demote).");
        }
    }

    // 1 to 9 for the style ids Heading1 to Heading9, else null.
    private static int? HeadingLevelOf(string? styleId) =>
        styleId is { Length: 8 } && styleId.StartsWith("Heading", StringComparison.Ordinal) && styleId[7] is >= '1' and <= '9' ? styleId[7] - '0' : null;
}
