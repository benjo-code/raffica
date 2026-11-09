using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose on the default app card.</summary>
internal enum DefaultAppChoice
{
    /// <summary>Set Raffica as the default app (the system may still leave the last step to the user).</summary>
    SetDefault,

    /// <summary>Not now: ask again at the next start.</summary>
    NotNow,

    /// <summary>Don't ask again: the choice is stored.</summary>
    DontAskAgain,
}

/// <summary>
/// The offer after the first page of a start: Set as default (primary), Not now, Don't ask again. The card appears on its own, so a key still held
/// down from before (a repeated Enter) must not confirm anything: Set as default and Don't ask again start disabled for the first 400 ms and are checked against
/// the clock on the click as well (<see cref="ArmingLock"/>). Not now, Esc and the close button are always possible.
/// </summary>
internal static class DefaultAppCard
{
    public static Dialog Create(Action<DefaultAppChoice> chosen, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.DefaultAppMessage, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        var notNow = CreateButton("PART_NotNow", PdfStrings.DefaultAppNotNow, primary: false);
        var dontAsk = CreateButton("PART_DontAsk", PdfStrings.DefaultAppDontAsk, primary: false);
        var set = CreateButton("PART_Set", PdfStrings.DefaultAppSet, primary: true);

        var dialog = new Dialog
        {
            Title = PdfStrings.DefaultAppTitle,
            Content = message,
            Footer = CardFooter.Build(set, [dontAsk], notNow),
        };

        var arming = new ArmingLock(time, dialog, set, dontAsk);
        var finished = false;
        void Finish(DefaultAppChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            chosen(choice);
        }

        set.Click += (_, _) =>
        {
            if (arming.IsArmed)
            {
                Finish(DefaultAppChoice.SetDefault);
            }
        };
        dontAsk.Click += (_, _) =>
        {
            if (arming.IsArmed)
            {
                Finish(DefaultAppChoice.DontAskAgain);
            }
        };
        notNow.Click += (_, _) => Finish(DefaultAppChoice.NotNow);
        dialog.CancelRequested += (_, _) => Finish(DefaultAppChoice.NotNow);
        return dialog;
    }

    private static Button CreateButton(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
