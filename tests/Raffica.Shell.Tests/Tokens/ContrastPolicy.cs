namespace Raffica.Shell.Tests.Tokens;

/// <summary>How a token takes part in the contrast test.</summary>
public enum ContrastKind
{
    /// <summary>Text on the five backgrounds, 4.5 : 1.</summary>
    Text,

    /// <summary>A UI part (edge, ring, icon) on the five backgrounds, 3 : 1.</summary>
    Ui,

    /// <summary>Not checked, with a reason.</summary>
    Exempt,

    /// <summary>Separators and gridlines that carry no meaning.</summary>
    Decorative,

    /// <summary>One of the five surfaces the foregrounds are checked against.</summary>
    Background,

    /// <summary>Checked on its own pairing only (appendix A section 2.3).</summary>
    Fill,
}

/// <summary>Classifies every token once. A token or an entry that is missing here fails <c>EveryTokenIsClassified</c>.</summary>
public static class ContrastPolicy
{
    public static readonly IReadOnlyDictionary<string, (ContrastKind Kind, string? Reason)> Tokens =
        new Dictionary<string, (ContrastKind, string?)>
        {
            ["Text.Primary"] = (ContrastKind.Text, null),
            ["Text.Secondary"] = (ContrastKind.Text, null),
            ["Ink.Write"] = (ContrastKind.Text, null),
            ["Ink.Sheets"] = (ContrastKind.Text, null),
            ["Ink.Slides"] = (ContrastKind.Text, null),
            ["Ink.Pdf"] = (ContrastKind.Text, null),
            ["Review.Violet"] = (ContrastKind.Text, null),
            ["Danger"] = (ContrastKind.Text, null),
            ["Border.Control"] = (ContrastKind.Ui, null),
            ["Warning.Icon"] = (ContrastKind.Ui, null),
            ["Text.Disabled"] = (ContrastKind.Exempt, "disabled controls are exempt from contrast"),
            ["Rule"] = (ContrastKind.Decorative, null),
            ["Grid.Line"] = (ContrastKind.Decorative, null),
            ["Surface.Page"] = (ContrastKind.Background, null),
            ["Surface.Band"] = (ContrastKind.Background, null),
            ["Surface.Desk"] = (ContrastKind.Background, null),
            ["State.Hover"] = (ContrastKind.Background, null),
            ["Selection.Tint"] = (ContrastKind.Background, null),
            ["Review.Tint"] = (ContrastKind.Fill, null),
            ["Button.Primary.Fill"] = (ContrastKind.Fill, null),
            ["Button.Primary.Hover"] = (ContrastKind.Fill, null),
            ["Scrim"] = (ContrastKind.Fill, null),

            // Text.OnInk is the text drawn on a fill; it is checked against every fill in FilledPairingsMeetTheirThreshold.
            ["Text.OnInk"] = (ContrastKind.Fill, null),
        };

    /// <summary>The five backgrounds every foreground is checked against.</summary>
    public static readonly string[] Backgrounds = ["Surface.Page", "Surface.Band", "Surface.Desk", "State.Hover", "Selection.Tint"];

    /// <summary>Pairings that fail and must not be used (appendix B): foreground, background, theme.</summary>
    public static readonly (string Foreground, string Background, string Theme)[] Forbidden =
    [
        ("Review.Violet", "Selection.Tint", "Dark"),
        ("Danger", "Selection.Tint", "Dark"),
    ];

    /// <summary>Pairings below their threshold that are not meaningful, with the reason (both themes).</summary>
    public static readonly (string Foreground, string Background, string Reason)[] ExemptPairings =
    [
        ("Border.Control", "Selection.Tint", "selection is carried by the 3 px ink edge"),
    ];

    public static double Threshold(ContrastKind kind) => kind == ContrastKind.Ui ? 3.0 : 4.5;

    public static bool IsExempt(string foreground, string background, string theme) =>
        Forbidden.Any(f => f.Foreground == foreground && f.Background == background && f.Theme == theme) ||
        ExemptPairings.Any(e => e.Foreground == foreground && e.Background == background);
}
