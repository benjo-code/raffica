using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// The keys of the organize list in the real window: the window's shortcut handler runs first (tunnel) and must not swallow a key the list owns.
/// The list tests alone use a plain window, where no catalog shortcut exists.
/// </summary>
public sealed class OrganizeWindowKeysTests
{
    private static async Task<(WindowFixture Fixture, PdfTab Tab, OrganizeList List)> OpenAsync()
    {
        var fixture = WindowFixture.Create(height: 1800);
        try
        {
            var tab = await fixture.OpenAsync("pages-marked.pdf");
            await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
            fixture.Window.ShowPagesPane();
            ViewFixture.Pump();
            fixture.Window.ToggleOrganize();
            await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
            var pane = Assert.IsType<PagesPane>(fixture.Window.NavigationPane.Content);
            await ViewFixture.WaitAsync(() => pane.Organize is { } list && list.ContainerOf(2) is not null);
            return (fixture, tab, pane.Organize!);
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private static void Press(WindowFixture fixture, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None, string? text = null)
    {
        fixture.Window.KeyPress(key, modifiers, physical, text);
        fixture.Window.KeyRelease(key, modifiers, physical, text);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task CtrlASelectsEveryPageWithTheFocusInTheList()
    {
        var (fixture, tab, list) = await OpenAsync();
        using (fixture)
        {
            tab.Organize.Selection.Select(1);
            Assert.True(list.ContainerOf(1)!.Focus());

            Press(fixture, Key.A, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(6, tab.Organize.Selection.Positions.Count);
            Assert.True(tab.Organize.Plan.IsIdentity);
        }
    }

    [AvaloniaFact]
    public async Task CtrlCWithTheFocusInTheListDoesNotChangeThePlan()
    {
        var (fixture, tab, list) = await OpenAsync();
        using (fixture)
        {
            tab.Organize.Selection.Select(1);
            Assert.True(list.ContainerOf(1)!.Focus());

            Press(fixture, Key.C, PhysicalKey.C, RawInputModifiers.Control);

            Assert.True(tab.Organize.Plan.IsIdentity);
            Assert.Equal([1], tab.Organize.Selection.Positions.ToArray());
        }
    }

    // Every other key the list owns reaches it through the window's handler: none of them is a catalog shortcut, and the test says so.
    [AvaloniaFact]
    public async Task TheListKeysReachTheListInTheRealWindow()
    {
        var (fixture, tab, list) = await OpenAsync();
        using (fixture)
        {
            int[] Order() => [.. tab.Organize.Plan.Slots.Select(s => s.SourceIndex)];

            // Alt+Down and Alt+Up move the selected page.
            tab.Organize.Selection.Select(1);
            Assert.True(list.ContainerOf(1)!.Focus());
            Press(fixture, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
            Assert.Equal([0, 2, 1, 3, 4, 5], Order());
            Press(fixture, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            Assert.Equal([0, 1, 2, 3, 4, 5], Order());

            // Alt+End and Alt+Home go to the ends.
            Press(fixture, Key.End, PhysicalKey.End, RawInputModifiers.Alt);
            Assert.Equal([0, 2, 3, 4, 5, 1], Order());
            tab.Organize.Undo();
            ViewFixture.Pump();
            Assert.Equal([0, 1, 2, 3, 4, 5], Order());

            // The brackets turn the selected page.
            tab.Organize.Selection.Select(1);
            Assert.True(list.ContainerOf(1)!.Focus());
            Press(fixture, Key.OemCloseBrackets, PhysicalKey.BracketRight, RawInputModifiers.None, "]");
            Assert.Equal(90, tab.Organize.Plan.Slots[1].ExtraRotation);

            // Ctrl+D duplicates (Ctrl+Shift+D is the dark page, a different shortcut).
            Press(fixture, Key.D, PhysicalKey.D, RawInputModifiers.Control);
            Assert.Equal(7, tab.Organize.Plan.Slots.Count);

            // Delete deletes the selection.
            Assert.True(list.ContainerOf(tab.Organize.Selection.Positions[0])!.Focus());
            var before = tab.Organize.Plan.Slots.Count;
            Press(fixture, Key.Delete, PhysicalKey.Delete);
            Assert.Equal(before - 1, tab.Organize.Plan.Slots.Count);

            // Escape collapses the selection to the focused page and never leaves the mode.
            tab.Organize.Selection.SelectAll();
            Assert.True(list.ContainerOf(2)!.Focus());
            Press(fixture, Key.Escape, PhysicalKey.Escape);
            Assert.True(tab.Organize.IsActive);
            Assert.Single(tab.Organize.Selection.Positions);
        }
    }
}
