using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose on the card about empty required fields.</summary>
internal enum RequiredChoice
{
    /// <summary>Do not save now; the window goes to the first empty field (Esc, the close button, the default button).</summary>
    GoToFirst,

    /// <summary>Save the file as it is.</summary>
    SaveAnyway,
}

/// <summary>
/// The card shown before a form with empty required fields is saved. It never blocks: Save anyway works at once, and nothing on it says the file
/// is invalid (a required field is the form's own wish, not a fault of the file). The default is to go to the first empty field; Esc means the same.
/// At most five names are listed, then how many more there are.
/// </summary>
internal static class RequiredFieldsCard
{
    public static Dialog Create(IReadOnlyList<string> names, Action<RequiredChoice> done)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(done);
        if (names.Count == 0)
        {
            throw new ArgumentException("A card about empty required fields needs at least one name.", nameof(names));
        }

        var finished = false;
        void Finish(RequiredChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(choice);
        }

        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.RequiredText(names.Count), TextWrapping = TextWrapping.Wrap };
        var list = new TextBlock { Name = "PART_Names", Text = PdfStrings.RequiredList(names), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        var hint = new TextBlock { Name = "PART_Hint", Text = PdfStrings.RequiredHint, TextWrapping = TextWrapping.Wrap };
        var first = new Button { Name = "PART_GoToFirst", Content = PdfStrings.RequiredGoToFirst };
        first.Classes.Add("primary");
        var anyway = new Button { Name = "PART_SaveAnyway", Content = PdfStrings.RequiredSaveAnyway };
        anyway.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = PdfStrings.RequiredTitle,
            Content = new StackPanel { Spacing = 8, Children = { message, list, hint } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { first, anyway } },
        };
        first.Click += (_, _) => Finish(RequiredChoice.GoToFirst);
        anyway.Click += (_, _) => Finish(RequiredChoice.SaveAnyway);
        dialog.CancelRequested += (_, _) => Finish(RequiredChoice.GoToFirst);
        return dialog;
    }
}
