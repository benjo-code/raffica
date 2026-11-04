using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Raffica.Gallery.FrameDemo;

/// <summary>The content of the document pane of the frame demo: a page on the desk with a heading, two paragraphs in Carlito and a text box to type in.</summary>
internal static class DemoDocument
{
    /// <summary>Name of the text box the input measurement types into.</summary>
    public const string InputTargetName = "InputTarget";

    private const string Document = "Raffica.Font.Document";

    /// <summary>The desk with the page; <paramref name="input"/> is the text box on the page.</summary>
    public static Control Create(out TextBox input)
    {
        var heading = new TextBlock { Text = GalleryStrings.DocHeading, FontSize = 26, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 16) };
        UseDocumentFont(heading);
        var first = Paragraph(GalleryStrings.DocParagraph1);
        var second = Paragraph(GalleryStrings.DocParagraph2);

        input = new TextBox { Name = InputTargetName, Text = GalleryStrings.DocInputSample, FontSize = 16, Margin = new Thickness(0, 8, 0, 0) };
        UseDocumentFont(input);
        AutomationProperties.SetName(input, GalleryStrings.DocInputName);

        var content = new StackPanel { Children = { heading, first, second, input } };
        var page = new Border
        {
            MaxWidth = 640,
            MinHeight = 800,
            Margin = new Thickness(32, 24),
            Padding = new Thickness(56, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1),
            Child = content,
        };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable("Raffica.Brush.Surface.Page"));
        page.Bind(Border.BorderBrushProperty, page.GetResourceObservable("Raffica.Brush.Rule"));

        var desk = new Border { Child = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        desk.Bind(Border.BackgroundProperty, desk.GetResourceObservable("Raffica.Brush.Surface.Desk"));
        return desk;
    }

    private static TextBlock Paragraph(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 16, LineHeight = 23, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        UseDocumentFont(block);
        return block;
    }

    private static void UseDocumentFont(TextBlock block) =>
        block.Bind(TextBlock.FontFamilyProperty, block.GetResourceObservable(Document));

    private static void UseDocumentFont(TextBox box) =>
        box.Bind(TemplatedControl.FontFamilyProperty, box.GetResourceObservable(Document));
}
