using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Annotations;
using Raffica.Pdf.Signing;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignaturePadTests
{
    private static (Window Window, SignaturePad Pad) Show()
    {
        var pad = new SignaturePad { Width = 400, Height = 200, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = pad, Width = 500, Height = 300 };
        window.Show();
        return (window, pad);
    }

    private static PointerPointProperties Props(RawInputModifiers modifiers, PointerUpdateKind kind) => new(modifiers, kind);

    private static void Press(SignaturePad pad, Window window, Pointer pointer, Point at, MouseButton button = MouseButton.Left)
    {
        var kind = button == MouseButton.Right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed;
        var modifiers = button == MouseButton.Right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton;
        pad.RaiseEvent(new PointerPressedEventArgs(pad, pointer, window, at, 1, Props(modifiers, kind), KeyModifiers.None) { RoutedEvent = InputElement.PointerPressedEvent });
    }

    private static void Move(SignaturePad pad, Window window, Pointer pointer, Point at) =>
        pad.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, pad, pointer, window, at, 2, Props(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    private static void Release(SignaturePad pad, Window window, Pointer pointer, Point at) =>
        pad.RaiseEvent(new PointerReleasedEventArgs(pad, pointer, window, at, 3, Props(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    private static void Stroke(SignaturePad pad, Window window, PointerType type, params Point[] points)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), type, true);
        Press(pad, window, pointer, points[0]);
        foreach (var p in points.Skip(1))
        {
            Move(pad, window, pointer, p);
        }

        Release(pad, window, pointer, points[^1]);
    }

    private static Point[] Line(double x0, double y0, double x1, double y1, int steps) =>
        [.. Enumerable.Range(0, steps + 1).Select(i => new Point(x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps)))];

    [AvaloniaFact]
    public void MouseStrokeIsSimplifiedAndKept()
    {
        var (window, pad) = Show();
        var changes = 0;
        pad.Changed += () => changes++;

        Stroke(pad, window, PointerType.Mouse, Line(20, 50, 220, 50, 40));

        var stroke = Assert.Single(pad.Strokes);
        Assert.Equal(2, stroke.Count); // a straight run keeps its two ends
        Assert.Equal(new DisplayPoint(20, 50), stroke[0]);
        Assert.Equal(new DisplayPoint(220, 50), stroke[^1]);
        Assert.True(pad.HasInk);
        Assert.Equal(1, changes);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(PointerType.Pen)]
    [InlineData(PointerType.Touch)]
    public void PenAndTouchAlsoDrawOnThePad(PointerType type)
    {
        var (window, pad) = Show();

        Stroke(pad, window, type, Line(10, 10, 100, 80, 10));

        Assert.Single(pad.Strokes);
        window.Close();
    }

    [AvaloniaFact]
    public void ACurveKeepsItsShape()
    {
        var (window, pad) = Show();
        var arc = Enumerable.Range(0, 61).Select(i => new Point(20 + (i * 5), 100 - (60 * Math.Sin(i * Math.PI / 60)))).ToArray();

        Stroke(pad, window, PointerType.Mouse, arc);

        var stroke = Assert.Single(pad.Strokes);
        Assert.InRange(stroke.Count, 5, 40);
        Assert.Contains(stroke, p => p.Y < 50); // the top of the arc survives
        window.Close();
    }

    [AvaloniaFact]
    public void RightButtonMoveWithoutPressAndATapDrawNothing()
    {
        var (window, pad) = Show();
        var mouse = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);

        Press(pad, window, mouse, new Point(10, 10), MouseButton.Right);
        Move(pad, window, mouse, new Point(50, 50));
        Release(pad, window, mouse, new Point(50, 50));
        Move(pad, window, mouse, new Point(80, 80));
        Stroke(pad, window, PointerType.Mouse, new Point(30, 30)); // a tap: one point only

        Assert.Empty(pad.Strokes);
        Assert.False(pad.HasInk);
        window.Close();
    }

    [AvaloniaFact]
    public void BackRemovesTheLastStrokeAndClearAllOfThem()
    {
        var (window, pad) = Show();
        Stroke(pad, window, PointerType.Mouse, Line(10, 10, 100, 10, 5));
        Stroke(pad, window, PointerType.Mouse, Line(10, 40, 100, 40, 5));
        var changes = 0;
        pad.Changed += () => changes++;

        pad.Back();
        Assert.Single(pad.Strokes);
        Assert.Equal(10, pad.Strokes[0][0].Y);

        pad.Clear();
        Assert.Empty(pad.Strokes);
        Assert.Equal(2, changes);

        pad.Back();
        pad.Clear();
        Assert.Equal(2, changes); // nothing to undo, nothing raised
        window.Close();
    }

    [AvaloniaFact]
    public void ALostCaptureEndsTheStroke()
    {
        var (window, pad) = Show();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        Press(pad, window, pointer, new Point(10, 10));
        Move(pad, window, pointer, new Point(60, 40));
        Move(pad, window, pointer, new Point(120, 20));

        pad.RaiseEvent(new PointerCaptureLostEventArgs(pad, pointer));

        Assert.Single(pad.Strokes);
        Move(pad, window, pointer, new Point(200, 200)); // the stroke is over
        Assert.Equal(3, Assert.Single(pad.Strokes).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void StrokesAreASnapshotAndTheColourDefaultsToBlack()
    {
        var (window, pad) = Show();
        Stroke(pad, window, PointerType.Mouse, Line(10, 10, 100, 10, 5));

        var snapshot = pad.Strokes;
        pad.Clear();

        Assert.Single(snapshot);
        Assert.Equal(DrawColor.Black, pad.Color);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePadStopsTakingPointsAtItsCap()
    {
        var (window, pad) = Show();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        Press(pad, window, pointer, new Point(1, 1));
        var rng = new Random(7);
        for (var i = 0; i < SignaturePad.MaxPoints + 500; i++)
        {
            Move(pad, window, pointer, new Point(1 + rng.NextDouble() * 390, 1 + rng.NextDouble() * 190));
        }

        Release(pad, window, pointer, new Point(5, 5));

        Assert.True(pad.Strokes.Sum(s => s.Count) <= SignaturePad.MaxPoints);
        window.Close();
    }
}
