using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace ShellProbe;

/// <summary>
/// The frame mode: a static copy of the Write board of Direction D at 1440 × 900 (Docs/design/direction-d/Main.dc.html).
/// Nothing in it works. The AXAML holds the layout; this file fills it with the texts of the resources and the controls
/// of the ribbon, so that the probe pays for the same kinds of control as the real frame.
/// </summary>
internal sealed partial class ProbeWindow : Window
{
    private static readonly IBrush Ink = Brush.Parse("#1D4F91");
    private static readonly IBrush Band = Brush.Parse("#F5F6F8");
    private static readonly IBrush Page = Brush.Parse("#FFFFFF");
    private static readonly IBrush Control = Brush.Parse("#7A818B");
    private static readonly IBrush Rule = Brush.Parse("#D3D7DD");
    private static readonly IBrush Secondary = Brush.Parse("#4A5059");
    private static readonly IBrush Selection = Brush.Parse("#DCDFE4");

    /// <summary>16 px stroke icons (1.5 px, round caps and joins), cycled over the commands.</summary>
    private static readonly string[] IconData =
    [
        "M3 3h10v10H3z", "M3 4h10M3 8h10M3 12h7", "M8 2v12M2 8h12", "M8 3a5 5 0 1 0 0.01 0z", "M3 13L13 3",
        "M4 12V4l8 8V4", "M3 8l3 3 7-7", "M2 4h12M2 8h12M2 12h12", "M4 3h8v10H4zM6 6h4M6 9h4", "M3 13h10M8 3v7M5 7l3 3 3-3",
        "M3 3h4v4H3zM9 9h4v4H9z", "M2 8c2-4 10-4 12 0-2 4-10 4-12 0z",
    ];

    private int _iconIndex;

    public ProbeWindow()
    {
        InitializeComponent();
        Title = Strings.Get("AppName");
        FillTitleBar();
        FillCommandTabs();
        FillRibbon();
        FillWorkArea();
        FillStatusBar();
    }

    private Path StrokeIcon(int size = 16)
    {
        var data = IconData[_iconIndex++ % IconData.Length];
        return new Path
        {
            Data = Geometry.Parse(data),
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
        };
    }

