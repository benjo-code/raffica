using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class AnnotationLimitTests
{
    // One page with a line of text and as many note annotations as the document may list; a new one would never be listed.
    internal static string WriteFullPdf(string folder)
    {
        var content = "BT /F1 18 Tf 40 150 Td (Full of notes) Tj ET";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> /Annots [{string.Concat(Enumerable.Repeat("<< /Subtype /Text /Rect [10 10 20 20] >> ", PdfDocument.MaxAnnotations))}] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        var path = Path.Combine(folder, "full.pdf");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
        return path;
    }

    [AvaloniaFact]
    public async Task LimitIsReportedAndNothingIsCreated()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-limit-").FullName;
        try
        {
            using var f = await AnnotateFixture.OpenPathAsync(WriteFullPdf(folder), width: 600, height: 500);
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(PdfDocument.MaxAnnotations, (await f.Tab.Session.Document!.GetAnnotationsAsync(0, ct)).Count);
            f.Tools.Choose(AnnotateTool.Highlight);
            f.Text.Text.Selection.SelectAll();
            Assert.True(f.Text.Text.Selection.HasSelection);

            var apply = f.Interaction.Markup.ApplyAsync(AnnotateTool.Highlight);
            await FlushUntilAsync(() => f.Host.IsOpen);

            // The error card names the limit; the pending handle is gone already (the card does not hold the tab dirty), and nothing was created.
            var card = Assert.IsType<Raffica.Shell.Controls.Dialog>(f.Host.ActiveDialog);
            var message = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Message").Text;
            Assert.Equal(PdfStrings.AnnotateLimitReached, message);
            Assert.False(f.Tab.Annotations.IsDirty);
            Assert.False(f.Tab.IsDirty);
            Assert.False(f.Tab.Annotations.CanUndo);
            Assert.Equal(PdfDocument.MaxAnnotations, (await f.Tab.Session.Document!.GetAnnotationsAsync(0, ct)).Count);

            card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await apply.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await f.Tab.Annotations.FlushAsync().WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.False(f.Tab.IsDirty);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task FlushUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 1200 && !condition(); i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
        }

        Assert.True(condition());
    }
}
