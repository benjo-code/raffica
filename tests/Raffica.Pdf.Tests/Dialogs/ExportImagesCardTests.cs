using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class ExportImagesCardTests
{
    private static readonly PdfPageSize A4 = new(595, 842);

    private static void Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 800 };
        window.Show();
        host.Show(dialog);
        PrintHost.Pump();
    }

    private static T Find<T>(Dialog dialog, string name)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Dialog Make(int pages, string? folder, List<ImageExportChoice?> results) =>
        ExportImagesCard.Create("doc.pdf", Enumerable.Repeat(A4, pages).ToArray(), () => Task.FromResult(folder), results.Add);

    private static async Task ChooseFolderAsync(Dialog dialog)
    {
        Find<Button>(dialog, "PART_ChooseFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await PrintHost.UntilAsync(() => Find<TextBlock>(dialog, "PART_Folder").Text != PdfStrings.SplitFolderNone);
    }

    private static string TempFolder() => Directory.CreateTempSubdirectory("raffica-export-card-").FullName;

    [AvaloniaFact]
    public async Task DefaultsAreAllPagesAt150DpiWithMarksAndNeedAFolder()
    {
        var results = new List<ImageExportChoice?>();
        var folder = TempFolder();
        try
        {
            var dialog = Make(6, folder, results);
            Show(dialog);
            var export = Find<Button>(dialog, "PART_Export");
            Assert.Equal("all", Find<TextBox>(dialog, "PART_Range").Text);
            Assert.True(Find<RadioButton>(dialog, "PART_Dpi150").IsChecked);
            Assert.True(Find<CheckBox>(dialog, "PART_Marks").IsChecked);
            Assert.False(export.IsEnabled);

            // The largest page is shown in pixels at the chosen resolution (A4 at 150 dpi).
            var largest = Find<TextBlock>(dialog, "PART_Largest").Text;
            Assert.Contains("1,240", largest, StringComparison.Ordinal);
            Assert.Contains("1,755", largest, StringComparison.Ordinal);

            await ChooseFolderAsync(dialog);
            Assert.True(export.IsEnabled);
            export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var choice = Assert.Single(results)!.Value;
            Assert.Equal(new ImageExportChoice("all", 150, true, folder), choice);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [AvaloniaTheory]
    [InlineData("0", "Enter pages like")]
    [InlineData("5-2", "Enter pages like")]
    [InlineData("abc", "Enter pages like")]
    [InlineData("", "Enter pages like")]
    [InlineData("9", "only 6 pages")]
    public async Task RangeIsValidatedWithAReasonAndExportStaysDisabled(string text, string reason)
    {
        var folder = TempFolder();
        try
        {
            var dialog = Make(6, folder, []);
            Show(dialog);
            await ChooseFolderAsync(dialog);
            var export = Find<Button>(dialog, "PART_Export");
            Assert.True(export.IsEnabled);

            Find<TextBox>(dialog, "PART_Range").Text = text;
            var hint = Find<TextBlock>(dialog, "PART_RangeHint");
            Assert.True(hint.IsVisible);
            Assert.Contains(reason, hint.Text, StringComparison.Ordinal);
            Assert.False(export.IsEnabled);

            Find<TextBox>(dialog, "PART_Range").Text = "2-3";
            Assert.False(hint.IsVisible);
            Assert.True(export.IsEnabled);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("1201")]
    [InlineData("100000")]
    [InlineData("-5")]
    [InlineData("12.5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("99999999999999999999")]
    public async Task ResolutionIsValidatedWithAReason(string text)
    {
        var folder = TempFolder();
        try
        {
            var dialog = Make(3, folder, []);
            Show(dialog);
            await ChooseFolderAsync(dialog);
            var export = Find<Button>(dialog, "PART_Export");
            Find<RadioButton>(dialog, "PART_DpiCustom").IsChecked = true;
            Find<TextBox>(dialog, "PART_DpiField").Text = text;

            var hint = Find<TextBlock>(dialog, "PART_DpiHint");
            Assert.True(hint.IsVisible);
            Assert.Equal(PdfStrings.ExportDpiInvalid, hint.Text);
            Assert.False(export.IsEnabled);

            // The bounds themselves are fine (A4 at 1200 dpi is over the pixel limit, which has its own reason).
            foreach (var ok in new[] { "1", "300", " 72 " })
            {
                Find<TextBox>(dialog, "PART_DpiField").Text = ok;
                Assert.False(hint.IsVisible, ok);
                Assert.True(export.IsEnabled, ok);
            }

            Find<TextBox>(dialog, "PART_DpiField").Text = "1200";
            Assert.False(hint.IsVisible);
            Assert.True(Find<TextBlock>(dialog, "PART_SizeHint").IsVisible);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [AvaloniaFact]
    public async Task FolderThatDoesNotExistIsAReason()
    {
        var missing = Path.Combine(Path.GetTempPath(), "raffica-export-missing-" + Guid.NewGuid().ToString("N"));
        var dialog = Make(3, missing, []);
        Show(dialog);
        await ChooseFolderAsync(dialog);

        Assert.Equal(PdfStrings.ExportFolderMissing, Find<TextBlock>(dialog, "PART_FolderHint").Text);
        Assert.True(Find<TextBlock>(dialog, "PART_FolderHint").IsVisible);
        Assert.False(Find<Button>(dialog, "PART_Export").IsEnabled);
    }

    [AvaloniaFact]
    public async Task OverLimitPageIsNamedAndTheLargestPageIsShown()
    {
        var folder = TempFolder();
        try
        {
            PdfPageSize[] sizes = [A4, A4, new PdfPageSize(5000, 5000), A4];
            var dialog = ExportImagesCard.Create("doc.pdf", sizes, () => Task.FromResult<string?>(folder), _ => { });
            Show(dialog);
            await ChooseFolderAsync(dialog);

            // 5000 pt at 150 dpi is 10 417 pixels a side: over 64 megapixels, page 3 by name.
            Assert.Contains("Page 3 ", Find<TextBlock>(dialog, "PART_SizeHint").Text, StringComparison.Ordinal);
            Assert.False(Find<Button>(dialog, "PART_Export").IsEnabled);
            Assert.Contains("10,417", Find<TextBlock>(dialog, "PART_Largest").Text, StringComparison.Ordinal);

            Find<RadioButton>(dialog, "PART_Dpi72").IsChecked = true;
            Assert.False(Find<TextBlock>(dialog, "PART_SizeHint").IsVisible);
            Assert.True(Find<Button>(dialog, "PART_Export").IsEnabled);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void ResolutionTextsAndPixelArithmetic()
    {
        Assert.Null(ImageExportPlan.DpiProblem("72", out var dpi));
        Assert.Equal(72, dpi);
        Assert.NotNull(ImageExportPlan.DpiProblem("0 dpi", out _));
        Assert.Equal(1.0, ImageExportPlan.ScaleOf(72));
        Assert.Equal(long.MaxValue, ImageExportPlan.PixelsOf(new PdfPageSize(1e12, 10), 1));
        Assert.Equal(1L, ImageExportPlan.PixelsOf(new PdfPageSize(0, 0), 1));
    }
}
