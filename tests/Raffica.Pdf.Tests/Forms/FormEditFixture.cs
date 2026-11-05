using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Forms;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

/// <summary>
/// A temp copy of a form fixture open in a headless <see cref="PdfWindow"/> (real save, own settings folder), shown at fit-page zoom, with the
/// widgets of page 0 read. The mouse goes through the window, so a click is the click of the user: the form interaction, the text interaction
/// and the annotate interaction all see it as they would.
/// </summary>
internal sealed class FormEditFixture : IDisposable
{
    private FormEditFixture(WindowFixture windows, TempCopy copy, PdfTab tab)
    {
        Windows = windows;
        Copy = copy;
        Tab = tab;
    }

    public WindowFixture Windows { get; }

    public TempCopy Copy { get; }

    public PdfTab Tab { get; private set; }

    public PdfWindow Window => Windows.Window;

    public PageView View => Window.ViewOf(Tab)!;

    public FormParts Parts => Window.FormsOf(Tab)!;

    public FormInteraction Interaction => Parts.Interaction;

    public FormSession Forms => Tab.Forms;

    /// <param name="prepare">Changes the temp copy before it is opened (it gets the path of the copy); the fixture in testdata is never touched.</param>
    public static async Task<FormEditFixture> OpenAsync(string file = "forms-basic.pdf", bool realSave = true, Action<string>? prepare = null)
    {
        var copy = new TempCopy(file);
        prepare?.Invoke(copy.Path);
        var windows = WindowFixture.Create(realSave: realSave);
        var tab = await OpenPathAsync(windows, copy.Path);
        var fixture = new FormEditFixture(windows, copy, tab);
        await fixture.ReadyAsync();
        return fixture;
    }

    public static async Task<PdfTab> OpenPathAsync(WindowFixture windows, string path)
    {
        var tab = await windows.Window.Flow.OpenAsync(path) ?? throw new InvalidOperationException(path + " did not open");
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => windows.Window.ViewOf(tab)!.FirstSharpPageShown);
        return tab;
    }

    /// <summary>Shows a whole page and waits for the widgets of page 0 and the permission to fill.</summary>
    public async Task ReadyAsync()
    {
        View.SetZoomMode(ZoomMode.FitPage);
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => Forms.IsLoaded(0));
        await ViewFixture.WaitAsync(() => Window.FillAllowedOf(Tab) is not null);
    }

    /// <summary>Closes the tab and opens the (saved) file again.</summary>
    public async Task ReopenAsync()
    {
        Assert.True(await Window.Workspace.CloseAsync(Tab));
        ViewFixture.Pump();
        Tab = await OpenPathAsync(Windows, Copy.Path);
        await ReadyAsync();
    }

    public FormField Field(string name, int index = 0, int page = 0) =>
        Forms.FieldsOn(page).Where(f => f.Key.FieldName == name).ElementAt(index);

    /// <summary>Reads page 1 and returns a widget of it.</summary>
    public async Task<FormField> OtherPageFieldAsync(string name)
    {
        await Forms.LoadPageAsync(1);
        return Field(name, 0, 1);
    }

    /// <summary>The widget as the document has it now (read from the document, not from the cache).</summary>
    public async Task<FormField> FromDocumentAsync(string name, int index = 0, int page = 0) =>
        (await Tab.Session.Document!.GetFormFieldsAsync(page, TestContext.Current.CancellationToken)).Where(f => f.Key.FieldName == name).ElementAt(index);

    /// <summary>The centre of a widget in the coordinates of the window.</summary>
    public Point PointOf(FormField field)
    {
        var rect = View.ToView(field.Key.Page, field.Bounds)!.Value;
        return View.TranslatePoint(rect.Center, Window)!.Value;
    }

    public void Click(FormField field) => ClickAt(PointOf(field));

    public void ClickAt(Point point)
    {
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        ViewFixture.Pump();
    }

    /// <summary>A click on the widget, then waits until the editor is there.</summary>
    public Avalonia.Controls.Control Open(FormField field)
    {
        Click(field);
        Assert.True(Interaction.IsEditing, field.Key.FieldName + " did not open an editor");
        return Interaction.Editor!.Input;
    }

    public static void SetText(Avalonia.Controls.TextBox box, string text)
    {
        box.Text = text;
        ViewFixture.Pump();
    }

    public static bool Press(Avalonia.Controls.Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target };
        target.RaiseEvent(e);
        ViewFixture.Pump();
        return e.Handled;
    }

    /// <summary>The focus leaves the editor for the page (what a click elsewhere does).</summary>
    public void Leave()
    {
        View.Focus();
        ViewFixture.Pump();
    }

    /// <summary>Waits until every value that was started is written and the tab's state is current.</summary>
    public async Task SettleAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await Interaction.Completion;
            await Tab.Annotations.FlushAsync();
        }

        ViewFixture.Pump();
    }

    public void PressKey(PhysicalKey key, RawInputModifiers modifiers)
    {
        Window.KeyPressQwerty(key, modifiers);
        ViewFixture.Pump();
    }

    public void Dispose()
    {
        // The window holds the file open: it goes first.
        Windows.Dispose();
        Copy.Dispose();
    }
}
