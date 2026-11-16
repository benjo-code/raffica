using Avalonia.Controls;
using Avalonia.Interactivity;
using Raffica.Write;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>What the save and close tests share: a window with a copy of a corpus file in the folder of the fixture, edits through the editing session and answers to the cards.</summary>
internal static class SaveHarness
{
    public const string Typed = "Savetext ";

    /// <summary>Opens a copy of the small corpus document in the window and waits for the whole model; the copy is returned too.</summary>
    public static async Task<(WriteTab Tab, string Path)> OpenCopyAsync(WindowFixture fixture, WriteWindow window, string name = "doc.docx", string? source = null)
    {
        var path = fixture.Copy(source ?? WindowFixture.Poi, name);
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        WindowFixture.Pump();
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        tab.EnsureEditor();
        return (tab, path);
    }

    /// <summary>Types <paramref name="text"/> at the start of the document through the session (the model changes, the tab becomes dirty).</summary>
    public static void Edit(WriteTab tab, string text = Typed)
    {
        var session = tab.EnsureEditor();
        Assert.NotNull(session.Apply(e => e.InsertText(session.Selection!.Value, text), 0));
        WindowFixture.Pump();
    }

    /// <summary>Awaits a task while the dispatcher runs (a save continues on the UI thread); fails when it does not finish.</summary>
    public static async Task<T> AwaitAsync<T>(Task<T> task, int milliseconds = 30000)
    {
        for (var waited = 0; waited < milliseconds && !task.IsCompleted; waited += 10)
        {
            await Task.Delay(10);
            WindowFixture.Pump();
        }

        Assert.True(task.IsCompleted, "the task did not finish in time");
        return await task;
    }

    public static async Task AwaitAsync(Task task, int milliseconds = 30000)
    {
        for (var waited = 0; waited < milliseconds && !task.IsCompleted; waited += 10)
        {
            await Task.Delay(10);
            WindowFixture.Pump();
        }

        Assert.True(task.IsCompleted, "the task did not finish in time");
        await task;
    }

    public static async Task WaitForCardAsync(WriteWindow window) => await WindowFixture.WaitAsync(() => window.Host.IsOpen);

    /// <summary>The buttons of the card that is open, by name.</summary>
    public static Button ButtonOf(WriteWindow window, string name) =>
        Assert.IsType<StackPanel>(window.Host.ActiveDialog!.Footer).Children.OfType<Button>().Single(b => b.Name == name);

    /// <summary>Clicks a button of the card that is open and lets the card go.</summary>
    public static void Click(WriteWindow window, string name)
    {
        ButtonOf(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
    }

    /// <summary>The whole text of the body paragraphs of a saved file, read again from disk.</summary>
    public static string TextOf(string path)
    {
        using var file = DocxFile.Open(path);
        return string.Concat(file.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text));
    }

    /// <summary>The whole text of the body paragraphs of the model a tab holds now.</summary>
    public static string TextOfModel(WriteTab tab) =>
        string.Concat(tab.Docx.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text));

    /// <summary>The names in a folder, sorted: a failed save leaves no temporary file.</summary>
    // The names in a folder, without the app's own recent-files bookkeeping: its writer runs behind the open and may be mid-write (a "write-recent.json.tmp") when a test takes the snapshot.
    public static string[] Names(string folder) => [.. Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Where(name => !name.StartsWith("write-recent.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
}
