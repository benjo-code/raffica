using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The switch for the whole line and the scrolling of the focused line, driven by the real pointer and the real keys of the window.</summary>
public sealed class TextEditSwitchAndScrollTests
{
    private const string Typed = "Invoice total: 1,259 EUR";

    private static async Task<(TextEditRig Rig, RunFieldEditor Field, TextRun Run)> OpenOfferingAsync()
    {
        var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Invoice total: 1,250 EUR");
        var field = rig.OpenField(run);
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        TextEditRig.Type(field, Typed);
        Assert.True((await rig.PreviewOfAsync(Typed)).WholeRunOffered);
        ViewFixture.Pump();
        return (rig, field, run);
    }

    [AvaloniaFact]
    public async Task ClickingTheSwitchKeepsTheDraftAndTurnsItOnAndTheCommitUsesIt()
    {
        var (rig, _, run) = await OpenOfferingAsync();
        using var _ = rig;
        var bar = rig.Window.TextEditOf(rig.Tab)!.Bar;
        Assert.True(bar.WholeSwitchVisible);
        var spot = bar.WholeSwitch.TranslatePoint(new Point(bar.WholeSwitch.Bounds.Width / 2, bar.WholeSwitch.Bounds.Height / 2), rig.Window)!.Value;

        rig.Window.MouseDown(spot, MouseButton.Left);
        rig.Window.MouseUp(spot, MouseButton.Left);
        ViewFixture.Pump();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        Assert.True(rig.Session.HasDraft, "the click on the switch must not write the line");
        Assert.True(rig.Session.WholeRun);
        Assert.True(bar.WholeSwitchVisible);
        Assert.True(bar.WholeSwitchOn);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);

        // The field has the keyboard again, and Enter writes the whole line in the replacement font.
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        var written = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == Typed);
        Assert.NotEqual(run.FontName, written.FontName);
    }

    [AvaloniaFact]
    public async Task TheKeyboardTogglesTheSwitchFromTheField()
    {
        var (rig, _, _) = await OpenOfferingAsync();
        using var _ = rig;
        var bar = rig.Window.TextEditOf(rig.Tab)!.Bar;
        Assert.False(rig.Session.WholeRun);

        // Ctrl+Shift+G (Cmd+Shift+G on a Mac), through the real window with the field focused; no Alt, so AltGr layouts are not touched.
        rig.Press(PhysicalKey.G, (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control) | RawInputModifiers.Shift);
        Assert.True(rig.Session.WholeRun);
        Assert.True(bar.WholeSwitchOn);
        Assert.True(rig.Session.HasDraft);
        Assert.True(rig.FieldHasFocus);
        Assert.Contains(TextEditHintBar.WholeRunKeyName, bar.WholeSwitch.Content as string ?? string.Empty, StringComparison.Ordinal);
        rig.Press(PhysicalKey.G, (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control) | RawInputModifiers.Shift);
        Assert.False(rig.Session.WholeRun);
        rig.Press(PhysicalKey.G, (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control) | RawInputModifiers.Shift);
        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.Contains(await rig.DocumentRunsAsync(), r => r.Text.TrimEnd() == Typed && r.FontName != "x");
        Assert.NotEqual((await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Kontoauszug März").FontName, (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == Typed).FontName);
    }

    [AvaloniaFact]
    public async Task AFocusedLineBelowTheFoldIsScrolledIntoView()
    {
        using var rig = await TextEditRig.OpenAsync("text-dense.pdf");
        await rig.EnterModeAsync();
        var last = rig.Session.RunsOf(0).OrderBy(r => r.Bounds.Top).Last();
        var height = rig.View.Bounds.Height;
        Assert.True(rig.View.ToView(0, last.Bounds)!.Value.Bottom > height, "the line starts below the fold");

        rig.Session.Focus(last);
        ViewFixture.Pump();
        var rect = rig.View.ToView(last.Page, last.Bounds)!.Value;
        Assert.True(rect.Top >= 0 && rect.Bottom <= height, $"{rect} not inside 0..{height}");

        // Walking with Tab brings each line into view too.
        rig.View.Focus();
        rig.Session.Focus(null);
        for (var i = 0; i < 40; i++)
        {
            Assert.True(rig.Interaction.HandleKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, Source = rig.View }));
            var focused = rig.Session.Focused!;
            ViewFixture.Pump();
            var r = rig.View.ToView(focused.Page, focused.Bounds)!.Value;
            Assert.True(r.Top >= 0 && r.Bottom <= height, $"line {i}: {r}");
        }
    }

    [AvaloniaFact]
    public async Task WithNoLineFocusedAnArrowScrollsThePage()
    {
        using var rig = await TextEditRig.OpenAsync("text-dense.pdf");
        await rig.EnterModeAsync();
        rig.View.Focus();
        Assert.Null(rig.Session.Focused);
        var run = rig.Session.RunsOf(0).OrderBy(r => r.Bounds.Top).First();
        var before = rig.View.ToView(0, run.Bounds)!.Value.Top;

        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, Source = rig.View };
        rig.View.RaiseEvent(e);
        ViewFixture.Pump();
        Assert.True(e.Handled, "the page view's own scrolling takes the arrow");
        Assert.Null(rig.Session.Focused);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.True(rig.View.ToView(0, run.Bounds)!.Value.Top < before, "the page scrolled");
    }
}
