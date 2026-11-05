using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

public sealed class MarksPaneTests
{
    private static readonly AnnotationColor Red = new(220, 38, 38);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private sealed class Fixture(AnnotateFixture annotate, MarksModel model, MarksPane pane, Window window) : IDisposable
    {
        public AnnotateFixture Annotate { get; } = annotate;

        public MarksModel Model { get; } = model;

        public MarksPane Pane { get; } = pane;

        public Window Window { get; } = window;

        public static async Task<Fixture> OpenAsync(string file, Func<AnnotateFixture, Task>? prepare = null, Func<bool>? selectAllowed = null, bool cards = false)
        {
            var annotate = await AnnotateFixture.OpenAsync(file);
            if (prepare is not null)
            {
                await prepare(annotate);
            }

            await ViewFixture.WaitAsync(() => annotate.View.FirstSharpPageShown);
            var model = new MarksModel(annotate.Tab, annotate.View);
            var pane = new MarksPane(model, annotate.Tab, annotate.View, annotate.Tools, annotate.Interaction.Select, cards ? new DialogQueue(annotate.Host) : null, selectAllowed ?? (() => true));
            var window = new Window { Width = 256, Height = 640, Content = pane };
            window.Show();
            var fixture = new Fixture(annotate, model, pane, window);
            await fixture.SettleAsync();
            return fixture;
        }

        public async Task SettleAsync()
        {
            for (var i = 0; i < 3; i++)
            {
                ViewFixture.Pump();
                await Model.Idle;
                await Pane.Completion;
                await Annotate.Interaction.Select.Completion;
                await Annotate.SettleAsync();
            }
        }

        public int IndexOf(string? name) => Enumerable.Range(0, Pane.RowCount).First(i => Pane.EntryAt(i)?.Annotation.Name == name);

        public void Click(Control target)
        {
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window) ?? throw new InvalidOperationException("not in the window");
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            ViewFixture.Pump();
        }

