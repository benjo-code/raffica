using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The labels of captions: the three Write offers, the sequence identifiers a file uses, the word that is shown for a label and the rule for a new label.</summary>
public static class CaptionLabels
{
    /// <summary>The sequence identifier of a figure caption.</summary>
    public const string Figure = "Figure";

    /// <summary>The sequence identifier of a table caption.</summary>
    public const string Table = "Table";

    /// <summary>The sequence identifier of an equation caption.</summary>
    public const string Equation = "Equation";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// The labels the caption command offers: Figure, Table and Equation, then every other <c>SEQ</c> identifier of the body (tables and content controls entered) in the order the file first uses it. An identifier that
    /// spells one of the three in another case is the same label (it adds no second entry).
    /// </summary>
    public static IReadOnlyList<string> Known(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var labels = new List<string> { Figure, Table, Equation };
        foreach (var paragraph in BookmarkOperations.ParagraphsOf(document.Body.Blocks))
        {
            if (!FieldRefresh.HasFieldMarkup(paragraph))
            {
                continue;
            }

            foreach (var span in FieldView.InParagraph(paragraph))
            {
                if (span.Support.Kind == FieldKind.Seq && span.Instruction.Argument is { Length: > 0 } label && !labels.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)))
                {
                    labels.Add(label);
                }
            }
        }

        return labels;
    }

    /// <summary>The reason <paramref name="label"/> cannot be a sequence identifier (a letter first, then letters, digits and underscores, at most 40: the rule of a bookmark name), or null.</summary>
    public static BookmarkNameProblem? Problem(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return BookmarkNames.Problem(label, []);
    }

    /// <summary>
    /// The language of the document: the <c>w:lang</c> of the default paragraph style, else of the document defaults (the part of the tag before a hyphen, lower case), or null when the file says none.
    /// </summary>
    public static string? DocumentLanguage(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Styles is not { } sheet)
        {
            return null;
        }

        var normal = sheet.Styles.FirstOrDefault(s => s.IsDefault && string.Equals(s.Type, "paragraph", StringComparison.OrdinalIgnoreCase)) ?? sheet.Find("Normal");
        var tag = normal?.RunProperties.Get(W + "lang") is { } own ? (string?)own.Attribute(W + "val") : null;
        tag ??= sheet.Defaults?.Descendants(W + "lang").Select(e => (string?)e.Attribute(W + "val")).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (string.IsNullOrEmpty(tag))
        {
            return null;
        }

        var dash = tag.IndexOf('-', StringComparison.Ordinal);
        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }

    /// <summary>
    /// The word shown in front of the number: for Figure, Table and Equation the German word ("Abbildung", "Tabelle", "Gleichung") when the document language is German, the English word when it is English, and
    /// otherwise (no language, or another one) the word of the language of the interface (<paramref name="ui"/>); any other label is shown as it is. The word is ordinary text afterwards.
    /// </summary>
    public static string WordFor(WordDocument document, string label, CultureInfo ui)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(ui);
        var language = DocumentLanguage(document);
        return Word(label, language is "de" || (language is not "en" && ui.TwoLetterISOLanguageName == "de"));
    }

    /// <summary>The word for a label in the language of the interface alone (the preview of the card, which has no document).</summary>
    public static string WordFor(string label, CultureInfo ui)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(ui);
        return Word(label, ui.TwoLetterISOLanguageName == "de");
    }

    private static string Word(string label, bool german) => label.ToUpperInvariant() switch
    {
        "FIGURE" => german ? "Abbildung" : "Figure",
        "TABLE" => german ? "Tabelle" : "Table",
        "EQUATION" => german ? "Gleichung" : "Equation",
        _ => label,
    };
}
