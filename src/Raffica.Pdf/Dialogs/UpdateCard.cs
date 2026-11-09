using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose on the update card.</summary>
internal enum UpdateChoice
{
    /// <summary>Download, check and install the update, then restart.</summary>
    Install,

    /// <summary>Not now; the offer stays.</summary>
    Later,

    /// <summary>Open the download page (after the card that confirms external addresses).</summary>
    OpenDownloadPage,
}

/// <summary>
/// The offer of a newer version: Install and restart (primary, only on an installation that can update itself), Open download page, Later. It says what is
/// checked before anything is installed (the signature, the size and the SHA-256 of the file) and that unsaved changes are asked about first. Install starts
/// disabled for the first 400 ms and is checked against the clock on the click as well (<see cref="ArmingLock"/>): a key still held down
/// from before must not start an installation. Later, Esc and the close button are always possible.
/// </summary>
internal static class UpdateCard
{
    public static Dialog Create(string version, string current, bool canInstall, Action<UpdateChoice> chosen, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(chosen);

        var lines = new StackPanel { Spacing = 8, MaxWidth = 440 };
        lines.Children.Add(new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(PdfStrings.UpdateMessageFormat, version, current), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        lines.Children.Add(new TextBlock { Name = "PART_Note", Text = canInstall ? PdfStrings.UpdateCheckedNote : PdfStrings.UpdateManualMessage, TextWrapping = TextWrapping.Wrap });
        if (canInstall)
        {
            lines.Children.Add(new TextBlock { Name = "PART_RestartNote", Text = PdfStrings.UpdateRestartNote, TextWrapping = TextWrapping.Wrap });
        }

        var later = CreateButton("PART_Later", PdfStrings.UpdateLater, primary: false);
        var page = CreateButton("PART_Page", PdfStrings.UpdateOpenPage, primary: !canInstall);
        var install = CreateButton("PART_Install", PdfStrings.UpdateInstall, primary: true);

        // Install is the primary where the app can update itself, else the download page is.
        var footer = canInstall ? CardFooter.Build(install, [page], later) : CardFooter.Build(page, null, later);
        var dialog = new Dialog { Title = PdfStrings.UpdateTitle, Content = lines, Footer = footer };
        var arming = new ArmingLock(time, dialog, canInstall ? [install] : []);
        var finished = false;
        void Finish(UpdateChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            chosen(choice);
        }

        install.Click += (_, _) =>
        {
            if (arming.IsArmed)
            {
                Finish(UpdateChoice.Install);
            }
        };
        page.Click += (_, _) => Finish(UpdateChoice.OpenDownloadPage);
        later.Click += (_, _) => Finish(UpdateChoice.Later);
        dialog.CancelRequested += (_, _) => Finish(UpdateChoice.Later);
        return dialog;
    }

    private static Button CreateButton(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
