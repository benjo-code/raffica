using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Ocr;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

/// <summary>Engines that answer from a function and can be held back, so a test decides when a page of a run is done (no native library).</summary>
internal sealed class FakeEngineFactory(Func<int, OcrRecognition>? behavior = null) : IOcrEngineFactory
{
    /// <summary>The word every page of the default behaviour has; a test looks for it where it must not be.</summary>
    public const string Secret = "Zebraquokka";

    private int _calls;

    /// <summary>What a call answers (by its index over the whole run); null: one word, <see cref="Secret"/>. A test may change it between runs.</summary>
    public Func<int, OcrRecognition>? Behavior { get; set; } = behavior;

    /// <summary>Calls from this index on wait for <see cref="Release"/> (-1: none waits).</summary>
    public int HoldFrom { get; set; } = -1;

    public ManualResetEventSlim Gate { get; } = new(initialState: true);

    /// <summary>Gets or sets an exception <see cref="Create"/> throws (a missing library or language file).</summary>
    public Exception? CreateFailure { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public static OcrRecognition One() => new([new OcrWord(Secret, 100, 100, 300, 150, 90)]);

    public IOcrEngine Create(string languages) => CreateFailure is { } failure ? throw failure : new Engine(this);

    /// <summary>Holds back every call from index <paramref name="index"/> on.</summary>
    public void HoldAt(int index)
    {
        HoldFrom = index;
        Gate.Reset();
    }

    public void Release() => Gate.Set();

    private sealed class Engine(FakeEngineFactory owner) : IOcrEngine
    {
        public OcrRecognition Recognize(ReadOnlySpan<byte> gray, int width, int height, int dpi, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref owner._calls) - 1;
            if (owner.HoldFrom >= 0 && index >= owner.HoldFrom)
            {
                owner.Gate.Wait(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return (owner.Behavior ?? (_ => One()))(index);
        }

        public void Dispose()
        {
        }
    }

}

/// <summary>A window with a copy of a test file open (the tests write only to their own folder), the fake engine in place and the library probe answering yes.</summary>
internal sealed class OcrRig : IDisposable
{
    private OcrRig(WindowFixture fixture, PdfTab tab, string path, FakeEngineFactory? engines)
    {
        Fixture = fixture;
        Tab = tab;
        Path = path;
        Engines = engines;
    }

    public WindowFixture Fixture { get; }

    public PdfWindow Window => Fixture.Window;

    public PdfTab Tab { get; }

    public string Path { get; }

    public FakeEngineFactory? Engines { get; }

    /// <summary>Opens a copy of <paramref name="file"/>; <paramref name="fake"/> true puts a fake engine in place, else the real library is used (the test requires it).</summary>
    public static async Task<OcrRig> OpenAsync(string file = "ocr-scan.pdf", bool fake = true, bool realSave = false, TimeProvider? time = null, Func<int, OcrRecognition>? behavior = null)
    {
        if (!fake)
        {
            OcrTestLibrary.Require();
        }

        var fixture = WindowFixture.Create(realSave: realSave, time: time);
        var copy = System.IO.Path.Combine(fixture.Folder, "work-" + file);
        File.Copy(TestData.Pdf(file), copy);
        var engines = fake ? new FakeEngineFactory(behavior) : null;
        fixture.Window.OcrLibraryProbe = () => true;
        fixture.Window.ShippedTessdataDirectory = fake ? System.IO.Path.Combine(fixture.Folder, "no-shipped") : OcrTestLibrary.Tessdata;
        if (engines is not null)
        {
            fixture.Window.OcrEngineFactory = _ => engines;
        }

        var tab = await fixture.Window.Flow.OpenAsync(copy) ?? throw new InvalidOperationException(file + " did not open");
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return new OcrRig(fixture, tab, copy, engines);
    }

    public Dialog? Card => Window.Host.ActiveDialog;

    /// <summary>Waits for a card that has a button of that name.</summary>
    public async Task<Dialog> CardWithAsync(string button)
    {
        await PrintHost.UntilAsync(() => Card is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == button));
        return Card!;
    }

    public static void Click(Dialog card, string name) => card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Every text a card shows: the title, the blocks and the buttons.</summary>
    public static IReadOnlyList<string> TextsOf(Dialog card) =>
    [
        card.Title?.ToString() ?? string.Empty,
        .. card.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!),
        .. card.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string).Select(b => (string)b.Content!),
    ];

    /// <summary>Waits until the run has decided at least <paramref name="pages"/> pages.</summary>
    public Task UntilPagesAsync(int pages) => PrintHost.UntilAsync(() => Tab.OcrIfCreated?.Progress is { } p && p.PagesDone >= pages);

    /// <summary>The text of a page of the open document as PDFium reads it.</summary>
    public async Task<string> PageTextAsync(int page)
    {
        var text = await Tab.Session.Document!.GetPageTextAsync(page, TestContext.Current.CancellationToken);
        return text.GetText(0, text.Chars.Count);
    }

    public void Dispose() => Fixture.Dispose();
}
