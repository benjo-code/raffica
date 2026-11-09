using Avalonia.Automation;
using Avalonia.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The look and the voice of a validation hint in a card: text in the danger color that appears when an entry is wrong. It is an assertive live
/// region, so a screen reader says it when it appears instead of waiting for the focus to come by.
/// </summary>
internal static class ValidationHint
{
    /// <summary>Colors the text as a problem and makes it an assertive live region; returns the same text block.</summary>
    public static TextBlock Mark(TextBlock hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Danger"));
        AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Assertive);
        return hint;
    }
}
