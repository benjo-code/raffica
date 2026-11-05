using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Imaging;
using Raffica.Core.Signatures;
using Raffica.Pdf.Signing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

/// <summary>Fix round 1 of the new-signature card: cancelling during a save, a stream of a closed card, a refused pick, coalesced cutouts.</summary>
public sealed class SignatureCardFixTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raffica-sigfix-" + Guid.NewGuid().ToString("N"));

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

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Pump();
            await Task.Delay(10);
        }

        Pump();
        Assert.True(condition(), "The condition was not reached in time.");
    }

    private sealed class Shown(Window window, Dialog card, List<SavedSignature?> results) : IDisposable
    {
        public Dialog Card { get; } = card;

        public List<SavedSignature?> Results { get; } = results;

        public T Part<T>(string name)
            where T : Control => Card.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        public Button Button(string name) => Part<Button>(name);

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        public void ImageTab()
        {
            Part<SegmentedControl>("PART_Tabs").SelectedIndex = 1;
            Pump();
        }

        public void Escape()
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
        }

        public void Dispose() => window.Close();
    }

    private static Shown Show(SignatureLibrary library, Func<Task<Stream?>> pick)
    {
        var results = new List<SavedSignature?>();
        var card = NewSignatureCard.Create(library, pick, results.Add);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 900, Height = 760 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card, results);
    }

    private static byte[] Scan()
    {
        var bgra = new byte[100 * 80 * 4];
        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var v = x is >= 40 and < 60 && y is >= 30 and < 40 ? (byte)20 : x is >= 10 and < 22 && y is >= 10 and < 22 ? (byte)150 : (byte)240;
                var o = ((y * 100) + x) * 4;
                bgra[o] = bgra[o + 1] = bgra[o + 2] = v;
                bgra[o + 3] = 255;
            }
        }

        return PngEncoder.EncodeWithAlpha(new SignatureImage(100, 80, bgra));
    }

    private static int Visible(SignatureImage image)
    {
        var count = 0;
        for (var i = 3; i < image.Bgra.Length; i += 4)
        {
            if (image.Bgra[i] > 128)
            {
                count++;
            }
        }

        return count;
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [AvaloniaFact]
    public async Task CancelAndEscDuringASaveDoNothingAndTheSavedSignatureIsReportedOnce()
    {
        using var gate = new ManualResetEventSlim();
        var library = new SignatureLibrary(() =>
        {
            gate.Wait(TimeSpan.FromSeconds(20));
            return _root;
        });
        var png = Scan();
        using var s = Show(library, () => Task.FromResult<Stream?>(new MemoryStream(png)));
        try
        {
            s.ImageTab();
            s.Click("PART_Choose");
            await Until(() => s.Button("PART_Save").IsEnabled);

            s.Click("PART_Save"); // the library is still closed behind the gate: the add is pending
            Pump();
            Assert.False(s.Button("PART_Cancel").IsEnabled);
            s.Click("PART_Cancel");
            s.Escape();
            Assert.Empty(s.Results); // nothing closed, nothing reported
        }
        finally
        {
            gate.Set();
        }

        await Until(() => s.Results.Count == 1);
        await Task.Delay(100);
        Pump();
        Assert.Single(s.Results);
        Assert.NotNull(s.Results[0]);
        Assert.Equal(s.Results[0]!.Id, Assert.Single(await new SignatureLibrary(() => _root).LoadAsync()).Id);
    }

    [AvaloniaFact]
    public async Task AStreamPickedWhileTheCardClosedIsDisposed()
    {
        var pick = new TaskCompletionSource<Stream?>();
        using var s = Show(new SignatureLibrary(() => _root), () => pick.Task);
        s.ImageTab();
        s.Click("PART_Choose");
        s.Click("PART_Cancel");
        Assert.Single(s.Results);

        var stream = new TrackingStream(Scan());
        pick.SetResult(stream);

        await Until(() => stream.Disposed);
        Assert.True(stream.Disposed);
    }

    [AvaloniaFact]
    public async Task ARefusedPickClearsTheOldImageSoTheReasonMatchesWhatSaveWouldStore()
    {
        var files = new Queue<byte[]>([Scan(), [1, 2, 3, 4]]);
        using var s = Show(new SignatureLibrary(() => _root), () => Task.FromResult<Stream?>(new MemoryStream(files.Dequeue())));
        s.ImageTab();
        s.Click("PART_Choose");
        await Until(() => s.Button("PART_Save").IsEnabled);
        Assert.NotNull(s.Part<SignaturePreview>("PART_Preview").Image);

        s.Click("PART_Choose"); // refused

        await Until(() => s.Part<TextBlock>("PART_Message").IsEffectivelyVisible);
        Assert.Equal(PdfStrings.SignatureNewRefusalUnreadable, s.Part<TextBlock>("PART_Message").Text);
        Assert.Null(s.Part<SignaturePreview>("PART_Preview").Image);
        Assert.False(s.Button("PART_Save").IsEnabled);
        Assert.False(s.Part<Slider>("PART_Cutout").IsEnabled);
    }

    [AvaloniaFact]
    public async Task ManyFastSliderMovesEndInTheCutoutOfTheLastOne()
    {
        var png = Scan();
        using var s = Show(new SignatureLibrary(() => _root), () => Task.FromResult<Stream?>(new MemoryStream(png)));
        s.ImageTab();
        s.Click("PART_Choose");
        await Until(() => s.Button("PART_Save").IsEnabled);
        var slider = s.Part<Slider>("PART_Cutout");
        var preview = s.Part<SignaturePreview>("PART_Preview");

        foreach (var value in new[] { 90, 110, 130, 150, 170, 200 })
        {
            slider.Value = value;
        }

        slider.Value = 100;
        var source = Assert.IsType<SignatureImageResult.Ok>(await SignatureImageCodec.DecodeAsync(new MemoryStream(png))).Image;
        var expected = Visible(SignatureCutout.TrimToContent(SignatureCutout.Apply(source, 100)));
        await Until(() => Visible(preview.Image!) == expected);
        await Task.Delay(150);
        Pump();
        Assert.Equal(expected, Visible(preview.Image!));
    }

    [AvaloniaFact]
    public void ThePreviewKeepsItsBitmapForTheSameImageAndDropsItOnClear()
    {
        var preview = new SignaturePreview();
        var image = new SignatureImage(4, 4, new byte[64]);

        preview.ShowImage(image);
        preview.ShowImage(image);
        Assert.Equal(1, preview.BitmapBuilds);

        preview.ShowImage(null);
        preview.ShowImage(image);
        Assert.Equal(2, preview.BitmapBuilds);
    }
}
