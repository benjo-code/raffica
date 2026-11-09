using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Gallery.Sections;

/// <summary>
/// The foundations of the design system: the type specimen (400, 500 and 600, the sizes of the type scale, tabular and proportional
/// figures), every colour token as a swatch bound to its brush with the key as the label, the four app inks and the icon set at 16 and 20 px.
/// </summary>
internal sealed partial class FoundationsSection : UserControl
{
    private static readonly string[] IconNames =
    [
        "Check", "ChevronDown", "ChevronRight", "X", "Search", "Plus", "Minus", "Info", "TriangleAlert", "OctagonAlert",
        "Bold", "Italic", "Underline", "Undo", "Redo", "AlignLeft", "AlignCenter", "AlignRight",
        "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp", "BookCopy", "BookOpen", "Brush", "ChevronLeft", "ChevronsLeft",
        "ChevronsRight", "Circle", "CircleHelp", "CircleX", "Copy", "CopyPlus", "CornerDownRight", "CornerLeftUp",
        "CornerRightDown", "Eraser", "EyeOff", "File", "FileInput", "FileOutput", "FilePlus", "FileX", "FolderOpen",
        "GalleryVertical", "Highlighter", "Image", "ImageDown", "Languages", "LayoutGrid", "ListChecks", "ListTree", "Maximize",
        "Moon", "MousePointer", "MoveDown", "MoveHorizontal", "MoveUp", "MoveUpRight", "OctagonX", "PanelLeft", "PanelRight",
        "PenLine", "Pencil", "Percent", "Printer", "RefreshCw", "RotateCcw", "RotateCw", "Save", "SaveAll", "ScanSearch", "ScanText",
        "Scissors", "Settings", "Signature", "Slash", "Square", "SquareCheck", "SquareDashed", "SquarePen", "SquareX",
        "Strikethrough", "TextSelect", "Trash2", "Type", "Undo2", "ZoomIn", "ZoomOut",
    ];

    public FoundationsSection()
    {
        InitializeComponent();
        Title.Text = GalleryStrings.SectionFoundations;
        TypeTitle.Text = GalleryStrings.FoundType;
        ColoursTitle.Text = GalleryStrings.FoundColours;
        InksTitle.Text = GalleryStrings.FoundInks;
        IconsTitle.Text = GalleryStrings.FoundIcons;

        AddType();
        AddFigures();
        foreach (var token in Tokens)
        {
            Colours.Children.Add(Swatch(token));
        }

        AddInks();
        AddIcons();
    }

    /// <summary>
    /// The colour tokens of appendix A of the design spec, written out because Native AOT does not look them up by reflection
    /// (a test compares this list with the token file).
    /// </summary>
    internal static IReadOnlyList<string> Tokens { get; } =
    [
        "Surface.Page", "Surface.Band", "Surface.Desk", "State.Hover", "Selection.Tint",
        "Text.Primary", "Text.Secondary", "Text.Disabled", "Text.OnInk",
        "Border.Control", "Rule", "Grid.Line",
        "Button.Primary.Fill", "Button.Primary.Hover", "Scrim",
        "Ink.Write", "Ink.Sheets", "Ink.Slides", "Ink.Pdf",
        "Review.Violet", "Review.Tint", "Danger", "Warning.Icon",
        "Paper.Caret", "Paper.Edge", "Paper.Selection", "Paper.OnSelection",
    ];

    private static TextBlock Cell(string text, int row, int column, params string[] classes)
    {
        var block = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in classes)
        {
            block.Classes.Add(c);
        }

        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        return block;
    }

    private static StackPanel Swatch(string token)
    {
        var chip = new Border
        {
            Name = "Swatch",
            Width = 40,
            Height = 24,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
        };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Raffica.Brush." + token));
        chip.Bind(Border.BorderBrushProperty, chip.GetResourceObservable("Raffica.Brush.Border.Control"));
        var label = new TextBlock { Text = "Raffica.Brush." + token, Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Width = 290, Margin = new Thickness(0, 0, 12, 8) };
        cell.Children.Add(chip);
        cell.Children.Add(label);
        return cell;
    }

    private static StrokeIcon Icon(string name, int size)
    {
        Application.Current!.TryGetResource("Raffica.Icon." + name, null, out var geometry);
        return new StrokeIcon { Size = size, Data = geometry as Geometry, VerticalAlignment = VerticalAlignment.Center };
    }

    private void AddType()
    {
        (string Class, string Label)[] scale =
        [
            ("ui", GalleryStrings.TypeUi),
            ("ui-medium", GalleryStrings.TypeMedium),
            ("ui-emphasis", GalleryStrings.TypeEmphasis),
            ("secondary", GalleryStrings.TypeSecondary),
            ("label", GalleryStrings.TypeLabel),
            ("heading-dialog", GalleryStrings.TypeHeading),
        ];
        foreach (var (style, label) in scale)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
            var name = new TextBlock { Text = label, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
            var sample = new TextBlock { Text = GalleryStrings.TypeSpecimen, Classes = { style } };
            Grid.SetColumn(sample, 1);
            row.Children.Add(name);
            row.Children.Add(sample);
            TypeRows.Children.Add(row);
        }
    }

    private void AddFigures()
    {
        // Numbers that align: the same column of figures with proportional and with tabular digits.
        string[] numbers = ["1,111.11", "8,808.08", "10,000.00"];
        Figures.Children.Add(Cell(GalleryStrings.TypeProportional, 0, 0, "label"));
        Figures.Children.Add(Cell(GalleryStrings.TypeTabular, 0, 1, "label"));
        for (var i = 0; i < numbers.Length; i++)
        {
            Figures.Children.Add(Cell(numbers[i], i + 1, 0, "ui"));
            Figures.Children.Add(Cell(numbers[i], i + 1, 1, "ui", "tnum"));
        }
    }

    private void AddInks()
    {
        (string Token, string Name)[] inks =
        [
            ("Ink.Write", GalleryStrings.InkWrite),
            ("Ink.Sheets", GalleryStrings.InkSheets),
            ("Ink.Slides", GalleryStrings.InkSlides),
            ("Ink.Pdf", GalleryStrings.InkPdf),
        ];
        foreach (var (token, name) in inks)
        {
            var block = new Border { Name = "InkSample", Width = 200, Height = 56, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 12, 8) };
            block.Bind(Border.BackgroundProperty, block.GetResourceObservable("Raffica.Brush." + token));
            var text = new TextBlock { Text = name + " - " + GalleryStrings.InkSample, Classes = { "ui-emphasis" }, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Raffica.Brush.Text.OnInk"));
            block.Child = text;
            Inks.Children.Add(block);
        }
    }

    private void AddIcons()
    {
        foreach (var name in IconNames)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Width = 190, Margin = new Thickness(0, 0, 12, 8) };
            cell.Children.Add(Icon(name, 16));
            cell.Children.Add(Icon(name, 20));
            cell.Children.Add(new TextBlock { Text = name, Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center });
            Icons.Children.Add(cell);
        }
    }
}
