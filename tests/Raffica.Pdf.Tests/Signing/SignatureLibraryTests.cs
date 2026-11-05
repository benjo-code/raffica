using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Signatures;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Signing;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignatureLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raffica-siglib-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public async Task LibraryDoesNotTouchTheFolderBeforeTheFirstUse()
    {
        var asked = 0;
        var library = new SignatureLibrary(() =>
        {
            asked++;
            return _root;
        });

        Assert.Equal(0, asked);
        Assert.False(Directory.Exists(_root));

        var items = await library.LoadAsync();

        Assert.Empty(items);
        Assert.False(Directory.Exists(_root)); // reading an absent library creates nothing
    }

    [Fact]
    public async Task AddRemoveAndReloadKeepOrderAndLimit()
    {
        var library = new SignatureLibrary(() => _root);
        var changes = 0;
        library.Changed += () => changes++;
        var first = await library.AddDrawnAsync("First", Stroke(), DrawColor.Blue);
        var second = await library.AddImageAsync("Second", Cutout());
        var third = await library.AddDrawnAsync("Third", Stroke(), DrawColor.Black);

        await library.RemoveAsync(second.Id);

        var reloaded = await new SignatureLibrary(() => _root).LoadAsync();
        Assert.Equal([first.Id, third.Id], reloaded.Select(s => s.Id));
        Assert.Equal(["First", "Third"], reloaded.Select(s => s.Name));
        Assert.Equal(DrawColor.Blue, reloaded[0].Color);
        Assert.Equal(4, changes);
        Assert.False(File.Exists(Path.Combine(_root, second.Id + ".png")));
    }

    [Fact]
    public async Task EleventhSignatureThrowsTheLimitException()
    {
        var library = new SignatureLibrary(() => _root);
        for (var i = 0; i < SignatureLimits.MaxSignatures; i++)
        {
            await library.AddDrawnAsync("S" + i, Stroke(), DrawColor.Red);
        }

        var changes = 0;
        library.Changed += () => changes++;
        await Assert.ThrowsAsync<SignatureLimitException>(() => library.AddDrawnAsync("Eleven", Stroke(), DrawColor.Red));
        await Assert.ThrowsAsync<SignatureLimitException>(() => library.AddImageAsync("Eleven", Cutout()));

        Assert.Equal(0, changes);
        Assert.Equal(SignatureLimits.MaxSignatures, (await library.LoadAsync()).Count);
    }

    [Fact]
    public async Task CorruptIndexIsMovedAsideAndTheLibraryStaysUsable()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "signatures.json"), "{ this is not json", TestContext.Current.CancellationToken);
        var library = new SignatureLibrary(() => _root);

        Assert.Empty(await library.LoadAsync());
        Assert.True(File.Exists(Path.Combine(_root, "signatures.json.bad")));

        var added = await library.AddDrawnAsync("After", Stroke(), DrawColor.Green);

        Assert.Equal([added.Id], (await new SignatureLibrary(() => _root).LoadAsync()).Select(s => s.Id));
    }

    [Fact]
    public async Task NamesAreRepairedAndLimitedTo60()
    {
        var library = new SignatureLibrary(() => _root);

        var long_ = await library.AddDrawnAsync(new string('x', 200), Stroke(), DrawColor.Red);
        var blank = await library.AddDrawnAsync("  \t ", Stroke(), DrawColor.Red);
        var control = await library.AddImageAsync("A\u0007B\nC", Cutout());

        Assert.Equal(60, long_.Name.Length);
        Assert.Equal("Signature 1", blank.Name);
        Assert.Equal("ABC", control.Name);
    }

    [Fact]
    public async Task FolderIsTheOneNextToTheSettingsFile()
    {
        var settings = Path.Combine(_root, "settings.json");
        var library = new SignatureLibrary(() => PdfSettingsFiles.SignaturesDirectoryFor(settings));

        Assert.Empty(await library.LoadAsync());
        Assert.False(Directory.Exists(_root));

        var saved = await library.AddDrawnAsync("Mine", Stroke(), DrawColor.Red);

        Assert.True(File.Exists(Path.Combine(_root, "signatures", "signatures.json")));
        Assert.Equal([saved.Id], (await library.LoadAsync()).Select(s => s.Id));
    }

    [AvaloniaFact]
    public async Task ReadImageGivesBackTheStoredPixels()
    {
        var library = new SignatureLibrary(() => _root);
        var saved = await library.AddImageAsync("Ink", Cutout());

        var image = await library.ReadImageAsync(saved.Id);

        var expected = Cutout();
        Assert.Equal((expected.Width, expected.Height), (image.Width, image.Height));
        Assert.Equal(255, image.Bgra[((2 * image.Width) + 2) * 4 + 3]); // the ink pixel
        Assert.Equal(0, image.Bgra[3]); // the transparent paper
        await Assert.ThrowsAsync<FileNotFoundException>(() => library.ReadImageAsync(new string('0', 32)));
    }

    [AvaloniaFact]
    public async Task ChangedIsRaisedOnTheUiThread()
    {
        var library = new SignatureLibrary(() => _root);
        var threads = new List<bool>();
        library.Changed += () => threads.Add(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());

        var first = await library.AddDrawnAsync("A", Stroke(), DrawColor.Red);
        await library.AddImageAsync("B", Cutout());
        await library.RemoveAsync(first.Id);

        Assert.Equal([true, true, true], threads);
    }

    [Fact]
    public async Task ThrowingChangedHandlerDoesNotFailTheSave()
    {
        var library = new SignatureLibrary(() => _root);
        library.Changed += () => throw new InvalidOperationException("handler bug");

        var saved = await library.AddDrawnAsync("Kept", Stroke(), DrawColor.Red);
        await library.RemoveAsync("0123456789abcdef0123456789abcdef");

        Assert.Equal([saved.Id], (await new SignatureLibrary(() => _root).LoadAsync()).Select(s => s.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstCallNeverReadsTheFolderOnTheCallingThread(bool image)
    {
        var caller = -1;
        var calledOn = -1;
        var library = new SignatureLibrary(() =>
        {
            calledOn = Environment.CurrentManagedThreadId;
            return _root;
        });

        // A dedicated thread is the caller: a pool thread could run the queued work itself once the call returns to it.
        await Task.Factory.StartNew(
            () =>
            {
                caller = Environment.CurrentManagedThreadId;
                return image ? (Task)library.AddImageAsync("First", Cutout()) : library.AddDrawnAsync("First", Stroke(), DrawColor.Red);
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        Assert.NotEqual(-1, calledOn);
        Assert.NotEqual(caller, calledOn);
    }

    [Fact]
    public async Task TwoConcurrentFirstCallsLoadOnce()
    {
        var loads = 0;
        var library = new SignatureLibrary(() =>
        {
            Interlocked.Increment(ref loads);
            Thread.Sleep(150);
            return _root;
        });

        var both = await Task.WhenAll(
            Task.Run(() => library.AddDrawnAsync("One", Stroke(), DrawColor.Red), TestContext.Current.CancellationToken),
            Task.Run(() => library.AddImageAsync("Two", Cutout()), TestContext.Current.CancellationToken));

        Assert.Equal(1, loads);
        Assert.Equal(2, (await new SignatureLibrary(() => _root).LoadAsync()).Count);
        Assert.NotEqual(both[0].Id, both[1].Id);
    }

    private static IReadOnlyList<IReadOnlyList<DisplayPoint>> Stroke() =>
        [[new DisplayPoint(0, 0), new DisplayPoint(40, 10), new DisplayPoint(80, 0)]];

    /// <summary>A 6 x 4 image: transparent paper with one black ink pixel at (2, 2).</summary>
    private static SignatureImage Cutout()
    {
        var bgra = new byte[6 * 4 * 4];
        var ink = ((2 * 6) + 2) * 4;
        bgra[ink + 3] = 255;
        return new SignatureImage(6, 4, bgra);
    }
}
