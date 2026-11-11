using Raffica.Pdf.Commands;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>A node of the macOS menu bar model; plain data that compiles and is tested on every system.</summary>
internal abstract record MenuNode;

/// <summary>An item that runs an app command; its header and shortcut come from the command catalog.</summary>
internal sealed record CommandNode(string CommandId) : MenuNode;

/// <summary>An item the system defines (Cut, Minimize, ...); <paramref name="Gesture"/> is set only where the catalog has no shortcut for it.</summary>
internal sealed record ActionNode(MenuAction Action, string HeaderKey, string? Gesture = null) : MenuNode;

/// <summary>A menu or submenu; <paramref name="Fill"/> names the action that fills it at run time.</summary>
internal sealed record SubmenuNode(string HeaderKey, IReadOnlyList<MenuNode> Items, MenuAction? Fill = null) : MenuNode;

/// <summary>A separator line.</summary>
internal sealed record SeparatorNode : MenuNode;

/// <summary>The items of the menu bar that no catalog command stands for.</summary>
internal enum MenuAction
{
    About,
    Cut,
    Paste,
    OpenRecent,
    Minimize,
    ZoomWindow,
    BringAllToFront,
    ToggleFullScreen,
}

/// <summary>
/// The menu bar of Raffica PDF on macOS as data. Services, Hide, Hide others, Show all and Quit are added to the
/// application menu by the native side and are not part of the model. Avalonia fills the menu bar from two places: the menu of the application is the
/// application menu only, the menus of a window (File, Edit, ...) come from the menu set on that window.
/// </summary>
internal static class MacMenuModel
{
    private static readonly SeparatorNode Separator = new();

    private static CommandNode Command(string id) => new(id);

    /// <summary>
    /// The items of the application menu (the leftmost menu, under the name of the app): the first menu of <see cref="Build"/> without its header, then Open.
    /// Open is shown only while no window exists (the File menu belongs to a window and is gone then), so Cmd+O never has two items.
    /// </summary>
    public static IReadOnlyList<MenuNode> AppItems() => [.. Build()[0].Items, Command(PdfCommandIds.Open)];

    /// <summary>The menus a window brings to the menu bar (File, Edit, View, Window, Help): every menu of <see cref="Build"/> after the application menu.</summary>
    public static IReadOnlyList<SubmenuNode> WindowMenus() => [.. Build().Skip(1)];

    public static IReadOnlyList<SubmenuNode> Build() =>
    [
        new SubmenuNode("AppMarkName",
        [
            new ActionNode(MenuAction.About, "MenuAbout"),
            Command(PdfCommandIds.Update),
            Command(PdfCommandIds.Settings),
        ]),
        new SubmenuNode("MenuFile",
        [
            Command(PdfCommandIds.Open),
            new SubmenuNode("MenuOpenRecent", [], MenuAction.OpenRecent),
            Separator,
            Command(PdfCommandIds.CloseTab),
            Command(PdfCommandIds.Save),
            Command(PdfCommandIds.SaveAs),
            Separator,
            Command(PdfCommandIds.Print),
        ]),
        new SubmenuNode("MenuEdit",
        [
            Command(PdfCommandIds.Undo),
            Command(PdfCommandIds.Redo),
            Separator,
            new ActionNode(MenuAction.Cut, "MenuCut"),
            Command(PdfCommandIds.Copy),
            new ActionNode(MenuAction.Paste, "MenuPaste"),
            Command(PdfCommandIds.SelectAll),
            Separator,
            Command(PdfCommandIds.Search),
        ]),
        new SubmenuNode("MenuView",
        [
            Command(PdfCommandIds.ZoomIn),
            Command(PdfCommandIds.ZoomOut),
            Command(PdfCommandIds.FitWidth),
            Command(PdfCommandIds.FitPage),
            Command(PdfCommandIds.ActualSize),
            Separator,
            Command(PdfCommandIds.Continuous),
            Command(PdfCommandIds.SinglePage),
            Command(PdfCommandIds.TwoPages),
            Command(PdfCommandIds.TwoPagesCover),
            Command(PdfCommandIds.DarkPage),
            Separator,
            Command(PdfCommandIds.PagesPane),
            Command(PdfCommandIds.OutlinePane),
            Separator,
            new ActionNode(MenuAction.ToggleFullScreen, "MenuEnterFullScreen", "Ctrl+Meta+F"),
        ]),
        new SubmenuNode("MenuWindow",
        [
            new ActionNode(MenuAction.Minimize, "MenuMinimize", "Meta+M"),
            new ActionNode(MenuAction.ZoomWindow, "MenuZoomWindow"),
            Separator,
            new ActionNode(MenuAction.BringAllToFront, "MenuBringAllToFront"),
        ]),
        new SubmenuNode("MenuHelp",
        [
            Command(PdfCommandIds.Help),
        ]),
    ];
}
