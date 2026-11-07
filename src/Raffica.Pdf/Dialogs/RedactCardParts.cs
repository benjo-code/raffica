using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Raffica.Pdf.Dialogs;

/// <summary>The pieces the redaction cards share: a heading with bulleted lines (a line is text, never colour alone) and the scrolling body.</summary>
internal static class RedactCardParts
{
    // The report scrolls beyond this height; the buttons stay in view.
    public const double BodyMaxHeight = 340;

    /// <summary>A section: a heading and one bulleted line each; null when there are no lines.</summary>
    public static StackPanel? Section(string name, string heading, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return null;
        }

        var section = new StackPanel { Name = name, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var title = new TextBlock { Name = name + "_Heading", Text = heading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(title, heading);
        section.Children.Add(title);
        foreach (var line in lines)
        {
            var block = new TextBlock { Name = "PART_Line", Text = "• " + line, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(block, line);
            section.Children.Add(block);
        }

        return section;
    }

    /// <summary>Adds a section to a body when it has lines.</summary>
    public static void AddSection(Panel body, string name, string heading, IReadOnlyList<string> lines)
    {
        if (Section(name, heading, lines) is { } section)
        {
            body.Children.Add(section);
        }
    }

    /// <summary>The scrolling body of a card.</summary>
    public static ScrollViewer Scroll(Control content) => new()
    {
        MaxHeight = BodyMaxHeight,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        Content = content,
    };

    /// <summary>The name of the file as the first line of a card.</summary>
    public static TextBlock Name(string fileName) => new() { Name = "PART_FileName", Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
}
