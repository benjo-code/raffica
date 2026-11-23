using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class SectionListTests
{
    private const string Break = "<w:p><w:pPr><w:sectPr/></w:pPr></w:p>";

    private static readonly XName TitlePage = XName.Get("titlePg", LayoutModel.W);

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string P(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static WordDocument Document(string body) => LayoutModel.Document(body + "<w:sectPr/>");

    private static void Add(SectionProperties section, HeaderFooterKind kind, HeaderFooterType type, string id) =>
        section.HeaderFooterReferences.Add(new HeaderFooterReference(kind, type, id));

    private static (string? Id, int Owner) Describe((HeaderFooterReference? Reference, int OwnerSection) result) => (result.Reference?.RelationshipId, result.OwnerSection);

    [Fact]
    public void SectionsEndAtParagraphsWithBreaksAndTheFinalProperties()
    {
        var document = Document(P("a") + Break + P("b") + P("c") + Break + P("d"));
        var sections = SectionList.Of(document);

        Assert.Equal(3, sections.Count);
        Assert.Equal([0, 1, 2], sections.Select(s => s.Index));
        Assert.Equal([(0, 1), (2, 4), (5, 5)], sections.Select(s => (s.FirstBlock, s.LastBlock)));
        Assert.Same(((Paragraph)document.Body.Blocks[1]).SectionBreak, sections[0].Properties);
        Assert.Same(document.Body.Blocks[1], sections[0].Break);
        Assert.Same(document.Body.Blocks[4], sections[1].Break);
        Assert.Null(sections[2].Break);
        Assert.Same(document.Body.FinalSection, sections[2].Properties);
    }

    [Fact]
    public void ADocumentWithNoBreakIsOneSectionAndAnEmptyFinalSectionIsEmpty()
    {
        var one = SectionList.Of(Document(P("a") + P("b")));
        Assert.Equal([(0, 1)], one.Select(s => (s.FirstBlock, s.LastBlock)));

        var empty = SectionList.Of(Document(P("a") + Break));
        Assert.Equal([(0, 1), (2, 1)], empty.Select(s => (s.FirstBlock, s.LastBlock)));
    }

    [Fact]
    public void BlocksInAContentControlBelongToTheControlsTopLevelIndex()
    {
        var control = "<w:sdt><w:sdtContent>" + P("x") + Break + P("y") + "</w:sdtContent></w:sdt>";
        var document = Document(P("a") + control + P("b"));
        var sections = SectionList.Of(document);

        Assert.Equal([(0, 1), (1, 2)], sections.Select(s => (s.FirstBlock, s.LastBlock)));
        Assert.NotNull(sections[0].Break);
    }

    [Fact]
    public void AMissingReferenceInheritsFromThePreviousSection()
    {
        var document = Document(Break + Break + P("c"));
        var sections = SectionList.Of(document);
        Add(sections[0].Properties, HeaderFooterKind.Header, HeaderFooterType.Default, "rIdA");
        Add(sections[1].Properties, HeaderFooterKind.Footer, HeaderFooterType.Default, "rIdF");
        Add(sections[2].Properties, HeaderFooterKind.Header, HeaderFooterType.Default, "rIdB");

        Assert.Equal(("rIdA", 0), Describe(SectionList.Resolve(sections, 1, HeaderFooterKind.Header, HeaderFooterType.Default)));
        Assert.Equal(("rIdB", 2), Describe(SectionList.Resolve(sections, 2, HeaderFooterKind.Header, HeaderFooterType.Default)));
        Assert.Equal(("rIdF", 1), Describe(SectionList.Resolve(sections, 2, HeaderFooterKind.Footer, HeaderFooterType.Default)));
        Assert.Equal((null, -1), Describe(SectionList.Resolve(sections, 2, HeaderFooterKind.Header, HeaderFooterType.Even)));
    }

    [Fact]
    public void TheFirstSectionHasNoInheritance()
    {
        var document = Document(Break + P("b"));
        var sections = SectionList.Of(document);
        Add(sections[1].Properties, HeaderFooterKind.Header, HeaderFooterType.Default, "rIdA");

        Assert.Equal((null, -1), Describe(SectionList.Resolve(sections, 0, HeaderFooterKind.Header, HeaderFooterType.Default)));
        Assert.Equal(("rIdA", 1), Describe(SectionList.Resolve(sections, 1, HeaderFooterKind.Header, HeaderFooterType.Default)));
    }

    [Fact]
    public void FirstPageWithoutAFirstReferenceFollowsWord()
    {
        // Section 2 has w:titlePg and no first-page header of its own: it shows the first-page header of the section before it, and none when no section has one.
        var document = Document(Break + P("b"));
        var sections = SectionList.Of(document);
        document.Body.FinalSection!.SetFlag(TitlePage, true);
        Add(sections[0].Properties, HeaderFooterKind.Header, HeaderFooterType.Default, "rIdD");
        Assert.Equal((null, -1), Describe(SectionList.Resolve(sections, 1, HeaderFooterKind.Header, HeaderFooterType.First)));

        Add(sections[0].Properties, HeaderFooterKind.Header, HeaderFooterType.First, "rIdFirst");
        Assert.Equal(("rIdFirst", 0), Describe(SectionList.Resolve(sections, 1, HeaderFooterKind.Header, HeaderFooterType.First)));

        Add(sections[1].Properties, HeaderFooterKind.Header, HeaderFooterType.First, "rIdOwn");
        Assert.Equal(("rIdOwn", 1), Describe(SectionList.Resolve(sections, 1, HeaderFooterKind.Header, HeaderFooterType.First)));
    }

    [Fact]
    public void SectionListAgreesWithTheLayoutOnEveryCorpusFile()
    {
        var pages = 0;
        var sectionsSeen = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var document = file.Model;
            var sections = SectionList.Of(document);

            var flat = FlatBlocks.From(document.Body.Blocks);
            var plans = SectionPlan.Build(flat, document.Body.FinalSection, []);
            Assert.True(plans.Count == sections.Count, $"{entry.Id}: {plans.Count} planned sections, {sections.Count} listed");
            for (var s = 0; s < plans.Count; s++)
            {
                var plan = plans[s];
                var info = sections[s];
                if (plan.IsEmpty)
                {
                    Assert.True(info.LastBlock == info.FirstBlock - 1, $"{entry.Id}: section {s} should be empty");
                }
                else
                {
                    Assert.True(flat[plan.FirstBlock].BodyIndex == info.FirstBlock && flat[plan.EndBlock - 1].BodyIndex == info.LastBlock, $"{entry.Id}: the range of section {s} differs");
                }

                if (plan.Properties is not null)
                {
                    Assert.Same(plan.Properties, info.Properties);
                }
            }

            sectionsSeen += sections.Count;
            var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
            var drawn = DrawnPageNumbers.Read(layout, document);
            var evenAndOdd = document.Settings?.EvenAndOddHeaders ?? false;
            for (var p = 0; p < layout.LaidOutPages; p++)
            {
                var section = drawn[p].Section;
                var firstOfSection = p == 0 || drawn[p - 1].Section != section;
                var titlePage = sections[section].Properties.GetFlag(TitlePage);
                HeaderFooterType[] types = titlePage && firstOfSection
                    ? [HeaderFooterType.First]
                    : evenAndOdd ? [HeaderFooterType.Default, HeaderFooterType.Even] : [HeaderFooterType.Default];
                var chrome = layout.Page(p).Chrome;
                foreach (var (kind, drawnId) in new[] { (HeaderFooterKind.Header, chrome?.HeaderPart), (HeaderFooterKind.Footer, chrome?.FooterPart) })
                {
                    var allowed = types.Select(t => SectionList.Resolve(sections, section, kind, t).Reference?.RelationshipId).ToList();
                    Assert.True(allowed.Contains(drawnId), $"{entry.Id} page {p + 1} {kind}: drew {drawnId ?? "none"}, expected one of {string.Join(", ", allowed.Select(a => a ?? "none"))}");
                    pages++;
                }
            }
        }

        Assert.True(sectionsSeen > 10, $"only {sectionsSeen} sections were compared");
        Assert.True(pages > 100, $"only {pages} header and footer slots were compared");
    }
}
