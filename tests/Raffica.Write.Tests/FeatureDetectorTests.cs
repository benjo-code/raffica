using System.Text;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests;

/// <summary>Every required feature tag is decided by the rule documented in <c>FeatureDetector</c>; each is asserted here on a minimal document.</summary>
public sealed class FeatureDetectorTests
{
    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Run = "<w:p><w:r><w:t>x</w:t></w:r></w:p>";

    private sealed record Case(string Body, (string Part, string Xml)[] Parts, string[] Expected);

    private static (string Part, string Xml) Part(string name, string root, string content) =>
        ("word/" + name + ".xml", $"<w:{root} xmlns:w=\"{WNs}\">{content}</w:{root}>");

    private static readonly Dictionary<string, Case> s_cases = new()
    {
        ["paragraphs-runs"] = new(Run, [], ["paragraphs-runs"]),
        ["styles"] = new("<w:p/>", [Part("styles", "styles", "<w:style w:styleId=\"A\"/><w:style w:styleId=\"B\"/>")], ["styles"]),
        ["numbering"] = new(
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:p>",
            [Part("numbering", "numbering", "<w:abstractNum w:abstractNumId=\"0\"/>")],
            ["numbering"]),
        ["sections"] = new("<w:p><w:pPr><w:sectPr/></w:pPr></w:p><w:sectPr/>", [], ["sections"]),
        ["tables"] = new("<w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>", [], ["tables"]),
        ["merged-cells"] = new("<w:p><w:tcPr><w:vMerge/></w:tcPr></w:p>", [], ["merged-cells"]),
        ["footnotes"] = new("<w:p/>", [Part("footnotes", "footnotes", "<w:footnote w:id=\"1\"><w:p/></w:footnote>")], ["footnotes"]),
        ["endnotes"] = new("<w:p/>", [Part("endnotes", "endnotes", "<w:endnote w:id=\"1\" w:type=\"normal\"><w:p/></w:endnote>")], ["endnotes"]),
        ["fields"] = new("<w:p><w:fldSimple w:instr=\" PAGE \"/></w:p>", [], ["fields"]),
        ["toc"] = new("<w:p><w:fldSimple w:instr=\" TOC \\o &quot;1-3&quot; \"/></w:p>", [], ["fields", "toc"]),
        ["comments"] = new("<w:p><w:commentReference w:id=\"0\"/></w:p>", [], ["comments"]),
        ["tracked-changes"] = new("<w:p><w:del w:id=\"1\"/></w:p>", [], ["tracked-changes"]),
        ["pictures-inline"] = new("<w:p><w:drawing><wp:inline/></w:drawing></w:p>", [], ["pictures-inline"]),
        ["pictures-floating"] = new("<w:p><w:drawing><wp:anchor/></w:drawing></w:p>", [], ["pictures-floating"]),
        ["text-boxes"] = new("<w:p><w:txbxContent><w:p/></w:txbxContent></w:p>", [], ["text-boxes"]),
        ["headers-footers"] = new("<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId9\"/></w:sectPr>", [], ["headers-footers"]),
        ["hyperlinks"] = new("<w:p><w:hyperlink w:anchor=\"b\"/></w:p>", [], ["hyperlinks"]),
        ["bookmarks"] = new("<w:p><w:bookmarkStart w:id=\"1\" w:name=\"b\"/></w:p>", [], ["bookmarks"]),
        ["content-controls"] = new("<w:sdt><w:sdtContent><w:p/></w:sdtContent></w:sdt>", [], ["content-controls"]),
        ["equations"] = new("<w:p><m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"/></w:p>", [], ["equations"]),
        ["rtl-text"] = new("<w:p><w:pPr><w:bidi/></w:pPr></w:p>", [], ["rtl-text"]),
    };

    public static TheoryData<string> AllRequiredTags() => [.. CorpusFeatures.Required];

    private static IReadOnlySet<string> DetectIn(string body, params (string Part, string Xml)[] parts)
    {
        using var folder = new TempFolder();
        var path = folder.File("t.docx");
        DocxBuilder.WithBody(path, body);
        foreach (var (name, xml) in parts)
        {
            DocxBuilder.EditInPlace(path, name, Encoding.UTF8.GetBytes(xml));
        }

        return FeatureDetector.Detect(path);
    }

    [Fact]
    public void EveryRequiredTagHasACase() => Assert.Equal(CorpusFeatures.Required.Order(StringComparer.Ordinal), s_cases.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(AllRequiredTags))]
    public void ATagIsFoundInAMinimalDocumentAndNothingElseIsImplied(string tag)
    {
        var scenario = s_cases[tag];
        var found = DetectIn(scenario.Body, scenario.Parts);
        Assert.Contains(tag, found);
        Assert.True(found.SetEquals(scenario.Expected), "found: " + string.Join(", ", found.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void ASeparatorOnlyNotesPartGivesNoNotes()
    {
        var found = DetectIn(
            "<w:p/>",
            Part("footnotes", "footnotes", "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p/></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p/></w:footnote>"),
            Part("endnotes", "endnotes", "<w:endnote w:type=\"continuationNotice\" w:id=\"-1\"><w:p/></w:endnote>"));
        Assert.Empty(found);
    }

    [Fact]
    public void AFootnotePartDoesNotGiveEndnotes()
    {
        var found = DetectIn("<w:p/>", Part("footnotes", "footnotes", "<w:footnote w:id=\"1\"><w:p/></w:footnote>"));
        Assert.Contains("footnotes", found);
        Assert.DoesNotContain("endnotes", found);
    }

    [Fact]
    public void AOneStyleStylesPartGivesNoStyles() =>
        Assert.Empty(DetectIn("<w:p/>", Part("styles", "styles", "<w:style w:styleId=\"Normal\"/>")));

    [Fact]
    public void ANumberingPartWithoutUseGivesNoNumbering() =>
        Assert.Empty(DetectIn("<w:p/>", Part("numbering", "numbering", "<w:abstractNum w:abstractNumId=\"0\"/>")));

    [Fact]
    public void ANumberingUseWithoutANumberingPartGivesNoNumbering() =>
        Assert.Empty(DetectIn("<w:p><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:p>"));

    [Fact]
    public void OneSectionPropertiesGivesNoSections() =>
        Assert.Empty(DetectIn("<w:p/><w:sectPr/>"));

    [Fact]
    public void AnInstructionElementFollowedByASiblingDoesNotSkipTheSibling()
    {
        // The instruction text is read as element content; the reader then stands on the next node, which must still be looked at.
        var found = DetectIn("<w:p><w:r><w:instrText>PAGE</w:instrText><wp:inline/></w:r></w:p>");
        Assert.Contains("fields", found);
        Assert.Contains("pictures-inline", found);
    }
}
