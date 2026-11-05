using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class DrawingToolTests
{
    private static readonly AnnotationKind[] DrawingKinds =
        [AnnotationKind.Ink, AnnotationKind.Line, AnnotationKind.Arrow, AnnotationKind.Rectangle, AnnotationKind.Ellipse];

    private static async Task<IReadOnlyList<PdfAnnotation>> DrawingsAsync(AnnotateFixture f) =>
        (await f.Tab.Session.Document!.GetAnnotationsAsync(0, TestContext.Current.CancellationToken)).Where(a => DrawingKinds.Contains(a.Kind)).ToList();

    // A point of page 0 in the coordinates of the view.
    private static Point At(AnnotateFixture f, double x, double y) => f.View.ToView(0, x, y)!.Value;

    private static void Drag(AnnotateFixture f, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None, int steps = 6)
    {
        f.Window.MouseDown(from, MouseButton.Left, modifiers);
        for (var i = 1; i <= steps; i++)
        {
            f.Window.MouseMove(new Point(from.X + ((to.X - from.X) * i / steps), from.Y + ((to.Y - from.Y) * i / steps)), modifiers);
        }

        f.Window.MouseUp(to, MouseButton.Left, modifiers);
        ViewFixture.Pump();
    }

    private static async Task FinishAsync(AnnotateFixture f)
    {
        await f.Interaction.Drawing.Completion;
        await f.SettleAsync();
    }

    [AvaloniaFact]
    public async Task FreehandStrokeIsSimplifiedAndApplied()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Freehand);
        var raw = 0;
        f.Window.MouseDown(At(f, 100, 100), MouseButton.Left);
        for (var i = 1; i <= 120; i++)
        {
            // A straight run with a little noise: the simplifier keeps far fewer points than the pointer made.
            f.Window.MouseMove(At(f, 100 + (i * 2), 100 + ((i % 2) * 0.1)));
            raw++;
        }

        f.Window.MouseUp(At(f, 340, 100), MouseButton.Left);
        Assert.True(f.Tab.IsDirty);
        await FinishAsync(f);

        var ink = Assert.Single(await DrawingsAsync(f));
        Assert.Equal(AnnotationKind.Ink, ink.Kind);
        Assert.InRange(ink.Drawing!.Strokes[0].Count, 2, raw / 4);
        Assert.Equal(2, ink.Drawing!.Width, 0.01);
        Assert.True(f.Tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task ClickShorterThan4DipDrawsNothing()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        foreach (var tool in new[] { AnnotateTool.Freehand, AnnotateTool.Rectangle, AnnotateTool.Line })
        {
            f.Tools.Choose(tool);
            var p = At(f, 150, 150);
            f.Window.MouseDown(p, MouseButton.Left);
            f.Window.MouseMove(new Point(p.X + 2, p.Y + 1));
            f.Window.MouseUp(new Point(p.X + 2, p.Y + 1), MouseButton.Left);
            ViewFixture.Pump();
            f.Tools.End();
        }

        await FinishAsync(f);
        Assert.Empty(await DrawingsAsync(f));
        Assert.False(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task PointsBeyondTheLimitAreIgnoredNotCrashed()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Freehand);
        var drawing = f.Interaction.Drawing;
        drawing.Press(At(f, 60, 60), 1, KeyModifiers.None);
        for (var i = 0; i < DrawingController.MaxStrokePoints + 5_000; i++)
        {
            drawing.Move(new Point(80 + (i % 300), 80 + ((i / 300) % 300) + ((i % 2) * 0.5)), KeyModifiers.None);
        }

        Assert.Equal(DrawingController.MaxStrokePoints, drawing.PointCount);
        drawing.Release(At(f, 200, 200), KeyModifiers.None);
        await FinishAsync(f);

        Assert.Single(await DrawingsAsync(f));
    }

    [AvaloniaFact]
    public async Task PenDraws()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Line);
        var pen = new Pointer(Pointer.GetNextFreeId(), PointerType.Pen, true);
        var from = At(f, 100, 100);
        var to = At(f, 300, 200);
        var down = new PointerPressedEventArgs(
            f.View, pen, f.Window, from, 1, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        };
        f.View.RaiseEvent(down);
        Assert.True(f.Interaction.Drawing.IsDrawing);
        Assert.Same(f.View, pen.Captured);

        var move = new PointerEventArgs(
            InputElement.PointerMovedEvent, f.View, pen, f.Window, to, 2, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None);
        f.View.RaiseEvent(move);
        var up = new PointerReleasedEventArgs(
            f.View, pen, f.Window, to, 3, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left);
        f.View.RaiseEvent(up);
        await FinishAsync(f);

        Assert.Equal(AnnotationKind.Line, Assert.Single(await DrawingsAsync(f)).Kind);
    }

    [AvaloniaFact]
    public async Task TouchDoesNotDraw()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Freehand);
        var touch = f.Window.TouchBegin(At(f, 100, 200));
        Assert.False(f.Interaction.Drawing.IsDrawing);
        f.Window.TouchMove(touch, At(f, 100, 120));
        f.Window.TouchEnd(touch, At(f, 100, 120));
        ViewFixture.Pump();
        await FinishAsync(f);

        Assert.Empty(await DrawingsAsync(f));
        Assert.False(f.Tab.IsDirty);
        Assert.Equal(AnnotateTool.Freehand, f.Tools.Tool);
    }

    [AvaloniaTheory]
    [InlineData(nameof(AnnotateTool.Rectangle), 120, 0)]
    [InlineData(nameof(AnnotateTool.Rectangle), 0, 120)]
    [InlineData(nameof(AnnotateTool.Ellipse), 120, 0)]
    [InlineData(nameof(AnnotateTool.Ellipse), 0, 120)]
    public async Task AnAxisAlignedShapeDragNeverFailsAndShowsNoCard(string toolName, int dx, int dy)
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(Enum.Parse<AnnotateTool>(toolName));

        // Longer than 4 DIPs, but the box has no width (or no height): it draws nothing, throws nothing and says nothing.
        Drag(f, At(f, 100, 100), At(f, 100 + (dx / 2), 100 + (dy / 2)));
        await FinishAsync(f);
        ViewFixture.Pump();

        Assert.Null(f.Host.ActiveDialog);
        Assert.Empty(await DrawingsAsync(f));
        Assert.False(f.Tab.IsDirty);
        Assert.False(f.Tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task DrawingLimitIsReportedAndNothingIsCreated()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-limit-").FullName;
        try
        {
            using var f = await AnnotateFixture.OpenPathAsync(AnnotationLimitTests.WriteFullPdf(folder), width: 600, height: 500);
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(PdfDocument.MaxAnnotations, (await f.Tab.Session.Document!.GetAnnotationsAsync(0, ct)).Count);
            f.Tools.Choose(AnnotateTool.Rectangle);

            Drag(f, At(f, 100, 60), At(f, 200, 120));
            for (var i = 0; i < 400 && !f.Host.IsOpen; i++)
            {
                await Task.Delay(25, ct);
                ViewFixture.Pump();
            }

            // The error card names the limit; nothing was created and the tab is not dirty.
            var card = Assert.IsType<Raffica.Shell.Controls.Dialog>(f.Host.ActiveDialog);
            var message = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Message").Text;
            Assert.Equal(PdfStrings.AnnotateLimitReached, message);
            Assert.False(f.Tab.IsDirty);
            Assert.False(f.Tab.Annotations.CanUndo);
            Assert.Equal(PdfDocument.MaxAnnotations, (await f.Tab.Session.Document!.GetAnnotationsAsync(0, ct)).Count);

            card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Interaction.Drawing.Completion.WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.False(f.Tab.IsDirty);
            Assert.Equal(AnnotateTool.Rectangle, f.Tools.Tool);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [AvaloniaFact]
    public async Task ShiftSnapsALineTo45Degrees()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Line);
        Drag(f, At(f, 100, 100), At(f, 300, 140), RawInputModifiers.Shift);
        Drag(f, At(f, 100, 200), At(f, 260, 300), RawInputModifiers.Shift);
        await FinishAsync(f);

        var lines = await DrawingsAsync(f);
        Assert.Equal(2, lines.Count);
        var flat = lines[0].Drawing!.Strokes[0];
        Assert.Equal(flat[0].Y, flat[^1].Y, 0.1);
        var diagonal = lines[1].Drawing!.Strokes[0];
        Assert.Equal(Math.Abs(diagonal[^1].X - diagonal[0].X), Math.Abs(diagonal[^1].Y - diagonal[0].Y), 0.1);
    }

    [AvaloniaFact]
    public async Task ShiftMakesASquareAndACircle()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Rectangle);
        Drag(f, At(f, 100, 100), At(f, 300, 160), RawInputModifiers.Shift);
        f.Tools.Choose(AnnotateTool.Ellipse);
        Drag(f, At(f, 100, 200), At(f, 160, 330), RawInputModifiers.Shift);
        await FinishAsync(f);

        var shapes = await DrawingsAsync(f);
        var rectangle = Assert.Single(shapes, a => a.Kind == AnnotationKind.Rectangle);
        var ellipse = Assert.Single(shapes, a => a.Kind == AnnotationKind.Ellipse);
        Assert.Equal(rectangle.Drawing!.Box.Width, rectangle.Drawing!.Box.Height, 0.5);
        Assert.Equal(ellipse.Drawing!.Box.Width, ellipse.Drawing!.Box.Height, 0.5);
    }

    [AvaloniaFact]
    public async Task ArrowIsRecognisedAfterReload()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Arrow);
        Drag(f, At(f, 100, 100), At(f, 300, 200));
        await FinishAsync(f);
        Assert.Equal(AnnotationKind.Arrow, Assert.Single(await DrawingsAsync(f)).Kind);

        var ct = TestContext.Current.CancellationToken;
        var saved = await f.Tab.Annotations.Editor.SaveAsync(new PdfEngine(), f.Path!, false, null, ct);

        // The saved file is adopted by the tab (it owns the reopened document), so it must not be disposed here.
        var back = (await saved.Saved.Reopened.GetAnnotationsAsync(0, ct)).Where(a => DrawingKinds.Contains(a.Kind)).ToList();
        Assert.Equal(AnnotationKind.Arrow, Assert.Single(back).Kind);
    }

    [AvaloniaFact]
    public async Task StrokesAreAppliedInReleaseOrder()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        // The first stroke is long (it takes the longest to simplify), the others are quick: the order must still be the release order.
        f.Tools.Choose(AnnotateTool.Freehand);
        var drawing = f.Interaction.Drawing;
        drawing.Press(At(f, 50, 50), 1, KeyModifiers.None);
        for (var i = 0; i < 15_000; i++)
        {
            drawing.Move(new Point(60 + (i % 400), 60 + ((i / 400) % 40) + ((i % 3) * 0.7)), KeyModifiers.None);
        }

        drawing.Release(At(f, 400, 60), KeyModifiers.None);
        f.Tools.Choose(AnnotateTool.Line);
        Drag(f, At(f, 100, 300), At(f, 300, 300));
        f.Tools.Choose(AnnotateTool.Rectangle);
        Drag(f, At(f, 100, 330), At(f, 300, 370));
        await FinishAsync(f);

        var kinds = (await DrawingsAsync(f)).Select(a => a.Kind).ToList();
        Assert.Equal([AnnotationKind.Ink, AnnotationKind.Line, AnnotationKind.Rectangle], kinds);
    }

    [AvaloniaFact]
    public async Task StrokeStartingNearThePageEdgeStaysOnThePage()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var size = f.View.SizeOfPage(0)!.Value;
        f.Tools.Choose(AnnotateTool.Line);
        var bounds = f.View.PageBounds(0)!.Value;
        Drag(f, new Point(bounds.X + 1, bounds.Y + 100), new Point(bounds.X - 30, bounds.Y - 30), steps: 4);
        await FinishAsync(f);

        var line = Assert.Single(await DrawingsAsync(f));
        foreach (var p in line.Drawing!.Strokes[0])
        {
            Assert.InRange(p.X, 0, size.Width);
            Assert.InRange(p.Y, 0, size.Height);
        }
    }

    [AvaloniaFact]
    public async Task ToolStaysActiveAfterAStroke()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Ellipse);
        Drag(f, At(f, 100, 100), At(f, 250, 200));
        await FinishAsync(f);

        Assert.Equal(AnnotateTool.Ellipse, f.Tools.Tool);
        Assert.Single(await DrawingsAsync(f));
    }

    [AvaloniaFact]
    public async Task EscDuringAStrokeCancelsItAndKeepsTheMode()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Rectangle);
        f.Window.MouseDown(At(f, 100, 100), MouseButton.Left);
        f.Window.MouseMove(At(f, 250, 200));
        Assert.True(f.Interaction.Drawing.IsDrawing);

        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(f.Interaction.Drawing.IsDrawing);
        Assert.Equal(AnnotateTool.Rectangle, f.Tools.Tool);
        f.Window.MouseUp(At(f, 250, 200), MouseButton.Left);
        ViewFixture.Pump();
        await FinishAsync(f);

        Assert.Empty(await DrawingsAsync(f));
        Assert.False(f.Tab.IsDirty);
    }

    [AvaloniaTheory]
    [InlineData(nameof(AnnotateTool.Freehand))]
    [InlineData(nameof(AnnotateTool.Rectangle))]
    [InlineData(nameof(AnnotateTool.Ellipse))]
    [InlineData(nameof(AnnotateTool.Line))]
    [InlineData(nameof(AnnotateTool.Arrow))]
    public async Task EveryToolSurvivesSaveReopenAndUndo(string toolName)
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(Enum.Parse<AnnotateTool>(toolName));
        Drag(f, At(f, 120, 140), At(f, 320, 260), steps: 12);
        await FinishAsync(f);
        var made = Assert.Single(await DrawingsAsync(f));

        // The saved file is adopted by the tab (it owns the reopened document), so it must not be disposed here.
        var ct = TestContext.Current.CancellationToken;
        var saved = await f.Tab.Annotations.Editor.SaveAsync(new PdfEngine(), f.Path!, false, null, ct);
        var back = Assert.Single(await saved.Saved.Reopened.GetAnnotationsAsync(0, ct), a => DrawingKinds.Contains(a.Kind));
        Assert.Equal(made.Kind, back.Kind);
        Assert.Equal(made.Drawing!.Box.Left, back.Drawing!.Box.Left, 0.5);
        Assert.Equal(made.Drawing.Box.Top, back.Drawing.Box.Top, 0.5);
        Assert.Equal(made.Drawing.Box.Right, back.Drawing.Box.Right, 0.5);
        Assert.Equal(made.Drawing.Box.Bottom, back.Drawing.Box.Bottom, 0.5);

        // Undo works on a fresh drawing (a save starts a new history).
        using var g = await AnnotateFixture.OpenAsync("text-rich.pdf");
        g.Tools.Choose(Enum.Parse<AnnotateTool>(toolName));
        Drag(g, At(g, 120, 140), At(g, 320, 260), steps: 12);
        await FinishAsync(g);
        Assert.Single(await DrawingsAsync(g));
        await g.Tab.Annotations.UndoAsync();
        Assert.Empty(await DrawingsAsync(g));
    }
}
