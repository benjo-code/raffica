using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The mode as a whole: it excludes the other modes with a reason, reads nothing before the first use, and shows its outlines in the right place.</summary>
public sealed class TextEditModeTests
{
    private static readonly string[] Excluded =
    [
        "annotate.mode", "annotate.highlight", "annotate.freehand", "annotate.textBox", "annotate.sign",
        "pages.organize", "redact.mode", "ocr.run",
        "pages.extract", "pages.split", "pages.exportImages",
    ];

    [AvaloniaFact]
    public async Task ExcludesTheOtherModesWithAReason()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        foreach (var id in Excluded.Where(id => id is not "ocr.run"))
        {
            Assert.True(rig.Command(id).IsEnabled, id + " before the mode");
        }

        await rig.EnterModeAsync();
        foreach (var id in Excluded)
        {
            var command = rig.Command(id);
            Assert.False(command.IsEnabled, id);
            Assert.Equal("Finish editing the text first.", command.CurrentDisabledReason);
        }

        // Print stays, undo stays, the way out is on offer; saving is not locked (the draft is written first).
        Assert.True(rig.Command("file.print").IsEnabled);
        Assert.True(rig.Command("text.editDone").IsEnabled);
        Assert.True(rig.Command("text.edit").IsEnabled, "the same command leaves the mode");

        // Leaving gives the others back, and the mode can start again.
        rig.Command("text.editDone").Run!();
        await ViewFixture.WaitAsync(() => !rig.Session.IsActive);
        foreach (var id in Excluded.Where(id => id is not "ocr.run"))
        {
            Assert.True(rig.Command(id).IsEnabled, id + " after the mode");
        }

