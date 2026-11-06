using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The card when a redaction cannot go on and nothing was written (the file is refused, or the check of the written file found something): the
/// reasons, one sentence each with the pages, and that the file is unchanged and the marks are still open. Only "Back". It names kinds and pages, never
/// the redacted text and never a value of the document.
/// </summary>
internal static class RedactRefusedCard
{
    /// <param name="fileName">The name of the open file.</param>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="intro">The sentence before the reasons (what happened to the file and the marks).</param>
    /// <param name="reasons">The reasons, one sentence each.</param>
    /// <param name="done">Raised when the card is closed.</param>
    /// <param name="hint">One sentence that names the ways out (shown after the reasons), or null.</param>
    public static Dialog Create(string fileName, string title, string intro, IReadOnlyList<string> reasons, Action done, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(intro);
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(done);

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(RedactCardParts.Name(fileName));
        body.Children.Add(new TextBlock { Name = "PART_Intro", Text = intro, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        foreach (var reason in reasons)
        {
            // The intro is the heading; a reason is a sentence (never colour alone).
            var line = new TextBlock { Name = "PART_Reason", Text = "• " + reason, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Avalonia.Automation.AutomationProperties.SetName(line, reason);
            body.Children.Add(line);
        }

        if (hint is not null)
        {
            body.Children.Add(new TextBlock { Name = "PART_Hint", Text = hint, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        }

        var back = new Button { Name = "PART_Back", Content = PdfStrings.ApplyBack };
        back.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = title,
            Content = RedactCardParts.Scroll(body),
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { back } },
        };
        var finished = false;
        void Finish()
        {
            if (!finished)
            {
                finished = true;
                done();
            }
        }

        back.Click += (_, _) => Finish();
        dialog.CancelRequested += (_, _) => Finish();
        return dialog;
    }
}