    private Button Command(string key, bool tall = false, bool showLabel = false)
    {
        var text = Strings.Get(key);
        var content = new StackPanel
        {
            Orientation = tall ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = tall ? 4 : 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(StrokeIcon(tall ? 20 : 16));
        if (tall || showLabel)
        {
            content.Children.Add(new TextBlock { Text = text, FontSize = tall ? 12 : 14 });
        }

        var button = new Button
        {
            Content = content,
            Height = tall ? 54 : 26,
            MinHeight = tall ? 54 : 26,
            MinWidth = tall ? 48 : 26,
            Padding = new Thickness(5, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button Dropdown(string key, double width)
    {
        var text = Strings.Get(key);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var chevron = new Path
        {
            Data = Geometry.Parse("M3 6l5 5 5-5"),
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
        };
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        var button = new Button
        {
            Content = content,
            Width = width,
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(7, 0, 4, 0),
            Background = Page,
            BorderBrush = Control,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button StyleTile(string key, double fontSize, bool on, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal)
    {
        var text = Strings.Get(key);
        var foreground = on ? Brushes.White : Brush.Parse("#1A1D21");
        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontFamily = new FontFamily("avares://ShellProbe/Assets/Fonts#Carlito"),
            FontSize = fontSize,
            FontWeight = weight,
            FontStyle = style,
            Foreground = foreground,
        });
        var name = new TextBlock { Text = text, FontSize = 12, Foreground = foreground };
        Grid.SetRow(name, 1);
        content.Children.Add(name);
        var button = new Button
        {
            Content = content,
            Width = 90,
            Height = 54,
            Padding = new Thickness(7, 6, 7, 5),
            Background = on ? Ink : Page,
            BorderBrush = on ? Ink : Control,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Grid Group(string labelKey, params Control[] rows)
    {
        var label = Strings.Get(labelKey);
        var grid = new Grid { RowDefinitions = new RowDefinitions("54,2,14"), Margin = new Thickness(8, 6, 8, 4) };
        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        body.Children.AddRange(rows);
        grid.Children.Add(body);
        var caption = new TextBlock { Text = label, FontSize = 12, Foreground = Secondary };
        Grid.SetRow(caption, 2);
        grid.Children.Add(caption);
        AutomationProperties.SetName(grid, label);
        return grid;
    }

    private static StackPanel Column(params Control[] rows)
    {
        var column = new StackPanel { Spacing = 2 };
        column.Children.AddRange(rows);
        return column;
    }

    private static StackPanel Row(params Control[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.AddRange(items);
        return row;
    }

    private Button[] Commands(params string[] keys) => [.. keys.Select(key => Command(key))];

    private static Button Overflow(string groupKey)
    {
        var button = new Button
        {
            Content = new Path { Data = Geometry.Parse("M3 6l5 5 5-5"), Stroke = Brushes.Black, StrokeThickness = 1.5, Width = 16, Height = 16, Stretch = Stretch.None },
            Width = 24,
            Height = 24,
            Padding = default,
            Background = Brushes.Transparent,
            BorderThickness = default,
            CornerRadius = new CornerRadius(3),
        };
        AutomationProperties.SetName(button, Strings.Get("MoreCommands", Strings.Get(groupKey)));
        return button;
    }

    private void FillTitleBar()
    {
        this.FindControl<TextBlock>("AppLabel")!.Text = Strings.Get("AppName");
        this.FindControl<TextBox>("SearchBox")!.PlaceholderText = Strings.Get("Search");
        var tabs = this.FindControl<StackPanel>("DocTabs")!;
        var first = true;
        foreach (var key in new[] { "Doc1", "Doc2", "Doc3" })
        {
            var name = Strings.Get(key);
            var close = new Button { Content = StrokeIcon(12), Width = 24, Height = 24, MinHeight = 24, Padding = default, Background = Brushes.Transparent, CornerRadius = new CornerRadius(3) };
            var tab = new Border
            {
                Height = 28,
                MaxWidth = 236,
                Background = first ? Band : Brushes.Transparent,
                BorderBrush = first ? Rule : Brushes.Transparent,
                BorderThickness = new Thickness(1, first ? 3 : 0, 1, 0),
                Padding = new Thickness(10, 0, 2, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = name, FontWeight = first ? FontWeight.Medium : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                        close,
                    },
                },
            };
            tabs.Children.Add(tab);
            first = false;
        }

        tabs.Children.Add(Command("NewDocument"));
        var captions = this.FindControl<StackPanel>("Captions")!;
        foreach (var key in new[] { "CmdPaste", "CmdCopy", "CmdCut" })
        {
            var cap = new Button { Content = StrokeIcon(), Width = 46, Height = 36, Padding = default, Background = Brushes.Transparent, CornerRadius = default };
            captions.Children.Add(cap);
        }
    }

    private void FillCommandTabs()
    {
        var tabs = this.FindControl<StackPanel>("CommandTabs")!;
        var current = "RibbonHome";
        foreach (var key in new[] { "RibbonFile", "RibbonHome", "RibbonInsert", "RibbonLayout", "RibbonReferences", "RibbonReview", "RibbonView" })
        {
            var on = key == current;
            tabs.Children.Add(new Button
            {
                Content = Strings.Get(key),
                Height = on ? 33 : 32,
                Padding = new Thickness(12, 0),
                FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium,
                Foreground = on ? Ink : Brush.Parse("#1A1D21"),
                Background = on ? Band : Brushes.Transparent,
                BorderBrush = on ? Ink : Brushes.Transparent,
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = default,
            });
        }
    }

    private void FillRibbon()
    {
        var ribbon = this.FindControl<StackPanel>("Ribbon")!;

        ribbon.Children.Add(Group(
            "GroupClipboard",
            Command("CmdPaste", tall: true),
            Column(Command("CmdCut"), Command("CmdCopy")),
            Overflow("GroupClipboard")));
        ribbon.Children.Add(Group(
            "GroupFont",
            Column(
                Row(Dropdown("CmdFontName", 142), Dropdown("CmdFontSize", 50)),
                Row(Commands("CmdBold", "CmdItalic", "CmdUnderline", "CmdHighlight", "CmdFontColor", "CmdClearFormat")
                    .Concat(Commands("CmdStrike", "CmdSubscript", "CmdSuperscript", "CmdCase", "CmdShading", "CmdBorders")).ToArray<Control>())),
            Overflow("GroupFont")));
        ribbon.Children.Add(Group(
            "GroupParagraph",
            Column(
                Row(Commands("CmdBullets", "CmdNumbering", "CmdOutdent", "CmdIndent", "CmdLineSpacing", "CmdSort", "CmdMarks")),
                Row(Commands("CmdAlignLeft", "CmdAlignCenter", "CmdAlignRight", "CmdJustify"))),
            Overflow("GroupParagraph")));
        ribbon.Children.Add(Group(
            "GroupStyles",
            StyleTile("StyleNormal", 15, false),
            StyleTile("StyleHeading1", 17, true, FontWeight.Bold),
            StyleTile("StyleHeading2", 15, false, FontWeight.Bold),
            StyleTile("StyleQuote", 15, false, style: FontStyle.Italic),
            Command("StyleMore"),
            Dropdown("StyleHeading1", 132),
            Overflow("GroupStyles")));
        ribbon.Children.Add(Group(
            "GroupEditing",
            Column(Command("CmdFind", showLabel: true), Command("CmdReplace", showLabel: true)),
            Overflow("GroupEditing")));
        ribbon.Children.Add(Group(
            "GroupReview",
            Column(Command("CmdTrack", showLabel: true), Command("CmdNewComment", showLabel: true)),
            Overflow("GroupReview")));
        ribbon.Children.Add(Group(
            "GroupInsert",
            Column(
                Row(Commands("CmdTable", "CmdPicture", "CmdShape", "CmdChart", "CmdLink")),
                Row(Commands("CmdBookmark", "CmdHeader", "CmdFooter", "CmdPageNumber"))),
            Overflow("GroupInsert")));
        ribbon.Children.Add(Group(
            "GroupPage",
            Column(Row(Commands("CmdMargins", "CmdOrientation", "CmdPageSize")), Row(Commands("CmdColumns", "CmdBreaks"))),
            Overflow("GroupPage")));
        ribbon.Children.Add(Group(
            "GroupTables",
            Column(Row(Commands("CmdTableRow", "CmdTableColumn")), Row(Commands("CmdMerge", "CmdSplit"))),
            Overflow("GroupTables")));
    }

    private void FillWorkArea()
    {
        this.FindControl<TextBlock>("NavigationLabel")!.Text = Strings.Get("Navigation");
        this.FindControl<TextBlock>("ThisDocumentLabel")!.Text = Strings.Get("ThisDocument");

        var views = this.FindControl<StackPanel>("NavViews")!;
        var selected = true;
        foreach (var key in new[] { "NavHeadings", "NavPages", "NavResults" })
        {
            views.Children.Add(new Button
            {
                Content = Strings.Get(key),
                Height = 24,
                MinHeight = 24,
                FontSize = 12,
                Padding = new Thickness(5, 0),
                Background = selected ? Selection : Page,
                BorderBrush = Control,
                BorderThickness = new Thickness(1),
                CornerRadius = default,
            });
            selected = false;
        }

        var outline = this.FindControl<StackPanel>("Outline")!;
        var entries = new (string Number, string Key, int Indent)[]
        {
            ("1", "Outline1", 12), ("2", "Outline2", 12), ("3", "Outline3", 12), ("3.1", "Outline31", 28),
            ("3.2", "Outline32", 28), ("3.3", "Outline33", 28), ("4", "Outline4", 12), ("5", "Outline5", 12),
        };
        foreach (var (number, key, indent) in entries)
        {
            var on = key == "Outline3";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = number, FontWeight = FontWeight.SemiBold, MinWidth = 24, Foreground = on ? Brush.Parse("#1A1D21") : Secondary });
            row.Children.Add(new TextBlock { Text = Strings.Get(key), FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal });
            outline.Children.Add(new Border { MinHeight = 32, Padding = new Thickness(indent, 0, 12, 0), Background = on ? Selection : Brushes.Transparent, Child = row });
        }

        var facts = this.FindControl<StackPanel>("DocumentFacts")!;
        foreach (var (key, value) in new[] { ("Comments", "1"), ("TrackedChanges", "1"), ("Footnotes", "3") })
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            line.Children.Add(new TextBlock { Text = Strings.Get(key) });
            var count = new TextBlock { Text = value, FontSize = 13, Foreground = Secondary };
            Grid.SetColumn(count, 1);
            line.Children.Add(count);
            facts.Children.Add(line);
        }

        this.FindControl<TextBlock>("PageHeading")!.Text = Strings.Get("PageHeading");
        this.FindControl<TextBlock>("PageBody")!.Text = Strings.Get("PageBody");
        this.FindControl<TextBlock>("PageItalic")!.Text = Strings.Get("StyleQuote");
        this.FindControl<TextBlock>("CommentAuthor")!.Text = Strings.Get("CommentAuthor");
        this.FindControl<TextBlock>("CommentTime")!.Text = Strings.Get("CommentTime");
        this.FindControl<TextBlock>("CommentText")!.Text = Strings.Get("CommentText");
        var buttons = this.FindControl<StackPanel>("CommentButtons")!;
        buttons.Children.Add(new Button { Content = Strings.Get("CommentReply"), Height = 26, MinHeight = 26, Padding = new Thickness(5, 0), Background = Page, BorderBrush = Control, CornerRadius = new CornerRadius(3) });
        buttons.Children.Add(new Button { Content = Strings.Get("CommentResolve"), Height = 26, MinHeight = 26, Padding = new Thickness(5, 0), Background = Brushes.Transparent, CornerRadius = new CornerRadius(3) });
    }

    private void FillStatusBar()
    {
        var cells = this.FindControl<StackPanel>("StatusCells")!;
        foreach (var key in new[] { "StatusSaved", "StatusPage", "StatusWords" })
        {
            cells.Children.Add(new TextBlock { Text = Strings.Get(key), FontSize = 13, Foreground = Secondary });
        }

        var zoom = this.FindControl<StackPanel>("Zoom")!;
        zoom.Children.Add(Command("ZoomOut"));
        zoom.Children.Add(new Border { Width = 96, Height = 1, Background = Control, VerticalAlignment = VerticalAlignment.Center });
        zoom.Children.Add(Command("ZoomIn"));
        zoom.Children.Add(new TextBlock { Text = Strings.Get("ZoomValue"), FontSize = 13, Foreground = Secondary, MinWidth = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
    }
}
