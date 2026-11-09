using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>One line of the help card: the ribbon group, the command and its shortcut already written for the platform and the language.</summary>
internal sealed record HelpEntry(string Group, string Name, string Shortcut);

/// <summary>The list of keyboard shortcuts (F1): the commands that have one, under the heading of their ribbon group. One button closes it; Esc does too.</summary>
internal static class HelpCard
{
    private const double ListWidth = 380;
    private const double ListMaxHeight = 380;

    /// <param name="entries">The shortcuts.</param>
    /// <param name="done">Called when the card is closed.</param>
    /// <param name="about">When given, the card has a second button "About Raffica PDF" that calls it.</param>
    public static Dialog Create(IReadOnlyList<HelpEntry> entries, Action done, Action? about = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(done);

        var list = new StackPanel { Spacing = 2, MinWidth = ListWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        string? group = null;
        foreach (var entry in entries)
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                var heading = new TextBlock { Text = group, Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 10, 0, 2) };
                heading.Classes.Add("secondary");
                list.Children.Add(heading);
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            var name = new TextBlock { Text = entry.Name, TextWrapping = TextWrapping.Wrap };
            var shortcut = new TextBlock { Text = entry.Shortcut, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            shortcut.Classes.Add("secondary");
            Grid.SetColumn(shortcut, 1);
            row.Children.Add(name);
            row.Children.Add(shortcut);
            list.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Name = "PART_List",
            MaxHeight = ListMaxHeight,
            Focusable = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list,
        };
        AutomationProperties.SetName(scroll, PdfStrings.HelpListName);

        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Close };
        ok.Classes.Add("primary");
        Button? aboutButton = null;
        if (about is not null)
        {
            aboutButton = new Button { Name = "PART_About", Content = PdfStrings.CmdAbout };
            aboutButton.Classes.Add("boxed");
            aboutButton.Click += (_, _) => about();
        }

        var dialog = new Dialog
        {
            Title = PdfStrings.HelpTitle,
            Content = scroll,
            Footer = CardFooter.Build(null, aboutButton is null ? null : [aboutButton], ok),
        };

        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
