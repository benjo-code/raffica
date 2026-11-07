using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The About card: the name, the version (with the note of a test build when there is one), the license, the project address and the
/// notices of the third-party components, one section per component that fills only when it is opened (the whole text in one box froze the card for a second).
/// Nothing in it is destructive, so one Close button closes it; Esc does too.
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

        // The notices are about 450 KB: one box with all of it takes the text engine a second or more to lay out, so the card would freeze when it opens. They are cut
        // into their sections (the separator lines of tools/licenses.cs) and each section's text goes into its box only when the user opens it.
        var (intro, sections) = SplitNotices(notices);
        var list = new StackPanel { Spacing = 2, Width = BoxWidth - 20, Margin = new Thickness(2) };
        list.Children.Add(CreateBox("PART_Notices", intro));
        for (var i = 0; i < sections.Count; i++)
        {
            list.Children.Add(CreateSection(i, sections[i]));
        }

        var scroll = new ScrollViewer
        {
            Name = "PART_NoticesScroll",
            Width = BoxWidth,
            Height = BoxHeight,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = list,
        };
        AutomationProperties.SetName(scroll, PdfStrings.AboutNoticesName);

        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Close };
        ok.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = PdfStrings.AboutTitle,
            Content = new StackPanel { Spacing = 4, Children = { name, versionLine, license, project, heading, scroll } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok } },
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }

    /// <summary>
    /// Cuts the notices text into the part before the first separator line (<c>====</c> alone on its line) and one section per component after it. The sections are
    /// verbatim: <c>intro + "====\n" + section</c> for each section in order gives the text back (a separator inside a licence text is longer than four characters and
    /// is no separator). A text without separator lines is all intro.
    /// </summary>
    internal static (string Intro, IReadOnlyList<string> Sections) SplitNotices(string notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        const string Separator = "====\n";
        var pieces = new List<string>();
        var start = 0;
        var at = 0;
        while ((at = notices.IndexOf(Separator, at, StringComparison.Ordinal)) >= 0)
        {
            if (at == 0 || notices[at - 1] == '\n')
            {
                pieces.Add(notices[start..at]);
                start = at + Separator.Length;
            }

            at += Separator.Length;
        }

        pieces.Add(notices[start..]);

        // The text ends with a separator in the shipped file, which leaves an empty piece at the end.
        var sections = pieces.Skip(1).ToList();
        if (sections.Count > 0 && sections[^1].Length == 0)
        {
            sections.RemoveAt(sections.Count - 1);
        }

        return (pieces[0], sections);
    }

    private static StrokeIcon Chevron(string key)
    {
        var icon = new StrokeIcon { Size = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable(key, value => value as Geometry));
        return icon;
    }

    private static TextBox CreateBox(string name, string text)
    {
        var box = new TextBox
        {
            Name = name,
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MaxHeight = BoxHeight - 40,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AutomationProperties.SetName(box, PdfStrings.AboutNoticesName);
        return box;
    }

    // One component: a toggle with its name and licence (the first two lines of the section) and, below it, the text, put into its box on the first open.
    private static StackPanel CreateSection(int index, string text)
    {
        var lines = text.Split('\n', 3);
        var title = string.IsNullOrWhiteSpace(lines[0]) ? PdfStrings.AboutNoticesName : lines[0].Trim();
        if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[0]) && !string.IsNullOrWhiteSpace(lines[1]))
        {
            title += " - " + lines[1].Trim();
        }

        var closed = Chevron("Raffica.Icon.ChevronRight");
        var open = Chevron("Raffica.Icon.ChevronDown");
        open.IsVisible = false;
        var header = new ToggleButton
        {
            Name = "PART_NoticeToggle" + index,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new Panel { Children = { closed, open } }, new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } },
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(header, title);

        var body = new ContentControl { Name = "PART_NoticeBody" + index, IsVisible = false };
        var panel = new StackPanel { Children = { header, body } };
        header.IsCheckedChanged += (_, _) =>
        {
            var expanded = header.IsChecked == true;
            closed.IsVisible = !expanded;
            open.IsVisible = expanded;
            body.IsVisible = expanded;
            if (expanded && body.Content is null)
            {
                body.Content = CreateBox("PART_Notice" + index, text);
            }
        };
        return panel;
    }
}
