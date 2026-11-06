using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class OcrSignedFileTests
{
    private static readonly Regex Valid = new(@"\b(valid|legally binding|gültig|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static async Task<(bool Warns, IReadOnlyList<string> Texts)> CardOfAsync(string file)
    {
        using var f = WindowFixture.Create();
        Directory.CreateDirectory(Path.Combine(f.Folder, "tessdata"));
        File.WriteAllBytes(Path.Combine(f.Folder, "tessdata", "eng.traineddata"), new byte[16]);
        f.Window.ShippedTessdataDirectory = Path.Combine(f.Folder, "no-shipped");
        f.Window.OcrLibraryProbe = () => true;
        var tab = await f.OpenAsync(file);
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);

        var run = f.Window.RecognizeAsync();
        await Raffica.Pdf.Tests.Printing.PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Cancel"));
        var card = f.Window.Host.ActiveDialog!;
        var warns = card.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Name == "PART_SignedChange");
        var texts = OcrRig.TextsOf(card);
        OcrRig.Click(card, "PART_Cancel");
        await run;
        return (warns, texts);
    }

    [AvaloniaFact]
    public async Task SignedFileWarnsThatTextCountsAsAChange()
    {
        // DocMDP level 1 allows no change: the card says that the recognized text counts as one. Level 3 allows annotating, so nothing to warn about.
        var (warns1, texts1) = await CardOfAsync("signed-docmdp-1.pdf");
        Assert.True(warns1);
        Assert.Contains("The recognized text counts as a change to the signed file.", texts1);
        var (warns3, texts3) = await CardOfAsync("signed-docmdp-3.pdf");
        Assert.False(warns3);

        // Neither says that a signature is valid or legally binding, in both languages.
        var before = PdfStrings.Culture;
        try
        {
            foreach (var language in new[] { "en", "de" })
            {
                PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
                var (_, texts) = await CardOfAsync("signed-docmdp-1.pdf");
                Assert.Contains(PdfStrings.OcrSignedChange, texts);
                Assert.All(texts, text => Assert.DoesNotMatch(Valid, text));
            }
        }
        finally
        {
            PdfStrings.Culture = before;
        }

        Assert.All(texts1.Concat(texts3), text => Assert.DoesNotMatch(Valid, text));
    }
}
