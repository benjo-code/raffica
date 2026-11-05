using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>A short message with one OK button: a title and a reason (an annotate refusal, a markup that could not be made).</summary>
internal static class NoticeCard
{
    public static Dialog Create(string title, string message, Action done)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(done);

        var text = new TextBlock { Name = "PART_Message", Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = title,
            Content = new StackPanel { Spacing = 8, Children = { text } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok } },
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
