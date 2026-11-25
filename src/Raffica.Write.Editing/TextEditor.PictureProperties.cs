using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The parts of a picture the Picture properties card changes, each optional: the frame (<paramref name="WidthEmu"/> and <paramref name="HeightEmu"/>, a missing one keeps the picture's own; or <paramref name="ResetSize"/>, which takes
/// the original size and wins over both), the description and how the text wraps. Whatever equals what the picture already has is left alone; nothing at all to change is a refusal.
/// </summary>
public sealed record PictureProperties(long? WidthEmu = null, long? HeightEmu = null, bool ResetSize = false, string? AltText = null, PictureWrap? Wrap = null)
{
    /// <summary>Whether no part is set.</summary>
    public bool IsEmpty => WidthEmu is null && HeightEmu is null && !ResetSize && AltText is null && Wrap is null;
}

public sealed partial class TextEditor
{
    private readonly record struct PropertiesPlan(bool Resize, long Width, long Height, bool Alt, string AltText, bool Wrap);

    /// <summary>
    /// Changes several parts of a picture as ONE history step (what one OK of the properties card means): the wrap first, then the frame, then the description. Every part obeys the rule of its own operation
    /// (<see cref="ResizePicture"/>, <see cref="ResetPictureSize"/>, <see cref="SetAltText"/>, <see cref="SetPictureWrap"/>); a part that already holds the asked value is skipped, a part the rules refuse refuses the
    /// whole call (nothing changes), and when every part is skipped the answer is <see cref="PictureRefusal.NothingToChange"/>. The selection stays on the picture.
    /// </summary>
    public EditResult? SetPictureProperties(ObjectId id, PictureProperties properties, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(properties);
        PropertiesPlan plan = default;
        return Change(id, prefixDocument, true, t => PropertiesRule(t, properties, out plan), t =>
        {
            if (plan.Wrap)
            {
                PictureOperations.SetWrap(t.Picture, properties.Wrap!.Value, PictureOperations.NextRelativeHeight(_document));
            }

            if (plan.Resize)
            {
                PictureOperations.SetExtent(t.Picture, plan.Width, plan.Height);
            }

            if (plan.Alt)
            {
                PictureOperations.SetAltText(t.Picture, plan.AltText);
            }
        }).Result;
    }

    /// <summary>The reason <see cref="SetPictureProperties"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForSetPictureProperties(ObjectId id, PictureProperties properties, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Change(id, prefixDocument, false, t => PropertiesRule(t, properties, out _), _ => { }).Refusal;
    }

    private PictureRefusal? PropertiesRule(PictureTarget target, PictureProperties properties, out PropertiesPlan plan)
    {
        plan = default;
        var resize = false;
        var (width, height) = (target.Info.ExtentEmu.Width, target.Info.ExtentEmu.Height);
        if (properties.ResetSize)
        {
            var refusal = ResetRule(target, out var original);
            if (refusal is null)
            {
                (resize, width, height) = (true, original!.Value.Width, original.Value.Height);
            }
            else if (refusal != PictureRefusal.NothingToChange)
            {
                return refusal;
            }
        }
        else if (properties.WidthEmu is not null || properties.HeightEmu is not null)
        {
            var (w, h) = (properties.WidthEmu ?? width, properties.HeightEmu ?? height);
            var refusal = ResizeRule(target, w, h);
            if (refusal is null)
            {
                (resize, width, height) = (true, w, h);
            }
            else if (refusal != PictureRefusal.NothingToChange)
            {
                return refusal;
            }
        }

        var alt = false;
        var clean = string.Empty;
        if (properties.AltText is { } text)
        {
            var refusal = AltRule(target, text);
            if (refusal is null)
            {
                (alt, clean) = (true, PictureRules.CheckAltText(text).Text ?? string.Empty);
            }
            else if (refusal != PictureRefusal.NothingToChange)
            {
                return refusal;
            }
        }

        var wrap = false;
        if (properties.Wrap is { } kind)
        {
            var refusal = WrapRule(target, kind);
            if (refusal is null)
            {
                wrap = true;
            }
            else if (refusal != PictureRefusal.NothingToChange)
            {
                return refusal;
            }
        }

        plan = new PropertiesPlan(resize, width, height, alt, clean, wrap);
        return resize || alt || wrap ? null : PictureRefusal.NothingToChange;
    }
}
