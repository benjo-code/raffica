using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Makes the files of the owner's Word check for milestone 4.4b (<c>Docs/write-word-check.md</c>, section "Sections, page numbers and page setup (milestone 4.4b)"): every operation goes through the editor as the
/// app calls it and the file is saved through <see cref="DocxFile.SaveAsync(string, CancellationToken)"/>. Each file gets a <c>&lt;name&gt;.numbers.json</c> with the number every page of Write's own layout
/// draws (<see cref="LaidOutPage.NumberValue"/>, null for a page with none), which <c>tools/word-page-numbers.ps1</c> compares with what Word says. The tests write nothing and skip unless the folder is named
/// by <c>RAFFICA_WORD_CHECK_DIR</c>.
/// </summary>
public sealed class WordCheckSectionFilesTests
{
    private const string FolderVariable = "RAFFICA_WORD_CHECK_DIR";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Folder()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder), "Set RAFFICA_WORD_CHECK_DIR to a folder to make the Word-check files.");

        Directory.CreateDirectory(folder!);
        return folder!;
    }

    private static TextPosition At(Block block) => new(StoryRef.Body, (Paragraph)block, 0);

    private static TextEditor EditorOf(DocxFile file) =>
        new(file.Model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(file.Model));

    private static DocumentLayout LayOut(DocxFile file) =>
        Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions()));

    /// <summary>A blank document (A4) with <paramref name="count"/> body paragraphs of about two lines each, several pages of them.</summary>
    private static DocxFile Filled(int count)
    {
        var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var first = file.Model.Body.Blocks.OfType<Paragraph>().First();
        var lines = Enumerable.Range(1, count).Select(n => string.Create(CultureInfo.InvariantCulture, $"Paragraph {n}: the quick brown fox jumps over the lazy dog and keeps running over the hill until the evening."));
        editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, first, 0), new TextPosition(StoryRef.Body, first, 0)), string.Join("\n", lines));
        return file;
    }

    /// <summary>The body block that starts page <paramref name="page"/> (from 0): the one at its top, or the next one when that is the tail of a block from the page before.</summary>
    private static Block StartOfPage(DocxFile file, DocumentLayout layout, int page)
    {
        var block = layout.BlockAt(page, 0f);
        Assert.True(block >= 0, "The page has no block.");
        if (layout.Locate(block)?.Page != page)
        {
            block++;
        }

        return file.Model.Body.Blocks[block];
    }

    private static Paragraph FirstParagraph(DocxFile file) => file.Model.Body.Blocks.OfType<Paragraph>().First();

    /// <summary>Saves the file and writes <c>&lt;name&gt;.numbers.json</c> next to it: the number of each page of a fresh layout of the saved model.</summary>
    internal static async Task SaveWithNumbersAsync(DocxFile file, string folder, string name, CancellationToken token)
    {
        var path = Path.Combine(folder, name);
        await file.SaveAsync(path, token);

        var layout = LayOut(file);
        var builder = new StringBuilder("[");
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            builder.Append(i == 0 ? string.Empty : ",").Append(layout.Page(i).NumberValue?.ToString(CultureInfo.InvariantCulture) ?? "null");
        }

        builder.Append(']');
        File.WriteAllText(Path.ChangeExtension(path, ".numbers.json"), builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    }

    private static void SetStart(DocxFile file, int section, int start)
    {
        var properties = SectionList.Of(file.Model)[section].Properties;
        var element = properties.Get(W + "pgNumType") ?? new XElement(W + "pgNumType");
        element.SetAttributeValue(W + "start", start.ToString(CultureInfo.InvariantCulture));
        properties.Set(W + "pgNumType", element);
    }

    [Fact]
    public async Task NumberingFromPageThreeIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        var at = StartOfPage(file, LayOut(file), 2);
        Assert.NotNull(editor.NumberPagesFrom(At(at), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

        await SaveWithNumbersAsync(file, folder, "page-three-one.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARestartOnAnEvenPageIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        var at = StartOfPage(file, LayOut(file), 3);
        Assert.NotNull(editor.NumberPagesFrom(At(at), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

        await SaveWithNumbersAsync(file, folder, "restart-page-four.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnAppendixWithAPrefixOnTheThesisIsSavedForWord()
    {
        var folder = Folder();
        var path = Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");
        using var file = DocxFile.Open(path);
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var last = Enumerable.Range(0, body.Count).Last(i => body[i] is Paragraph { Properties.StyleId: "Heading1" });
        Assert.NotNull(editor.NumberPagesFrom(At(body[last]), new PageNumbering(1, PageNumberFormat.Arabic, "A-")));

        await SaveWithNumbersAsync(file, folder, "appendix-a1.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RomanFrontMatterIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.LowerRoman, string.Empty)));
        var at = StartOfPage(file, LayOut(file), 2);
        Assert.NotNull(editor.NumberPagesFrom(At(at), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

        await SaveWithNumbersAsync(file, folder, "roman-front-matter.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ADifferentFirstPageIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(60);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        Assert.NotNull(editor.SetDifferentFirstPage(0, true));

        await SaveWithNumbersAsync(file, folder, "first-page.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LeftAndRightPagesAreSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(60);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        Assert.NotNull(editor.SetLeftRightPages(true));

        await SaveWithNumbersAsync(file, folder, "left-right.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ALandscapeSectionIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

        var layout = LayOut(file);
        var landscapeAt = StartOfPage(file, layout, 1);
        var setup = SectionPageSetup.Read(SectionList.Of(file.Model)[0].Properties);
        Assert.NotNull(editor.ApplyPageSetup(At(landscapeAt), new PageSetupChange(setup.WithLandscape(true), PageSetupParts.Orientation), ApplyScope.FromHere));

        // Back to portrait from a later page.
        layout = LayOut(file);
        var portraitAt = StartOfPage(file, layout, layout.LaidOutPages - 2);
        var turned = SectionPageSetup.Read(SectionList.Of(file.Model)[1].Properties);
        Assert.NotNull(editor.ApplyPageSetup(At(portraitAt), new PageSetupChange(turned.WithLandscape(false), PageSetupParts.Orientation), ApplyScope.FromHere));

        await SaveWithNumbersAsync(file, folder, "landscape-section.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TwoColumnsFromHereAreSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

        var at = StartOfPage(file, LayOut(file), 1);
        var two = SectionPageSetup.Read(SectionList.Of(file.Model)[0].Properties) with { Columns = 2, ColumnSpaceTwips = 600, ColumnLine = true };
        Assert.NotNull(editor.ApplyPageSetup(At(at), new PageSetupChange(two, PageSetupParts.Columns), ApplyScope.FromHere));

        await SaveWithNumbersAsync(file, folder, "two-columns.docx", TestContext.Current.CancellationToken);
    }

    // The files below hold the rules the page number oracle (SectionOracleTests) takes from the standard and from forum reports and has not seen Word apply:
    // a unit of Docs/write-word-check.md, section "Sections, page numbers and page setup (milestone 4.4b)", "Rules not yet seen in Word".

    [Fact]
    public async Task AContinuousSectionWithAStartOnTheSamePaperIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(60);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        var layout = LayOut(file);

        // A block in the middle of page 2.
        var middle = StartOfPage(file, layout, 1);
        var index = file.Model.Body.Blocks.ToList().IndexOf(middle) + 4;
        Assert.NotNull(editor.InsertSectionBreak(At(file.Model.Body.Blocks[index]), SectionBreakKind.Continuous));
        SetStart(file, 1, 5);

        await SaveWithNumbersAsync(file, folder, "continuous-start-same-paper.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AContinuousSectionWithAStartOnOtherPaperIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(60);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        var layout = LayOut(file);
        var middle = StartOfPage(file, layout, 1);
        var index = file.Model.Body.Blocks.ToList().IndexOf(middle) + 4;
        Assert.NotNull(editor.InsertSectionBreak(At(file.Model.Body.Blocks[index]), SectionBreakKind.Continuous));
        var setup = SectionPageSetup.Read(SectionList.Of(file.Model)[1].Properties);
        Assert.NotNull(editor.ApplyPageSetup(At(file.Model.Body.Blocks[index]), new PageSetupChange(setup.WithLandscape(true), PageSetupParts.Orientation), ApplyScope.ThisSection));
        SetStart(file, 1, 5);

        await SaveWithNumbersAsync(file, folder, "continuous-start-other-paper.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnEvenPageSectionWithAStartOfOneIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        var at = StartOfPage(file, LayOut(file), 1);
        Assert.NotNull(editor.InsertSectionBreak(At(at), SectionBreakKind.EvenPage));
        SetStart(file, 1, 1);

        await SaveWithNumbersAsync(file, folder, "even-page-start-one.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnOddPageSectionWithAStartOfTwoIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        var at = StartOfPage(file, LayOut(file), 2);
        Assert.NotNull(editor.InsertSectionBreak(At(at), SectionBreakKind.OddPage));
        SetStart(file, 1, 2);

        await SaveWithNumbersAsync(file, folder, "odd-page-start-two.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ABlankPageBeforeAnEvenPageSectionInRomanIsSavedForWord()
    {
        var folder = Folder();
        using var file = Filled(100);
        var editor = EditorOf(file);
        Assert.NotNull(editor.NumberPagesFrom(At(FirstParagraph(file)), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

        // Page 3 is odd, so an even-page section there needs the blank page 3 and starts on page 4.
        var at = StartOfPage(file, LayOut(file), 2);
        Assert.NotNull(editor.InsertSectionBreak(At(at), SectionBreakKind.EvenPage));
        var properties = SectionList.Of(file.Model)[1].Properties;
        var element = new XElement(W + "pgNumType");
        element.SetAttributeValue(W + "fmt", "lowerRoman");
        properties.Set(W + "pgNumType", element);

        await SaveWithNumbersAsync(file, folder, "blank-page-roman.docx", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ASplitAfterAParagraphThatEndsInAPageBreakIsSavedForWord()
    {
        var folder = Folder();
        var temp = Path.Combine(Path.GetTempPath(), "word-check-br-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            var body = new StringBuilder();
            for (var i = 1; i <= 8; i++)
            {
                body.Append(string.Create(CultureInfo.InvariantCulture, $"<w:p><w:r><w:t>Before the break {i}.</w:t></w:r></w:p>"));
            }

            body.Append("<w:p><w:r><w:t>Last line of the page.</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            for (var i = 1; i <= 8; i++)
            {
                body.Append(string.Create(CultureInfo.InvariantCulture, $"<w:p><w:r><w:t>After the break {i}.</w:t></w:r></w:p>"));
            }

            DocxBuilder.WithBody(temp, body.ToString());
            using var file = DocxFile.Open(temp);
            var editor = EditorOf(file);
            var after = file.Model.Body.Blocks[9];
            Assert.NotNull(editor.NumberPagesFrom(At(after), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));

            await SaveWithNumbersAsync(file, folder, "trailing-page-break-split.docx", TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
