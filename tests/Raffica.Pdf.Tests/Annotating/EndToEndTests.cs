using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

/// <summary>The whole path through the window, the way a user takes it: ribbon commands, keys and pointer events on a temp copy of a test file.</summary>
public sealed class EndToEndTests
{
    private static readonly AnnotationKind[] MadeKinds =
    [
        AnnotationKind.Highlight, AnnotationKind.Underline, AnnotationKind.StrikeOut, AnnotationKind.Ink, AnnotationKind.Rectangle,
        AnnotationKind.Ellipse, AnnotationKind.Line, AnnotationKind.Arrow, AnnotationKind.TextBox,
    ];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static void RunCommand(WindowFixture window, string id) =>
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).Run!.Invoke();

    private static bool IsEnabled(WindowFixture window, string id) =>
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).IsEnabled;

    private static void Press(WindowFixture window, PhysicalKey key, RawInputModifiers modifiers)
    {
        window.Window.KeyPressQwerty(key, modifiers);
        ViewFixture.Pump();
    }

    // A point of page 0 in the coordinates of the window.
    private static Point At(WindowFixture window, double x, double y)
    {
        var view = window.Window.ActiveView!;
        return view.TranslatePoint(view.ToView(0, x, y)!.Value, window.Window)!.Value;
    }

    private static void Drag(WindowFixture window, Point from, Point to)
    {
        window.Window.MouseDown(from, MouseButton.Left);
        for (var i = 1; i <= 6; i++)
        {
            window.Window.MouseMove(new Point(from.X + ((to.X - from.X) * i / 6), from.Y + ((to.Y - from.Y) * i / 6)));
        }

        window.Window.MouseUp(to, MouseButton.Left);
        ViewFixture.Pump();
    }

    private static async Task<(PageText Text, int Start, int End)> WordAsync(PdfTab tab, string word)
    {
        var text = await tab.Session.EnsurePageTexts()!.GetAsync(0, cancellationToken: Cancel);
        var start = text.GetText(0, text.Chars.Count).IndexOf(word, StringComparison.Ordinal);
        Assert.True(start >= 0, word);
        return (text, start, start + word.Length);
    }

    private static Point CharPoint(WindowFixture window, PageText text, int index, bool right)
    {
        var box = text.Chars[index].Box;
        return At(window, right ? box.Right - 0.5 : box.Left + 0.5, (box.Top + box.Bottom) / 2);
    }

    private static async Task SettleAsync(WindowFixture window, PdfTab tab)
    {
        var interaction = window.Window.AnnotateOf(tab)!;
        await interaction.Drawing.Completion;
        await interaction.TextBox.Completion;
        ViewFixture.Pump();
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
    }

    private static async Task<Dictionary<AnnotationKind, int>> KindsAsync(PdfTab tab)
    {
        var counts = new Dictionary<AnnotationKind, int>();
        for (var page = 0; page < tab.Session.Document!.PageCount; page++)
        {
            foreach (var annotation in await tab.Session.Document.GetAnnotationsAsync(page, Cancel))
            {
                counts[annotation.Kind] = counts.GetValueOrDefault(annotation.Kind) + 1;
            }
        }

        return counts;
    }

    private static async Task<PdfTab> OpenAsync(WindowFixture window, string path)
    {
        var tab = await window.Window.Flow.OpenAsync(path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => window.Window.ActiveView!.FirstSharpPageShown);
        return tab;
    }

    [AvaloniaFact]
    public async Task EveryToolSurvivesSaveReopenAndUndoThroughTheWindow()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var window = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(window, copy.Path);
        var baseline = await KindsAsync(tab);

        // The three markups drag over words of the text.
        var (text, start, end) = await WordAsync(tab, "Handbuch");
        await window.Window.TextOf(tab)!.Selection.EnsureVisibleTextAsync();
        var (second, secondStart, secondEnd) = await WordAsync(tab, "Zweite");
        var (third, thirdStart, thirdEnd) = await WordAsync(tab, "Weiter");
        // After a change the text of the page is read again; the user needs longer than that for the next drag, the test waits for it.
        foreach (var (id, t, s, e) in new[] { (PdfCommandIds.Highlight, text, start, end), (PdfCommandIds.Underline, second, secondStart, secondEnd), (PdfCommandIds.StrikeOut, third, thirdStart, thirdEnd) })
        {
            await window.Window.TextOf(tab)!.Selection.EnsureVisibleTextAsync();
            RunCommand(window, id);
            Drag(window, CharPoint(window, t, s, right: false), CharPoint(window, t, e - 1, right: true));
            await SettleAsync(window, tab);
        }

        // The drawing tools, each with its own drag.
        var drawings = new (string Id, Point From, Point To)[]
        {
            (PdfCommandIds.Freehand, At(window, 300, 100), At(window, 400, 130)),
            (PdfCommandIds.Rectangle, At(window, 300, 150), At(window, 400, 200)),
            (PdfCommandIds.Ellipse, At(window, 300, 210), At(window, 400, 250)),
            (PdfCommandIds.Line, At(window, 300, 260), At(window, 400, 275)),
            (PdfCommandIds.Arrow, At(window, 300, 290), At(window, 400, 330)),
        };
        foreach (var (id, from, to) in drawings)
        {
            RunCommand(window, id);
            Drag(window, from, to);
            await SettleAsync(window, tab);
        }

        // The text box: a click opens the field, Ctrl+Enter makes the box.
        RunCommand(window, PdfCommandIds.TextBox);
        var click = At(window, 400, 60);
        window.Window.MouseDown(click, MouseButton.Left);
        window.Window.MouseUp(click, MouseButton.Left);
        ViewFixture.Pump();
        var controller = window.Window.AnnotateOf(tab)!.TextBox;
        Assert.True(controller.IsEditing);
        var field = controller.Editor!.Field;
        field.Text = "End to end";
        ViewFixture.Pump();
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
        ViewFixture.Pump();
        await SettleAsync(window, tab);

        RunCommand(window, PdfCommandIds.AnnotateDone);
        var made = await KindsAsync(tab);
        foreach (var kind in MadeKinds)
        {
            Assert.True(baseline.GetValueOrDefault(kind) + 1 == made.GetValueOrDefault(kind), kind.ToString());
        }

        Assert.True(tab.IsDirty);

        // Ctrl+S saves, the tab is clean, and the file is closed and opened again.
        Press(window, PhysicalKey.S, RawInputModifiers.Control);
        await ViewFixture.WaitAsync(() => !tab.IsDirty && !window.Window.Saves.IsSaving);
        Assert.True(await window.Window.Workspace.CloseAsync(tab));
        ViewFixture.Pump();
        var reopened = await OpenAsync(window, copy.Path);

        var again = await KindsAsync(reopened);
        foreach (var kind in MadeKinds)
        {
            Assert.Equal(made.GetValueOrDefault(kind), again.GetValueOrDefault(kind));
        }

        Assert.False(reopened.IsDirty);
        Assert.False(reopened.Annotations.CanUndo);

        // A fresh edit is the only thing Ctrl+Z takes back.
        var (fresh, freshStart, freshEnd) = await WordAsync(reopened, "Handbuch");
        await window.Window.TextOf(reopened)!.Selection.EnsureVisibleTextAsync();
        RunCommand(window, PdfCommandIds.Highlight);
        Drag(window, CharPoint(window, fresh, freshStart, right: false), CharPoint(window, fresh, freshEnd - 1, right: true));
        await SettleAsync(window, reopened);
        Assert.Equal(again.GetValueOrDefault(AnnotationKind.Highlight) + 1, (await KindsAsync(reopened)).GetValueOrDefault(AnnotationKind.Highlight));
        RunCommand(window, PdfCommandIds.AnnotateDone);

        Press(window, PhysicalKey.Z, RawInputModifiers.Control);
        await ViewFixture.WaitAsync(() => !reopened.Annotations.CanUndo);
        var undone = await KindsAsync(reopened);
        foreach (var kind in MadeKinds)
        {
            Assert.Equal(again.GetValueOrDefault(kind), undone.GetValueOrDefault(kind));
        }

        // The history version of Core never comes back, so the tab counts as changed after an undo too (it is never wrongly clean).
        Assert.True(reopened.IsDirty);
    }

    [AvaloniaFact]
    public async Task UndoAfterSaveIsDisabled()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var window = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(window, copy.Path);
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(tab.Annotations.CanUndo);
        Assert.True(IsEnabled(window, PdfCommandIds.Undo));

        Press(window, PhysicalKey.S, RawInputModifiers.Control);
        await ViewFixture.WaitAsync(() => !tab.IsDirty && !window.Window.Saves.IsSaving);

        // Core cleared the history: nothing to undo, the command says so, and Ctrl+Z leaves the saved mark alone.
        Assert.False(tab.Annotations.CanUndo);
        Assert.False(IsEnabled(window, PdfCommandIds.Undo));
        var marks = (await KindsAsync(tab)).GetValueOrDefault(AnnotationKind.Highlight);
        Press(window, PhysicalKey.Z, RawInputModifiers.Control);
        await Task.Delay(100, Cancel);
        ViewFixture.Pump();
        Assert.Equal(marks, (await KindsAsync(tab)).GetValueOrDefault(AnnotationKind.Highlight));
        Assert.False(tab.IsDirty);
    }

    private static (byte[] Pixels, int Stride, int Width, int Height) Capture(WindowFixture window)
    {
        var view = window.Window.ActiveView!;
        var size = new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(view);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            frame.CopyPixels(new Avalonia.PixelRect(size), buffer, pixels.Length, stride);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return (pixels, stride, size.Width, size.Height);
    }

    private static async Task RenderedAsync(PdfTab tab)
    {
        for (var i = 0; i < 400 && tab.Session.Scheduler is { PendingCount: > 0 }; i++)
        {
            await Task.Delay(25, Cancel);
            ViewFixture.Pump();
        }

        await Task.Delay(100, Cancel);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task DarkPageKeepsMarkupVisible()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var window = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(window, copy.Path);
        RunCommand(window, PdfCommandIds.DarkPage);
        await RenderedAsync(tab);
        Assert.Equal(PageTheme.Dark, window.Window.ActiveView!.PageTheme);
        var plain = Capture(window);

        // A rectangle with a clear outline and a highlight over a word, both made with the tools.
        RunCommand(window, PdfCommandIds.Rectangle);
        var from = At(window, 300, 150);
        var to = At(window, 420, 230);
        Drag(window, from, to);
        await SettleAsync(window, tab);
        var (text, start, end) = await WordAsync(tab, "Handbuch");
        await window.Window.TextOf(tab)!.Selection.EnsureVisibleTextAsync();
        RunCommand(window, PdfCommandIds.Highlight);
        Drag(window, CharPoint(window, text, start, right: false), CharPoint(window, text, end - 1, right: true));
        await SettleAsync(window, tab);
        await RenderedAsync(tab);
        var marked = Capture(window);

        // Inside the window the view is at an offset; the same bounds are compared in both frames. The rectangle region and the word
        // region differ, and the page outside them is as before.
        var view = window.Window.ActiveView!;
        var topLeft = view.TranslatePoint(new Point(0, 0), window.Window)!.Value;
        int Differing(Rect region)
        {
            var count = 0;
            for (var y = (int)region.Top; y < (int)region.Bottom; y++)
            {
                for (var x = (int)region.Left; x < (int)region.Right; x++)
                {
                    if (x < 0 || y < 0 || x >= plain.Width || y >= plain.Height)
                    {
                        continue;
                    }

                    var o = (y * plain.Stride) + (x * 4);
                    var delta = Math.Abs(plain.Pixels[o] - marked.Pixels[o]) + Math.Abs(plain.Pixels[o + 1] - marked.Pixels[o + 1]) + Math.Abs(plain.Pixels[o + 2] - marked.Pixels[o + 2]);
                    if (delta > 60)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        Rect InView(Point a, Point b) => new(Math.Min(a.X, b.X) - topLeft.X - 4, Math.Min(a.Y, b.Y) - topLeft.Y - 4, Math.Abs(a.X - b.X) + 8, Math.Abs(a.Y - b.Y) + 8);

        Assert.True(Differing(InView(from, to)) > 40, "the rectangle is visible on the dark page");
        var wordFrom = CharPoint(window, text, start, right: false);
        var wordTo = CharPoint(window, text, end - 1, right: true);
        Assert.True(Differing(InView(new Point(wordFrom.X, wordFrom.Y - 6), new Point(wordTo.X, wordTo.Y + 6))) > 40, "the highlight is visible on the dark page");

        // The dark page is a view setting: the file itself has the marks and nothing of the theme.
        var kinds = await KindsAsync(tab);
        Assert.Equal(1, kinds.GetValueOrDefault(AnnotationKind.Rectangle));
        Assert.Equal(1, kinds.GetValueOrDefault(AnnotationKind.Highlight));
    }

    [AvaloniaFact]
    public async Task PrintKeepsAnnotationsOnALightPage()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var service = new FakePrintService { DialogResult = new PrintTicket(new PrintSettings { Printer = "Office" }) };
        using var window = WindowFixture.Create(realSave: true, printService: service);
        var tab = await OpenAsync(window, copy.Path);
        RunCommand(window, PdfCommandIds.DarkPage);
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(tab.IsDirty);

        RunCommand(window, PdfCommandIds.Print);
        await PrintHost.UntilAsync(() => service.Jobs.Count == 1);

        // The service gets the document with the unsaved mark in it; the print path of the service draws pages with annotations and
        // without the page theme (the theme is a setting of the view and of its render requests only).
        var job = service.Jobs[0];
        Assert.Same(tab.Session.Document, job.Document);
        Assert.Contains(await job.Document.GetAnnotationsAsync(0, Cancel), a => a.Kind == AnnotationKind.Highlight);
        Assert.Equal(PageTheme.Dark, window.Window.ActiveView!.PageTheme);
        Assert.True(RenderOptions.Default.Annotations);
        Assert.Equal(PageTheme.Light, RenderOptions.Default.PageTheme);
        await PrintHost.UntilAsync(() => service.Printed.Count == 1 && !window.Window.Host.IsOpen);
    }
}
