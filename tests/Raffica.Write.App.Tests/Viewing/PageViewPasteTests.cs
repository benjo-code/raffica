using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>Paste of a picture from the clipboard through the page view (milestone 4.5a, Task 13): the order fragment, text, picture; one history step over a selection; refusals with their reason; the clipboard reader of the app.</summary>
public sealed class PageViewPasteTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-paste-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    private sealed class FakeClipboard : IEditClipboard
    {
        public ClipboardContent? Content { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public async Task<ClipboardContent?> ReadAsync()
        {
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            return Content;
        }

        public Task<bool> WriteAsync(ClipboardContent content)
        {
            Content = content;
            return Task.FromResult(true);
        }
    }

    private static byte[] Png(int width = 12, int height = 12)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(new SkiaSharp.SKColor(10, 200, 60));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static string TextOf(ViewHarness h, Paragraph paragraph) =>
        ParagraphProjection.Build(paragraph, h.View.Runner.PageLayout.ProjectionHost(StoryRef.Body, paragraph)).Text;

    private static int PictureCount(ViewHarness h) =>
        h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<Picture>().Count();

    private static EditSession Select(ViewHarness h, Paragraph paragraph, int from, int to)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, paragraph, from), new TextPosition(StoryRef.Body, paragraph, to)));
        h.View.Focus();
        return session;
    }

    private async Task<ViewHarness> OpenAsync() => await PageViewObjectTests.OpenAsync(ObjectDocx.Write(_folder));

    [AvaloniaFact]
    public async Task AnImageOnlyClipboardInsertsAPictureAtTheCaret()
    {
        using var h = await OpenAsync();
        var clipboard = new FakeClipboard { Content = new ClipboardContent(string.Empty, null, Image: Png()) };
        h.View.EditClipboard = clipboard;
        var session = Select(h, P(h, 0), 6, 6);
        var events = 0;
        session.PicturesChanged += () => events++;
        var pictures = PictureCount(h);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal(pictures + 1, PictureCount(h));
        Assert.Equal(1, events);
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.Equal(7, session.Caret!.Value.Offset);
        Assert.Equal("Before￼ the picture", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task ASelectionIsReplacedByThePictureAsByTextInOneUndoStep()
    {
        using var h = await OpenAsync();
        h.View.EditClipboard = new FakeClipboard { Content = new ClipboardContent(string.Empty, null, Image: Png()) };
        var session = Select(h, P(h, 0), 0, 6);
        var pictures = PictureCount(h);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal("￼ the picture", TextOf(h, P(h, 0)));
        Assert.Equal(pictures + 1, PictureCount(h));
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Undo));
        Assert.Equal("Before the picture", TextOf(h, P(h, 0)));
        Assert.Equal(pictures, PictureCount(h));
    }


    [AvaloniaFact]
    public async Task APasteThatLeavesPicturesOutSaysSoAndChangesNothingWhenNothingElseIsLeft()
    {
        using var h = await OpenAsync();
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;

        // The picture itself is copied by the editor (the fragment holds it) and the paste flattens it away: the user is told.
        Select(h, P(h, 1), 0, 1);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Copy));
        Assert.NotNull(clipboard.Content?.Fragment);
        var session = Select(h, P(h, 0), 0, 0);
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;
        var pictures = PictureCount(h);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal([WriteStrings.NotePasteLeftOutPictures], notes);
        Assert.Equal(pictures, PictureCount(h));
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);

        // Text with a picture: the text goes in as one step, and the note says what was left out.
        notes.Clear();
        Select(h, P(h, 0), 0, 0);
        clipboard.Content = session.Editor.Copy(new TextRange(new TextPosition(StoryRef.Body, P(h, 1), 0), new TextPosition(StoryRef.Body, P(h, 2), 5)));
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        Assert.Equal([WriteStrings.NotePasteLeftOutPictures], notes);
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.Equal(pictures, PictureCount(h));

        // A paste without any picture says nothing.
        notes.Clear();
        clipboard.Content = new ClipboardContent("plain", null);
        Select(h, P(h, 0), 0, 0);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        Assert.Empty(notes);
    }

    [AvaloniaFact]
    public async Task CopyAndCutWithAPictureSelectedSayThatAPictureIsNotCopiedAndLeaveTheClipboardAlone()
    {
        using var h = await OpenAsync();
        var clipboard = new FakeClipboard { Content = new ClipboardContent("kept", null) };
        h.View.EditClipboard = clipboard;
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var session = h.View.Tab.EnsureEditor();
        var picture = session.Editor.PictureAt(new TextPosition(StoryRef.Body, P(h, 1), 0));
        Assert.True(session.SetObjectSelection(new PictureSelection(picture!)));
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Copy));
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Cut));

        Assert.Equal([WriteStrings.NotePictureNotCopied, WriteStrings.NotePictureNotCopied], notes);
        Assert.Equal("kept", clipboard.Content!.PlainText);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(1, PictureCount(h));
        Assert.IsType<PictureSelection>(session.ObjectSelection);
    }
    [AvaloniaFact]
    public async Task AnImageWithTextPastesTheText()
    {
        using var h = await OpenAsync();
        h.View.EditClipboard = new FakeClipboard { Content = new ClipboardContent("TEXT", null, Image: Png()) };
        Select(h, P(h, 0), 0, 0);
        var pictures = PictureCount(h);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal("TEXTBefore the picture", TextOf(h, P(h, 0)));
        Assert.Equal(pictures, PictureCount(h));
        Assert.Empty(h.View.Tab.Docx.Model.Media.Entries);
    }

    [AvaloniaFact]
    public async Task OwnFragmentWinsOverAnImage()
    {
        using var h = await OpenAsync();
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        Select(h, P(h, 0), 0, 6);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Copy));
        Assert.NotNull(clipboard.Content!.Fragment);
        clipboard.Content = clipboard.Content with { PlainText = string.Empty, Image = Png() };
        Select(h, P(h, 2), 0, 0);
        var pictures = PictureCount(h);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal("BeforeAfter the picture", TextOf(h, P(h, 2)));
        Assert.Equal(pictures, PictureCount(h));
        Assert.Empty(h.View.Tab.Docx.Model.Media.Entries);
    }

    [AvaloniaFact]
    public async Task AZeroByteOrTruncatedImagePastesNothingAndSaysWhy()
    {
        using var h = await OpenAsync();
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        var session = Select(h, P(h, 0), 0, 6);
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        var selection = session.Selection;

        foreach (var bytes in new[] { Array.Empty<byte>(), Png()[..12] })
        {
            clipboard.Content = new ClipboardContent(string.Empty, null, Image: bytes);
            Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        }

        Assert.Equal([PictureWording.ReasonText(PictureRefusal.NotAnImage), PictureWording.ReasonText(PictureRefusal.NotAnImage)], notes);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal("Before the picture", TextOf(h, P(h, 0)));
        Assert.Equal(selection, session.Selection);
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Empty(h.View.Tab.Docx.Model.Media.Entries);
    }

    [AvaloniaFact]
    public async Task AnImageInATableCellIsInsertedInline()
    {
        using var h = await OpenAsync();
        h.View.EditClipboard = new FakeClipboard { Content = new ClipboardContent(string.Empty, null, Image: Png(600, 300)) };
        var model = h.View.Tab.Docx.Model;
        var table = model.Body.Blocks.OfType<Table>().Single();
        System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        table.Properties.Set(w + "tblLayout", new System.Xml.Linq.XElement(w + "tblLayout", new System.Xml.Linq.XAttribute(w + "type", "fixed")));
        var cell = (TableCell)((TableRow)table.Rows[0]).Items[0];
        var paragraph = cell.Blocks.OfType<Paragraph>().Single();
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, paragraph, 1), new TextPosition(StoryRef.Body, paragraph, 1)));

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        var picture = paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Single();
        Assert.Equal(PictureWrap.Inline, PictureView.Of(picture)!.Wrap);
        var info = PictureView.Of(picture)!;

        // Capped to the content of the narrow fixed-layout cell (2400 twips of grid minus 108 each side), aspect kept (600 x 300 pixels).
        Assert.Equal(2184 * 635L, info.ExtentEmu.Width);
        Assert.Equal(2184 * 635L / 2, info.ExtentEmu.Height);
    }

    [AvaloniaFact]
    public async Task AnAsyncPictureReadIsDroppedWhenTheTabIsClosedMeanwhile()
    {
        using var h = await OpenAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.View.EditClipboard = new FakeClipboard { Gate = gate, Content = new ClipboardContent(string.Empty, null, Image: Png()) };
        Select(h, P(h, 0), 0, 0);
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;

        var paste = h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.False(paste.IsCompleted);
        h.Tab.Dispose();
        ViewHarness.Pump();
        gate.SetResult();
        await paste.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(revision, model.Edits.Revision);
        Assert.Empty(model.Media.Entries);
    }

    [AvaloniaFact]
    public async Task APictureIsNotPastedIntoADocumentThatHasNotFullyOpened()
    {
        var path = Path.Combine(_folder, "long.docx");
        File.Copy(StyledDocx.Write(_folder, "long-source.docx", StyledDocx.Styles, Enumerable.Range(0, 120).Select(i => "<w:r><w:t>paragraph " + i + "</w:t></w:r>").ToArray()), path, overwrite: true);
        using var h = new ViewHarness(path, 900, 700);
        Assert.True(h.Tab.Docx.IsPrefix);
        h.View.EditClipboard = new FakeClipboard { Content = new ClipboardContent(string.Empty, null, Image: Png()) };
        var notes = new List<string>();
        var refused = new List<EditBlockedReason>();
        h.View.StructureNote += notes.Add;
        h.View.InputRefused += refused.Add;
        var revision = h.Tab.Docx.Model.Edits.Revision;

        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);

        // The paste is refused as every edit is while the file is still opening (the hint of the window says so); no picture note is made for it.
        Assert.Equal([EditBlockedReason.StillOpening], refused);
        Assert.Empty(notes);
        Assert.Equal(revision, h.Tab.Docx.Model.Edits.Revision);
        Assert.Empty(h.Tab.Docx.Model.Media.Entries);
    }

    // ---- the reader of the system clipboard -----------------------------------------------------------------------------------

    private static DataTransfer ImageTransfer(byte[] png, string? text = null, byte[]? fragment = null)
    {
        var item = new DataTransferItem();
        item.Set(AvaloniaEditClipboard.Png, png);
        if (text is not null)
        {
            item.SetText(text);
        }

        if (fragment is not null)
        {
            item.Set(AvaloniaEditClipboard.Fragment, fragment);
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    [AvaloniaFact]
    public async Task TheSystemClipboardGivesAPngAsItIsAndOnlyWhenThereIsNoText()
    {
        using var h = await OpenAsync();
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(h.View)!;
        var clipboard = new AvaloniaEditClipboard(() => topLevel.Clipboard);
        var png = Png(5, 7);

        await topLevel.Clipboard!.SetDataAsync(ImageTransfer(png));
        var read = await clipboard.ReadAsync();
        Assert.NotNull(read);
        Assert.Equal(string.Empty, read.PlainText);
        Assert.Equal(png, read.Image!.Value.ToArray());

        // With text beside the picture the text is taken and the picture is not even read.
        await topLevel.Clipboard.SetDataAsync(ImageTransfer(png, "words"));
        var withText = await clipboard.ReadAsync();
        Assert.Equal("words", withText!.PlainText);
        Assert.Null(withText.Image);

        // A zero-byte PNG comes through as an empty image (a paste refuses it with its reason), nothing at all as nothing.
        await topLevel.Clipboard.SetDataAsync(ImageTransfer([]));
        Assert.Equal(0, (await clipboard.ReadAsync())!.Image!.Value.Length);
        await topLevel.Clipboard.ClearAsync();
        Assert.Null(await clipboard.ReadAsync());
    }

    [AvaloniaFact]
    public async Task ASystemBitmapIsConvertedToPngOffTheUiThread()
    {
        using var h = await OpenAsync();
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(h.View)!;
        var clipboard = new AvaloniaEditClipboard(() => topLevel.Clipboard);
        using var source = new MemoryStream(Png(9, 4));
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(source);
        var item = new DataTransferItem();
        item.SetBitmap(bitmap);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await topLevel.Clipboard!.SetDataAsync(transfer);

        var read = await clipboard.ReadAsync();

        Assert.NotNull(read?.Image);
        var info = ImageHeaderProbe.Read(read.Image.Value.Span);
        Assert.NotNull(info);
        Assert.Equal(9, info.Value.Width);
        Assert.Equal(4, info.Value.Height);
        Assert.Equal("image/png", info.Value.ContentType);
    }

    [AvaloniaFact]
    public async Task AnImageThatCouldNotBeHandedOverShowsItsReasonAndPastesNothing()
    {
        using var h = await OpenAsync();
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        var session = Select(h, P(h, 0), 0, 6);
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        var selection = session.Selection;

        foreach (var refusal in new[] { PictureRefusal.TooManyPixels, PictureRefusal.NotAnImage })
        {
            clipboard.Content = new ClipboardContent(string.Empty, null, ImageRefusal: refusal);
            Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        }

        Assert.Equal([PictureWording.ReasonText(PictureRefusal.TooManyPixels), PictureWording.ReasonText(PictureRefusal.NotAnImage)], notes);
        Assert.NotEqual(notes[0], notes[1]);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(selection, session.Selection);

        // Text beside the refused picture is pasted as text and says nothing.
        clipboard.Content = new ClipboardContent("words", null, ImageRefusal: PictureRefusal.TooManyPixels);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        Assert.Equal(2, notes.Count);
        Assert.Equal("words the picture", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task ASystemBitmapOverThePixelCapComesBackWithTheTooManyPixelsReason()
    {
        using var h = await OpenAsync();
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(h.View)!;
        var clipboard = new AvaloniaEditClipboard(() => topLevel.Clipboard);
        using var huge = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(6000, 5400), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        var item = new DataTransferItem();
        item.SetBitmap(huge);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await topLevel.Clipboard!.SetDataAsync(transfer);

        var read = await clipboard.ReadAsync();

        Assert.NotNull(read);
        Assert.Null(read.Image);
        Assert.Equal(PictureRefusal.TooManyPixels, read.ImageRefusal);
    }

    [AvaloniaFact]
    public async Task ABitmapThatCannotBeConvertedComesBackWithTheNotAnImageReason()
    {
        using var h = await OpenAsync();
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(h.View)!;
        var clipboard = new AvaloniaEditClipboard(() => topLevel.Clipboard);
        using var source = new MemoryStream(Png(9, 4));
        var bitmap = new Avalonia.Media.Imaging.Bitmap(source);
        var item = new DataTransferItem();
        item.SetBitmap(bitmap);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await topLevel.Clipboard!.SetDataAsync(transfer);
        bitmap.Dispose();

        var read = await clipboard.ReadAsync();

        Assert.NotNull(read);
        Assert.Null(read.Image);
        Assert.Equal(PictureRefusal.NotAnImage, read.ImageRefusal);
        Assert.Equal(string.Empty, read.PlainText);

        // Nothing offered at all stays nothing.
        await topLevel.Clipboard.ClearAsync();
        Assert.Null(await clipboard.ReadAsync());
    }

    [AvaloniaFact]
    public void NoMessageHoldsBytes()
    {
        // The wording of every refusal a paste can show is fixed text: it names no file, no bytes and no description.
        foreach (var refusal in Enum.GetValues<PictureRefusal>())
        {
            var text = PictureWording.ReasonText(refusal);
            Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
        }

        var secret = new byte[] { 0x53, 0x45, 0x43, 0x52, 0x45, 0x54 };
        var content = new ClipboardContent(string.Empty, null, Image: secret);
        Assert.DoesNotContain("SECRET", content.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(string.Join(',', secret), content.ToString(), StringComparison.Ordinal);
    }
}
