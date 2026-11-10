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
            Raffica.Pdf.Tests.Viewing.PixelOrder.ToBgra(pixels, frame.Format);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return (pixels, stride, size.Width, size.Height);
    }

    private static bool Saturated(byte[] pixels, int offset) =>
        Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])) - Math.Min(pixels[offset], Math.Min(pixels[offset + 1], pixels[offset + 2])) > 80;

    // The yellow of the highlight on the dark page: red and green clearly above blue.
    private static bool Yellowish(byte[] pixels, int offset) =>
        pixels[offset + 2] - pixels[offset] > 40 && pixels[offset + 1] - pixels[offset] > 40;

    // The number of pixels in a region (in view pixels) that satisfy the predicate; BGRA frames.
    private static int CountIn((byte[] Pixels, int Stride, int Width, int Height) frame, Rect region, Func<byte[], int, bool> predicate)
    {
        var count = 0;
        for (var y = Math.Max(0, (int)region.Top); y < Math.Min(frame.Height, (int)region.Bottom); y++)
        {
            for (var x = Math.Max(0, (int)region.Left); x < Math.Min(frame.Width, (int)region.Right); x++)
            {
                if (predicate(frame.Pixels, (y * frame.Stride) + (x * 4)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int DifferingIn((byte[] Pixels, int Stride, int Width, int Height) before, (byte[] Pixels, int Stride, int Width, int Height) after, Rect region)
    {
        var count = 0;
        for (var y = Math.Max(0, (int)region.Top); y < Math.Min(before.Height, (int)region.Bottom); y++)
        {
            for (var x = Math.Max(0, (int)region.Left); x < Math.Min(before.Width, (int)region.Right); x++)
            {
                var o = (y * before.Stride) + (x * 4);
                if (before.Pixels[o] != after.Pixels[o] || before.Pixels[o + 1] != after.Pixels[o + 1] || before.Pixels[o + 2] != after.Pixels[o + 2])
                {
                    count++;
                }
            }
        }

        return count;
    }

    [AvaloniaFact]
    public async Task DarkPageKeepsMarkupVisible()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var window = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(window, copy.Path);
        var view = window.Window.ActiveView!;
        RunCommand(window, PdfCommandIds.DarkPage);
        await ViewFixture.WaitAsync(() => view.PageTheme == PageTheme.Dark && tab.Session.Scheduler is { PendingCount: 0 });
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

        // The mode ends first: no preview, selection or overlay is left to repaint the regions.
        RunCommand(window, PdfCommandIds.AnnotateDone);
        window.Window.TextOf(tab)!.Selection.Clear();
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, window.Window.Tools.Tool);

        var topLeft = view.TranslatePoint(new Point(0, 0), window.Window)!.Value;
        Rect InView(Point a, Point b, double pad) => new(Math.Min(a.X, b.X) - topLeft.X - pad, Math.Min(a.Y, b.Y) - topLeft.Y - pad, Math.Abs(a.X - b.X) + (2 * pad), Math.Abs(a.Y - b.Y) + (2 * pad));
        var wordFrom = CharPoint(window, text, start, right: false);
        var wordTo = CharPoint(window, text, end - 1, right: true);
        var rectangleRegion = InView(from, to, 4);
        var wordRegion = InView(new Point(wordFrom.X, wordFrom.Y - 6), new Point(wordTo.X, wordTo.Y + 6), 0);

        // Wait for the tiles with the marks: the state is "the colour is on the screen" (a failure shows as the end of the wait).
        var marked = plain;
        for (var i = 0; i < 400; i++)
        {
            ViewFixture.Pump();
            marked = Capture(window);
            if (tab.Session.Scheduler is { PendingCount: 0 } && CountIn(marked, rectangleRegion, Saturated) > 40 && CountIn(marked, wordRegion, Yellowish) > 40)
            {
                break;
            }

            await Task.Delay(25, Cancel);
        }

        // The dark page has no saturated colour of its own: the outline and the highlight bring it, and only there.
        Assert.Equal(0, CountIn(plain, rectangleRegion, Saturated));
        Assert.Equal(0, CountIn(plain, wordRegion, Yellowish));
        Assert.True(CountIn(marked, rectangleRegion, Saturated) > 40, "the outline is visible on the dark page");
        Assert.True(CountIn(marked, wordRegion, Yellowish) > 40, "the highlight is visible on the dark page");

        // A control region away from both marks is as it was.
        var control = InView(At(window, 470, 150), At(window, 560, 330), 0);
        Assert.Equal(0, DifferingIn(plain, marked, control));

        // The dark page is a view setting: the file itself has the marks and nothing of the theme.
        var kinds = await KindsAsync(tab);
        Assert.Equal(1, kinds.GetValueOrDefault(AnnotationKind.Rectangle));
        Assert.Equal(1, kinds.GetValueOrDefault(AnnotationKind.Highlight));
    }

    [AvaloniaFact]
    public async Task PrintKeepsAnnotationsOnALightPage()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The print path draws onto a Windows device context.");
        }

        using var copy = new TempCopy("text-rich.pdf");
        var service = new FakePrintService { DialogResult = new PrintTicket(new PrintSettings { Printer = "Office" }) };
        using var window = WindowFixture.Create(realSave: true, printService: service);
        var tab = await OpenAsync(window, copy.Path);
        RunCommand(window, PdfCommandIds.DarkPage);
        Assert.Equal(PageTheme.Dark, window.Window.ActiveView!.PageTheme);
        var red = new AnnotationColor(220, 38, 38);
        Assert.NotNull(await tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(new PageRect(300, 150, 420, 230), 4), red, null, null));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(tab.IsDirty);

        RunCommand(window, PdfCommandIds.Print);
        await PrintHost.UntilAsync(() => service.Jobs.Count == 1);
        await PrintHost.UntilAsync(() => service.Printed.Count == 1 && !window.Window.Host.IsOpen);

        // The job carries the document and nothing more: the print path of the service draws the page itself. So the page is printed here
        // with the same call the Windows service makes, onto a memory device context, and looked at.
        var job = service.Jobs[0];
        Assert.Same(tab.Session.Document, job.Document);
        var size = window.Window.ActiveView!.SizeOfPage(0)!.Value;
        const int Width = 600;
        var height = (int)Math.Round(Width * size.Height / size.Width);
        var scale = Width / size.Width;
        var pixels = await PrintToMemoryAsync(job.Document, Width, height);

        int Count(PageRect region, Func<byte[], int, bool> predicate)
        {
            var count = 0;
            for (var y = (int)(region.Top * scale); y < (int)(region.Bottom * scale); y++)
            {
                for (var x = (int)(region.Left * scale); x < (int)(region.Right * scale); x++)
                {
                    if (predicate(pixels, ((y * Width) + x) * 4))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // The red outline is on the paper (the top edge of the rectangle), and the paper is light: the empty corner is white.
        var edge = new PageRect(300, 148, 420, 152);
        Assert.True(Count(edge, (p, o) => p[o + 2] > 180 && p[o + 1] < 100 && p[o] < 100) > 100, "the annotation is printed");
        var corner = new PageRect(size.Width - 60, size.Height - 60, size.Width - 10, size.Height - 10);
        Assert.Equal(Count(corner, (_, _) => true), Count(corner, (p, o) => p[o] == 255 && p[o + 1] == 255 && p[o + 2] == 255));
    }

    // Prints a page with the call of the Windows print service onto a memory device context; BGRA, top-down.
    private static async Task<byte[]> PrintToMemoryAsync(PdfDocument document, int width, int height)
    {
        var dc = CreateCompatibleDC(0);
        Assert.NotEqual(0, dc);
        try
        {
            var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var bitmap = CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
            Assert.NotEqual(0, bitmap);
            try
            {
                var old = SelectObject(dc, bitmap);
                var pixels = new byte[width * height * 4];
                Array.Fill(pixels, (byte)0xFF);
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                await document.PrintPageToDeviceAsync(0, dc, new DevicePlacement(0, 0, width, height, 0), Cancel);
                GdiFlush();
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                SelectObject(dc, old);
                return pixels;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }
}
