using Raffica.Core.Forms;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The name a screen reader gets for a widget, composed in one place for the page view (the focused widget) and the editor elements: the
/// display name, then why the widget is marked (locked: the reason of the hover text; required and still empty), and for a check box or a
/// radio button whether it is checked. State is never carried by the star, the lock or the colour alone. Pure; UI thread not needed.
/// </summary>
internal static class FormAutomation
{
    /// <summary>The reason a widget is locked, in the language of the app; <see langword="null"/> for a widget that is not locked.</summary>
    public static string? LockText(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Lock == FormLock.None)
        {
            return null;
        }

        return field.Lock == FormLock.ReadOnly ? PdfStrings.FormTipReadOnly : field.Kind switch
        {
            FormFieldKind.Signature => PdfStrings.FormTipSignature,
            FormFieldKind.PushButton => PdfStrings.FormTipButton,
            _ => PdfStrings.FormTipUnsupported,
        };
    }

    /// <summary>The name of a widget: "name", "name, reason", "name, checked".</summary>
    public static string NameOf(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var name = field.DisplayName;
        if (field.Kind == FormFieldKind.RadioButton && !string.IsNullOrEmpty(field.ExportValue))
        {
            name = Join(name, field.ExportValue);
        }

        if (LockText(field) is { } locked)
        {
            name = Join(name, locked);
        }
        else if (FormMarkers.IsMarkedRequired(field))
        {
            name = Join(name, PdfStrings.FormTipRequired);
        }

        if (field.Kind == FormFieldKind.CheckBox)
        {
            name = Join(name, field.State.Checked == true ? PdfStrings.FormStateChecked : PdfStrings.FormStateNotChecked);
        }
        else if (field.Kind == FormFieldKind.RadioButton)
        {
            var chosen = field.ExportValue is not null && string.Equals(field.State.Text, field.ExportValue, StringComparison.Ordinal);
            name = Join(name, chosen ? PdfStrings.FormStateChecked : PdfStrings.FormStateNotChecked);
        }

        return name;
    }

    private static string Join(string name, string part) => PdfStrings.FormNameFormat.Replace("{0}", name, StringComparison.Ordinal).Replace("{1}", part, StringComparison.Ordinal);
}
