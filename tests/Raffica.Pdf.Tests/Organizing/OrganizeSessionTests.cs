using Avalonia.Headless.XUnit;
using Raffica.Core.Pages;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>A temp copy of a test file opened as a tab (pages-marked.pdf has six pages).</summary>
internal sealed class OrganizeFile : IDisposable
{
    private readonly TempCopy _copy;

    private OrganizeFile(TempCopy copy, ViewFixture fixture)
    {
        _copy = copy;
        Fixture = fixture;
    }

    public ViewFixture Fixture { get; }

    public PdfTab Tab => Fixture.Tab;

    public string Path => _copy.Path;

    public string Folder => _copy.Folder;

    public static async Task<OrganizeFile> OpenAsync(string file = "pages-marked.pdf")
    {
        var copy = new TempCopy(file);
        return new OrganizeFile(copy, await ViewFixture.OpenAsync(copy.Path, show: false));
    }

    /// <summary>Another copy of a test file in the folder of the open one, to insert.</summary>
    public string CopyOf(string file, string? name = null)
    {
        var target = System.IO.Path.Combine(Folder, name ?? file);
        File.Copy(TestData.Pdf(file), target, overwrite: true);
        return target;
    }

    public void Dispose()
    {
        Fixture.Dispose();
        _copy.Dispose();
    }
}

public sealed class OrganizeSessionTests
{
    private const string Password = "hôtel";

    private static OrganizeSession Enter(OrganizeFile file)
    {
        var session = file.Tab.Organize;
        session.Enter();
        return session;
    }

    [AvaloniaFact]
    public async Task NoSessionIsMadeUntilTheFirstUse()
    {
        using var file = await OrganizeFile.OpenAsync();
        Assert.Null(file.Tab.OrganizeIfCreated);

        ViewFixture.Pump();
        file.Fixture.View.GoToPage(2);
        ViewFixture.Pump();

        Assert.Null(file.Tab.OrganizeIfCreated);
        Assert.NotNull(file.Tab.Organize);
        Assert.False(file.Tab.Organize.IsActive);
    }

