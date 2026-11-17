using Avalonia.Controls;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Write.Recovery;

namespace Raffica.Write.Dialogs;

/// <summary>
/// The offer for the copies of unsaved work found at start: one card for all of them. Restore is the default, Discard the second button, "Decide later" the cancel (Esc). At most
/// <see cref="MaxListed"/> copies are named (the newest first), the rest is one line with their number. Titles are file names, never document text.
/// </summary>
internal static class RecoveryCard
{
    /// <summary>How many copies the card names.</summary>
    public const int MaxListed = 20;

    // A title comes from a file of the folder (untrusted): it is shortened like the names of an open failure.
    private static string Shown(string title) => title.Length > WriteWindow.MaxShownNameLength ? title[..WriteWindow.MaxShownNameLength] : title;

    public static Dialog Create(IReadOnlyList<RecoveryEntry> entries, Action<RecoveryChoice> chosen)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(chosen);

        var finished = false;
        void Finish(RecoveryChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            chosen(choice);
        }

        var list = new StackPanel { Name = "PART_List", Spacing = 2 };
        foreach (var entry in entries.Take(MaxListed))
        {
            list.Children.Add(new TextBlock
            {
                Text = WriteStrings.Fill(WriteStrings.RecoveryEntryFormat, Shown(entry.Title), entry.Written.ToLocalTime().ToString("g", WriteStrings.Culture)),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.SemiBold,
            });
        }

        if (entries.Count > MaxListed)
        {
            list.Children.Add(new TextBlock { Name = "PART_More", Text = WriteStrings.Fill(WriteStrings.RecoveryMoreFormat, entries.Count - MaxListed), TextWrapping = TextWrapping.Wrap });
        }

        var body = new TextBlock { Name = "PART_Message", Text = WriteStrings.RecoveryBody, TextWrapping = TextWrapping.Wrap };
        var restore = new Button { Name = "PART_Restore", Content = WriteStrings.RecoveryRestore };
        restore.Classes.Add("primary");
        var discard = new Button { Name = "PART_Discard", Content = WriteStrings.RecoveryDiscard };
        discard.Classes.Add("boxed");
        var later = new Button { Name = "PART_Cancel", Content = WriteStrings.RecoveryLater };
        later.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = WriteStrings.RecoveryTitle,
            Content = new StackPanel { Spacing = 8, Children = { body, list } },
            Footer = CardFooter.Build(restore, [discard], later),
        };
        restore.Click += (_, _) => Finish(RecoveryChoice.Restore);
        discard.Click += (_, _) => Finish(RecoveryChoice.Discard);
        later.Click += (_, _) => Finish(RecoveryChoice.Later);
        dialog.CancelRequested += (_, _) => Finish(RecoveryChoice.Later);
        return dialog;
    }
}
