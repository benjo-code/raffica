using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Signatures;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Signing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignCommandTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Raffica.Shell.Frame.CommandDefinition SignCommand(WindowFixture window) =>
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "annotate.sign");

    private static async Task<IReadOnlyList<PdfAnnotation>> SignaturesAsync(PdfTab tab) =>
        (await tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind == AnnotationKind.Signature).ToList();

    private static async Task<DialogHolder> NextCardAsync(WindowFixture window)
    {
        await ViewFixture.WaitAsync(() => window.Window.Host.IsOpen);
        return new DialogHolder(window.Window.Host.ActiveDialog!);
    }

    private sealed class DialogHolder(Dialog card)
    {
        public Dialog Card { get; } = card;

        public Button Button(string name) => Card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }

    private static async Task SettleAsync(WindowFixture window, PdfTab tab)
    {
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await window.Window.AnnotateOf(tab)!.Select.Completion;
            await tab.Annotations.FlushAsync();
        }

        ViewFixture.Pump();
    }

    private static Task<SavedSignature> SaveDrawnAsync(WindowFixture window) =>
        window.Window.Signatures.AddDrawnAsync("Mine", [[new DisplayPoint(0, 0.6), new DisplayPoint(0.4, 0.1), new DisplayPoint(1, 0.8)]], DrawColor.Black);

    [AvaloniaFact]
    public async Task SignWithoutSavedSignaturesOpensTheNewCardAndPlacesTheResult()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("text-rich.pdf");
        Assert.True(SignCommand(window).IsEnabled);

        SignCommand(window).Run!.Invoke();
        var card = await NextCardAsync(window);

        // No saved signature: no picker, the card that makes one.
        Assert.Equal(PdfStrings.SignatureNewTitle, card.Card.Title);
        var pad = card.Card.GetVisualDescendants().OfType<SignaturePad>().Single();
        var origin = pad.TranslatePoint(default, window.Window)!.Value;
        window.Window.MouseDown(origin + new Point(20, 60), Avalonia.Input.MouseButton.Left);
        foreach (var x in new[] { 60, 100, 140, 180 })
        {
            window.Window.MouseMove(origin + new Point(x, 60 + (x % 40)));
        }

        window.Window.MouseUp(origin + new Point(180, 80), Avalonia.Input.MouseButton.Left);
        ViewFixture.Pump();
        card.Click("PART_Save");

        await ViewFixture.WaitAsync(() => window.Window.AnnotateOf(tab)!.Select.Selected is not null);
        await SettleAsync(window, tab);
        var placed = Assert.Single(await SignaturesAsync(tab));
        Assert.Equal(AnnotateTool.Select, window.Window.Tools.Tool);
        Assert.Equal(placed.Name, window.Window.AnnotateOf(tab)!.Select.Selected!.Name);
        Assert.Single(await window.Window.Signatures.LoadAsync());
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SignWithASavedSignatureOpensThePickerAndPlacesThePick()
    {
        using var window = WindowFixture.Create();
        var saved = await SaveDrawnAsync(window);
        var tab = await window.OpenAsync("text-rich.pdf");

        SignCommand(window).Run!.Invoke();
        var card = await NextCardAsync(window);
        Assert.Equal(PdfStrings.SignaturePickerTitle, card.Card.Title);
        card.Click("PART_Pick_" + saved.Id);

        await ViewFixture.WaitAsync(() => window.Window.AnnotateOf(tab)!.Select.Selected is not null);
        await SettleAsync(window, tab);
        Assert.Single(await SignaturesAsync(tab));
        Assert.False(window.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task CancellingThePickerPlacesNothing()
    {
        using var window = WindowFixture.Create();
        await SaveDrawnAsync(window);
        var tab = await window.OpenAsync("text-rich.pdf");

        SignCommand(window).Run!.Invoke();
        var card = await NextCardAsync(window);
        card.Click("PART_Cancel");
        await ViewFixture.WaitAsync(() => !window.Window.Host.IsOpen);
        await SettleAsync(window, tab);

        Assert.Empty(await SignaturesAsync(tab));
        Assert.False(tab.IsDirty);
        Assert.Equal(AnnotateTool.None, window.Window.Tools.Tool);
    }

    [AvaloniaFact]
    public async Task SignCommandIsDisabledWhenAnnotatingIsForbidden()
    {
        using var window = WindowFixture.Create();
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf"), "hôtel");
        var tab = window.Window.Workspace.Add(session, TestData.Pdf("encrypted_hello_world_r3.pdf"));
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => window.Window.AnnotateAllowedOf(tab) is not null);
        Assert.False(window.Window.AnnotateAllowedOf(tab));

        var command = SignCommand(window);
        Assert.False(command.IsEnabled);
        Assert.Equal(PdfStrings.AnnotateLockedReason, command.CurrentDisabledReason);

        // A path around the disabled command (a key, the palette) ends with nothing opened and nothing placed.
        await window.Window.SignAsync();
        ViewFixture.Pump();
        Assert.False(window.Window.Host.IsOpen);
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task ALibraryThatCannotBeReadGivesACardAndPlacesNothing()
    {
        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("en");

        // A library whose folder cannot be found out (the directory function throws, as for an inaccessible profile).
        using var window = WindowFixture.Create();
        window.Window.UseSignatureLibrary(new SignatureLibrary(() => throw new IOException(Path.Combine(window.Folder, "secret"))));
        var tab = await window.OpenAsync("text-rich.pdf");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var running = window.Window.SignAsync();
            var card = await NextCardAsync(window);
            var texts = card.Card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(PdfStrings.SignaturePlaceFailed, texts);
            Assert.DoesNotContain(window.Folder, string.Join(" ", texts), StringComparison.OrdinalIgnoreCase);
            card.Click("PART_Ok");
            await running;

            // Nothing placed, nothing changed, and the command is usable again (the second round asks again).
            Assert.Empty(await SignaturesAsync(tab));
            Assert.False(tab.IsDirty);
            Assert.False(window.Window.Host.IsOpen);
        }
    }

    [AvaloniaFact]
    public async Task ASecondSignWhileOneRunsDoesNothing()
    {
        using var window = WindowFixture.Create();
        var saved = await SaveDrawnAsync(window);
        var tab = await window.OpenAsync("text-rich.pdf");

        var first = window.Window.SignAsync();
        var picker = await NextCardAsync(window);
        var second = window.Window.SignAsync();
        ViewFixture.Pump();
        Assert.True(second.IsCompleted, "a second Sign while one runs returns at once");
        Assert.Same(picker.Card, window.Window.Host.ActiveDialog);

        picker.Click("PART_Pick_" + saved.Id);
        await first;
        await SettleAsync(window, tab);
        Assert.Single(await SignaturesAsync(tab));
        Assert.False(window.Window.Host.IsOpen); // no second picker was queued behind the first

        // Finished: Sign works again.
        var third = window.Window.SignAsync();
        var again = await NextCardAsync(window);
        again.Click("PART_Cancel");
        await third;
        Assert.Single(await SignaturesAsync(tab));
    }

    [AvaloniaFact]
    public async Task SignCommandNeedsADocument()
    {
        using var window = WindowFixture.Create();
        Assert.False(SignCommand(window).IsEnabled);
        await window.Window.SignAsync();
        Assert.False(window.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task SignedFileWarningStillAppliesOnSave()
    {
        using var copy = new TempCopy("signed-docmdp-1.pdf");
        var calls = 0;
        using var window = WindowFixture.Create(saveRunner: (_, _, _, _, _) =>
        {
            calls++;
            throw new OperationCanceledException();
        });
        var saved = await SaveDrawnAsync(window);
        var tab = await window.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();

        SignCommand(window).Run!.Invoke();
        var picker = await NextCardAsync(window);
        picker.Click("PART_Pick_" + saved.Id);
        await ViewFixture.WaitAsync(() => window.Window.AnnotateOf(tab)!.Select.Selected is not null);
        await SettleAsync(window, tab);
        Assert.True(tab.IsDirty);

        // The document already holds a signature: a save asks first, as for any change to a signed file.
        var saving = window.Window.SaveActiveAsync();
        var card = await NextCardAsync(window);
        Assert.Equal(PdfStrings.SignedWarnTitle, card.Card.Title);
        Assert.NotNull(card.Card.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "PART_SaveAnyway"));
        card.Click("PART_Cancel");
        Assert.False(await saving);
        Assert.Equal(0, calls);
    }
}
