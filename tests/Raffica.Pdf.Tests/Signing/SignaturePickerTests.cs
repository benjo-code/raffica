using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Signatures;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Signing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignaturePickerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raffica-sigpick-" + Guid.NewGuid().ToString("N"));

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

    private SignatureLibrary NewLibrary(Func<string>? directory = null) => new(directory ?? (() => _root));

    private static Task<SavedSignature> DrawnAsync(SignatureLibrary library, string name = "Drawn") =>
        library.AddDrawnAsync(name, [[new DisplayPoint(0, 0.6), new DisplayPoint(0.3, 0.1), new DisplayPoint(0.6, 0.9), new DisplayPoint(1, 0.4)]], DrawColor.Blue);

    private static Task<SavedSignature> ImageAsync(SignatureLibrary library, string name = "Image")
    {
        var bgra = new byte[60 * 30 * 4];
        for (var y = 8; y < 22; y++)
        {
            for (var x = 10; x < 50; x++)
            {
                var i = ((y * 60) + x) * 4;
                bgra[i] = bgra[i + 1] = bgra[i + 2] = 20;
                bgra[i + 3] = 255;
            }
        }

        return library.AddImageAsync(name, new SignatureImage(60, 30, bgra));
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class Shown(Window window, Dialog card, List<PickerResult> results) : IDisposable
    {
        public Window Window { get; } = window;

        public Dialog Card { get; } = card;

        public List<PickerResult> Results { get; } = results;

        public IEnumerable<T> All<T>()
            where T : Control => Card.GetVisualDescendants().OfType<T>();

        public Button Button(string name) => All<Button>().Single(b => b.Name == name);

        public Button? MaybeButton(string name) => All<Button>().SingleOrDefault(b => b.Name == name);

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        public string AllText => string.Join("\n", All<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

        public void Escape()
        {
            Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
        }

        public void Dispose() => Window.Close();
    }

    private static Shown Show(IReadOnlyList<SavedSignature> items, SignatureLibrary library, string culture = "en")
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        Raffica.Shell.Localization.ShellStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var results = new List<PickerResult>();
        var card = SignaturePickerCard.Create(items, library, results.Add);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 900, Height = 760 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card, results);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            Pump();
            await Task.Delay(10);
        }

        Pump();
        Assert.True(condition(), "The condition was not reached in time.");
    }

    [AvaloniaFact]
    public async Task PickerListsSavedSignaturesWithThumbnails()
    {
        var library = NewLibrary();
        var drawn = await DrawnAsync(library, "Alpha");
        var image = await ImageAsync(library, "Beta");
        using var shown = Show([drawn, image], library);

        Assert.Equal(2, shown.All<Button>().Count(b => b.Name?.StartsWith("PART_Pick_", StringComparison.Ordinal) == true));
        var thumbnails = shown.All<SignatureThumbnail>().ToList();
        Assert.Equal(2, thumbnails.Count);
        foreach (var thumbnail in thumbnails)
        {
            await thumbnail.LoadTask;
            Pump();
            Assert.True(thumbnail.HasContent, "every saved signature shows what it looks like");
        }

        Assert.Contains("Alpha", shown.AllText);
        Assert.Contains("Beta", shown.AllText);
        Assert.Contains(PdfStrings.SignatureHintWritten, shown.AllText);
        Assert.Contains(PdfStrings.SignatureHintImage, shown.AllText);
        Assert.Equal(PdfStrings.SignaturePickerNew, shown.Button("PART_New").Content);
        Assert.Equal(PdfStrings.SignaturePickerTitle, shown.Card.Title);
        Assert.Empty(shown.Results);
    }

    [AvaloniaFact]
    public async Task PickNewAndCancelEachAnswerExactlyOnce()
    {
        var library = NewLibrary();
        var drawn = await DrawnAsync(library);

        using (var picked = Show([drawn], library))
        {
            picked.Click("PART_Pick_" + drawn.Id);
            picked.Click("PART_Pick_" + drawn.Id);
            var result = Assert.IsType<PickerResult.Picked>(Assert.Single(picked.Results));
            Assert.Equal(drawn.Id, result.Signature.Id);
        }

        using (var fresh = Show([drawn], library))
        {
            fresh.Click("PART_New");
            fresh.Click("PART_Cancel");
            Assert.IsType<PickerResult.New>(Assert.Single(fresh.Results));
        }

        using (var cancelled = Show([drawn], library))
        {
            cancelled.Click("PART_Cancel");
            cancelled.Escape();
            Assert.IsType<PickerResult.Cancelled>(Assert.Single(cancelled.Results));
        }

        using var escaped = Show([drawn], library);
        escaped.Escape();
        Assert.IsType<PickerResult.Cancelled>(Assert.Single(escaped.Results));
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task DeleteAsksFirstAndKeepKeepsTheSignature()
    {
        var library = NewLibrary();
        var drawn = await DrawnAsync(library);
        using var shown = Show([drawn], library);

        shown.Click("PART_Delete_" + drawn.Id);
        Assert.Contains(PdfStrings.SignaturePickerDeleteConfirm, shown.AllText);
        Assert.Single(await library.LoadAsync());

        shown.Click("PART_DeleteKeep");
        Assert.DoesNotContain(PdfStrings.SignaturePickerDeleteConfirm, shown.AllText);
        Assert.Single(await library.LoadAsync());
        Assert.NotNull(shown.MaybeButton("PART_Pick_" + drawn.Id));
        Assert.Empty(shown.Results);
    }

    [AvaloniaFact]
    public async Task DeleteRemovesFromTheLibraryNotFromDocuments()
    {
        var library = NewLibrary();
        var drawn = await DrawnAsync(library, "Alpha");
        var image = await ImageAsync(library, "Beta");
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var placer = new SignaturePlacer(library, f.Tools, new DialogQueue(f.Host));
        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, drawn));
        Assert.True(await placer.PlaceAsync(f.Tab, f.View, f.Interaction.Select, image));
        await f.SettleAsync();
        var before = (await f.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind == AnnotationKind.Signature).ToList();
        Assert.Equal(2, before.Count);

        using var shown = Show([drawn, image], library);
        shown.Click("PART_Delete_" + drawn.Id);
        shown.Click("PART_DeleteYes");
        await UntilAsync(() => shown.MaybeButton("PART_Pick_" + drawn.Id) is null);

        // The library lost it (and its file); the picker lists the other; the document keeps both placed signatures and the undo history.
        Assert.Equal(image.Id, Assert.Single(await library.LoadAsync()).Id);
        Assert.NotNull(shown.MaybeButton("PART_Pick_" + image.Id));
        Assert.Empty(shown.Results);
        var after = (await f.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind == AnnotationKind.Signature).ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.True(f.Tab.Annotations.CanUndo);

        // Deleting the last one leaves an honest empty list and New still works.
        shown.Click("PART_Delete_" + image.Id);
        shown.Click("PART_DeleteYes");
        await UntilAsync(() => shown.MaybeButton("PART_Pick_" + image.Id) is null);
        Assert.Empty(await library.LoadAsync());
        Assert.Contains(PdfStrings.SignaturePickerEmpty, shown.AllText);
        shown.Click("PART_New");
        Assert.IsType<PickerResult.New>(Assert.Single(shown.Results));
    }

    [AvaloniaFact]
    public async Task NothingCancelsOrClosesWhileADeleteIsRunning()
    {
        var seed = NewLibrary();
        var drawn = await DrawnAsync(seed);
        var other = await ImageAsync(seed);
        var gate = new ManualResetEventSlim(false);
        var library = NewLibrary(() =>
        {
            gate.Wait(TimeSpan.FromSeconds(20));
            return _root;
        });
        using var shown = Show([drawn, other], library);

        shown.Click("PART_Delete_" + drawn.Id);
        shown.Click("PART_DeleteYes");
        Pump();

        // In flight: Cancel is disabled, Esc is ignored, a pick or New does nothing, and no answer was given.
        Assert.False(shown.Button("PART_Cancel").IsEnabled);
        shown.Escape();
        shown.Button("PART_New").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        shown.Button("PART_Pick_" + other.Id).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Empty(shown.Results);

        gate.Set();
        await UntilAsync(() => shown.MaybeButton("PART_Pick_" + drawn.Id) is null);
        Assert.True(shown.Button("PART_Cancel").IsEnabled);
        Assert.Empty(shown.Results);
        shown.Escape();
        Assert.IsType<PickerResult.Cancelled>(Assert.Single(shown.Results));
    }

    [AvaloniaFact]
    public async Task AFailingDeleteSaysSoAndKeepsTheListAndTheCardUsable()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var seed = NewLibrary();
        var drawn = await DrawnAsync(seed);
        var failing = NewLibrary(() => throw new IOException("C:\\secret\\folder"));
        using var shown = Show([drawn], failing);

        shown.Click("PART_Delete_" + drawn.Id);
        shown.Click("PART_DeleteYes");
        await UntilAsync(() => shown.AllText.Contains(PdfStrings.SignaturePickerDeleteFailed, StringComparison.Ordinal));

        Assert.DoesNotContain("secret", shown.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(shown.MaybeButton("PART_Pick_" + drawn.Id));
        Assert.True(shown.Button("PART_Cancel").IsEnabled);
        Assert.Single(await seed.LoadAsync());
    }

    [AvaloniaFact]
    public async Task ThumbnailsFinishingAfterTheCardClosedAnswerNothingAndAreDisposed()
    {
        var library = NewLibrary();
        var image = await ImageAsync(library);
        var drawn = await DrawnAsync(library);
        var shown = Show([image, drawn], library);
        var thumbnails = shown.All<SignatureThumbnail>().ToList();

        shown.Escape(); // closes before the stored image was necessarily read
        foreach (var thumbnail in thumbnails)
        {
            await thumbnail.LoadTask;
        }

        Pump();
        Assert.IsType<PickerResult.Cancelled>(Assert.Single(shown.Results));
        Assert.All(thumbnails, t => Assert.True(t.IsDisposed));
        shown.Dispose();
    }

    [AvaloniaFact]
    public async Task EveryTextOfThePickerIsGermanInGerman()
    {
        var library = NewLibrary();
        var drawn = await DrawnAsync(library, "Alpha");
        using var shown = Show([drawn], library, "de");

        Assert.Equal(PdfStrings.SignaturePickerTitle, shown.Card.Title);
        Assert.Equal("Unterschreiben", shown.Card.Title);
        Assert.Contains("Neue Unterschrift", shown.AllText + shown.Button("PART_New").Content);
        Assert.Contains(PdfStrings.SignatureHintWritten, shown.AllText);
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        await Task.CompletedTask;
    }

    [Fact]
    public void NoTextSaysValidOrLegallyBinding()
    {
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var names = typeof(PdfStrings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && (p.Name.StartsWith("Signature", StringComparison.Ordinal) || p.Name is "CmdSign" or "GroupSign"))
            .Select(p => p.Name)
            .ToList();
        Assert.Contains("SignaturePickerDeleteConfirm", names);
        Assert.Contains("SignaturePlaceHistoryFull", names);
        Assert.Contains("CmdSign", names);
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            Assert.All(names, n => Assert.False(banned.IsMatch(PdfStrings.GetIn(n, info)), n + " (" + culture + ")"));
        }

        // The new texts are real translations, not the English copied over.
        var fresh = names.Where(n => n.StartsWith("SignaturePicker", StringComparison.Ordinal) || n.StartsWith("SignaturePlace", StringComparison.Ordinal) || n is "CmdSign" or "GroupSign");
        Assert.All(fresh.Where(n => n is not "SignaturePickerDeleteYes" and not "SignaturePickerDelete"),
            n => Assert.NotEqual(PdfStrings.GetIn(n, CultureInfo.GetCultureInfo("en")), PdfStrings.GetIn(n, CultureInfo.GetCultureInfo("de"))));
    }
}