        public void Dispose()
        {
            Window.Close();
            Pane.Dispose();
            Model.Dispose();
            Annotate.Dispose();
        }
    }

    private static Task<PdfAnnotation?> AddRectangleAsync(AnnotateFixture f, int page, double left, double top) =>
        f.Tab.Annotations.Editor.ApplyDrawingAsync(page, DrawingGeometry.Rectangle(new PageRect(left, top, left + 60, top + 30), 2), Red, null, null);

    private static async Task<PdfAnnotation> CurrentAsync(Fixture f, string? name) =>
        (await f.Annotate.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).First(a => a.Name == name);

    [AvaloniaFact]
    public async Task ClickRevealsTheMarkAndSelectsIt()
    {
        PdfAnnotation? made = null;
        using var f = await Fixture.OpenAsync("pages-marked.pdf", async a => made = await AddRectangleAsync(a, 4, 80, 80));
        Assert.Equal(AnnotateTool.None, f.Annotate.Tools.Tool);
        var (_, lastBefore) = f.Annotate.View.VisiblePages;
        Assert.True(lastBefore < 4, "the page of the mark is not on screen yet");

        var row = f.IndexOf(made!.Name);
        f.Click(f.Pane.ContainerOf(row)!);
        await f.SettleAsync();

        var (first, last) = f.Annotate.View.VisiblePages;
        Assert.InRange(4, first, last);

        // The mode was off: the click entered the Select tool, and the mark is selected in the document.
        Assert.Equal(AnnotateTool.Select, f.Annotate.Tools.Tool);
        Assert.Equal(made.Name, f.Annotate.Interaction.Select.Selected?.Name);
        Assert.Equal(row, f.Pane.List.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task AClickWhereTheDocumentForbidsAnnotatingOnlyReveals()
    {
        PdfAnnotation? made = null;
        using var f = await Fixture.OpenAsync("pages-marked.pdf", async a => made = await AddRectangleAsync(a, 4, 80, 80), selectAllowed: () => false);

        f.Click(f.Pane.ContainerOf(f.IndexOf(made!.Name))!);
        await f.SettleAsync();

        Assert.Equal(AnnotateTool.None, f.Annotate.Tools.Tool);
        Assert.False(f.Annotate.Interaction.Select.HasSelection);
        var (first, last) = f.Annotate.View.VisiblePages;
        Assert.InRange(4, first, last);
    }

    [AvaloniaFact]
    public async Task ApplyingAnEmptyCommentRemovesIt()
    {
        PdfAnnotation? made = null;
        using var f = await Fixture.OpenAsync(
            "text-rich.pdf",
            async a => made = await a.Tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(90, 95, 310, 115)], Red, "first thought", null));

        f.Pane.List.SelectedIndex = f.IndexOf(made!.Name);
        ViewFixture.Pump();
        Assert.True(f.Pane.CommentEditor.IsVisible);
        Assert.Equal("first thought", f.Pane.CommentEditor.Text);

        // Ctrl+Enter applies the new text.
        f.Pane.CommentEditor.Text = "second thought";
        f.Pane.CommentEditor.Focus();
        f.Window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
        await f.SettleAsync();
        Assert.Equal("second thought", (await CurrentAsync(f, made.Name)).Contents);
        Assert.Equal("second thought", f.Pane.EntryAt(f.IndexOf(made.Name))!.Annotation.Contents);
        Assert.Equal(f.IndexOf(made.Name), f.Pane.List.SelectedIndex);

        // An empty text takes the comment away (the engine's "empty deletes the comment").
        f.Pane.CommentEditor.Text = string.Empty;
        f.Pane.CommentEditor.Focus();
        f.Window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
        await f.SettleAsync();
        Assert.True(string.IsNullOrEmpty((await CurrentAsync(f, made.Name)).Contents));
        Assert.True(string.IsNullOrEmpty(f.Pane.EntryAt(f.IndexOf(made.Name))!.Annotation.Contents));
        Assert.True(f.Annotate.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task EscapeDiscardsTheEdit()
    {
        PdfAnnotation? made = null;
        using var f = await Fixture.OpenAsync(
            "text-rich.pdf",
            async a => made = await a.Tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(90, 95, 310, 115)], Red, "kept", null));

        f.Pane.List.SelectedIndex = f.IndexOf(made!.Name);
        ViewFixture.Pump();
        f.Pane.CommentEditor.Text = "typed but not applied";
        f.Pane.CommentEditor.Focus();
        f.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await f.SettleAsync();

        Assert.Equal("kept", f.Pane.CommentEditor.Text);
        Assert.Equal("kept", (await CurrentAsync(f, made.Name)).Contents);
    }

    // Two marks of the foreign file that can be commented; the tab is clean (nothing was changed yet).
    private static async Task<(Fixture F, MarkEntry A, MarkEntry B)> OpenWithTwoCommentableMarksAsync(bool cards = false)
    {
        var f = await Fixture.OpenAsync("annotations-foreign.pdf", cards: cards);
        var entries = Enumerable.Range(0, f.Pane.RowCount).Select(f.Pane.EntryAt).Where(e => e is { CanComment: true, Annotation.Name: not null }).Select(e => e!).ToList();
        Assert.True(entries.Count >= 2);
        Assert.False(f.Annotate.Tab.IsDirty);
        return (f, entries[0], entries[1]);
    }

    private static async Task<string> ContentsAsync(Fixture f, MarkEntry entry) =>
        (await f.Annotate.Tab.Session.Document!.GetAnnotationsAsync(entry.Page, Cancel)).First(a => a.Name == entry.Annotation.Name).Contents ?? string.Empty;

    private static void Select(Fixture f, MarkEntry entry)
    {
        f.Pane.List.SelectedIndex = f.IndexOf(entry.Annotation.Name);
        ViewFixture.Pump();
    }

    private static void Type(Fixture f, string text)
    {
        f.Pane.CommentEditor.Text = text;
        f.Pane.CommentEditor.Focus();
        ViewFixture.Pump();
    }

    private static void CtrlEnter(Fixture f) => f.Window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);

    [AvaloniaFact]
    public async Task CommentTypedButNotAppliedKeepsTheTabDirty()
    {
        var (f, a, _) = await OpenWithTwoCommentableMarksAsync();
        using var _ = f;
        var original = a.Comment ?? string.Empty;
        Select(f, a);
        Assert.False(f.Annotate.Tab.IsDirty);

        Type(f, "typed, not applied");
        Assert.True(f.Pane.HasUnappliedComment);
        Assert.True(f.Annotate.Tab.IsDirty);

        // Typing the old text back, and Esc, both leave the tab as it was.
        Type(f, original);
        Assert.False(f.Annotate.Tab.IsDirty);
        Type(f, "typed again");
        Assert.True(f.Annotate.Tab.IsDirty);
        f.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await f.SettleAsync();
        Assert.Equal(original, f.Pane.CommentEditor.Text);
        Assert.False(f.Annotate.Tab.IsDirty);
        Assert.Equal(original, await ContentsAsync(f, a));
    }

    [AvaloniaFact]
    public async Task SaveAppliesAPendingComment()
    {
        var (f, a, _) = await OpenWithTwoCommentableMarksAsync();
        using var _ = f;
        Select(f, a);
        Type(f, "typed before save");

        // What Save and the close guard do first: the flush ends the open edits, and the comment is applied.
        await f.Annotate.Tab.Annotations.FlushAsync();
        await f.SettleAsync();

        Assert.Equal("typed before save", await ContentsAsync(f, a));
        Assert.False(f.Pane.HasUnappliedComment);
        Assert.True(f.Annotate.Tab.Annotations.Editor.IsDirty);
        Assert.True(f.Annotate.Tab.IsDirty);
        Assert.Equal("typed before save", f.Pane.CommentEditor.Text);
    }

    [AvaloniaFact]
    public async Task SelectionChangeDoesNotDropATypedComment()
    {
        var (f, a, b) = await OpenWithTwoCommentableMarksAsync();
        using var _ = f;
        var other = b.Comment ?? string.Empty;
        Select(f, a);
        Type(f, "typed for the first");

        Select(f, b);
        await f.SettleAsync();

        Assert.Equal("typed for the first", await ContentsAsync(f, a));
        Assert.Equal(other, await ContentsAsync(f, b));
        Assert.Equal(other, f.Pane.CommentEditor.Text);
        Assert.False(f.Pane.HasUnappliedComment);
    }

    [AvaloniaFact]
    public async Task CloseWithATypedCommentAsks()
    {
        var (f, a, _) = await OpenWithTwoCommentableMarksAsync();
        using var _ = f;
        Select(f, a);
        Type(f, "typed before close");

        var prompts = 0;
        var guard = new CloseGuard(_ => { prompts++; return Task.FromResult(CloseChoice.Cancel); }, _ => Task.FromResult(true));
        Assert.False(await guard.CanCloseAsync(f.Annotate.Tab));
        Assert.Equal(1, prompts);
    }

    [AvaloniaFact]
    public async Task AFailedApplyKeepsTheTypedTextAndTheTabDirty()
    {
        var (f, a, _) = await OpenWithTwoCommentableMarksAsync(cards: true);
        using var _ = f;
        var original = a.Comment ?? string.Empty;
        Select(f, a);
        f.Pane.UpdateComment = (_, _) => throw new InvalidOperationException("injected");
        Type(f, "typed, refused");

        CtrlEnter(f);
        await f.Pane.Completion;
        await ViewFixture.WaitAsync(() => f.Annotate.Host.ActiveDialog is not null);

        // The card says the mark is as it was; the field still holds the text and the tab still counts as changed.
        Assert.NotNull(f.Annotate.Host.ActiveDialog);
        Assert.Equal("typed, refused", f.Pane.CommentEditor.Text);
        Assert.True(f.Pane.HasUnappliedComment);
        Assert.True(f.Annotate.Tab.IsDirty);
        Assert.Equal(original, await ContentsAsync(f, a));

        // Once the engine works again the same text can be applied.
        f.Pane.UpdateComment = null;
        Raffica.Pdf.Tests.Printing.PrintHost.Find<Button>(f.Annotate.Host.ActiveDialog!, "PART_Ok").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        f.Pane.CommentEditor.Focus();
        CtrlEnter(f);
        await f.SettleAsync();
        Assert.Equal("typed, refused", await ContentsAsync(f, a));
        Assert.False(f.Pane.HasUnappliedComment);
    }

    [AvaloniaFact]
    public async Task AMarkThatIsGoneTellsInsteadOfDroppingTheTextSilently()
    {
        var (f, a, _) = await OpenWithTwoCommentableMarksAsync(cards: true);
        using var _ = f;
        Select(f, a);
        Type(f, "typed for a mark that goes");

        // The mark is deleted behind the pane's back. Whether the list has read the page by now depends on timing: if it has, its row went and
        // the text is applied because of that, and if not, the apply below does it. Either way the card must come up. The apply is called
        // as Ctrl+Enter does and not with a key press: a key press runs the pending work of the window first, so on a slow machine the card
        // is already up when the key arrives, and Enter then presses the button of the card and closes it (the key itself is covered by
        // AFailedApplyKeepsTheTypedTextAndTheTabDirty).
        await f.Annotate.Tab.Annotations.Editor.DeleteAsync(a.Locator);
        await f.Pane.ApplyCommentAsync();
        await f.SettleAsync();
        await ViewFixture.WaitAsync(() => f.Annotate.Host.ActiveDialog is not null);

        Assert.NotNull(f.Annotate.Host.ActiveDialog);
        Assert.False(f.Pane.HasUnappliedComment);
        Assert.DoesNotContain(Enumerable.Range(0, f.Pane.RowCount), i => f.Pane.EntryAt(i)?.Annotation.Name == a.Annotation.Name);
    }

    [AvaloniaFact]
    public async Task ASecondCtrlEnterBeforeTheListReadsAgainDoesNotRepeatTheUpdate()
    {
        var (f, a, _) = await OpenWithTwoCommentableMarksAsync();
        using var _ = f;
        Select(f, a);
        var calls = 0;
        var gate = new TaskCompletionSource();
        f.Pane.UpdateComment = async (locator, text) =>
        {
            calls++;
            await gate.Task;
            await f.Annotate.Tab.Annotations.Editor.UpdateAsync(locator, new AnnotationEdit(Contents: text));
        };
        Type(f, "applied once");

        // The change is on its way (held at the gate): the second Ctrl+Enter finds the text already counted as applied.
        CtrlEnter(f);
        CtrlEnter(f);
        ViewFixture.Pump();
        Assert.Equal(1, calls);
        gate.SetResult();
        await f.SettleAsync();

        // The list has read the mark again, the field shows the new comment, and one more Ctrl+Enter changes nothing.
        Assert.Equal("applied once", await ContentsAsync(f, a));
        Assert.Equal("applied once", f.Pane.EntryAt(f.IndexOf(a.Annotation.Name))!.Comment);
        Assert.Equal("applied once", f.Pane.CommentEditor.Text);
        CtrlEnter(f);
        await f.SettleAsync();
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public async Task ShowingThePaneAgainAndAgainKeepsOneWindowSubscription()
    {
        using var f = await Fixture.OpenAsync("text-basic.pdf");
        Assert.Equal(1, f.Pane.DeactivationHooks);

        for (var i = 0; i < 5; i++)
        {
            f.Window.Content = null;
            ViewFixture.Pump();
            Assert.Equal(0, f.Pane.DeactivationHooks);
            f.Window.Content = f.Pane;
            ViewFixture.Pump();
            Assert.Equal(1, f.Pane.DeactivationHooks);
        }

        // A pane that is disposed while shown gives the subscription back too.
        f.Pane.Dispose();
        Assert.Equal(0, f.Pane.DeactivationHooks);
    }

    [AvaloniaFact]
    public async Task LockedEntryShowsTheReasonAndNoEditor()
    {
        using var f = await Fixture.OpenAsync("annotations-foreign.pdf");

        // hl-0001 has a reply, so Core lists it as not editable.
        var row = f.IndexOf("hl-0001");
        f.Pane.List.SelectedIndex = row;
        ViewFixture.Pump();
        var entry = f.Pane.EntryAt(row)!;
        Assert.False(entry.Annotation.Editable);
        Assert.False(f.Pane.CommentEditor.IsVisible);
        Assert.True(f.Pane.Note.IsVisible);
        Assert.Equal(entry.LockReason, f.Pane.Note.Text);
        Assert.Equal(PdfStrings.MarkRepliesReason, entry.LockReason);

        // A mark that can be commented gets the editor and no note.
        var free = Enumerable.Range(0, f.Pane.RowCount).First(i => f.Pane.EntryAt(i)?.Annotation.Kind == AnnotationKind.Underline);
        f.Pane.List.SelectedIndex = free;
        ViewFixture.Pump();
        Assert.True(f.Pane.CommentEditor.IsVisible);
        Assert.False(f.Pane.Note.IsVisible);
    }

    [AvaloniaFact]
    public async Task FilterAndSummaryFollowTheChoice()
    {
        using var f = await Fixture.OpenAsync("annotations-foreign.pdf");
        Assert.Equal("7 marks on 2 pages", f.Pane.Summary.Text);

        f.Pane.Filter.SelectedIndex = (int)MarkFilter.Underline;
        ViewFixture.Pump();
        Assert.Equal("1 mark on page 2", f.Pane.Summary.Text);
        Assert.Equal(1, f.Pane.RowCount);
    }

    [AvaloniaFact]
    public async Task AFileWithoutMarksSaysSo()
    {
        using var f = await Fixture.OpenAsync("text-basic.pdf");

        Assert.True(f.Pane.EmptyText.IsVisible);
        Assert.Equal("This document has no marks.", f.Pane.EmptyText.Text);
        Assert.False(f.Pane.CommentEditor.IsVisible);
    }

    [AvaloniaFact]
    public async Task TheHelpTextSaysADeletedMarkStaysInEarlierRevisions()
    {
        using var f = await Fixture.OpenAsync("text-basic.pdf");

        Assert.Contains("earlier revisions", f.Pane.Help.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task EnterOnAGroupRowExpandsIt()
    {
        using var f = await Fixture.OpenAsync(
            "text-rich.pdf",
            async a =>
            {
                for (var i = 0; i < 5; i++)
                {
                    await a.Tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Ink([[new DisplayPoint(20 + (30 * i), 100), new DisplayPoint(40 + (30 * i), 105)]], 2), Red, null, null);
                }
            });

        Assert.Equal(1, f.Pane.RowCount);
        Assert.Equal("Freehand, 5 strokes", f.Pane.GroupTextAt(0));
        f.Pane.List.SelectedIndex = 0;
        f.Pane.ContainerOf(0)!.Focus(NavigationMethod.Tab);
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        ViewFixture.Pump();
        Assert.Equal(6, f.Pane.RowCount);
    }

    [AvaloniaFact]
    public async Task DeleteInsideTheModeRemovesTheMark()
    {
        PdfAnnotation? made = null;
        using var f = await Fixture.OpenAsync("pages-marked.pdf", async a => made = await AddRectangleAsync(a, 0, 80, 80));
        f.Annotate.Tools.Choose(AnnotateTool.Select);
        var row = f.IndexOf(made!.Name);
        f.Click(f.Pane.ContainerOf(row)!);
        await f.SettleAsync();
        Assert.Equal(made.Name, f.Annotate.Interaction.Select.Selected?.Name);

        f.Pane.ContainerOf(f.Pane.List.SelectedIndex)!.Focus(NavigationMethod.Tab);
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await f.SettleAsync();

        Assert.Equal(0, f.Pane.RowCount);
        Assert.Empty(await f.Annotate.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel));
    }

    [AvaloniaFact]
    public async Task DeleteIgnoresKeyRepeatInTheList()
    {
        PdfAnnotation? first = null;
        PdfAnnotation? second = null;
        using var f = await Fixture.OpenAsync("pages-marked.pdf", async a =>
        {
            first = await AddRectangleAsync(a, 0, 80, 80);
            second = await AddRectangleAsync(a, 0, 80, 200);
        });
        f.Annotate.Tools.Choose(AnnotateTool.Select);
        Assert.Equal(2, f.Pane.RowCount);
        var row = f.IndexOf(first!.Name);
        f.Click(f.Pane.ContainerOf(row)!);
        await f.SettleAsync();
        f.Pane.ContainerOf(f.Pane.List.SelectedIndex)!.Focus(NavigationMethod.Tab);

        // One physical press: the key goes down and the system repeats it while the delete is on its way; one mark goes.
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await f.SettleAsync();
        Assert.Equal(1, f.Pane.RowCount);
        var left = Assert.Single(await f.Annotate.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel));
        Assert.Equal(second!.Name, left.Name);

        // Released and pressed again, the next press deletes the next mark.
        f.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        f.Pane.ContainerOf(0)!.Focus(NavigationMethod.Tab);
        f.Pane.List.SelectedIndex = 0;
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await f.SettleAsync();
        Assert.Equal(0, f.Pane.RowCount);
    }

    [AvaloniaTheory]
    [InlineData("click")]
    [InlineData("focusLeft")]
    public async Task ADeleteAfterADeleteWorksWhenNoKeyUpReachedTheList(string how)
    {
        PdfAnnotation? first = null;
        PdfAnnotation? second = null;
        using var f = await Fixture.OpenAsync("pages-marked.pdf", async a =>
        {
            first = await AddRectangleAsync(a, 0, 80, 80);
            second = await AddRectangleAsync(a, 0, 80, 200);
        });
        f.Annotate.Tools.Choose(AnnotateTool.Select);
        f.Click(f.Pane.ContainerOf(f.IndexOf(first!.Name))!);
        await f.SettleAsync();
        f.Pane.ContainerOf(f.Pane.List.SelectedIndex)!.Focus(NavigationMethod.Tab);

        // The key goes down on the row and the row goes away with the delete: the release never reaches the list.
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await f.SettleAsync();
        Assert.Equal(1, f.Pane.RowCount);

        if (how == "click")
        {
            f.Click(f.Pane.ContainerOf(f.IndexOf(second!.Name))!);
        }
        else
        {
            f.Window.Focus();
            ViewFixture.Pump();
            f.Pane.List.SelectedIndex = f.IndexOf(second!.Name);
        }

        await f.SettleAsync();
        f.Pane.ContainerOf(f.Pane.List.SelectedIndex)!.Focus(NavigationMethod.Tab);
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await f.SettleAsync();

        Assert.Equal(0, f.Pane.RowCount);
    }

    [AvaloniaFact]
    public async Task DeleteOutsideTheModeDoesNothing()
    {
        PdfAnnotation? made = null;
        using var f = await Fixture.OpenAsync("pages-marked.pdf", async a => made = await AddRectangleAsync(a, 0, 80, 80));
        var row = f.IndexOf(made!.Name);
        f.Pane.List.SelectedIndex = row;
        f.Pane.ContainerOf(row)!.Focus(NavigationMethod.Tab);
        f.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await f.SettleAsync();

        Assert.Equal(1, f.Pane.RowCount);
        Assert.Single(await f.Annotate.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel));
    }
}
