using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The gate of the field steps of milestone 4.5b (bookmarks now, captions, cross-references and tables of contents with the later tasks): over the whole corpus, every step is brought into the layout as the session does and
/// compared, exactly, with a full layout, and the bookmarks are checked after each step.
/// </summary>
public sealed class FieldOracleTests
{
    private const int StepsPerFile = 14;
    private const int StepsOnTheThesis = 4;

    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    /// <summary>The corpus files.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id);
        }

        return data;
    }

    // FNV-1a over the UTF-16 code units: stable across processes (string.GetHashCode is randomized per process).
    private static Random RandomFor(string name)
    {
        var hash = 2166136261u;
        foreach (var c in "fields " + name)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return new Random(unchecked((int)hash));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task FieldStepsEqualAFullLayoutOnTheCorpus(string name)
    {
        using var file = DocxFile.Open(Path.Combine(Root, name, name + ".docx"));
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var steps = new FieldSteps(file.Model, layout, new LayoutOptions());

        var compared = await steps.RunAsync(RandomFor(name), name == "thesis" ? StepsOnTheThesis : StepsPerFile, name);

        if (BookmarkOperations.ParagraphsOf(file.Model.Body.Blocks).Any())
        {
            Assert.True(compared > 0, $"{name}: no step changed anything");
        }

        TestContext.Current.SendDiagnosticMessage($"{name}: {compared} field steps compared, refused {steps.RefusedPerKind.Sum()}, bookmarks taken by a delete {steps.RemovedByDelete}");
    }

    // A guard that only the per-file runs carry would stay green if a kind were skipped everywhere.
    [Fact]
    public async Task EveryFieldStepKindRanOnTheCorpus()
    {
        var totals = new int[FieldSteps.Kinds];
        var removed = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files.Where(f => f.Id != "thesis"))
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var layout = await SectionOracleTests.LaidAsync(file.Model);
            var steps = new FieldSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(RandomFor("kinds " + entry.Id), 6, entry.Id);
            for (var k = 0; k < totals.Length; k++)
            {
                totals[k] += steps.RanPerKind[k];
            }

            removed += steps.RemovedByDelete;
        }

        foreach (var kind in Enum.GetValues<FieldStepKind>())
        {
            // A refusal kind: it never changes anything, so it is counted as refused (the unit tests of the protection run it on a table of contents).
            if (kind is FieldStepKind.TypeInToc or FieldStepKind.InsertChapterCaption)
            {
                continue;
            }

            Assert.True(totals[(int)kind] > 0, $"the step kind {kind} never ran a step that changed something in the corpus (ran per kind: {string.Join(", ", Enum.GetValues<FieldStepKind>().Select(k => k + " " + totals[(int)k]))})");
        }

        Assert.True(removed > 0, "no delete ever took a bookmark away with its text");
    }

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static string P(string inner, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr>{inner}</w:p>";

    // Headings, captions with SEQ (restarting per chapter), a chapter reference, references to a caption's bookmark and a note reference: results the refresh really writes.
    internal static string FieldsBody() =>
        P(Run("Chapter one"), "Heading1")
        + P(Run("See ") + Field("REF Cap1 \\h", "Figure 9") + Run(" and ") + Field("STYLEREF 1 \\s", "7"))
        + P("<w:bookmarkStart w:id=\"1\" w:name=\"Cap1\"/>" + Run("Figure ") + Field("SEQ Figure \\s 1", "5") + "<w:bookmarkEnd w:id=\"1\"/>" + Run(" a caption"))
        + P(Run("Some text") + "<w:r><w:footnoteReference w:id=\"3\"/></w:r>")
        + P(Run("Chapter two"), "Heading1")
        + P(Run("Figure ") + Field("SEQ Figure \\s 1", "5") + Run(" and table ") + Field("SEQ Table", "8"))
        + P("<w:bookmarkStart w:id=\"2\" w:name=\"Note1\"/><w:r><w:footnoteReference w:id=\"4\"/></w:r><w:bookmarkEnd w:id=\"2\"/>" + Run(" ") + Field("NOTEREF Note1", "9"))
        + P(Run("Figure ") + Field("SEQ Figure \\s 1", "5") + Run(" ") + Field("REF Cap1 \\h", "x"))
        + "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/>" + P(Run("Table ") + Field("SEQ Table", "4"), "Caption") + "</w:tc></w:tr></w:tbl>"
        + P("<w:sdt><w:sdtPr/><w:sdtContent>" + Run("Figure ") + Field("SEQ Figure \\s 1", "6") + "</w:sdtContent></w:sdt>" + Run(" ") + Field("SEQ Roman \\* roman", "x") + Run(" ") + Field("SEQ Roman \\* roman", "x"))
        + P(Run("Again ") + Field("SEQ Figure \\c", "2") + Run(" ") + Field("DATE", "today") + Run(" ") + Field("REF Missing \\h", "kept"))
        + "<w:sdt><w:sdtPr/><w:sdtContent>" + P(Run("Table ") + Field("SEQ Table", "3"), "Caption") + "</w:sdtContent></w:sdt>"
        + P(Run("end"))
        + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";

    private static WordDocument WithFields()
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 3; i++)
        {
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.Document(FieldsBody(), LayoutModel.StylesPart(styles));
    }

    private static string TocBody()
    {
        var begin = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>";
        var end = "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        return P(Run("Intro"))
            + P(begin + Run("Entry A")) + P(Run("Entry B")) + P(Run("Entry C") + end)
            + FieldsBody().Replace("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>", string.Empty, StringComparison.Ordinal)
            + "<w:sdt><w:sdtPr/><w:sdtContent>" + P(Run("Contents")) + P(begin + Run("E1")) + P(Run("E2") + end) + "</w:sdtContent></w:sdt>"
            + P(Run("last"))
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";
    }

    // A document with a table of contents at the top level and one in a block content control: the steps type into them (a counted refusal each) and the editor's kept answer must equal a fresh search after every step.
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public async Task TypingInATocIsARefusalAndTheKeptAnswerStaysTrue(int seed)
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>";
        var model = LayoutModel.Document(TocBody(), LayoutModel.StylesPart(styles));
        var layout = await SectionOracleTests.LaidAsync(model);
        var steps = new FieldSteps(model, layout, new LayoutOptions());
        Assert.True(steps.Editor.TocRangesOf(model.Body.Blocks) is { Count: 1 }, "the document has one table of contents at the top level");

        await steps.RunAsync(new Random(seed), 60, "toc " + seed);

        Assert.True(steps.RefusedPerKind[(int)FieldStepKind.TypeInToc] > 0, "no step typed into a table of contents");
    }

    // Numbered headings (the style carries the list): the chapter caption is possible here, and every caption step ends in an exact layout and the independent count.
    private static WordDocument WithNumberedHeadings()
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/></w:style>";
        for (var i = 1; i <= 3; i++)
        {
            var number = i == 1 ? "<w:numPr><w:numId w:val=\"1\"/></w:numPr>" : string.Empty;
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{number}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(FieldsBody(), LayoutModel.StylesPart(styles), LayoutModel.NumberingPart(numbering));
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public async Task CaptionStepsOnADocumentWithNumberedHeadingsEqualAFullLayoutAndTheReferenceCount(int seed)
    {
        var model = WithNumberedHeadings();
        var layout = await SectionOracleTests.LaidAsync(model);
        var steps = new FieldSteps(model, layout, new LayoutOptions());

        await steps.RunAsync(new Random(seed), 40, "captions " + seed);

        foreach (var kind in new[] { FieldStepKind.InsertCaption, FieldStepKind.InsertChapterCaption, FieldStepKind.MoveCaption, FieldStepKind.DeleteCaption, FieldStepKind.InsertCrossReference, FieldStepKind.DeleteTarget })
        {
            Assert.True(steps.RanPerKind[(int)kind] > 0, $"the step kind {kind} never changed anything (ran per kind: {string.Join(", ", Enum.GetValues<FieldStepKind>().Select(k => k + " " + steps.RanPerKind[(int)k]))})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RefreshStepsOnADocumentWithFieldsEqualAFullLayoutAndTheReferenceCount(int seed)
    {
        var model = WithFields();
        var layout = await SectionOracleTests.LaidAsync(model);
        var steps = new FieldSteps(model, layout, new LayoutOptions());

        await steps.RunAsync(new Random(seed), 20, "with fields " + seed);

        Assert.True(steps.RanPerKind[(int)FieldStepKind.Refresh] > 0);
        Assert.True(steps.Written > 0, "no refresh wrote a result");
    }
}