        Assert.False(rig.Command("text.editDone").IsEnabled);
        await rig.EnterModeAsync();
        Assert.True(rig.Session.IsActive);
    }

    [AvaloniaFact]
    public async Task AnotherModeInTheWayRefusesTheStartWithItsReason()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");

        // An annotate tool.
        rig.Command("annotate.highlight").Run!();
        ViewFixture.Pump();
        Assert.False(rig.Command("text.edit").IsEnabled);
        Assert.Equal("Finish annotating first.", rig.Command("text.edit").CurrentDisabledReason);
        rig.Window.Tools.End();
        ViewFixture.Pump();
        Assert.True(rig.Command("text.edit").IsEnabled);

        // A redaction plan.
        rig.Command("redact.mode").Run!();
        await ViewFixture.WaitAsync(() => rig.Tab.RedactIfCreated is { IsActive: true });
        Assert.False(rig.Command("text.edit").IsEnabled);
        Assert.Equal("Apply or discard the redactions first.", rig.Command("text.edit").CurrentDisabledReason);
        await rig.Window.ToggleTextEditAsync();
        Assert.Null(rig.Tab.TextEditIfCreated);
        Assert.Null(rig.Window.TextEditOf(rig.Tab));
    }

    [AvaloniaFact]
    public async Task NothingIsReadAtStartOrBeforeTheFirstUse()
    {
        using var rig = await TextEditRig.OpenAsync("text-dense.pdf");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        // After the first sharp page nothing of the mode exists.
        Assert.Null(rig.Tab.TextEditIfCreated);
        Assert.Null(rig.Window.TextEditOf(rig.Tab));

        // The session made for the test counts the reads: none before the mode, none after it was made.
        var reads = new List<int>();
        var session = rig.Session;
        session.ReadRuns = (document, page, token) =>
        {
            reads.Add(page);
            return document.GetTextRunsAsync(page, token);
        };
        Assert.Empty(reads);
        Assert.False(session.IsActive);

        // The first use: the mode reads the pages in view, one job each, and nothing else.
        rig.Command("text.edit").Run!();
        await ViewFixture.WaitAsync(() => session.IsActive && reads.Count > 0 && session.IsLoaded(0));
        var (first, last) = rig.View.VisiblePages;
        Assert.All(reads, page => Assert.InRange(page, first, last));
        Assert.Equal(reads.Count, reads.Distinct().Count());
        Assert.Equal(reads.Count, session.RunReads);

        // A page that scrolls into view is read when it is shown, and the pages that left are let go.
        var before = reads.Count;
        rig.View.GoToPage(120);
        await ViewFixture.WaitAsync(() => session.IsLoaded(120));
        Assert.True(reads.Count > before);
        Assert.False(session.IsLoaded(0));
        Assert.All(reads.Skip(before), page => Assert.InRange(page, 100, 140));
    }

    [AvaloniaFact]
    public async Task OutlinesFollowTheRotatedPageAndCropBox()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-rotated.pdf");
        await rig.EnterModeAsync();
        var runs = rig.Session.RunsOf(0);
        Assert.Equal(3, runs.Count);
        var page = rig.View.PageBounds(0)!.Value;
        foreach (var run in runs)
        {
            var rect = RunOutlines.RectOf(rig.View, run);
            Assert.NotNull(rect);
            Assert.True(page.Inflate(2).Contains(rect.Value.TopLeft) && page.Inflate(2).Contains(rect.Value.BottomRight), $"{run.Text}: {rect} not in {page}");
            Assert.True(rect.Value.Width > 0 && rect.Value.Height > 0);
        }

        // A click in the middle of the outline hits that line (the mapping of the outline and of the hit test are one).
        foreach (var run in runs)
        {
            var spot = rig.View.PageAt(RunOutlines.RectOf(rig.View, run)!.Value.Center)!.Value;
            Assert.Equal(run.ObjectIndex, rig.Session.RunAt(spot.Page, spot.X, spot.Y)!.ObjectIndex);
        }
    }

    [AvaloniaFact]
    public async Task LockedOutlinesAreDashedAndDrawnApartFromEditableOnes()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-locked.pdf");
        await rig.EnterModeAsync();
        var runs = rig.Session.RunsOf(0);
        var invisible = runs.Single(r => r.Text.TrimEnd() == "Invisible text layer");
        var control = runs.Single(r => r.Text.TrimEnd() == "Editable control line");

        var (outlined, stride) = Capture(rig);
        rig.Command("text.editDone").Run!();
        await ViewFixture.WaitAsync(() => !rig.Session.IsActive);
        var (plain, _) = Capture(rig);

        // The mode draws on the page: around both lines the pixels differ from the page without the mode, and the lock icon sits above the locked line.
        int Changed(Rect rect) => CountDifferent(outlined, plain, stride, rect);
        var lockedRect = RunOutlines.RectOf(rig.View, invisible)!.Value;
        var editableRect = RunOutlines.RectOf(rig.View, control)!.Value;
        Assert.True(Changed(lockedRect.Inflate(4)) > 20, "the locked outline is drawn");
        Assert.True(Changed(editableRect.Inflate(4)) > 20, "the editable outline is drawn");
        var iconSpot = new Rect(lockedRect.Left, Math.Max(0, lockedRect.Top - 16), 10, 14);
        Assert.True(Changed(iconSpot) > 10, "the lock icon is drawn above the locked line");
    }

    private static (byte[] Pixels, int Stride) Capture(TextEditRig rig)
    {
        var view = rig.View;
        var size = new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(view);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixels.Length);
        try
        {
            frame.CopyPixels(new PixelRect(size), buffer, pixels.Length, stride);
            System.Runtime.InteropServices.Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }

        return (pixels, stride);
    }

    private static int CountDifferent(byte[] a, byte[] b, int stride, Rect rect)
    {
        var count = 0;
        for (var y = Math.Max(0, (int)rect.Top); y < (int)rect.Bottom && (y + 1) * stride <= a.Length; y++)
        {
            for (var x = Math.Max(0, (int)rect.Left); x < (int)rect.Right && (x + 1) * 4 <= stride; x++)
            {
                var at = (y * stride) + (x * 4);
                if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2])
                {
                    count++;
                }
            }
        }

        return count;
    }
}
