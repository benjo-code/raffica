using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>The keys routed through the real page view (its own scrolling runs too), not handed to the interaction directly.</summary>
public sealed class RedactViewKeyTests
{
    private static PageRect AreaOf(RedactFixture f, Guid id) => f.Session.Plan.Marks.Single(m => m.Id == id).Areas[0];

    [AvaloniaFact]
    public async Task ArrowsMoveAndShiftArrowsResizeThroughTheRealView()
    {
        using var f = await RedactFixture.OpenAsync();
        var id = f.Session.AddArea(0, new PageRect(100, 100, 160, 140)).MarkId!.Value;
        f.View.Focus();

        Assert.True(f.PressInView(Key.Right));
        Assert.Equal(new PageRect(101, 100, 161, 140), AreaOf(f, id));
        f.Time.Advance(1000);
        Assert.True(f.PressInView(Key.Down));
        Assert.Equal(new PageRect(101, 101, 161, 141), AreaOf(f, id));
        f.Time.Advance(1000);
        Assert.True(f.PressInView(Key.Left));
        Assert.True(f.PressInView(Key.Up));
        Assert.Equal(new PageRect(100, 100, 160, 140), AreaOf(f, id));
        f.Time.Advance(1000);
        Assert.True(f.PressInView(Key.Right, KeyModifiers.Shift));
        Assert.True(f.PressInView(Key.Down, KeyModifiers.Shift));
        Assert.Equal(new PageRect(100, 100, 161, 141), AreaOf(f, id));
    }

    [AvaloniaFact]
    public async Task TabDeleteAndCtrlEnterWorkThroughTheRealView()
    {
        using var f = await RedactFixture.OpenAsync();
        var a = f.Session.AddArea(0, new PageRect(50, 100, 150, 140)).MarkId!.Value;
        var b = f.Session.AddArea(0, new PageRect(50, 200, 150, 240)).MarkId!.Value;
        f.Session.Selection.Clear();
        f.View.Focus();

        Assert.True(f.PressInView(Key.Tab));
        Assert.Equal(a, f.Session.Selection.Single);
        Assert.True(f.PressInView(Key.Tab));
        Assert.Equal(b, f.Session.Selection.Single);
        Assert.True(f.PressInView(Key.Delete));
        Assert.Single(f.Session.Plan.Marks);

        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);
        Assert.True(f.PressInView(Key.Enter, KeyModifiers.Control));
        await f.Interaction.Completion;
        Assert.Equal(2, f.Session.Plan.Marks.Count);
    }

    [AvaloniaFact]
    public async Task ArrowsStillScrollWhenNoAreaIsSelected()
    {
        using var f = await RedactFixture.OpenAsync();
        f.Session.AddArea(0, new PageRect(100, 100, 160, 140));
        f.Session.Selection.Clear();
        f.View.Focus();
        var before = f.View.CurrentPage;

        // Handled by the view's own scrolling, and the mark did not move.
        Assert.True(f.PressInView(Key.Down));
        Assert.Equal(new PageRect(100, 100, 160, 140), f.Session.Plan.Marks[0].Areas[0]);
        Assert.Equal(before, f.View.CurrentPage);
    }

    [AvaloniaFact]
    public async Task AClampedNudgeDoesNotLetTheNextOneAmendAnotherStep()
    {
        using var f = await RedactFixture.OpenAsync();
        var id = f.Session.AddArea(0, new PageRect(0, 100, 60, 140)).MarkId!.Value;

        // Left at the page edge changes nothing; it must not start a run.
        Assert.True(f.Press(Key.Left));
        f.Time.Advance(50);
        Assert.True(f.Press(Key.Right));
        Assert.Equal(new PageRect(1, 100, 61, 140), f.Session.Plan.Marks.Single(m => m.Id == id).Areas[0]);

        // One undo takes back the nudge only; the mark itself is still there.
        f.Session.Undo();
        Assert.Equal(new PageRect(0, 100, 60, 140), f.Session.Plan.Marks.Single(m => m.Id == id).Areas[0]);
    }

    [AvaloniaFact]
    public async Task EscDropsAnOpenTextSelectionBeforeLeavingTheMode()
    {
        using var f = await RedactFixture.OpenAsync();
        var ends = 0;
        f.Interaction.EndRequested += () => ends++;
        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);
        Assert.True(f.Text.Text.Selection.HasSelection);

        Assert.True(f.Press(Key.Escape));
        Assert.False(f.Text.Text.Selection.HasSelection);
        Assert.Equal(0, ends);

        Assert.True(f.Press(Key.Escape));
        Assert.Equal(1, ends);
    }

    [AvaloniaFact]
    public async Task HeldCtrlEnterMarksOnceAndTwoRunsAtOnceMarkOnce()
    {
        using var f = await RedactFixture.OpenAsync();
        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);

        // Key repeat: the same press arrives again before the key went up.
        Assert.True(f.PressInView(Key.Enter, KeyModifiers.Control));
        Assert.True(f.PressInView(Key.Enter, KeyModifiers.Control));
        await f.Interaction.Completion;
        f.ReleaseInView(Key.Enter);
        Assert.Single(f.Session.Plan.Marks);

        // Two calls at once (the command and the key): the selection is marked once, whichever way the second is refused (busy while the
        // first reads the text, or nothing selected once it is done; a cached page finishes at once, so the test cannot force the first).
        await f.SelectAsync(0, start, end - 1);
        var first = f.Session.MarkSelectionAsync();
        var second = f.Session.MarkSelectionAsync();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, r => r.IsDone);
        Assert.Contains(results, r => r.Problem is RedactProblem.Busy or RedactProblem.NothingSelected);
        Assert.Equal(2, f.Session.Plan.Marks.Count);
        Assert.Equal(2, f.Session.Plan.Marks.Count(m => m.Kind == RedactionKind.Text));
    }
}
