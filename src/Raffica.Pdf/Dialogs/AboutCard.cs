using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The About card: the name, the version (with the note of a test build when there is one), the license, the project address and the
/// notices of the third-party components in a box that scrolls. Nothing in it is destructive, so one Close button closes it; Esc does too.
/// The notices text is handed in by the caller, who reads it when the card is opened.
/// </summary>
internal static class AboutCard
{
    /// <summary>The license of the app.</summary>
    public const string License = "GPL-3.0-or-later";

    /// <summary>The address of the project, shown as text (the card opens nothing).</summary>
    public const string ProjectAddress = "https://github.com/benjo-code/raffica";

    private const double BoxWidth = 560;
    private const double BoxHeight = 260;

    /// <param name="version">The version of the app.</param>
    /// <param name="testBuildNote">The note after the version of a test build, or null in a release.</param>
    /// <param name="notices">The notices text of the third-party components.</param>
    /// <param name="done">Called once when the card is closed.</param>
    public static Dialog Create(string version, string? testBuildNote, string notices, Action done)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(done);

        var name = new TextBlock { Name = "PART_Name", Text = PdfStrings.AboutName };
        name.Classes.Add("heading-dialog");
        var versionText = PdfStrings.Fill(PdfStrings.AboutVersionFormat, version);
        var versionLine = new TextBlock
        {
            Name = "PART_Version",
            Text = string.IsNullOrEmpty(testBuildNote) ? versionText : versionText + " " + testBuildNote,
        };
        var license = new TextBlock { Name = "PART_License", Text = PdfStrings.Fill(PdfStrings.AboutLicenseFormat, License) };
        var project = new TextBlock
        {
            Name = "PART_Project",
            Text = PdfStrings.Fill(PdfStrings.AboutProjectFormat, ProjectAddress),
            TextWrapping = TextWrapping.Wrap,
        };
        var heading = new TextBlock { Text = PdfStrings.AboutNoticesHeading, Margin = new Thickness(0, 8, 0, 0) };
        heading.Classes.Add("secondary");

        var box = new TextBox
        {
            Name = "PART_Notices",
            Text = notices,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Width = BoxWidth,
            Height = BoxHeight,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AutomationProperties.SetName(box, PdfStrings.AboutNoticesName);

        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Close };
        ok.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = PdfStrings.AboutTitle,
            Content = new StackPanel { Spacing = 4, Children = { name, versionLine, license, project, heading, box } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok } },
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
