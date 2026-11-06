using Avalonia.Headless.XUnit;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactSessionTests
{
    private static string Characters(PageText text, int start, int end)
    {
        var chars = new System.Text.StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (!text.Chars[i].IsGenerated)
            {
                chars.Append(char.ConvertFromUtf32(text.Chars[i].CodePoint));
            }
        }

        return chars.ToString().Trim();
    }

    [AvaloniaFact]
    public async Task AreaMarkIsPendingNothingIsWritten()
    {
        using var f = await RedactFixture.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.Path, TestContext.Current.CancellationToken);
        var versionBefore = f.Session.Version;

        var result = f.Session.AddArea(0, new PageRect(50, 100, 200, 130));

        Assert.True(result.IsDone, result.Problem?.ToString());
        Assert.NotNull(result.MarkId);
        var mark = Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(RedactionKind.Area, mark.Kind);
        Assert.Equal(new PageRect(50, 100, 200, 130), Assert.Single(mark.Areas));
        Assert.True(f.Session.Version > versionBefore);
        Assert.True(f.Session.CanUndo);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Path, TestContext.Current.CancellationToken));
        Assert.False(f.Tab.IsDirty);
        Assert.True(f.Tab.AnnotationsIfCreated is null || !f.Tab.AnnotationsIfCreated.IsDirty);
    }

    [AvaloniaFact]
    public async Task OneDragIsOneUndoStepAndAnEmptyDragIsNone()
    {
        using var f = await RedactFixture.OpenAsync();

        // A drag in many moves is one mark and one step.
        var from = f.At(0, 60, 120);
        var to = f.At(0, 220, 170);
        f.Down(from);
        f.MoveTo(f.At(0, 100, 140));
        f.MoveTo(f.At(0, 160, 150));
        f.MoveTo(to);
        f.Up(to);
        ViewFixture.Pump();

        var mark = Assert.Single(f.Session.Plan.Marks);
        var area = Assert.Single(mark.Areas);
        Assert.InRange(area.Left, 59, 61);
        Assert.InRange(area.Right, 219, 221);
        f.Session.Undo();
        Assert.Empty(f.Session.Plan.Marks);
        Assert.False(f.Session.CanUndo);

        // A click and a drag that stays within the slop make nothing and add no step.
        f.Down(from);
        f.Up(from);
        f.Down(from);
        f.MoveTo(new Avalonia.Point(from.X + 2, from.Y + 1));
        f.Up(new Avalonia.Point(from.X + 2, from.Y + 1));
        ViewFixture.Pump();
        Assert.Empty(f.Session.Plan.Marks);
        Assert.False(f.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task AnAreaIsKeptOnThePage()
    {
        using var f = await RedactFixture.OpenAsync();
        var size = f.View.SizeOfPage(0)!.Value;

        Assert.True(f.Session.AddArea(0, new PageRect(-40, -40, 30, 30)).IsDone);
        var area = f.Session.Plan.Marks[0].Areas[0];
        Assert.Equal(0, area.Left);
        Assert.Equal(0, area.Top);

        Assert.Equal(RedactProblem.OutOfRange, f.Session.AddArea(0, new PageRect(size.Width + 10, 10, size.Width + 50, 40)).Problem);
        Assert.Equal(RedactProblem.OutOfRange, f.Session.AddArea(0, new PageRect(10, 10, 10, 40)).Problem);
        Assert.Single(f.Session.Plan.Marks);
    }

    [AvaloniaFact]
    public async Task TextSelectionMakesOneMarkPerPageWithOneRectPerLine()
    {
        using var f = await RedactFixture.OpenAsync("redact-control.pdf");
        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (firstStart, firstEnd) = text.LineAt(0);
        var next = firstEnd;
        while (next < text.Chars.Count && text.Chars[next].IsGenerated)
        {
            next++;
        }

        var (secondStart, secondEnd) = text.LineAt(next);
        Assert.True(secondEnd > secondStart, "the fixture needs a second line");

        await f.SelectAsync(0, firstStart, secondEnd - 1);
        var result = await f.Session.MarkSelectionAsync();

        Assert.True(result.IsDone, result.Problem?.ToString());
        var mark = Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(RedactionKind.Text, mark.Kind);
        Assert.Equal(0, mark.Page);
        Assert.Equal(2, mark.Areas.Count);
        Assert.Equal(Characters(text, firstStart, firstEnd) + " " + Characters(text, secondStart, secondEnd), mark.Term);
        Assert.False(f.Text.Text.Selection.HasSelection, "a marked selection is cleared");
        Assert.Contains(mark.Id, f.Session.Selection.Ids);
    }

    [AvaloniaFact]
    public async Task NothingSelectedMarksNothing()
    {
        using var f = await RedactFixture.OpenAsync();
        var result = await f.Session.MarkSelectionAsync();
        Assert.Equal(RedactProblem.NothingSelected, result.Problem);
        Assert.Empty(f.Session.Plan.Marks);
        Assert.False(f.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task UnmarkableSelectionMakesNoMarkAtAll()
    {
        using var f = await RedactFixture.OpenAsync("text-dense.pdf");
        var text = await f.Text.TextOfAsync(0);

        // More characters than a term may have (RedactionLimits.MaxTermLength): the hit is not markable.
        Assert.True(text.Chars.Count > RedactionLimits.MaxTermLength, "the fixture needs a long page");
        await f.SelectAsync(0, 0, text.Chars.Count - 1);
        var versionBefore = f.Session.Version;
        var result = await f.Session.MarkSelectionAsync();

        Assert.Equal(RedactProblem.TextNotMarkable, result.Problem);
        Assert.Empty(f.Session.Plan.Marks);
        Assert.Equal(versionBefore, f.Session.Version);
        Assert.False(f.Session.CanUndo);
        Assert.True(f.Text.Text.Selection.HasSelection, "a refused selection stays, so the user can shorten it");
    }

    [AvaloniaFact]
    public async Task TooManyMarksIsRefusedWithAReason()
    {
        using var f = await RedactFixture.OpenAsync();
        var full = RedactionPlan.Empty.WithRange(Enumerable.Range(0, RedactionLimits.MaxMarks)
            .Select(i => new RedactionMark(Guid.NewGuid(), RedactionKind.Area, 0, [new PageRect(1, 1, 5, 5)], null, false)));
        Assert.True(f.Session.Replace(full).IsDone);

        var result = f.Session.AddArea(0, new PageRect(10, 10, 60, 40));

        Assert.Equal(RedactProblem.TooManyMarks, result.Problem);
        Assert.Equal(RedactionLimits.MaxMarks, f.Session.Plan.Marks.Count);
        Assert.Contains(RedactionLimits.MaxMarks.ToString("N0", PdfStrings.Culture).Replace(" ", string.Empty, StringComparison.Ordinal), PdfStrings.RedactRefusal(result.Problem!.Value).Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task OnlyAreasMoveAndResize()
    {
        using var f = await RedactFixture.OpenAsync();
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);
        Assert.True((await f.Session.MarkSelectionAsync()).IsDone);
        var textMark = f.Session.Plan.Marks[0];

        Assert.Equal(RedactProblem.NotAnArea, f.Session.MoveArea(textMark.Id, 3, 3).Problem);
        Assert.Equal(RedactProblem.NotAnArea, f.Session.ResizeArea(textMark.Id, new PageRect(1, 1, 50, 50)).Problem);
        Assert.Equal(RedactProblem.UnknownMark, f.Session.MoveArea(Guid.NewGuid(), 3, 3).Problem);

        var area = f.Session.AddArea(0, new PageRect(200, 300, 260, 340)).MarkId!.Value;
        Assert.True(f.Session.MoveArea(area, 10, 5).IsDone);
        Assert.Equal(new PageRect(210, 305, 270, 345), f.Session.Plan.Marks.Single(m => m.Id == area).Areas[0]);
        Assert.True(f.Session.ResizeArea(area, new PageRect(210, 305, 280, 345)).IsDone);
        Assert.Equal(280, f.Session.Plan.Marks.Single(m => m.Id == area).Areas[0].Right);

        // Delete removes any kind; the selection follows the plan.
        Assert.True(f.Session.Delete(new HashSet<Guid> { textMark.Id, area }).IsDone);
        Assert.Empty(f.Session.Plan.Marks);
        Assert.Equal(0, f.Session.Selection.Count);
        Assert.Equal(RedactProblem.NothingSelected, f.Session.Delete(new HashSet<Guid> { area }).Problem);
    }

    [AvaloniaFact]
    public async Task EndDropsThePlanAndTheModeIsOffUntilEnteredAgain()
    {
        using var f = await RedactFixture.OpenAsync();
        f.Session.AddArea(0, new PageRect(10, 10, 60, 40));
        var ending = 0;
        f.Session.Ending += () => ending++;
        var version = f.Session.Version;

        f.Session.End();

        Assert.Equal(1, ending);
        Assert.False(f.Session.IsActive);
        Assert.True(f.Session.Plan.IsEmpty);
        Assert.False(f.Session.CanUndo);
        Assert.True(f.Session.Version > version);
        Assert.Equal(RedactProblem.NotActive, f.Session.AddArea(0, new PageRect(10, 10, 60, 40)).Problem);
        f.Session.Enter();
        Assert.True(f.Session.Plan.IsEmpty);
    }

    [AvaloniaFact]
    public async Task UndoAndRedoWalkTheHistory()
    {
        using var f = await RedactFixture.OpenAsync();
        f.Session.AddArea(0, new PageRect(10, 10, 60, 40));
        f.Session.AddArea(0, new PageRect(10, 60, 60, 90));

        f.Session.Undo();
        Assert.Single(f.Session.Plan.Marks);
        Assert.True(f.Session.CanRedo);
        f.Session.Redo();
        Assert.Equal(2, f.Session.Plan.Marks.Count);
        f.Session.Undo();
        f.Session.Undo();
        Assert.Empty(f.Session.Plan.Marks);
        Assert.False(f.Session.CanUndo);
    }
}
