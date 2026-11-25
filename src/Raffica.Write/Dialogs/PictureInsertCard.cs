using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Insert picture: asked after the file was chosen and read, so that nothing is inserted before the user says so. The card has one optional field, the description for screen readers (an empty one is allowed and can be
/// written later in Picture properties). A description longer than <see cref="PictureRules.MaxAltTextLength"/> shows a hint and keeps the card open; Enter confirms, Esc and Cancel insert nothing. The card never shows the
/// name or the contents of the file.
/// </summary>
internal static class PictureInsertCard
{
    /// <param name="done">The description (possibly empty), or null when the user cancelled.</param>
    public static Dialog Create(Action<string?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var finish = CardParts.Once(done);
        var alt = CardParts.Text("PART_Alt", WriteStrings.PiAlt, null, 320);
        var hint = CardParts.Hint();

        var body = new StackPanel { Spacing = 10, MinWidth = 340 };
        body.Children.Add(CardParts.Field(WriteStrings.PiAlt, alt));
        body.Children.Add(new TextBlock { Text = WriteStrings.PiAltHint, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "secondary" } });
        body.Children.Add(hint);

        var (ok, cancel) = CardParts.Buttons(WriteStrings.PiOk, ShellStrings.Cancel);
        var dialog = new Dialog { Title = WriteStrings.PiTitle, Content = body, Footer = CardFooter.Build(ok, null, cancel) };

        void Confirm()
        {
            var text = alt.Text ?? string.Empty;
            if (text.Length > PictureRules.MaxAltTextLength)
            {
                hint.Text = WriteStrings.PiAltTooLong;
                hint.IsVisible = true;
                alt.Focus();
                alt.SelectAll();
                return;
            }

            finish(text);
        }

        CardParts.ConfirmOnEnter(Confirm, [alt]);
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        return dialog;
    }
}
