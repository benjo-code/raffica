using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Raffica.Core.Pdf;
using Raffica.Core.TextEdit;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>
/// The window of the PDF app over a temp copy of a test file, with a real save: the text edit mode is driven the way the user does it (the ribbon command, the
/// keys of the focused element, the field), and the result is read from the document and from the written file. Nothing is written outside the temp folder.
/// </summary>
internal sealed class TextEditRig : IDisposable
{
    private readonly TempCopy _copy;

    private TextEditRig(WindowFixture fixture, TempCopy copy, PdfTab tab, string file)
    {
        Fixture = fixture;
        _copy = copy;
        Tab = tab;
        File = file;
    }

    public WindowFixture Fixture { get; }

    public PdfWindow Window => Fixture.Window;

    public PdfTab Tab { get; }

    /// <summary>The name of the fixture file.</summary>
    public string File { get; }

    /// <summary>The temp copy of the file that is open.</summary>
    public string Path => _copy.Path;

    public TextEditSession Session => Tab.TextEdit;

    public Raffica.Pdf.Viewing.PageView View => Window.ViewOf(Tab)!;

    public TextEditInteraction Interaction => Window.TextEditOf(Tab)!.Interaction;

    public static async Task<TextEditRig> OpenAsync(string file, Raffica.Core.Printing.IPrintService? printService = null)
    {
        var copy = new TempCopy(file);
        var fixture = WindowFixture.Create(realSave: true, printService: printService);
        try
        {
            var tab = await fixture.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException(file + " did not open");
            await ViewFixture.WaitAsync(() => tab.Session.State == ViewerState.Ready && fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
            return new TextEditRig(fixture, copy, tab, file);
        }
        catch
        {
            fixture.Dispose();
            copy.Dispose();
            throw;
        }
    }

    public CommandDefinition Command(string id) =>
        Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    /// <summary>Starts the mode with the ribbon command and waits until the lines of the page in view are read.</summary>
    public async Task EnterModeAsync()
    {
        Command("text.edit").Run!();
        await ViewFixture.WaitAsync(() => Tab.TextEditIfCreated is { IsActive: true } && Window.TextEditOf(Tab) is not null);
        await ViewFixture.WaitAsync(() => Session.IsLoaded(0));
    }

    /// <summary>The run of a page whose text (without trailing spaces) is <paramref name="text"/>, as the session read it.</summary>
    public async Task<TextRun> RunAsync(string text, int page = 0)
    {
        await Session.LoadPageAsync(page);
        return Session.RunsOf(page).Single(r => r.Text.TrimEnd() == text);
    }

    /// <summary>The runs of the document as Core reads them now (not the session's cache).</summary>
    public async Task<IReadOnlyList<TextRun>> DocumentRunsAsync(int page = 0) =>
        await Tab.Session.Document!.GetTextRunsAsync(page, TestContext.Current.CancellationToken);

    public async Task<string> PageTextAsync(int page = 0)
    {
        var text = await Tab.Session.Document!.GetPageTextAsync(page, TestContext.Current.CancellationToken);
        return text.GetText(0, text.Chars.Count);
    }

    /// <summary>Opens the draft on a run and waits until the field is over it.</summary>
    public RunFieldEditor OpenField(TextRun run)
    {
        Assert.True(Session.OpenDraft(run));
        ViewFixture.Pump();
        return Interaction.Field ?? throw new InvalidOperationException("no field opened");
    }

    /// <summary>Types a text into the field the way the field reports it (the text changes, the field says so).</summary>
    public static void Type(RunFieldEditor field, string text)
    {
        field.Field.Text = text;
        ViewFixture.Pump();
    }

    /// <summary>The preview of the draft once the decision came (the debounce is off).</summary>
    public async Task<TextEditPreview> PreviewOfAsync(string text, bool whole = false)
    {
        Session.Debounce = TimeSpan.Zero;
        return await Session.PreviewAsync(text, whole);
    }

    /// <summary>Waits until the editor has nothing in flight and the session has no commit running.</summary>
    public async Task SettleAsync()
    {
        await Tab.Annotations.Editor.WhenIdleAsync();
        ViewFixture.Pump();
    }

    public void Press(Avalonia.Input.PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPressQwerty(key, modifiers);
        ViewFixture.Pump();
    }

    /// <summary>Whether the keyboard focus is inside the field.</summary>
    public bool FieldHasFocus => Interaction.Field?.Field.IsFocused == true;

    /// <summary>The point of the middle of a run in the coordinates of the window.</summary>
    public Point WindowPointOf(TextRun run)
    {
        var rect = View.ToView(run.Page, run.Bounds)!.Value;
        return View.TranslatePoint(rect.Center, Window)!.Value;
    }

    /// <summary>Saves with the real save and returns the written bytes.</summary>
    public async Task<byte[]> SaveAsync()
    {
        Assert.True(await Window.SaveActiveAsync());
        return await System.IO.File.ReadAllBytesAsync(Path, TestContext.Current.CancellationToken);
    }

    /// <summary>The bytes of the fixture as they were before anything was written.</summary>
    public static byte[] OriginalBytes(string file) => System.IO.File.ReadAllBytes(Raffica.TestSupport.TestData.Pdf(file));

    public void Dispose()
    {
        Fixture.Dispose();
        _copy.Dispose();
    }
}
