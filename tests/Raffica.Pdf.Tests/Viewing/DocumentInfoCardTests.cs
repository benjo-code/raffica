using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class DocumentInfoCardTests
{
    private static Window Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 500 };
        window.Show();
        host.Show(dialog);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return window;
    }

    private static string AllText(Dialog dialog) =>
        string.Join('\n', dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    [AvaloniaFact]
    public void ShowsTheFieldsOfTheDocument()
    {
        var info = new DocumentInfo("report.pdf", @"C:\Docs", 12, "1.7", 3_145_728, "Annual report", "A. Author", new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.Zero));
        var done = 0;
        var dialog = DocumentInfoCard.Create(info, () => done++, new PdfPageSize(595.28, 841.89));
        var window = Show(dialog);
        var text = AllText(dialog);

        Assert.Contains("report.pdf", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\Docs", text, StringComparison.Ordinal);
        Assert.Contains("12", text, StringComparison.Ordinal);
        Assert.Contains("210 × 297 mm", text, StringComparison.Ordinal);
        Assert.Contains("1.7", text, StringComparison.Ordinal);
        Assert.Contains("3 MB", text, StringComparison.Ordinal);
        Assert.Contains("Annual report", text, StringComparison.Ordinal);
        Assert.Contains("A. Author", text, StringComparison.Ordinal);
        Assert.Contains("2024", text, StringComparison.Ordinal);
        Assert.Contains(PdfStrings.InfoTitle, dialog.Title ?? string.Empty, StringComparison.Ordinal);

        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, done);
        window.Close();
    }

    [AvaloniaFact]
    public void MissingMetadataShowsAPlaceholderNotAnEmptyCell()
    {
        var info = new DocumentInfo("a.pdf", string.Empty, 1, "1.4", 0, null, null, null);
        var dialog = DocumentInfoCard.Create(info, () => { });
        var window = Show(dialog);
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheWindowReadsTheInfoFromCore()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");

        var shown = windows.Window.ShowDocumentInfoAsync();
        for (var i = 0; i < 400 && !windows.Window.Host.IsOpen; i++)
        {
            await Task.Delay(25);
            ViewFixture.Pump();
        }

        Assert.True(windows.Window.Host.IsOpen);
        var dialog = windows.Window.Host.ActiveDialog!;
        var text = AllText(dialog);
        Assert.Contains("text-rich.pdf", text, StringComparison.Ordinal);
        Assert.Contains(windows.Window.ViewOf(tab)!.PageCount.ToString(System.Globalization.CultureInfo.CurrentCulture), text, StringComparison.Ordinal);
        Assert.Contains(" mm", text, StringComparison.Ordinal);

        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await shown;
        Assert.False(windows.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task WithoutADocumentThereIsNothingToShow()
    {
        using var windows = WindowFixture.Create();
        await windows.Window.ShowDocumentInfoAsync();
        await windows.Window.GoToPageAsync();
        Assert.False(windows.Window.Host.IsOpen);
    }
}
