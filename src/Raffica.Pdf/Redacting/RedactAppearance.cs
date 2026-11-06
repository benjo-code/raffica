using Raffica.Core.Redaction;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// The look of a plan as the interface offers it: the colour field (<c>#RRGGBB</c> only), the three resolutions of pages turned into images,
/// the caution for blurred and pixelated pictures and the contrast of a text colour the user chose. Pure rules, no state; the plan holds the values.
/// </summary>
internal static class RedactAppearance
{
    /// <summary>The resolutions, in dots per inch, offered for pages turned into images (the plan starts at <see cref="DefaultDpi"/>).</summary>
    public static IReadOnlyList<int> Dpis { get; } = [150, RedactionLimits.DefaultRasterDpi, 300];

    /// <summary>The place of a resolution in <see cref="Dpis"/>; -1 if it is none of them.</summary>
    public static int DpiIndex(int dpi) => Dpis.ToList().IndexOf(dpi);

    /// <summary>The resolution a plan starts with.</summary>
    public static int DefaultDpi => RedactionLimits.DefaultRasterDpi;

    /// <summary>Reads a colour written as <c>#RRGGBB</c> (spaces around it are ignored); nothing else is accepted.</summary>
    public static bool TryParseFill(string? text, out RedactionFill fill) => RedactionFill.TryParse(text?.Trim(), out fill);

    /// <summary>Whether the pane says that a treatment keeps some of the picture (blurring and pixelating do, the solid fill does not).</summary>
    public static bool NeedsCaution(RedactionImageTreatment treatment) => treatment is RedactionImageTreatment.Pixelate or RedactionImageTreatment.Blur;

    /// <summary>
    /// The contrast of the text colour the user chose against the fill when it is below the least the automatic colour reaches, else null. Only
    /// a chosen colour warns; the automatic one is black or white, whichever reads better.
    /// </summary>
    public static double? LowContrast(RedactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Overlay.TextColor is not { } color)
        {
            return null;
        }

        var contrast = RedactionOverlayColor.Contrast(color, plan.Fill);
        return contrast < RedactionOverlayColor.MinContrast ? contrast : null;
    }
}
