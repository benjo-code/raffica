using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class ListCatalogTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static WordDocument NewDocument()
    {
        var story = new Story(new OoxmlRoot(W + "document", []));
        return new WordDocument(story)
        {
            Styles = new StyleSheet(new OoxmlRoot(W + "styles", [])),
            Numbering = new NumberingDefinitions(new OoxmlRoot(W + "numbering", [])),
        };
    }

    // An abstract definition with one level 0 of the given format, optionally linked to a style, and a w:num on it.
    private static void AddList(WordDocument document, int abstractId, int numId, string format, string? linkedStyle = null)
    {
        var abstractNumbering = new AbstractNumbering { AbstractNumId = abstractId, Position = abstractId };
        var level = new NumberingLevel { Level = 0, Start = 1, Format = format, Text = format == "bullet" ? "o" : "%1." };
        if (linkedStyle is not null)
        {
            level.KeptChildren.Add(new KeptChild(0, new UnknownContent(new XElement(W + "pStyle", new XAttribute(W + "val", linkedStyle)))));
        }

        abstractNumbering.Levels.Add(level);
        document.Numbering!.Abstracts.Add(abstractNumbering);
        document.Numbering.Instances.Add(new NumberingInstance { NumId = numId, AbstractNumId = abstractId, Position = 100 + numId });
    }

    private static void AddStyle(WordDocument document, string id, string name, int numId)
    {
        var style = new Style { Id = id, Type = "paragraph", Name = name };
        style.ParagraphProperties.Set(W + "numPr", new XElement(W + "numPr", new XElement(W + "numId", new XAttribute(W + "val", numId))));
        document.Styles!.Styles.Add(style);
    }

    private static Paragraph ListParagraph(int numId)
    {
        var paragraph = new Paragraph();
        paragraph.Properties.Set(W + "numPr", new XElement(
            W + "numPr",
            new XElement(W + "ilvl", new XAttribute(W + "val", 0)),
            new XElement(W + "numId", new XAttribute(W + "val", numId))));
        return paragraph;
    }

    [Fact]
    public void FindListPrefersTheStylePointedAt()
    {
        var document = NewDocument();
        AddList(document, 0, 3, "bullet");
        AddList(document, 1, 7, "bullet", "ListBullet");
        AddStyle(document, "ListBullet", "List Bullet", 7);

        Assert.Equal(7, new ListCatalog(document).FindList(ListKind.Bullet));
    }

    [Fact]
    public void FindListSkipsDefinitionsLinkedToStyles()
    {
        var document = NewDocument();
        AddList(document, 0, 1, "decimal", "Heading1");
        AddList(document, 1, 2, "bullet", "Quote");
        AddList(document, 2, 5, "decimal");
        AddList(document, 3, 6, "bullet");

        var catalog = new ListCatalog(document);

        Assert.Equal(5, catalog.FindList(ListKind.Numbered));
        Assert.Equal(6, catalog.FindList(ListKind.Bullet));
    }

    [Fact]
    public void FindListIsZeroWhenThereIsNone()
    {
        var document = NewDocument();
        AddList(document, 0, 1, "decimal");

        Assert.Equal(0, new ListCatalog(document).FindList(ListKind.Bullet));
    }

    [Fact]
    public void EnsureListAddsWritesOwnDefinitionOnce()
    {
        var document = NewDocument();
        AddList(document, 0, 1, "decimal");
        var catalog = new ListCatalog(document);

        var first = catalog.EnsureList(ListKind.Bullet);
        var second = catalog.EnsureList(ListKind.Bullet);

        Assert.Equal(first, second);
        Assert.Equal(2, document.Numbering!.Abstracts.Count);
        Assert.Equal(2, document.Numbering.Instances.Count);
        Assert.Equal(first, document.Numbering.Instances[^1].NumId);
        Assert.Equal("bullet", document.Numbering.FindAbstract(document.Numbering.FindInstance(first)!.AbstractNumId!.Value)!.Levels[0].Format);
    }

    [Fact]
    public void NewRunReusesAnUnreferencedCreatedInstance()
    {
        var document = NewDocument();
        var catalog = new ListCatalog(document);

        var first = catalog.NewRun(ListKind.Numbered);
        var second = catalog.NewRun(ListKind.Numbered);

        Assert.Equal(first, second);
        Assert.Single(document.Numbering!.Instances);
    }

    [Fact]
    public void NewRunNeverReusesAnInstanceThatIsReferenced()
    {
        var document = NewDocument();
        var catalog = new ListCatalog(document);
        var first = catalog.NewRun(ListKind.Numbered);
        document.Body.Blocks.Add(ListParagraph(first));

        var second = catalog.NewRun(ListKind.Numbered);

        Assert.NotEqual(first, second);
        Assert.True(catalog.IsReferenced(first));
        Assert.False(catalog.IsReferenced(second));
        Assert.Equal(
            document.Numbering!.FindInstance(first)!.AbstractNumId,
            document.Numbering.FindInstance(second)!.AbstractNumId);
    }

    [Fact]
    public void IsReferencedLooksIntoTablesControlsStylesNotesAndHeaders()
    {
        var document = NewDocument();
        AddList(document, 0, 1, "decimal");
        AddList(document, 1, 2, "decimal");
        AddList(document, 2, 3, "decimal");
        AddList(document, 3, 4, "decimal");
        AddList(document, 4, 5, "decimal");
        AddList(document, 5, 6, "decimal");

        var cell = new TableCell();
        cell.Blocks.Add(ListParagraph(1));
        var row = new TableRow();
        row.Items.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        document.Body.Blocks.Add(table);

        var control = new ContentControlBlock();
        control.Content.Add(ListParagraph(2));
        document.Body.Blocks.Add(control);

        AddStyle(document, "Mine", "Mine", 3);

        var footnotes = new NoteCollection(new OoxmlRoot(W + "footnotes", []));
        var noteStory = new Story(new OoxmlRoot(W + "footnote", []));
        noteStory.Blocks.Add(ListParagraph(4));
        footnotes.Notes.Add(new Note(1, NoteKind.Normal, noteStory));
        document.Footnotes = footnotes;

        var headerStory = new Story(new OoxmlRoot(W + "hdr", []));
        headerStory.Blocks.Add(ListParagraph(5));
        document.HeadersFooters["rId9"] = new HeaderFooterPart(headerStory);

        var catalog = new ListCatalog(document);

        Assert.True(catalog.IsReferenced(1));
        Assert.True(catalog.IsReferenced(2));
        Assert.True(catalog.IsReferenced(3));
        Assert.True(catalog.IsReferenced(4));
        Assert.True(catalog.IsReferenced(5));
        Assert.False(catalog.IsReferenced(6));
    }

    [Fact]
    public void RestartAddsAnInstanceWithStartOverride1()
    {
        var document = NewDocument();
        AddList(document, 0, 4, "decimal");
        var catalog = new ListCatalog(document);

        var restarted = catalog.Restart(4, 2);

        var instance = document.Numbering!.FindInstance(restarted)!;
        Assert.NotEqual(4, restarted);
        Assert.Equal(0, instance.AbstractNumId);
        var levelOverride = Assert.Single(instance.Overrides);
        Assert.Equal(2, levelOverride.Level);
        Assert.Equal(1, levelOverride.StartOverride);
    }

    [Fact]
    public void HeadingListAddsTheLinkedDefinitionOnceAndFindsItAgain()
    {
        var document = NewDocument();
        var catalog = new ListCatalog(document);

        var first = catalog.HeadingList();
        var second = new ListCatalog(document).HeadingList();

        Assert.Equal(first, second);
        var abstractNumbering = Assert.Single(document.Numbering!.Abstracts);
        Assert.Equal(9, abstractNumbering.Levels.Count);
        Assert.Equal("%1.%2.%3.", abstractNumbering.Levels[2].Text);
    }

    [Fact]
    public void WritesOwnDefinitionsEqualTheBlankDocumentsLevelByLevel()
    {
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var blank = file.Model.Numbering!;
        var document = NewDocument();
        var catalog = new ListCatalog(document);
        catalog.HeadingList();
        catalog.EnsureList(ListKind.Bullet);
        catalog.EnsureList(ListKind.Numbered);

        // Blank: 0 is the heading-shaped multilevel list, 1 the bullets, 2 the numbers. Write's were added in the order heading, bullet, number.
        for (var index = 0; index < 3; index++)
        {
            var expected = blank.FindAbstract(index)!;
            var actual = document.Numbering!.FindAbstract(index)!;
            Assert.Equal(expected.Levels.Count, actual.Levels.Count);
            Assert.Equal(MultiLevelType(expected), MultiLevelType(actual));
            for (var level = 0; level < expected.Levels.Count; level++)
            {
                var want = expected.Levels[level];
                var got = actual.Levels[level];
                Assert.Equal(want.Level, got.Level);
                Assert.Equal(want.Start, got.Start);
                Assert.Equal(want.Format, got.Format);
                Assert.Equal(want.Text, got.Text);
                Assert.Equal(want.Justification, got.Justification);
                Assert.Equal(Xml(want.ParagraphProperties), Xml(got.ParagraphProperties));
                Assert.Equal(Xml(want.RunProperties), Xml(got.RunProperties));
            }
        }

        // The heading definition adds the style links the blank one leaves out.
        var heading = document.Numbering!.FindAbstract(0)!;
        for (var level = 0; level < 9; level++)
        {
            var link = heading.Levels[level].KeptChildren.Single(k => k.Content.Element.Name == W + "pStyle");
            Assert.Equal("Heading" + (level + 1), (string?)link.Content.Element.Attribute(W + "val"));
        }
    }

    private static string? MultiLevelType(AbstractNumbering abstractNumbering) =>
        (string?)abstractNumbering.KeptChildren.Single(k => k.Content.Element.Name == W + "multiLevelType").Content.Element.Attribute(W + "val");

    // The children by name and attributes, without the namespace declarations a read element carries and a made one does not.
    private static string Xml(PropertyBag bag) => string.Join(
        ";",
        bag.Children.Select(c => c.Name.LocalName + "(" + string.Join(",", c.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName + "=" + a.Value)) + ")"));
}
