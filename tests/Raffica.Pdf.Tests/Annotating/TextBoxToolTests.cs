using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class TextBoxToolTests
{
    private static async Task<IReadOnlyList<PdfAnnotation>> BoxesAsync(AnnotateFixture f, int page = 0) =>
        (await f.Tab.Session.Document!.GetAnnotationsAsync(page, TestContext.Current.CancellationToken)).Where(a => a.Kind == AnnotationKind.TextBox).ToList();

    private static Point At(AnnotateFixture f, double x, double y) => f.View.ToView(0, x, y)!.Value;

    // Opens a draft with a click (no drag) at a point of page 0 and returns the editor field.
    private static Avalonia.Controls.TextBox Open(AnnotateFixture f, double x = 60, double y = 60)
    {
        if (f.Tools.Tool != AnnotateTool.TextBox)
        {
            f.Tools.Choose(AnnotateTool.TextBox);
        }

        var p = At(f, x, y);
        f.Window.MouseDown(p, MouseButton.Left);
        f.Window.MouseUp(p, MouseButton.Left);
        ViewFixture.Pump();
        Assert.True(f.Interaction.TextBox.IsEditing);
        return f.Interaction.TextBox.Editor!.Field;
    }

    // TextChanged arrives from the dispatcher, so a text set by a test is pumped before the next step.
    private static void SetText(Avalonia.Controls.TextBox field, string text)
    {
        field.Text = text;
        ViewFixture.Pump();
    }

    private static void Press(Avalonia.Controls.TextBox field, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        ViewFixture.Pump();
    }

    private static async Task FinishAsync(AnnotateFixture f)
    {
        await f.Interaction.TextBox.Completion;
        await f.SettleAsync();
    }

    [AvaloniaFact]
    public async Task DragBelow16PointsMakesTheDefaultBox()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.TextBox);

        // A short drag (10 x 8 points) is a click: the default 160 x 48 box at the press point.
        f.Window.MouseDown(At(f, 100, 100), MouseButton.Left);
        f.Window.MouseMove(At(f, 110, 108));
        f.Window.MouseUp(At(f, 110, 108), MouseButton.Left);
        ViewFixture.Pump();
        var box = f.Interaction.TextBox.DraftBox!.Value;
        Assert.Equal(100, box.Left, 0.5);
        Assert.Equal(100, box.Top, 0.5);
        Assert.Equal(160, box.Width, 0.5);

        // The headless font may be taller than one line of Geist, in which case the empty field already grew; it never starts smaller.
        Assert.True(box.Height >= 48 - 0.5, box.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Press(f.Interaction.TextBox.Editor!.Field, Key.Escape);

        // Near the right edge the box is shifted onto the page.
        var size = f.View.SizeOfPage(0)!.Value;
        var p = At(f, size.Width - 10, 20);
        f.Window.MouseDown(p, MouseButton.Left);
        f.Window.MouseUp(p, MouseButton.Left);
        ViewFixture.Pump();
        box = f.Interaction.TextBox.DraftBox!.Value;
        Assert.Equal(size.Width, box.Right, 0.5);
        Assert.Equal(160, box.Width, 0.5);
        Press(f.Interaction.TextBox.Editor!.Field, Key.Escape);

        // A real drag (100 x 50 points) is the box.
        f.Drag(At(f, 50, 50), At(f, 150, 100));
        box = f.Interaction.TextBox.DraftBox!.Value;
        Assert.Equal(50, box.Left, 0.5);
        Assert.Equal(100, box.Width, 0.5);
        Assert.True(box.Height >= 50 - 0.5);
        Press(f.Interaction.TextBox.Editor!.Field, Key.Escape);
        await FinishAsync(f);
        Assert.Empty(await BoxesAsync(f));
    }

    [AvaloniaFact]
    public async Task CtrlEnterCommitsAndEscDiscards()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "Hello");
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);

        var made = Assert.Single(await BoxesAsync(f));
        Assert.Equal("Hello", made.Contents);
        Assert.Equal(16, made.TextBox!.FontSize);
        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Equal(AnnotateTool.TextBox, f.Tools.Tool);

        field = Open(f, 60, 200);
        SetText(field, "Dropped");
        Press(field, Key.Escape);
        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Equal(AnnotateTool.TextBox, f.Tools.Tool);
        await FinishAsync(f);
        Assert.Single(await BoxesAsync(f));
        Assert.Null(f.View.Field);
    }

    [AvaloniaFact]
    public async Task CmdEnterCommitsLikeCtrlEnter()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "From the Mac");

        // Cmd (Meta) + Enter commits, as Ctrl+Enter does and as the comment field of the marks pane does.
        Press(field, Key.Enter, KeyModifiers.Meta);
        await FinishAsync(f);

        var made = Assert.Single(await BoxesAsync(f));
        Assert.Equal("From the Mac", made.Contents);
        Assert.False(f.Interaction.TextBox.IsEditing);
    }

    [AvaloniaFact]
    public async Task EmptyTextCreatesNothing()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "  \n ");
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);

        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Empty(await BoxesAsync(f));
        Assert.False(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task RejectedCharactersKeepTheFieldOpenAndAreNamed()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        const string text = "Hi ☃ ☂ ☁ ☀ ♥ ♦ ♣ ♠ ★ \t";
        SetText(field, text);
        Press(field, Key.Enter, KeyModifiers.Control);
        await f.Interaction.TextBox.Completion;
        ViewFixture.Pump();

        // Nothing is replaced or dropped: the text is as typed, the field is open, up to 8 characters are named.
        Assert.True(f.Interaction.TextBox.IsEditing);
        Assert.Equal(text, field.Text);
        var hint = f.Interaction.TextBox.Editor!.Hint;
        Assert.Contains("☃", hint, StringComparison.Ordinal);
        Assert.Contains("♠", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("★", hint, StringComparison.Ordinal);
        Assert.Empty(await BoxesAsync(f));
        Assert.True(f.Tab.IsDirty, "the open draft counts as a change");

        // Fixing the text clears the hint and the commit goes through.
        SetText(field, "Hi");
        Assert.Equal(string.Empty, f.Interaction.TextBox.Editor!.Hint);
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);
        Assert.Single(await BoxesAsync(f));
    }

    [AvaloniaFact]
    public async Task GermanUmlautsAndEuroSurvive()
    {
        const string text = "Grüße aus Köln – 5 €";
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, text);
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);

        var ct = TestContext.Current.CancellationToken;
        var saved = await f.Tab.Annotations.Editor.SaveAsync(new PdfEngine(), f.Path!, false, null, ct);
        await using (saved.Saved.Reopened)
        {
            var back = (await saved.Saved.Reopened.GetAnnotationsAsync(0, ct)).Single(a => a.Kind == AnnotationKind.TextBox);
            Assert.Equal(text, back.Contents);
            Assert.Equal(text, back.TextBox!.Text);
        }
    }

    [AvaloniaFact]
    public async Task BoxGrowsToFitTheTextAndNeverShrinks()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f, 40, 40);
        var start = f.Interaction.TextBox.DraftBox!.Value;
        SetText(field, string.Join('\n', Enumerable.Range(1, 12).Select(i => "Line " + i)));
        ViewFixture.Pump();
        var grown = f.Interaction.TextBox.DraftBox!.Value;
        Assert.True(grown.Height > start.Height + 20, $"{grown.Height} vs {start.Height}");
        Assert.Equal(start.Top, grown.Top, 0.01);
        Assert.Equal(start.Width, grown.Width, 0.01);

        // Less text: the box stays.
        SetText(field, "x");
        ViewFixture.Pump();
        Assert.Equal(grown.Height, f.Interaction.TextBox.DraftBox!.Value.Height, 0.01);

        // Far more text than the page holds: the box stops at the page edge.
        SetText(field, string.Join('\n', Enumerable.Range(1, 190).Select(i => "Line " + i)));
        ViewFixture.Pump();
        var size = f.View.SizeOfPage(0)!.Value;
        Assert.True(f.Interaction.TextBox.DraftBox!.Value.Bottom <= size.Height + 0.01);
        Press(field, Key.Escape);
    }

    [AvaloniaFact]
    public async Task DraftCountsAsDirtyAndIsFlushedBeforeSave()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "Draft");
        Assert.True(f.Tab.IsDirty);

        // What the save does first: the draft is made, then the changes are written.
        await f.Tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Equal("Draft", Assert.Single(await BoxesAsync(f)).Contents);
        Assert.True(f.Tab.Annotations.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task EnterReopensAnOwnTextBox()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "First");
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);
        var made = Assert.Single(await BoxesAsync(f));

        Assert.True(await f.Interaction.TextBox.EditAsync(new AnnotationLocator(made.Page, made.Index, made.Name)));
        ViewFixture.Pump();
        field = f.Interaction.TextBox.Editor!.Field;
        Assert.Equal("First", field.Text);
        SetText(field, "Second");
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);

        Assert.Equal("Second", Assert.Single(await BoxesAsync(f)).Contents);
        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.True(f.Tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task ForeignTextBoxCannotBeEdited()
    {
        using var f = await AnnotateFixture.OpenAsync("annotations-foreign.pdf");
        var ct = TestContext.Current.CancellationToken;
        PdfAnnotation? foreign = null;
        for (var page = 0; page < f.Tab.Session.Document!.PageCount && foreign is null; page++)
        {
            foreign = (await f.Tab.Session.Document!.GetAnnotationsAsync(page, ct)).FirstOrDefault(a => a.Kind == AnnotationKind.TextBox);
        }

        Assert.NotNull(foreign);
        Assert.False(foreign.GeometryEditable);
        f.Tools.Choose(AnnotateTool.TextBox);
        f.View.GoToPage(foreign.Page);
        ViewFixture.Pump();

        Assert.False(await f.Interaction.TextBox.EditAsync(new AnnotationLocator(foreign.Page, foreign.Index, foreign.Name)));
        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Null(f.View.Field);
    }

    [AvaloniaFact]
    public async Task LeavingTheFieldCommitsOnce()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "Left");
        f.View.Focus();
        ViewFixture.Pump();
        await FinishAsync(f);

        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Equal("Left", Assert.Single(await BoxesAsync(f)).Contents);
    }

    [AvaloniaFact]
    public async Task ToolChangeMakesAValidDraftAndDropsNothingElse()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "Kept");
        f.Tools.Choose(AnnotateTool.Rectangle);
        await FinishAsync(f);
        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.Equal("Kept", Assert.Single(await BoxesAsync(f)).Contents);
        Assert.Null(f.View.Field);
    }

    [AvaloniaFact]
    public async Task DisposeReleasesTheDraft()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "Gone");
        Assert.True(f.Tab.IsDirty);
        f.Interaction.TextBox.Dispose();

        var done = f.Tab.Annotations.FlushAsync();
        Assert.Same(done, await Task.WhenAny(done, Task.Delay(5000, TestContext.Current.CancellationToken)));
        Assert.False(f.Tab.IsDirty);
        Assert.Null(f.View.Field);
    }

    [AvaloniaFact]
    public async Task EscInTheFieldOfTheWindowDiscardsAndKeepsTheMode()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("text-rich.pdf");
        ViewFixture.Pump();
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "annotate.textBox").Run!.Invoke();
        var controller = window.Window.AnnotateOf(tab)!.TextBox;
        var view = window.Window.ActiveView!;
        var at = view.TranslatePoint(view.ToView(0, 60, 60)!.Value, window.Window)!.Value;
        window.Window.MouseDown(at, MouseButton.Left);
        window.Window.MouseUp(at, MouseButton.Left);
        ViewFixture.Pump();
        Assert.True(controller.IsEditing);
        var field = controller.Editor!.Field;
        field.Focus();
        ViewFixture.Pump();
        Assert.Same(field, window.Window.FocusManager!.GetFocusedElement());

        window.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewFixture.Pump();

        Assert.False(controller.IsEditing);
        Assert.Equal(AnnotateTool.TextBox, window.Window.Tools.Tool);

        // With the field gone the next Esc ends the mode as before.
        window.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, window.Window.Tools.Tool);
    }

    private const string Refused = "Keep me ☃";

    private static async Task<bool> CompletesAsync(Task task) =>
        ReferenceEquals(task, await Task.WhenAny(task, Task.Delay(5000, TestContext.Current.CancellationToken)));

    [AvaloniaFact]
    public async Task ClosingWithARefusedDraftStillAsks()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("text-rich.pdf");
        ViewFixture.Pump();
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "annotate.textBox").Run!.Invoke();
        var controller = window.Window.AnnotateOf(tab)!.TextBox;
        var view = window.Window.ActiveView!;
        var at = view.TranslatePoint(view.ToView(0, 60, 60)!.Value, window.Window)!.Value;
        window.Window.MouseDown(at, MouseButton.Left);
        window.Window.MouseUp(at, MouseButton.Left);
        ViewFixture.Pump();
        SetText(controller.Editor!.Field, Refused);

        var closing = window.Window.Workspace.CloseAsync(tab);
        for (var i = 0; i < 400 && window.Window.Host.ActiveDialog is null; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
        }

        // The flush did not hang, the tab counts as changed, so the close asks; the text is still there.
        Assert.NotNull(window.Window.Host.ActiveDialog);
        Assert.True(controller.IsEditing);
        Assert.Equal(Refused, controller.Editor!.Field.Text);
        var footer = Assert.IsType<Avalonia.Controls.StackPanel>(window.Window.Host.ActiveDialog!.Footer);
        footer.Children.OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_Cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ViewFixture.Pump();
        Assert.False(await closing);
        Assert.True(controller.IsEditing);
    }

    [AvaloniaFact]
    public async Task RefusedDraftSurvivesAToolChange()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, Refused);

        f.Tools.Choose(AnnotateTool.Rectangle);
        ViewFixture.Pump();

        Assert.True(f.Interaction.TextBox.IsEditing);
        Assert.Equal(Refused, field.Text);
        Assert.NotEqual(string.Empty, f.Interaction.TextBox.Editor!.Hint);
        Assert.Same(f.Interaction.TextBox.Editor, f.View.Field);
        Assert.True(f.Tab.IsDirty);
        Assert.Empty(await BoxesAsync(f));
    }

    [AvaloniaFact]
    public async Task SaveKeepsARefusedDraftOpenAndTheTabDirty()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "Saved");
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);
        field = Open(f, 60, 200);
        SetText(field, Refused);

        // What a save does first: the flush returns although the draft is refused.
        Assert.True(await CompletesAsync(f.Tab.Annotations.FlushAsync()));
        Assert.True(f.Interaction.TextBox.IsEditing);
        var ct = TestContext.Current.CancellationToken;
        var saved = await f.Tab.Annotations.Editor.SaveAsync(new PdfEngine(), f.Path!, false, null, ct);
        await using (saved.Saved.Reopened)
        {
            Assert.Equal("Saved", Assert.Single(await saved.Saved.Reopened.GetAnnotationsAsync(0, ct), a => a.Kind == AnnotationKind.TextBox).Contents);
        }

        f.Tab.Annotations.Refresh();
        Assert.True(f.Interaction.TextBox.IsEditing);
        Assert.Equal(Refused, field.Text);
        Assert.True(f.Tab.Annotations.IsDirty);
        Assert.True(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task EscStillDiscardsARefusedDraft()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, Refused);
        Assert.True(await CompletesAsync(f.Tab.Annotations.FlushAsync()));
        Assert.True(f.Tab.IsDirty);

        Press(field, Key.Escape);

        Assert.False(f.Interaction.TextBox.IsEditing);
        Assert.False(f.Tab.IsDirty);
        Assert.Empty(await BoxesAsync(f));
        Assert.True(await CompletesAsync(f.Tab.Annotations.FlushAsync()));
    }

    [AvaloniaFact]
    public async Task RibbonUndoWithAnOpenFieldOnlyDiscardsTheDraft()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        SetText(field, "First");
        Press(field, Key.Enter, KeyModifiers.Control);
        await FinishAsync(f);
        field = Open(f, 60, 200);
        SetText(field, "Second");

        await f.Tab.Annotations.UndoAsync();
        ViewFixture.Pump();

        // The draft is gone like with Esc: not made, and the first text box is not undone.
        Assert.False(f.Interaction.TextBox.IsEditing);
        await f.SettleAsync();
        Assert.Equal("First", Assert.Single(await BoxesAsync(f)).Contents);
        Assert.True(f.Tab.Annotations.CanUndo);
        Assert.False(f.Tab.Annotations.CanRedo);

        // With no field open Undo works as before.
        await f.Tab.Annotations.UndoAsync();
        Assert.Empty(await BoxesAsync(f));
    }

    [AvaloniaFact]
    public async Task LongPasteIsRejectedAsAWholeAndNamedNotCut()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var field = Open(f);
        var text = new string('a', TextBoxSpec.MaxTextLength + 500);
        SetText(field, text);
        Press(field, Key.Enter, KeyModifiers.Control);
        await f.Interaction.TextBox.Completion;

        Assert.Equal(text, field.Text);
        Assert.True(f.Interaction.TextBox.IsEditing);
        Assert.Equal(Raffica.Pdf.PdfStrings.TextBoxTooLong, f.Interaction.TextBox.Editor!.Hint);
        Press(field, Key.Escape);
        Assert.Empty(await BoxesAsync(f));
    }
}
