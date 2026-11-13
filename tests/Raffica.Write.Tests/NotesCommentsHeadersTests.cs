using System.Text;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class NotesCommentsHeadersTests
{
    private static readonly XNamespace W = XmlSnippets.W;
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    private const string Declarations =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" xmlns:x=\"urn:test\" mc:Ignorable=\"w14 w15\"";

    private static string Thesis => Path.Combine(TestData.Root, "spikes", "write", "testdoc", "thesis.docx");

    private static string Wrap(string root, string inner) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:{root} {Declarations}>{inner}</w:{root}>";

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private static void AssertEquivalent(string xml, byte[] written)
    {
        var text = Encoding.UTF8.GetString(written);
        Assert.False(written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF, "no BOM");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", text, StringComparison.Ordinal);
        var difference = XmlEquivalence.Explain(XElement.Parse(xml), XElement.Parse(text));
        Assert.True(difference is null, difference + "\n" + text);
    }

    private static (NoteCollection Model, byte[] Written) NotesRoundTrip(byte[] source, string root)
    {
        using var input = new MemoryStream(source);
        var model = NotesXml.Read(input, root);
        using var output = new MemoryStream();
        NotesXml.Write(model, output);
        AssertEquivalent(Encoding.UTF8.GetString(source), output.ToArray());
        return (model, output.ToArray());
    }

    private static (CommentCollection Model, byte[] Comments, byte[]? Extended) CommentsRoundTrip(string comments, string? extended)
    {
        using var input = new MemoryStream(Bytes(comments));
        using var extendedInput = extended is null ? null : new MemoryStream(Bytes(extended));
        var model = CommentsXml.Read(input, extendedInput);
        using var output = new MemoryStream();
        using var extendedOutput = new MemoryStream();
        var needed = CommentsXml.Write(model, output, extendedOutput);
        AssertEquivalent(comments, output.ToArray());
        if (extended is not null)
        {
            Assert.True(needed);
            AssertEquivalent(extended, extendedOutput.ToArray());
        }

        return (model, output.ToArray(), needed ? extendedOutput.ToArray() : null);
    }

    private static void CollectReferences(IEnumerable<Block> blocks, List<int> ids)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    CollectReferences(paragraph.Inlines, ids);
                    break;
                case Table table:
                    foreach (var item in table.Rows)
                    {
                        CollectRowItem(item, ids);
                    }

                    break;
                case ContentControlBlock control:
                    CollectReferences(control.Content, ids);
                    break;
            }
        }
    }

    private static void CollectRowItem(RowItem item, List<int> ids)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    CollectRowItem(child, ids);
                }

                break;
            case TableCell cell:
                CollectReferences(cell.Blocks, ids);
                break;
        }
    }

    private static void CollectReferences(IEnumerable<Inline> inlines, List<int> ids)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    ids.AddRange(run.Content.OfType<FootnoteReference>().Select(r => r.Id));
                    break;
                case Hyperlink link:
                    CollectReferences(link.Inlines, ids);
                    break;
                case SimpleField field:
                    CollectReferences(field.Inlines, ids);
                    break;
                case TrackedChange change:
                    CollectReferences(change.Inlines, ids);
                    break;
                case ContentControlInline control:
                    CollectReferences(control.Inlines, ids);
                    break;
            }
        }
    }

    private static Paragraph Para(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    private static Story NewStory(string root) => new(new OoxmlRoot(W + root, []));

    [Fact]
    public void FootnotesKeepSeparatorsAndRealNotes()
    {
        var (model, _) = NotesRoundTrip(PartComparer.ReadPart(Thesis, "word/footnotes.xml"), "footnotes");

        Assert.Equal(102, model.Notes.Count);
        Assert.Equal(NoteKind.Separator, model.Find(-1)!.Kind);
        Assert.Equal(NoteKind.ContinuationSeparator, model.Find(0)!.Kind);
        Assert.Equal(100, model.Notes.Count(n => n.Kind == NoteKind.Normal));
        Assert.Equal(Enumerable.Range(1, 100), model.Notes.Where(n => n.Kind == NoteKind.Normal).Select(n => n.Id));
        Assert.Null(model.Find(101));

        var body = DocumentXml.Read(new MemoryStream(PartComparer.ReadPart(Thesis, "word/document.xml"))).Blocks;
        var ids = new List<int>();
        CollectReferences(body, ids);
        Assert.Equal(100, ids.Count);
        Assert.All(ids, id => Assert.NotNull(model.Find(id)));
    }

    [Fact]
    public void EndnotesUseTheirOwnRoot()
    {
        var xml = Wrap("endnotes", """<w:endnote w:type="separator" w:id="-1"><w:p/></w:endnote><w:endnote w:id="1"><w:p><w:r><w:t>e</w:t></w:r></w:p></w:endnote>""");
        var (model, _) = NotesRoundTrip(Bytes(xml), "endnotes");
        Assert.Equal(2, model.Notes.Count);
        Assert.Equal("e", Assert.IsType<TextContent>(Assert.IsType<Run>(Assert.IsType<Paragraph>(model.Find(1)!.Content.Blocks[0]).Inlines[0]).Content[0]).Text);
        Assert.Throws<System.Xml.XmlException>(() => NotesXml.Read(new MemoryStream(Bytes(xml)), "footnotes"));
    }

    [Fact]
    public void NoteTypesAndOddIdsSurviveAsRead()
    {
        var xml = Wrap(
            "footnotes",
            """<w:footnote w:type="made-up" w:id="2" x:a="1"><w:p/></w:footnote><w:footnote w:type="normal" w:id="3"><w:p/></w:footnote><w:footnote w:type="continuationNotice" w:id="4"><w:p/></w:footnote><w:footnote w:id="007"><w:p/></w:footnote><w:footnote><w:p/></w:footnote>""");
        var (model, _) = NotesRoundTrip(Bytes(xml), "footnotes");
        Assert.Equal(3, model.Notes.Count);
        Assert.Equal(NoteKind.Normal, model.Find(2)!.Kind);
        Assert.Equal(2, model.Find(2)!.UnknownAttributes.Count(a => !a.IsNamespaceDeclaration));
        Assert.Equal(NoteKind.Normal, model.Find(3)!.Kind);
        Assert.Equal(NoteKind.ContinuationNotice, model.Find(4)!.Kind);
        Assert.Equal(2, model.KeptChildren.Count);
    }

    [Fact]
    public void UnknownContentSurvivesInNotesAndComments()
    {
        var notes = Wrap(
            "footnotes",
            """<x:before/><w:footnote w:id="1" x:attr="n"><x:in-note/><w:p w14:paraId="0000AAAA" x:p="1"><x:in-paragraph a="1"/><w:r><w:t>one</w:t></w:r></w:p><x:between-blocks/></w:footnote><x:between/><w:footnote w:id="2"><w:p/></w:footnote><x:after w14:z="1"/>""");
        var (noteModel, _) = NotesRoundTrip(Bytes(notes), "footnotes");
        Assert.Equal(2, noteModel.Notes.Count);
        Assert.Equal(3, noteModel.KeptChildren.Count);
        Assert.Equal(3, noteModel.Find(1)!.Content.Blocks.Count);
        Assert.IsType<UnknownBlock>(noteModel.Find(1)!.Content.Blocks[0]);

        var comments = Wrap(
            "comments",
            """<x:before/><w:comment w:id="0" w:author="Ann" w:date="2024-05-01T10:00:00Z" w:initials="A" x:attr="c"><x:in-comment/><w:p w14:paraId="0000BBBB" w14:textId="77777777" x:p="1"><x:in-paragraph a="1"/><w:r><w:t>hi</w:t></w:r></w:p></w:comment><x:between/><w:comment w:id="1" w:author="Bob"><w:p/></w:comment><x:after/>""");
        var (commentModel, _, _) = CommentsRoundTrip(comments, null);
        Assert.Equal(2, commentModel.Comments.Count);
        Assert.Equal(3, commentModel.KeptChildren.Count);
        Assert.Equal("0000BBBB", commentModel.Find(0)!.ParaId);
        Assert.Equal(1, commentModel.Find(0)!.UnknownAttributes.Count(a => !a.IsNamespaceDeclaration));
    }

    [Fact]
    public void CommentsJoinTheExtendedPart()
    {
        var comments = Wrap(
            "comments",
            """<w:comment w:id="0" w:author="Ann" w:initials="A" w:date="2024-05-01T10:00:00Z"><w:p w14:paraId="11111111"><w:r><w:t>Question</w:t></w:r></w:p></w:comment><w:comment w:id="1" w:author="Bob" w:initials="B" w:date="2024-05-02T09:30:00+02:00"><w:p w14:paraId="22222222"><w:r><w:t>first</w:t></w:r></w:p><w:p w14:paraId="33333333"><w:r><w:t>Answer</w:t></w:r></w:p></w:comment><w:comment w:id="2" w:author="Cy"><w:p w14:paraId="44444444"/></w:comment>""");
        var extended = Wrap(
            "commentsEx",
            """<w15:commentEx w15:paraId="11111111" w15:done="1" x:keep="k"/><w15:commentEx w15:paraId="33333333" w15:paraIdParent="11111111" w15:done="0"/><x:unknown/><w15:commentEx w15:paraId="99999999" w15:done="1"/>""");
        var (model, _, extendedWritten) = CommentsRoundTrip(comments, extended);

        var question = model.Find(0)!;
        Assert.True(question.Done);
        Assert.Null(question.ParentParaId);
        Assert.Equal("11111111", question.ParaId);
        Assert.Equal("Ann", question.Author);
        Assert.Equal("A", question.Initials);
        Assert.Equal("2024-05-01T10:00:00Z", question.DateText);
        Assert.Equal(DateTimeOffset.Parse("2024-05-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture), question.Date);

        var answer = model.Find(1)!;
        Assert.False(answer.Done);
        Assert.Equal("33333333", answer.ParaId);
        Assert.Equal("11111111", answer.ParentParaId);
        Assert.Equal("2024-05-02T09:30:00+02:00", answer.DateText);

        var plain = model.Find(2)!;
        Assert.False(plain.Done);
        Assert.Null(plain.ParentParaId);
        Assert.NotNull(extendedWritten);

        // The entry that matches no comment and the unknown element are still there.
        var entries = XElement.Parse(Encoding.UTF8.GetString(extendedWritten)).Elements().ToList();
        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, e => (string?)e.Attribute(W15 + "paraId") == "99999999");
        Assert.DoesNotContain(entries, e => (string?)e.Attribute(W15 + "paraId") == "44444444");
    }

    [Fact]
    public void ACommentWithoutExtendedStateWritesNoExtendedPart()
    {
        var comments = Wrap("comments", """<w:comment w:id="0" w:author="Ann"><w:p w14:paraId="11111111"/></w:comment>""");
        var (model, _, extended) = CommentsRoundTrip(comments, null);
        Assert.Null(extended);
        Assert.Null(model.ExtendedRoot);

        // State set in code makes the part needed; a comment that has none still gets no entry.
        model.Find(0)!.Done = true;
        var added = new Comment(1, NewStory("comment")) { Author = "Bob" };
        added.Content.Blocks.Add(Para("x"));
        model.Comments.Add(added);
        using var plain = new MemoryStream();
        using var ex = new MemoryStream();
        Assert.True(CommentsXml.Write(model, plain, ex));
        var entries = XElement.Parse(Encoding.UTF8.GetString(ex.ToArray())).Elements().ToList();
        var entry = Assert.Single(entries);
        Assert.Equal("1", (string?)entry.Attribute(W15 + "done"));
        Assert.Equal("11111111", (string?)entry.Attribute(W15 + "paraId"));
    }

    [Fact]
    public void ExtendedStateFollowsDoneAndSpelling()
    {
        var comments = Wrap("comments", """<w:comment w:id="0"><w:p w14:paraId="11111111"/></w:comment><w:comment w:id="1"><w:p w14:paraId="22222222"/></w:comment>""");
        var extended = Wrap("commentsEx", """<w15:commentEx w15:paraId="11111111" w15:done="true"/><w15:commentEx w15:paraId="22222222"/>""");
        var (model, _, _) = CommentsRoundTrip(comments, extended);
        Assert.True(model.Find(0)!.Done);
        Assert.False(model.Find(1)!.Done);

        model.Find(0)!.Done = false;
        model.Find(1)!.Done = true;
        using var plain = new MemoryStream();
        using var ex = new MemoryStream();
        CommentsXml.Write(model, plain, ex);
        var entries = XElement.Parse(Encoding.UTF8.GetString(ex.ToArray())).Elements().ToList();
        Assert.Equal("0", (string?)entries[0].Attribute(W15 + "done"));
        Assert.Equal("1", (string?)entries[1].Attribute(W15 + "done"));
    }

    [Fact]
    public void AnEditedReadModelWritesNewItemsAfterTheNotesAndReadsBack()
    {
        var notes = Wrap("footnotes", """<w:footnote w:type="separator" w:id="-1"><w:p/></w:footnote><w:footnote w:id="1"><w:p/></w:footnote><x:tail/>""");
        var (model, _) = NotesRoundTrip(Bytes(notes), "footnotes");
        var note = new Note(2, NoteKind.Normal, NewStory("footnote"));
        note.Content.Blocks.Add(Para("added"));
        model.Notes.Add(note);
        using var output = new MemoryStream();
        NotesXml.Write(model, output);
        var written = XElement.Parse(Encoding.UTF8.GetString(output.ToArray()));
        Assert.Equal(["footnote", "footnote", "footnote", "tail"], written.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("2", (string?)written.Elements().ElementAt(2).Attribute(W + "id"));

        var again = NotesXml.Read(new MemoryStream(output.ToArray()), "footnotes");
        Assert.Equal(3, again.Notes.Count);
        Assert.Equal(NoteKind.Normal, again.Find(2)!.Kind);
        Assert.Equal(NoteKind.Separator, again.Find(-1)!.Kind);

        var comments = Wrap("comments", """<w:comment w:id="0" w:author="Ann"><w:p w14:paraId="11111111"/></w:comment><x:tail/>""");
        var (commentModel, _, _) = CommentsRoundTrip(comments, null);
        var added = new Comment(1, NewStory("comment")) { Author = "Bob", Initials = "B", DateText = "2024-06-01T00:00:00Z", ParentParaId = "11111111" };
        var paragraph = Para("reply");
        paragraph.UnknownAttributes.Add(new XAttribute(W14 + "paraId", "55555555"));
        added.Content.Blocks.Add(paragraph);
        commentModel.Comments.Add(added);
        using var commentsOut = new MemoryStream();
        using var extendedOut = new MemoryStream();
        Assert.True(CommentsXml.Write(commentModel, commentsOut, extendedOut));
        Assert.Equal(["comment", "comment", "tail"], XElement.Parse(Encoding.UTF8.GetString(commentsOut.ToArray())).Elements().Select(e => e.Name.LocalName));

        var back = CommentsXml.Read(new MemoryStream(commentsOut.ToArray()), new MemoryStream(extendedOut.ToArray()));
        Assert.Equal(2, back.Comments.Count);
        Assert.Equal("55555555", back.Find(1)!.ParaId);
        Assert.Equal("11111111", back.Find(1)!.ParentParaId);
        Assert.Equal("B", back.Find(1)!.Initials);
        Assert.False(back.Find(1)!.Done);
    }

    [Fact]
    public void HeadersAndFootersAreStories()
    {
        var bytes = PartComparer.ReadPart(Thesis, "word/footer1.xml");
        var part = StoryXml.ReadPart(new MemoryStream(bytes));
        Assert.Equal(HeaderFooterKind.Footer, part.Kind);
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(part.Content.Blocks));
        var field = Assert.Single(FieldScanner.Scan(paragraph.Inlines));
        Assert.Equal("PAGE", field.Instruction);
        Assert.True(field.IsComplete);

        using var output = new MemoryStream();
        StoryXml.WritePart(part, output);
        AssertEquivalent(Encoding.UTF8.GetString(bytes), output.ToArray());

        foreach (var name in new[] { "word/header1.xml", "word/header2.xml", "word/footer2.xml" })
        {
            var source = PartComparer.ReadPart(Thesis, name);
            var read = StoryXml.ReadPart(new MemoryStream(source));
            Assert.Equal(name.Contains("header", StringComparison.Ordinal) ? HeaderFooterKind.Header : HeaderFooterKind.Footer, read.Kind);
            using var written = new MemoryStream();
            StoryXml.WritePart(read, written);
            AssertEquivalent(Encoding.UTF8.GetString(source), written.ToArray());
        }
    }

    [Fact]
    public void AHeaderKeepsItsRootAndUnknownContent()
    {
        var xml = Wrap("hdr", """<w:p w14:paraId="12345678" x:a="1"><x:u/><w:r><w:t xml:space="preserve"> h </w:t></w:r></w:p><x:block/>""");
        var part = StoryXml.ReadPart(new MemoryStream(Bytes(xml)));
        Assert.Equal(HeaderFooterKind.Header, part.Kind);
        Assert.Equal(2, part.Content.Blocks.Count);
        using var output = new MemoryStream();
        StoryXml.WritePart(part, output);
        AssertEquivalent(xml, output.ToArray());

        Assert.Throws<System.Xml.XmlException>(() => StoryXml.ReadPart(new MemoryStream(Bytes(Wrap("document", "<w:body/>")))));
    }
}
