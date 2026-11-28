using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>What a paste keeps of a field: a complete SEQ or STYLEREF stays a field, everything else becomes its result text; no bookmark is ever pasted.</summary>
public sealed class FieldClipboardTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Run Mark(FieldCharKind kind)
    {
        var run = new Run();
        run.Content.Add(new FieldChar(kind));
        return run;
    }

    private static Run Code(string text)
    {
        var run = new Run();
        run.Content.Add(new InstructionText(text));
        return run;
    }

    private static Run Text(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Paragraph Field(string before, string instruction, string result, bool end = true)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(Text(before));
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        paragraph.Inlines.Add(Code(instruction));
        paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
        paragraph.Inlines.Add(Text(result));
        if (end)
        {
            paragraph.Inlines.Add(Mark(FieldCharKind.End));
        }

        paragraph.Inlines.Add(Text(" tail"));
        return paragraph;
    }

    private static string Visible(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static int FieldChars(Paragraph paragraph) => paragraph.Inlines.OfType<Run>().Sum(r => r.Content.OfType<FieldChar>().Count());

    // Copies the whole source paragraph and pastes it at the end of a new target paragraph "Target"; returns the target.
    private static Paragraph CopyPaste(Paragraph source)
    {
        var target = new Paragraph();
        target.Inlines.Add(Text("Target"));
        var story = new Story(new OoxmlRoot(W + "document", []));
        story.Blocks.Add(source);
        story.Blocks.Add(target);
        var editor = new TextEditor(new WordDocument(story), (_, _) => ProjectionHost.Plain) { FragmentCodec = new DocxFragmentCodec() };
        var content = editor.Copy(new TextRange(new TextPosition(StoryRef.Body, source, 0), new TextPosition(StoryRef.Body, source, Visible(source).Length)));
        Assert.NotNull(content.Fragment);
        var end = new TextPosition(StoryRef.Body, target, 6);
        Assert.NotEmpty(editor.Paste(new TextRange(end, end), content, _ => true).Changes);
        return target;
    }

    [Theory]
    [InlineData(" SEQ Figure \\* ARABIC ", "SEQ")]
    [InlineData(" STYLEREF 1 \\s ", "STYLEREF")]
    public void ACopiedCaptionKeepsItsSeqField(string instruction, string name)
    {
        var target = CopyPaste(Field("Figure ", instruction, "3"));

        Assert.Equal(3, FieldChars(target));
        var span = Assert.Single(FieldView.InParagraph(target));
        Assert.True(span.IsComplete);
        Assert.Equal(name, span.Instruction.Name);
        Assert.True(span.Support.IsSupported);
        Assert.Equal("TargetFigure 3 tail", Visible(target));
    }

    [Fact]
    public void ACopiedSimpleSeqFieldIsKept()
    {
        var source = new Paragraph();
        var simple = new SimpleField(" SEQ Table ");
        simple.Inlines.Add(Text("2"));
        source.Inlines.Add(Text("Table "));
        source.Inlines.Add(simple);
        var target = CopyPaste(source);

        Assert.Single(target.Inlines.OfType<SimpleField>());
        Assert.Equal("TargetTable 2", Visible(target));
    }

    [Fact]
    public void ACopiedTocPastesAsPlainText()
    {
        var target = CopyPaste(Field("Contents ", " TOC \\o \"1-3\" \\h ", "Entry"));

        Assert.Equal(0, FieldChars(target));
        Assert.Empty(FieldView.InParagraph(target));
        Assert.Equal("TargetContents Entry tail", Visible(target));
    }

    [Theory]
    [InlineData(" REF _Ref123 \\h ", "Figure 1")]
    [InlineData(" PAGEREF _Toc9 \\h ", "12")]
    [InlineData(" NOTEREF _Ref5 ", "4")]
    public void ARefFlattensToItsResult(string instruction, string result)
    {
        var target = CopyPaste(Field("See ", instruction, result));

        Assert.Equal(0, FieldChars(target));
        Assert.Equal($"TargetSee {result} tail", Visible(target));
    }

    [Fact]
    public void AnIncompleteSeqFlattens()
    {
        var target = CopyPaste(Field("Figure ", " SEQ Figure ", "7", end: false));

        Assert.Equal(0, FieldChars(target));
        Assert.Equal("TargetFigure 7 tail", Visible(target));

        // A field with no separate has no result: it leaves nothing.
        var noResult = new Paragraph();
        noResult.Inlines.Add(Text("A"));
        noResult.Inlines.Add(Mark(FieldCharKind.Begin));
        noResult.Inlines.Add(Code(" SEQ Figure "));
        noResult.Inlines.Add(Text("B"));
        Assert.Equal(0, FieldChars(CopyPaste(noResult)));
    }

    [Fact]
    public void NoTocOrRefBookmarkIsPasted()
    {
        var source = new Paragraph();
        source.Inlines.Add(new BookmarkStart(1, "_Toc100"));
        source.Inlines.Add(Text("Heading"));
        source.Inlines.Add(new BookmarkEnd(1));
        source.Inlines.Add(new BookmarkStart(2, "_Ref200"));
        source.Inlines.Add(Text(" label"));
        source.Inlines.Add(new BookmarkEnd(2));
        source.Inlines.Add(new BookmarkStart(3, "Mine"));
        source.Inlines.Add(Text(" mine"));
        source.Inlines.Add(new BookmarkEnd(3));
        var target = CopyPaste(source);

        Assert.DoesNotContain(target.Inlines, i => i is BookmarkStart or BookmarkEnd);
        Assert.Equal("TargetHeading label mine", Visible(target));
    }
}
