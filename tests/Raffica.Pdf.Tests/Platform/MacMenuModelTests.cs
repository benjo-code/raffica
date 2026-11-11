using System.Globalization;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Platform.MacOS;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class MacMenuModelTests
{
    private static IEnumerable<MenuNode> Walk(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node is SubmenuNode submenu)
            {
                foreach (var inner in Walk(submenu.Items))
                {
                    yield return inner;
                }
            }
        }
    }

    private static List<string> CommandIds(IEnumerable<MenuNode> nodes) =>
        Walk(nodes).OfType<CommandNode>().Select(n => n.CommandId).ToList();

    private static string Shape(MenuNode node) => node switch
    {
        CommandNode command => command.CommandId,
        ActionNode action => "@" + action.Action,
        SubmenuNode => "submenu",
        _ => "-",
    };

    private static SubmenuNode Menu(string headerKey) => MacMenuModel.Build().Single(m => m.HeaderKey == headerKey);

    [Fact]
    public void EveryCommandNodeNamesACatalogCommand()
    {
        var known = PdfCommandCatalog.Commands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var ids = CommandIds(MacMenuModel.Build());
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Contains(id, known));
    }

    [Fact]
    public void NoCommandAppearsTwice()
    {
        var ids = CommandIds(MacMenuModel.Build());
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheMenusAreTheSpecsInOrder()
    {
        var menus = MacMenuModel.Build();
        Assert.Equal(["AppMarkName", "MenuFile", "MenuEdit", "MenuView", "MenuWindow", "MenuHelp"], menus.Select(m => m.HeaderKey).ToArray());
        Assert.Equal(
            ["@About", "app.update", "file.settings"],
            menus[0].Items.Select(Shape).ToArray());
        Assert.Equal(
            ["file.open", "submenu", "-", "file.close", "file.save", "file.saveAs", "-", "file.print"],
            menus[1].Items.Select(Shape).ToArray());
        Assert.Equal(MenuAction.OpenRecent, ((SubmenuNode)menus[1].Items[1]).Fill);
        Assert.Equal(
            ["edit.undo", "edit.redo", "-", "@Cut", "text.copy", "@Paste", "text.selectall", "-", "find.search"],
            menus[2].Items.Select(Shape).ToArray());
        Assert.Equal(
            ["zoom.in", "zoom.out", "zoom.fitwidth", "zoom.fitpage", "zoom.actual", "-", "layout.continuous", "layout.single", "layout.two", "layout.twocover", "page.dark", "-", "panes.pages", "panes.outline", "-", "@ToggleFullScreen"],
            menus[3].Items.Select(Shape).ToArray());
        Assert.Equal(["@Minimize", "@ZoomWindow", "-", "@BringAllToFront"], menus[4].Items.Select(Shape).ToArray());
        Assert.Equal(["help.shortcuts"], menus[5].Items.Select(Shape).ToArray());
    }

    [Fact]
    public void TheApplicationMenuHoldsTheFirstMenuAndOpenForTheNoWindowCase()
    {
        Assert.Equal(["@About", "app.update", "file.settings", "file.open"], MacMenuModel.AppItems().Select(Shape).ToArray());
    }

    [Fact]
    public void TheWindowMenusAreEveryMenuAfterTheApplicationMenu()
    {
        var all = MacMenuModel.Build();
        Assert.Equal(["MenuFile", "MenuEdit", "MenuView", "MenuWindow", "MenuHelp"], MacMenuModel.WindowMenus().Select(m => m.HeaderKey).ToArray());
        Assert.Equal(
            all.Skip(1).SelectMany(m => m.Items.Select(Shape)).ToArray(),
            MacMenuModel.WindowMenus().SelectMany(m => m.Items.Select(Shape)).ToArray());
    }

    [Fact]
    public void OnlyOpenIsInBothTheApplicationMenuAndTheWindowMenus()
    {
        var inApp = CommandIds(MacMenuModel.AppItems());
        var inWindows = CommandIds(MacMenuModel.WindowMenus());
        Assert.Equal([PdfCommandIds.Open], inApp.Intersect(inWindows).ToArray());
    }

    [Fact]
    public void EveryHeaderKeyExistsInBothLanguages()
    {
        var keys = new List<string>();
        foreach (var node in Walk(MacMenuModel.Build()))
        {
            switch (node)
            {
                case SubmenuNode submenu:
                    keys.Add(submenu.HeaderKey);
                    break;
                case ActionNode action:
                    keys.Add(action.HeaderKey);
                    break;
            }
        }

        keys.AddRange(["MenuNoRecent", "MenuEnterFullScreen", "MenuExitFullScreen"]);
        foreach (var key in keys.Distinct())
        {
            Assert.False(string.IsNullOrWhiteSpace(PdfStrings.GetIn(key, new CultureInfo("en"))), key);
            Assert.False(string.IsNullOrWhiteSpace(PdfStrings.GetIn(key, new CultureInfo("de"))), key);
        }
    }

    [Fact]
    public void AMacShortcutOfAnItemIsOnlyDefinedByTheCatalog()
    {
        static string Modifiers(PdfShortcut s) => s.Base switch
        {
            ShortcutBase.Command => "Meta+",
            ShortcutBase.Control => "Ctrl+",
            _ => string.Empty,
        };

        var catalogGestures = PdfCommandCatalog.Commands
            .Select(c => c.ShortcutFor(true))
            .OfType<PdfShortcut>()
            .Select(s => Modifiers(s) + (s.Shift ? "Shift+" : string.Empty) + s.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var action in Walk(MacMenuModel.Build()).OfType<ActionNode>().Where(a => a.Gesture is not null))
        {
            Assert.DoesNotContain(action.Gesture!, catalogGestures);
        }
    }
}
