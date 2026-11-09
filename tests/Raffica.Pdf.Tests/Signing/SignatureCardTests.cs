using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Imaging;
using Raffica.Core.Signatures;
using Raffica.Pdf.Signing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignatureCardTests : IDisposable
{
    private const string Secret = "secret-folder-name";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "raffica-sigcard-" + Guid.NewGuid().ToString("N"));

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

    private SignatureLibrary NewLibrary() => new(() => _root);

    private sealed class Shown(Window window, Dialog card, List<SavedSignature?> results) : IDisposable
    {
        public Window Window { get; } = window;

        public Dialog Card { get; } = card;

        public List<SavedSignature?> Results { get; } = results;

        public T Part<T>(string name)
            where T : Control => Card.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        public Button Button(string name) => Part<Button>(name);

        public SignaturePad Pad => Part<SignaturePad>("PART_Pad");

        public SignaturePreview Preview => Part<SignaturePreview>("PART_Preview");

        public SegmentedControl Tabs => Part<SegmentedControl>("PART_Tabs");

        public TextBlock Message => Part<TextBlock>("PART_Message");

        public string AllText => string.Join("\n", Card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        public void SelectTab(int index)
        {
            Tabs.SelectedIndex = index;
            Pump();
        }

        public void Draw(params Point[] points)
        {
            var pad = Pad;
            var origin = pad.TranslatePoint(default, Window)!.Value;
            Window.MouseDown(origin + points[0], MouseButton.Left);
            foreach (var p in points.Skip(1))
            {
                Window.MouseMove(origin + p);
            }

            Window.MouseUp(origin + points[^1], MouseButton.Left);
            Pump();
        }

        public void Escape()
        {
            Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
        }

        public void Dispose() => Window.Close();
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
        for (var i = 0; i < 400 && !condition(); i++)
        {
            Pump();
            await Task.Delay(10);
        }

        Pump();
        Assert.True(condition(), "The condition was not reached in time.");
    }

    private static Shown Show(SignatureLibrary library, Func<Task<Stream?>>? pick = null, string culture = "en")
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        Raffica.Shell.Localization.ShellStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var results = new List<SavedSignature?>();
        var card = NewSignatureCard.Create(library, pick ?? (() => Task.FromResult<Stream?>(null)), results.Add);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 900, Height = 760 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card, results);
    }

    private static Point[] Line(double x0, double y0, double x1, double y1, int steps = 8) =>
        [.. Enumerable.Range(0, steps + 1).Select(i => new Point(x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps)))];

    /// <summary>Paper of one brightness with a dark block and a mid-grey smudge: a scan.</summary>
    private static byte[] ScanPng(int width, int height, (int X, int Y, int W, int H) ink, (int X, int Y, int W, int H)? smudge = null, byte paper = 240)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = paper;
                if (x >= ink.X && x < ink.X + ink.W && y >= ink.Y && y < ink.Y + ink.H)
                {
                    value = 20;
                }
                else if (smudge is { } s && x >= s.X && x < s.X + s.W && y >= s.Y && y < s.Y + s.H)
                {
                    value = 150;
                }

                var o = ((y * width) + x) * 4;
                bgra[o] = bgra[o + 1] = bgra[o + 2] = value;
                bgra[o + 3] = 255;
            }
        }

        return PngEncoder.EncodeWithAlpha(new SignatureImage(width, height, bgra));
    }

    private static byte[] DefaultScan() => ScanPng(100, 80, (40, 30, 20, 10), (10, 10, 12, 12));

    private static Func<Task<Stream?>> Picker(params byte[][] files)
    {
        var queue = new Queue<byte[]>(files);
        return () => Task.FromResult<Stream?>(new MemoryStream(queue.Dequeue(), writable: false));
    }

    private static byte[] HugeHeader()
    {
        var head = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(head, 0);
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(8), 13);
        "IHDR"u8.CopyTo(head.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(16), SignatureLimits.MaxSourceSide + 1);
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(20), 10);
        head[24] = 8;
        head[25] = 6;
        return head;
    }

    private static async Task LoadImageAsync(Shown s)
    {
        s.SelectTab(1);
        s.Click("PART_Choose");
        await Until(() => s.Button("PART_Save").IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void HintIsVisibleInBothTabsWithoutScrolling(string culture)
    {
        using var s = Show(NewLibrary(), culture: culture);
        var info = CultureInfo.GetCultureInfo(culture);

        foreach (var tab in new[] { 0, 1 })
        {
            s.SelectTab(tab);
            var hint = s.Part<TextBlock>("PART_Hint");
            Assert.True(hint.IsEffectivelyVisible);
            Assert.Contains(PdfStrings.GetIn("SignatureHintWritten", info), hint.Text, StringComparison.Ordinal);
            Assert.Contains(PdfStrings.GetIn("SignatureHintImage", info), hint.Text, StringComparison.Ordinal);
            var bottom = hint.TranslatePoint(new Point(0, hint.Bounds.Height), s.Card)!.Value.Y;
            Assert.True(bottom <= s.Card.Bounds.Height, $"tab {tab}: the hint ends at {bottom}, the card at {s.Card.Bounds.Height}");
            Assert.True(s.Card.TranslatePoint(new Point(0, s.Card.Bounds.Height), s.Window)!.Value.Y <= s.Window.ClientSize.Height);
            // The name field has its own scroll viewer inside its template; the dialog's body scroll viewer (M2.7) has nothing to scroll here.
            var scrollers = s.Card.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.FindAncestorOfType<TextBox>() is null).ToList();
            Assert.All(scrollers, v => Assert.True(v.Name == "PART_BodyScroll" && v.Extent.Height <= v.Viewport.Height + 0.5, "a scroll viewer in the card that scrolls"));
        }

        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal("Replaces no handwritten signature where written form is required.", PdfStrings.SignatureHintWritten);
        Assert.Equal("This is an image of your signature, not a digital signature.", PdfStrings.SignatureHintImage);
    }

    [AvaloniaFact]
    public async Task DrawnSignatureIsStoredAsStrokesInTheUnitSquare()
    {
        var library = NewLibrary();
        using var s = Show(library);
        s.Draw(Line(30, 40, 230, 90));
        s.Draw(Line(30, 90, 230, 40));
        Assert.Equal("Signature 1", s.Part<TextBox>("PART_Name").Text);

        s.Click("PART_Save");
        await Until(() => s.Results.Count == 1);

        var saved = Assert.IsType<SavedSignature>(s.Results[0]);
        Assert.Equal(SavedSignatureKind.Drawn, saved.Kind);
        Assert.Equal(DrawColor.Black, saved.Color);
        Assert.Equal(2, saved.Strokes!.Count);
        var all = saved.Strokes.SelectMany(p => p).ToList();
        Assert.All(all, p => Assert.InRange(p.X, 0, 1));
        Assert.All(all, p => Assert.InRange(p.Y, 0, 1));
        Assert.Equal(0, all.Min(p => p.X), 6);
        Assert.Equal(1, all.Max(p => p.X), 6);
        Assert.Equal(0, all.Min(p => p.Y), 6);
        Assert.Equal(1, all.Max(p => p.Y), 6);
        Assert.Equal(200.0 / 50.0, saved.Aspect, 6);
        var stored = Assert.Single(await library.LoadAsync());
        Assert.Equal((saved.Id, saved.Name, saved.Kind, saved.Aspect, saved.Color), (stored.Id, stored.Name, stored.Kind, stored.Aspect, stored.Color));
        Assert.Equal(saved.Strokes!.SelectMany(p => p), stored.Strokes!.SelectMany(p => p));
    }

    [AvaloniaFact]
    public async Task TheChosenInkColourIsStoredAndTheNameIsTaken()
    {
        var library = NewLibrary();
        using var s = Show(library);
        s.Draw(Line(30, 40, 230, 90));
        s.Part<ToggleButton>("PART_Ink_" + DrawColor.Blue).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Pump();
        Assert.Equal(DrawColor.Blue, s.Pad.Color);
        s.Part<TextBox>("PART_Name").Text = "Letters";

        s.Click("PART_Save");
        await Until(() => s.Results.Count == 1);

        Assert.Equal((DrawColor.Blue, "Letters"), (s.Results[0]!.Color, s.Results[0]!.Name));
    }

    [AvaloniaFact]
    public async Task AStraightHorizontalLineIsPaddedNotRefused()
    {
        // Core pads a degenerate drawing to the extreme aspect (20), so a drawn signature never needs the aspect refusal.
        var library = NewLibrary();
        using var s = Show(library);
        s.Draw(Line(30, 60, 330, 60));

        s.Click("PART_Save");
        await Until(() => s.Results.Count == 1);

        Assert.Equal(20, s.Results[0]!.Aspect, 6);
        Assert.All(s.Results[0]!.Strokes!.SelectMany(p => p), p => Assert.InRange(p.Y, 0, 1));
    }

    [AvaloniaFact]
    public async Task SaveIsDisabledForAnEmptyPadAndForNoImage()
    {
        var library = NewLibrary();
        using var s = Show(library);
        Assert.False(s.Button("PART_Save").IsEnabled);
        s.Click("PART_Save");

        s.SelectTab(1);
        Assert.False(s.Button("PART_Save").IsEnabled);
        s.Click("PART_Save");

        s.SelectTab(0);
        s.Draw(Line(30, 40, 230, 90));
        Assert.True(s.Button("PART_Save").IsEnabled);
        s.SelectTab(1);
        Assert.False(s.Button("PART_Save").IsEnabled); // the drawing does not count in the Image tab

        Assert.Empty(s.Results);
        Assert.Empty(await library.LoadAsync());
    }

    [AvaloniaFact]
    public void ClearAndBackWork()
    {
        using var s = Show(NewLibrary());
        Assert.False(s.Button("PART_Back").IsEnabled);
        Assert.False(s.Button("PART_Clear").IsEnabled);
        s.Draw(Line(30, 40, 230, 40));
        s.Draw(Line(30, 90, 230, 90));
        Assert.Equal(2, s.Pad.Strokes.Count);

        s.Click("PART_Back");
        Pump();
        Assert.Single(s.Pad.Strokes);
        Assert.True(s.Button("PART_Save").IsEnabled);

        s.Click("PART_Clear");
        Pump();
        Assert.Empty(s.Pad.Strokes);
        Assert.False(s.Button("PART_Save").IsEnabled);
        Assert.False(s.Button("PART_Clear").IsEnabled);
    }

    [AvaloniaFact]
    public async Task CutoutSliderChangesTheAlphaPreview()
    {
        using var s = Show(NewLibrary(), Picker(DefaultScan()));
        await LoadImageAsync(s);
        var slider = s.Part<Slider>("PART_Cutout");

        slider.Value = 100; // the grey smudge (150) counts as paper
        await Until(() => s.Preview.Image is not null && CountVisible(s.Preview.Image) > 0);
        var strict = CountVisible(s.Preview.Image!);
        slider.Value = 200; // now the smudge is ink
        await Until(() => CountVisible(s.Preview.Image!) > strict);

        Assert.True(CountVisible(s.Preview.Image!) > strict);
        Assert.True(s.Button("PART_Save").IsEnabled);
    }

    private static int CountVisible(SignatureImage image)
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

    [AvaloniaFact]
    public async Task ImageIsTrimmedToItsContent()
    {
        var library = NewLibrary();
        using var s = Show(library, Picker(ScanPng(100, 80, (40, 30, 20, 10))));
        await LoadImageAsync(s);

        Assert.Equal((20 + (2 * SignatureCutout.TrimMargin), 10 + (2 * SignatureCutout.TrimMargin)), (s.Preview.Image!.Width, s.Preview.Image.Height));
        s.Click("PART_Save");
        await Until(() => s.Results.Count == 1);

        var saved = s.Results[0]!;
        Assert.Equal(SavedSignatureKind.Image, saved.Kind);
        Assert.Equal(24.0 / 14.0, saved.Aspect, 6);
        var stored = await library.ReadImageAsync(saved.Id);
        Assert.Equal((24, 14), (stored.Width, stored.Height));
    }

    public static TheoryData<string> Refusals() => new() { "limit", "toolarge", "toomanypixels", "unreadable", "empty", "aspect", "pickerfails" };

    [AvaloniaTheory]
    [MemberData(nameof(Refusals))]
    public async Task EveryRefusalEndsInAReasonAndNothingIsStored(string refusal)
    {
        var library = NewLibrary();
        if (refusal == "limit")
        {
            for (var i = 0; i < SignatureLimits.MaxSignatures; i++)
            {
                await library.AddDrawnAsync("Old " + i, [[new DisplayPoint(0, 0), new DisplayPoint(10, 5)]], DrawColor.Black);
            }
        }

        var good = DefaultScan();
        var calls = 0;
        Task<Stream?> Pick()
        {
            calls++;
            if (calls > 1 || refusal == "limit")
            {
                return Task.FromResult<Stream?>(new MemoryStream(good, writable: false));
            }

            return refusal switch
            {
                "toolarge" => Task.FromResult<Stream?>(new MemoryStream(new byte[SignatureLimits.MaxFileBytes + 1])),
                "toomanypixels" => Task.FromResult<Stream?>(new MemoryStream(HugeHeader())),
                "unreadable" => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3, 4, 5])),
                "empty" => Task.FromResult<Stream?>(new MemoryStream(PngEncoder.EncodeWithAlpha(new SignatureImage(10, 10, new byte[400])))),
                "aspect" => Task.FromResult<Stream?>(new MemoryStream(ScanPng(400, 12, (0, 5, 400, 2), paper: 250))),
                _ => throw new IOException(Path.Combine(_root, Secret, "photo.png")),
            };
        }

        using var s = Show(library, Pick);
        s.SelectTab(1);
        s.Click("PART_Choose");
        var expected = refusal switch
        {
            "limit" => PdfStrings.SignatureNewRefusalLimit,
            "toolarge" => PdfStrings.SignatureNewRefusalTooLarge,
            "toomanypixels" => PdfStrings.SignatureNewRefusalTooManyPixels,
            "empty" => PdfStrings.SignatureNewRefusalEmpty,
            "aspect" => PdfStrings.SignatureNewRefusalAspect,
            _ => PdfStrings.SignatureNewRefusalUnreadable,
        };
        if (refusal == "limit")
        {
            await Until(() => s.Button("PART_Save").IsEnabled);
            s.Click("PART_Save");
        }

        await Until(() => s.Message.IsEffectivelyVisible);

        // A live reason inside the card, the card still open, nothing stored, no path anywhere.
        Assert.Equal(expected, s.Message.Text);
        Assert.Contains(expected, s.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, s.AllText, StringComparison.Ordinal);
        Assert.Empty(s.Results);
        Assert.Equal(refusal == "limit" ? SignatureLimits.MaxSignatures : 0, (await library.LoadAsync()).Count);
        if (refusal == "limit")
        {
            Assert.True(s.Button("PART_Cancel").IsEnabled); // still usable, and Esc still ends it
        }
        else
        {
            Assert.False(s.Button("PART_Save").IsEnabled);

            // The card stays usable: a good image afterwards takes the message away.
            s.Click("PART_Choose");
            await Until(() => s.Button("PART_Save").IsEnabled);
            Assert.False(s.Message.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public async Task CancelStoresNothing()
    {
        var library = NewLibrary();
        using var s = Show(library);
        s.Draw(Line(30, 40, 230, 90));

        s.Click("PART_Cancel");

        Assert.Equal([null], s.Results);
        Assert.Empty(await library.LoadAsync());
        Assert.False(Directory.Exists(_root));
    }

    [AvaloniaFact]
    public void EscCancels()
    {
        using var s = Show(NewLibrary());
        s.Draw(Line(30, 40, 230, 90));

        s.Escape();

        Assert.Equal([null], s.Results);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GermanTextFitsTheCard(int tab)
    {
        using var s = Show(NewLibrary(), Picker(DefaultScan(), ScanPng(400, 12, (0, 5, 400, 2), paper: 250)), culture: "de");
        s.SelectTab(tab);
        if (tab == 1)
        {
            s.Click("PART_Choose");
            await Until(() => s.Button("PART_Save").IsEnabled);
            s.Click("PART_Choose"); // a refusal is on show as well
            await Until(() => s.Message.IsEffectivelyVisible);
        }

        var card = s.Card;
        var width = card.Bounds.Width;
        foreach (var control in card.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0))
        {
            var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), card)!.Value.X;
            Assert.True(right <= width + 0.5, $"{control.GetType().Name} {control.Name} ends at {right}, the card is {width} wide");
        }

        foreach (var text in card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.TextWrapping == TextWrapping.NoWrap && !string.IsNullOrEmpty(t.Text)))
        {
            var natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
            natural.Measure(Size.Infinity);
            Assert.True(natural.DesiredSize.Width <= text.Bounds.Width + 0.5, $"'{text.Text}' needs {natural.DesiredSize.Width} but has {text.Bounds.Width}");
        }
    }

    [Fact]
    public void NoTextSaysValidOrLegallyBinding()
    {
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && (p.Name.StartsWith("SignatureNew", StringComparison.Ordinal) || p.Name.StartsWith("SignatureHint", StringComparison.Ordinal)))
            .Select(p => p.Name).ToList();
        Assert.True(keys.Count >= 20, "the guard must find the strings of the card");
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            Assert.All(keys, k => Assert.False(banned.IsMatch(PdfStrings.GetIn(k, info)), k + " " + culture));
        }
    }

    [Fact]
    public void GermanHintIsTranslatedNotCopied()
    {
        var de = CultureInfo.GetCultureInfo("de");
        foreach (var key in new[] { "SignatureHintWritten", "SignatureHintImage" })
        {
            Assert.NotEqual(PdfStrings.GetIn(key, CultureInfo.GetCultureInfo("en")), PdfStrings.GetIn(key, de));
        }
    }
}