    [AvaloniaFact]
    public async Task EnterStartsAtTheIdentityAndTheTabStaysCleanUntilAChange()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);

        Assert.True(session.IsActive);
        Assert.True(session.Plan.IsIdentity);
        Assert.Equal(6, session.Plan.Slots.Count);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.False(file.Tab.IsDirty);
        Assert.Empty(session.Selection.Positions);

        session.Selection.Select(0);
        Assert.True(session.Rotate(90).IsDone);
        Assert.True(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task RotateDeleteMoveDuplicateAreOneHistoryStepEach()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var versions = new List<int> { session.Version };

        session.Selection.Select(0);
        Assert.True(session.Rotate(90).IsDone);
        versions.Add(session.Version);
        Assert.Equal(90, session.Plan.Slots[0].ExtraRotation);

        session.Selection.Select(1);
        Assert.True(session.Duplicate().IsDone);
        versions.Add(session.Version);
        Assert.Equal(7, session.Plan.Slots.Count);

        session.Selection.Select(0);
        Assert.True(session.Move(4).IsDone);
        versions.Add(session.Version);
        Assert.Equal(0, session.Plan.Slots[3].SourceIndex);

        session.Selection.Select(0);
        Assert.True(session.Delete().IsDone);
        versions.Add(session.Version);
        Assert.Equal(6, session.Plan.Slots.Count);

        Assert.Equal(versions.Count, versions.Distinct().Count());
        for (var i = 0; i < 4; i++)
        {
            session.Undo();
        }

        Assert.True(session.Plan.IsIdentity);
        Assert.False(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task UndoRedoRestoreThePlanAndTheSelection()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        session.Selection.Set(6, [1, 2]);
        session.Rotate(90);
        session.Selection.Select(5);
        session.Delete();
        var afterDelete = session.Plan;
        Assert.Empty(session.Selection.Positions);

        session.Undo();
        Assert.Equal(6, session.Plan.Slots.Count);
        Assert.Equal([5], session.Selection.Positions);
        Assert.True(session.CanRedo);

        session.Undo();
        Assert.True(session.Plan.IsIdentity);
        Assert.Equal([1, 2], session.Selection.Positions);

        session.Redo();
        session.Redo();
        Assert.Same(afterDelete, session.Plan);
        Assert.Empty(session.Selection.Positions);
    }

    [AvaloniaFact]
    public async Task RefusedOperationLeavesPlanAndHistoryAlone()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);

        // The last page cannot be deleted.
        session.Selection.SelectAll();
        var plan = session.Plan;
        var version = session.Version;
        var all = session.Delete();
        Assert.Equal(OrganizeProblem.LastPage, all.Problem);
        Assert.Same(plan, session.Plan);
        Assert.Equal(version, session.Version);
        Assert.False(session.CanUndo);

        // Out of range destination, bad rotation.
        Assert.Equal(OrganizeProblem.OutOfRange, session.Move(99).Problem);
        Assert.Equal(OrganizeProblem.InvalidRotation, session.Rotate(45).Problem);
        Assert.Equal(OrganizeProblem.OutOfRange, session.InsertBlank(99, new BlankPage(100, 100)).Problem);
        Assert.Equal(OrganizeProblem.OutOfRange, session.InsertBlank(0, new BlankPage(0, 100)).Problem);
        Assert.Same(plan, session.Plan);
        Assert.Equal(version, session.Version);
        Assert.False(file.Tab.IsDirty);

        // Doubling the plan reaches a size where the next doubling would pass MaxSlots.
        while (session.Plan.Slots.Count * 2 <= PageEditLimits.MaxSlots)
        {
            session.Selection.SelectAll();
            Assert.True(session.Duplicate().IsDone);
        }

        var before = session.Plan;
        var steps = session.Version;
        session.Selection.SelectAll();
        Assert.Equal(OrganizeProblem.LimitExceeded, session.Duplicate().Problem);
        Assert.Same(before, session.Plan);
        Assert.Equal(steps, session.Version);
    }

    [AvaloniaFact]
    public async Task SelectionFollowsMoveInsertAndDelete()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);

        // Moved pages stay selected at their new place.
        session.Selection.Set(6, [0, 1]);
        session.Move(5);
        Assert.Equal([3, 4], session.Selection.Positions);
        Assert.Equal(0, session.Plan.Slots[3].SourceIndex);
        Assert.Equal(1, session.Plan.Slots[4].SourceIndex);

        session.MoveBy(-1);
        Assert.Equal([2, 3], session.Selection.Positions);
        session.MoveBy(1);
        Assert.Equal([3, 4], session.Selection.Positions);

        // The new pages are selected after an insert.
        Assert.True(session.InsertBlank(2, new BlankPage(200, 300)).IsDone);
        Assert.Equal([2], session.Selection.Positions);
        Assert.NotNull(session.Plan.Slots[2].Blank);

        // The copies are selected after a duplicate.
        session.Selection.Set(7, [0, 3]);
        session.Duplicate();
        Assert.Equal([1, 5], session.Selection.Positions);

        // Nothing is selected after a delete.
        Assert.True(session.Delete().IsDone);
        Assert.Empty(session.Selection.Positions);
        Assert.Equal(7, session.Plan.Slots.Count);
    }

    [AvaloniaFact]
    public async Task MoveByAtTheEndOfTheListChangesNothing()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        session.Selection.Select(0);
        Assert.True(session.MoveBy(-1).IsDone);
        session.Selection.Select(5);
        Assert.True(session.MoveBy(1).IsDone);

        Assert.False(session.CanUndo);
        Assert.False(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task NonIdentityPlanMarksTheTabDirtyAndEndMarksItClean()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var changes = 0;
        file.Tab.DirtyChanged += () => changes++;

        session.Selection.Select(2);
        session.Delete();
        Assert.True(file.Tab.IsDirty);
        Assert.True(file.Tab.Tab.IsDirty);

        session.End();
        Assert.False(session.IsActive);
        Assert.False(file.Tab.IsDirty);
        Assert.Equal(2, changes);
        Assert.Empty(session.Inserted);
    }

    [AvaloniaFact]
    public async Task EndKeepsTheTabDirtyWhileTheMarksAreUnsaved()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        using var draft = file.Tab.Annotations.BeginPending();
        session.Selection.Select(0);
        session.Rotate(90);

        session.End();

        Assert.True(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task RoundTripBackToIdentityIsCleanAgain()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        session.Selection.Select(1);

        session.Rotate(90);
        Assert.True(file.Tab.IsDirty);
        session.Rotate(-90);

        Assert.True(session.Plan.IsIdentity);
        Assert.False(file.Tab.IsDirty);
        Assert.True(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task InsertFileRegistersASourceAndTheRangeSelectsPages()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var other = file.CopyOf("pages-marked.pdf", "other.pdf");

        var result = await session.InsertFileAsync(other, null, "2-3", 2);

        Assert.True(result.IsDone, result.Problem + " " + result.Detail);
        Assert.Equal(8, session.Plan.Slots.Count);
        Assert.Equal([2, 3], session.Selection.Positions);
        var id = new PageSourceId(1);
        Assert.Equal(id, session.Plan.Slots[2].Source);
        Assert.Equal(1, session.Plan.Slots[2].SourceIndex);
        Assert.Equal(2, session.Plan.Slots[3].SourceIndex);
        var inserted = Assert.Single(session.Inserted);
        Assert.Equal(id, inserted.Key);
        Assert.Equal("other.pdf", inserted.Value.Name);
        Assert.Equal(6, inserted.Value.PageCount);
        Assert.Equal(Path.GetFullPath(other), inserted.Value.File.Path);
        Assert.NotNull(inserted.Value.File.Stamp);
        Assert.True(file.Tab.IsDirty);
        Assert.Equal(1, session.Summary.InsertedFiles);
    }

    [AvaloniaFact]
    public async Task InsertOfTheOpenFileItselfIsRefused()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var version = session.Version;

        var same = await session.InsertFileAsync(file.Path, null, "all", 0);
        var viaDots = await session.InsertFileAsync(Path.Combine(file.Folder, "x", "..", Path.GetFileName(file.Path)), null, "all", 0);
        var otherCase = await session.InsertFileAsync(file.Path.ToUpperInvariant(), null, "all", 0);

        Assert.Equal(OrganizeProblem.SameFile, same.Problem);
        Assert.Equal(OrganizeProblem.SameFile, viaDots.Problem);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(OrganizeProblem.SameFile, otherCase.Problem);
        }

        Assert.Equal(version, session.Version);
        Assert.Empty(session.Inserted);
    }

    [AvaloniaFact]
    public async Task TwentyFirstFileIsRefused()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var other = file.CopyOf("hello_world.pdf", "one.pdf");

        for (var i = 0; i < PageEditLimits.MaxInsertedFiles; i++)
        {
            var ok = await session.InsertFileAsync(other, null, "1", session.Plan.Slots.Count);
            Assert.True(ok.IsDone, i + ": " + ok.Problem);
        }

        var version = session.Version;
        var refused = await session.InsertFileAsync(other, null, "1", 0);

        Assert.Equal(OrganizeProblem.TooManyFiles, refused.Problem);
        Assert.Equal(version, session.Version);
        Assert.Equal(PageEditLimits.MaxInsertedFiles, session.Inserted.Count);
    }

    [AvaloniaFact]
    public async Task EncryptedFileNeedsAPasswordAndAWrongOneIsRefused()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var encrypted = file.CopyOf("encrypted_hello_world_r3.pdf");

        var none = await session.InsertFileAsync(encrypted, null, "all", 0);
        var wrong = await session.InsertFileAsync(encrypted, "nope", "all", 0);
        Assert.Equal(OrganizeProblem.NeedsPassword, none.Problem);
        Assert.Equal(OrganizeProblem.WrongPassword, wrong.Problem);
        Assert.Empty(session.Inserted);
        Assert.True(session.Plan.IsIdentity);

        var good = await session.InsertFileAsync(encrypted, Password, "all", 6);
        Assert.True(good.IsDone, good.Problem + " " + good.Detail);
        Assert.Equal(Password, Assert.Single(session.Inserted).Value.File.Password);
    }

    [AvaloniaFact]
    public async Task NotAPdfIsUnreadable()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);

        var notPdf = await session.InsertFileAsync(file.CopyOf("not-a-pdf.pdf"), null, "all", 0);
        var missing = await session.InsertFileAsync(Path.Combine(file.Folder, "missing.pdf"), null, "all", 0);

        Assert.Equal(OrganizeProblem.Unreadable, notPdf.Problem);
        Assert.Equal(OrganizeProblem.Unreadable, missing.Problem);
        Assert.Empty(session.Inserted);
        Assert.True(session.Plan.IsIdentity);
        Assert.False(file.Tab.IsDirty);
    }

    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("5-2")]
    [InlineData("999999999")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData("abc")]
    public async Task BadRangesAreRefusedWithTheirReason(string range)
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var other = file.CopyOf("pages-marked.pdf", "other.pdf");

        var result = await session.InsertFileAsync(other, null, range, 0);

        Assert.Equal(OrganizeProblem.BadRange, result.Problem);
        Assert.False(string.IsNullOrEmpty(result.Detail));
        Assert.Empty(session.Inserted);
        Assert.True(session.Plan.IsIdentity);
    }

    [AvaloniaFact]
    public async Task InsertAtAnOutOfRangeGapIsRefusedAndRegistersNothing()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);

        var result = await session.InsertFileAsync(file.CopyOf("hello_world.pdf"), null, "all", 99);

        Assert.Equal(OrganizeProblem.OutOfRange, result.Problem);
        Assert.Empty(session.Inserted);
    }

    [AvaloniaFact]
    public async Task EndDisposesTheInsertedDocuments()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        Assert.True((await session.InsertFileAsync(file.CopyOf("hello_world.pdf"), null, "all", 0)).IsDone);
        var document = Assert.Single(session.Inserted).Value.Document;
        Assert.Equal(1, document.PageCount);

        session.End();

        Assert.Empty(session.Inserted);
        Assert.Throws<ObjectDisposedException>(() => document.PageCount);
    }

    [AvaloniaFact]
    public async Task TheRewriteResetDisposesTheSessionAndItsInsertedDocuments()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        Assert.True((await session.InsertFileAsync(file.CopyOf("hello_world.pdf"), null, "all", 0)).IsDone);
        var document = Assert.Single(session.Inserted).Value.Document;

        file.Tab.ResetForRewrittenDocument();

        Assert.Null(file.Tab.OrganizeIfCreated);
        Assert.Throws<ObjectDisposedException>(() => document.PageCount);
        Assert.False(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task TheTabDisposesTheInsertedDocuments()
    {
        var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        Assert.True((await session.InsertFileAsync(file.CopyOf("hello_world.pdf"), null, "all", 0)).IsDone);
        var document = Assert.Single(session.Inserted).Value.Document;

        file.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.PageCount);
    }

    [AvaloniaFact]
    public async Task OperationsNeedEnter()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = file.Tab.Organize;

        Assert.Equal(OrganizeProblem.NotActive, session.Rotate(90).Problem);
        Assert.Equal(OrganizeProblem.NotActive, (await session.InsertFileAsync(file.CopyOf("hello_world.pdf"), null, "all", 0)).Problem);
        Assert.Throws<InvalidOperationException>(() => session.Plan);
    }

    [AvaloniaFact]
    public async Task SummaryCountsTurnedInsertedBlankDuplicated()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        Assert.Equal(new PlanSummary(6, 6, 0, 0, 0, 0), session.Summary);

        session.Selection.Set(6, [0, 1]);
        session.Rotate(90);
        session.Selection.Select(2);
        session.Duplicate();
        session.InsertBlank(0, new BlankPage(100, 100));
        Assert.True((await session.InsertFileAsync(file.CopyOf("hello_world.pdf"), null, "all", 0)).IsDone);

        Assert.Equal(new PlanSummary(6, 9, 2, 1, 1, 1), session.Summary);
    }

    [AvaloniaFact]
    public async Task ChangedIsRaisedForEveryStepOnce()
    {
        using var file = await OrganizeFile.OpenAsync();
        var session = Enter(file);
        var raised = 0;
        session.Changed += () => raised++;

        session.Selection.Select(0);
        Assert.Equal(1, raised);
        session.Rotate(90);
        Assert.Equal(2, raised);
        session.Undo();
        Assert.Equal(3, raised);
        session.Redo();
        Assert.Equal(4, raised);
    }
}
