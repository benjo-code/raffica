using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Signatures;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Signing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignaturePlacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raffica-sigplace-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private SignatureLibrary NewLibrary() => new(() => _root);

    private static Task<SavedSignature> DrawnAsync(SignatureLibrary library, DrawColor color = DrawColor.Blue) =>
        library.AddDrawnAsync("Drawn", [[new DisplayPoint(0, 0.6), new DisplayPoint(0.3, 0.1), new DisplayPoint(0.6, 0.9), new DisplayPoint(1, 0.4)]], color);

    private static SignatureImage Cutout(int width = 60, int height = 30, bool noise = false)
    {
        var bgra = new byte[width * height * 4];
        var random = new Random(7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                var ink = noise || (x > width / 5 && x < width * 4 / 5 && y > height / 4 && y < height * 3 / 4);
                if (ink)
                {
                    bgra[i] = noise ? (byte)random.Next(256) : (byte)20;
                    bgra[i + 1] = noise ? (byte)random.Next(256) : (byte)20;
                    bgra[i + 2] = noise ? (byte)random.Next(256) : (byte)20;
                    bgra[i + 3] = noise ? (byte)random.Next(40, 256) : (byte)255;
                }
            }
        }

        return new SignatureImage(width, height, bgra);
    }

    private static Task<SavedSignature> ImageAsync(SignatureLibrary library) => library.AddImageAsync("Image", Cutout());

    private static SignaturePlacer PlacerOf(AnnotateFixture f, SignatureLibrary library) => new(library, f.Tools, new DialogQueue(f.Host));

    private static async Task SettleAsync(AnnotateFixture f)
    {
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await f.Interaction.Select.Completion;
            await f.SettleAsync();
        }
    }

    private static async Task<IReadOnlyList<PdfAnnotation>> SignaturesAsync(AnnotateFixture f, int page = 0) =>
        (await f.Tab.Session.Document!.GetAnnotationsAsync(page, Cancel)).Where(a => a.Kind == AnnotationKind.Signature).ToList();

    private static Rect VisiblePart(AnnotateFixture f) =>
        f.View.PageBounds(f.View.CurrentPage)!.Value.Intersect(new Rect(f.View.Bounds.Size));

    private static async Task<PdfAnnotation> PlaceOneAsync(AnnotateFixture f, SignaturePlacer placer, SavedSignature signature)
    {
        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, signature));
        await SettleAsync(f);
        return Assert.Single(await SignaturesAsync(f));
    }

    private static KeyEventArgs KeyOf(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };

    private static Point CentreInView(AnnotateFixture f, PdfAnnotation mark)
    {
        var b = mark.Bounds;
        return f.View.ToView(mark.Page, (b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2)!.Value;
    }

    [AvaloniaTheory]
    [InlineData(SavedSignatureKind.Drawn)]
    [InlineData(SavedSignatureKind.Image)]
    public async Task PickPlacesInTheCentreOfTheVisiblePartAndSelectsIt(SavedSignatureKind kind)
    {
        var library = NewLibrary();
        var signature = kind == SavedSignatureKind.Drawn ? await DrawnAsync(library) : await ImageAsync(library);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf", 800, 900, zoom: 2.5);
        f.View.Offset = new Vector(300, 400);
        ViewFixture.Pump();
        var visible = VisiblePart(f);
        var pageCentre = f.View.PageBounds(f.View.CurrentPage)!.Value.Center;
        Assert.True(Math.Abs(visible.Center.X - pageCentre.X) > 50 || Math.Abs(visible.Center.Y - pageCentre.Y) > 50, "the test must scroll so that the visible part is not the page");

        var placed = await PlaceOneAsync(f, PlacerOf(f, library), signature);

        // The centre of the box is the centre of the visible part of the page, found through the same mapping the page view uses.
        var shown = CentreInView(f, placed);
        Assert.Equal(visible.Center.X, shown.X, 1.5);
        Assert.Equal(visible.Center.Y, shown.Y, 1.5);
        Assert.Equal(AnnotateTool.Select, f.Tools.Tool);
        var selected = Assert.IsType<PdfAnnotation>(f.Interaction.Select.Selected);
        Assert.Equal((placed.Name, placed.Index, AnnotationKind.Signature), (selected.Name, selected.Index, selected.Kind));
        Assert.Equal(kind == SavedSignatureKind.Image, placed.IsSignatureImage);
        Assert.Equal(signature.Aspect, placed.Bounds.Width / placed.Bounds.Height, 0.05);
    }

    [AvaloniaFact]
    public async Task PlacingKeepsAnAlreadyChosenSelectTool()
    {
        var library = NewLibrary();
        var signature = await DrawnAsync(library);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Select);
        await SettleAsync(f);

        await PlaceOneAsync(f, PlacerOf(f, library), signature);

        Assert.Equal(AnnotateTool.Select, f.Tools.Tool);
        Assert.NotNull(f.Interaction.Select.Selected);
    }

    [AvaloniaTheory]
    [InlineData(SavedSignatureKind.Drawn)]
    [InlineData(SavedSignatureKind.Image)]
    public async Task PlacedSignatureIsAnnotationKindSignatureAndOneUndoStep(SavedSignatureKind kind)
    {
        var library = NewLibrary();
        var signature = kind == SavedSignatureKind.Drawn ? await DrawnAsync(library, DrawColor.Green) : await ImageAsync(library);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");

        var placed = await PlaceOneAsync(f, PlacerOf(f, library), signature);

        Assert.Equal(AnnotationKind.Signature, placed.Kind);
        if (kind == SavedSignatureKind.Drawn)
        {
            Assert.Equal(DrawPalette.ColorOf(DrawColor.Green), placed.Color);
        }

        Assert.True(f.Tab.IsDirty);
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Empty(await SignaturesAsync(f));
        Assert.False(f.Tab.Annotations.CanUndo);
        await f.Tab.Annotations.RedoAsync();
        await SettleAsync(f);
        Assert.Single(await SignaturesAsync(f));
    }

    [AvaloniaTheory]
    [InlineData(SavedSignatureKind.Drawn)]
    [InlineData(SavedSignatureKind.Image)]
    public async Task ArrowKeysMoveAndHandlesScaleWithAFixedAspect(SavedSignatureKind kind)
    {
        var library = NewLibrary();
        var signature = kind == SavedSignatureKind.Drawn ? await DrawnAsync(library) : await ImageAsync(library);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var before = await PlaceOneAsync(f, PlacerOf(f, library), signature);

        Assert.True(f.Interaction.HandleKey(KeyOf(Key.Right)));
        f.Time.Advance(500);
        await SettleAsync(f);
        var moved = Assert.Single(await SignaturesAsync(f));
        Assert.Equal(before.Bounds.Left + 1, moved.Bounds.Left, 0.5);
        Assert.Equal(before.Bounds.Top, moved.Bounds.Top, 0.5);

        // The bottom right handle dragged away from the centre: bigger, with the aspect ratio of the signature.
        var corner = f.View.ToView(0, moved.Bounds.Right, moved.Bounds.Bottom)!.Value;
        f.Window.MouseDown(corner, MouseButton.Left);
        f.Window.MouseMove(corner + new Vector(20, 2));
        f.Window.MouseMove(corner + new Vector(60, 4));
        f.Window.MouseUp(corner + new Vector(60, 4), MouseButton.Left);
        ViewFixture.Pump();
        await SettleAsync(f);
        var grown = Assert.Single(await SignaturesAsync(f));
        Assert.True(grown.Bounds.Width > moved.Bounds.Width + 5, "the handle must have scaled the signature");
        Assert.Equal(moved.Bounds.Width / moved.Bounds.Height, grown.Bounds.Width / grown.Bounds.Height, 0.05);
    }

    [AvaloniaFact]
    public async Task DrawnSignatureKeepsItsColourChangeable()
    {
        var library = NewLibrary();
        var signature = await DrawnAsync(library, DrawColor.Blue);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        using var strip = new StyleStrip(f.Tools, f.Interaction.Select);
        await PlaceOneAsync(f, PlacerOf(f, library), signature);

        Assert.True(strip.IsVisible);
        Assert.True(strip.ActsOnSelection);
        Assert.Null(strip.DisabledReason);
        Assert.NotEmpty(strip.Swatches);
        var red = strip.Swatches.First(s => strip.ColorOf(s) == DrawPalette.ColorOf(DrawColor.Red));
        red.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));
        await SettleAsync(f);

        var recoloured = Assert.Single(await SignaturesAsync(f));
        Assert.Equal(AnnotationKind.Signature, recoloured.Kind);
        Assert.Equal(DrawPalette.ColorOf(DrawColor.Red), recoloured.Color);
    }

    [AvaloniaFact]
    public async Task ImageSignatureOffersNoColourOrWidth()
    {
        var library = NewLibrary();
        var signature = await ImageAsync(library);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        using var strip = new StyleStrip(f.Tools, f.Interaction.Select);
        var placed = await PlaceOneAsync(f, PlacerOf(f, library), signature);

        Assert.False(strip.IsVisible);
        Assert.Empty(strip.Swatches);
        Assert.Empty(strip.WidthButtons);
        Assert.False(f.Interaction.Select.CanChangeSelectionStyle);

        // A direct request changes nothing either: no history step is added, the mark stays as placed.
        await f.Interaction.Select.SetSelectionStyleAsync(DrawPalette.ColorOf(DrawColor.Red), 4);
        await SettleAsync(f);
        var same = Assert.Single(await SignaturesAsync(f));
        Assert.True(same.IsSignatureImage);
        Assert.Equal(placed.Bounds, same.Bounds);
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Empty(await SignaturesAsync(f)); // the one undo step removed the placing itself
    }

    [AvaloniaTheory]
    [InlineData("rotated-page.pdf", 90, 100, 200)] // MediaBox 200 x 100, /Rotate 90: displayed 100 x 200
    [InlineData("cropbox.pdf", 0, 200, 150)] // MediaBox 300 x 300, CropBox [50 75 250 225]: displayed 200 x 150
    public async Task SignatureSurvivesSaveAndReopenOnARotatedAndACropBoxPage(string file, int rotation, double displayedWidth, double displayedHeight)
    {
        var library = NewLibrary();
        var drawn = await DrawnAsync(library);
        var image = await ImageAsync(library);
        using var f = await AnnotateFixture.OpenAsync(file, 600, 700);
        var placer = PlacerOf(f, library);
        var visible = VisiblePart(f);

        // The fixture really is rotated or cropped: the page is not shown as it is stored, so the test cannot pass on a plain page.
        Assert.Equal(rotation, await f.Tab.Session.Document!.GetPageRotationAsync(0, Cancel));
        var shownSize = f.View.SizeOfPage(0)!.Value;
        Assert.Equal(displayedWidth, shownSize.Width, 0.5);
        Assert.Equal(displayedHeight, shownSize.Height, 0.5);

        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, drawn));
        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, image));
        await SettleAsync(f);
        var before = await SignaturesAsync(f);
        Assert.Equal(2, before.Count);
        var size = f.View.SizeOfPage(0)!.Value;
        foreach (var mark in before)
        {
            // On the displayed page, at the centre of what is visible of it.
            var expectedAspect = mark.IsSignatureImage ? image.Aspect : drawn.Aspect;
            Assert.Equal(expectedAspect, mark.Bounds.Width / mark.Bounds.Height, expectedAspect * 0.05);
            Assert.True(mark.Bounds.Left >= -0.5 && mark.Bounds.Top >= -0.5 && mark.Bounds.Right <= size.Width + 0.5 && mark.Bounds.Bottom <= size.Height + 0.5);
            var shown = CentreInView(f, mark);
            Assert.Equal(visible.Center.X, shown.X, 2);
            Assert.Equal(visible.Center.Y, shown.Y, 2);
        }

        var saved = await f.Tab.Annotations.Editor.SaveAsync(new PdfEngine(), f.Path!, false, null, Cancel);
        await using (saved.Saved.Reopened)
        {
            var after = (await saved.Saved.Reopened.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind == AnnotationKind.Signature).ToList();
            Assert.Equal(2, after.Count);
            foreach (var mark in before)
            {
                var twin = after.Single(a => a.IsSignatureImage == mark.IsSignatureImage);
                Assert.Equal(mark.Bounds.Left, twin.Bounds.Left, 0.5);
                Assert.Equal(mark.Bounds.Top, twin.Bounds.Top, 0.5);
                Assert.Equal(mark.Bounds.Right, twin.Bounds.Right, 0.5);
                Assert.Equal(mark.Bounds.Bottom, twin.Bounds.Bottom, 0.5);
            }
        }
    }

    [AvaloniaFact]
    public async Task PlacedTwiceIsTwoAnnotationsButOneStoredFile()
    {
        var library = NewLibrary();
        var signature = await ImageAsync(library);
        var filesBefore = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        var indexBefore = await File.ReadAllBytesAsync(Path.Combine(_root, "signatures.json"), Cancel);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var placer = PlacerOf(f, library);

        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, signature));
        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, signature));
        await SettleAsync(f);

        Assert.Equal(2, (await SignaturesAsync(f)).Count);
        Assert.Equal(filesBefore, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Equal(indexBefore, await File.ReadAllBytesAsync(Path.Combine(_root, "signatures.json"), Cancel));
        Assert.Equal(signature.Id, Assert.Single(await library.LoadAsync()).Id);
    }

    [AvaloniaFact]
    public async Task FullHistoryAsksToSaveFirst()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var library = NewLibrary();
        var big = await library.AddImageAsync("Big", Cutout(1200, 600, noise: true));
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");

        // Fill the history with images until it refuses one.
        var placed = 0;
        for (; placed < 40; placed++)
        {
            try
            {
                await f.Tab.Annotations.Editor.ApplySignatureImageAsync(0, new PageRect(20, 20, 220, 120), Cutout(1200, 600, noise: true), null);
            }
            catch (AnnotationHistoryFullException)
            {
                break;
            }
        }

        Assert.InRange(placed, 1, 39);
        await SettleAsync(f);
        var count = (await SignaturesAsync(f)).Count;
        var libraryBefore = await File.ReadAllBytesAsync(Path.Combine(_root, "signatures.json"), Cancel);
        f.Tools.End();

        var placing = PlacerOf(f, library).PlaceAsync(f.Tab, f.View, f.Interaction.Select, big);
        await ViewFixture.WaitAsync(() => f.Host.IsOpen || placing.IsCompleted);
        Assert.True(f.Host.IsOpen, "a card must say why nothing was placed");
        var card = f.Host.ActiveDialog!;
        var texts = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(PdfStrings.SignaturePlaceHistoryFull, texts);
        card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.False(await placing);
        await SettleAsync(f);
        Assert.Equal(count, (await SignaturesAsync(f)).Count);
        Assert.Equal(AnnotateTool.None, f.Tools.Tool);
        Assert.Equal(libraryBefore, await File.ReadAllBytesAsync(Path.Combine(_root, "signatures.json"), Cancel));
        Assert.Single(await library.LoadAsync());
    }

    [AvaloniaFact]
    public async Task AFailingSelectAfterThePlacementLeavesThePlacementStanding()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var library = NewLibrary();
        var signature = await DrawnAsync(library);
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var placer = PlacerOf(f, library);
        placer.SelectStep = (_, _) => throw new InvalidOperationException("select failed");

        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, signature));
        await SettleAsync(f);

        // The signature exists and is one history step; the selection was only a courtesy, so there is no card and no failure.
        Assert.Single(await SignaturesAsync(f));
        Assert.False(f.Host.IsOpen);
        Assert.True(f.Tab.Annotations.CanUndo);
        Assert.Equal(AnnotateTool.Select, f.Tools.Tool);
    }

    [AvaloniaFact]
    public async Task AnUnreadableStoredImageGivesTheFailureCardAndChangesNothing()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var library = NewLibrary();
        var signature = await ImageAsync(library);
        foreach (var png in Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories))
        {
            await File.WriteAllBytesAsync(png, [1, 2, 3], Cancel);
        }

        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var placing = PlacerOf(f, library).PlaceAsync(f.Tab, f.View, f.Interaction.Select, signature);
        await ViewFixture.WaitAsync(() => f.Host.IsOpen || placing.IsCompleted);
        var card = Assert.IsType<Dialog>(f.Host.ActiveDialog);
        Assert.Contains(PdfStrings.SignaturePlaceFailed, card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain(_root, string.Join("\n", card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)), StringComparison.OrdinalIgnoreCase);
        card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.False(await placing);
        await SettleAsync(f);
        Assert.Empty(await SignaturesAsync(f));
        Assert.False(f.Tab.IsDirty);
        Assert.Single(await library.LoadAsync());
    }
}
