using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>The typed view of fields: instruction parsing, the support table, finding complex and simple fields, writing a result and making a field.</summary>
public sealed class FieldViewTests
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

    private static Run Text(string text, bool bold = false)
    {
        var run = new Run();
        if (bold)
        {
            run.Properties.Set(W + "b", new XElement(W + "b"));
        }

        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Paragraph Complex(string instruction, string result, bool bold = false)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(Text("Figure "));
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        paragraph.Inlines.Add(Code(instruction));
        paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
        paragraph.Inlines.Add(Text(result, bold));
        paragraph.Inlines.Add(Mark(FieldCharKind.End));
        return paragraph;
    }

    [Fact]
    public void SwitchSpellingIsCaseInsensitiveQuotedOrNotAnyOrder()
    {
        var seq = FieldInstruction.Parse(" seq figure \\S 1 \\* roman ");
        Assert.Equal("SEQ", seq.Name);
        Assert.Equal("figure", seq.Argument);
        Assert.Equal([("s", "1"), ("*", "roman")], seq.Switches);
        Assert.False(seq.HasUnknownToken);
        Assert.Equal(FieldKind.Seq, FieldSupport.Of(seq).Kind);
        Assert.True(FieldSupport.Of(seq).IsSupported);

        var toc = FieldInstruction.Parse("TOC \\o \"1-3\" \\h");
        var reversed = FieldInstruction.Parse("toc \\H \\O 1-3");
        Assert.Equal([("o", "1-3"), ("h", null)], toc.Switches);
        Assert.Equal([("h", null), ("o", "1-3")], reversed.Switches);
        Assert.False(toc.HasUnknownToken);
        Assert.False(reversed.HasUnknownToken);
        Assert.True(FieldSupport.Of(toc).IsSupported);

        var ref1 = FieldInstruction.Parse("REF _Ref1 \\h \\* MERGEFORMAT");
        Assert.Equal("_Ref1", ref1.Argument);
        Assert.True(FieldSupport.Of(ref1).IsSupported);
        Assert.True(FieldSupport.Of(FieldInstruction.Parse("STYLEREF 1 \\s")).IsSupported);
        Assert.True(FieldSupport.Of(FieldInstruction.Parse("TOC \\n 2-3 \\c \"Figure\"")).IsSupported);
        Assert.True(FieldSupport.Of(FieldInstruction.Parse("TOC \\o \"1-3\" \\n \\u")).IsSupported);
    }

    [Theory]
    [InlineData("TOC \\t \"Style,1\"")]
    [InlineData("TOC \\o \"1-3\" \\x")]
    [InlineData("REF x \\w")]
    [InlineData("PAGEREF x \\p")]
    [InlineData("STYLEREF 2 \\s")]
    [InlineData("STYLEREF \"Heading 1\"")]
    [InlineData("REF x \\* UPPER")]
    [InlineData("SEQ Figure extra")]
    public void AnUnknownSwitchMakesTheFieldNotSupported(string text)
    {
        var instruction = FieldInstruction.Parse(text);
        Assert.True(instruction.HasUnknownToken);
        Assert.False(FieldSupport.Of(instruction).IsSupported);
    }

    [Fact]
    public void AnotherFieldNameOrAMissingTargetIsNotSupported()
    {
        var page = FieldSupport.Of(FieldInstruction.Parse("PAGE \\* ARABIC"));
        Assert.Equal(FieldKind.None, page.Kind);
        Assert.False(page.IsSupported);
        Assert.False(FieldSupport.Of(FieldInstruction.Parse("REF \\h")).IsSupported);
        Assert.Equal(FieldKind.Ref, FieldSupport.Of(FieldInstruction.Parse("REF \\h")).Kind);
    }

    [Fact]
    public void SimpleAndComplexFieldsAreBothFound()
    {
        var paragraph = Complex(" SEQ Figure \\* ARABIC ", "1");
        var simple = new SimpleField(" PAGEREF _Ref9 \\h ");
        simple.Inlines.Add(Text("4"));
        paragraph.Inlines.Add(simple);

        var spans = FieldView.InParagraph(paragraph);

        Assert.Equal(2, spans.Count);
        Assert.Equal(FieldKind.Seq, spans[0].Support.Kind);
        Assert.True(spans[0].IsComplete);
        Assert.Equal(FieldKind.PageRef, spans[1].Support.Kind);
        Assert.True(spans[1].IsComplete);
        Assert.False(spans[0].InTrackedChange);
    }

    [Fact]
    public void SetResultKeepsTheInstructionBytesAndTheResultFormatting()
    {
        var paragraph = Complex(" SEQ Figure \\* ARABIC ", "1", bold: true);
        var before = paragraph.Inlines.Select(i => i).ToList();
        var span = FieldView.InParagraph(paragraph)[0];

        Assert.True(FieldWriter.SetResult(paragraph, span, "12"));

        Assert.Equal(6, paragraph.Inlines.Count);
        Assert.Same(before[2], paragraph.Inlines[2]);
        Assert.Equal(" SEQ Figure \\* ARABIC ", ((InstructionText)((Run)paragraph.Inlines[2]).Content[0]).Text);
        var result = Assert.IsType<Run>(paragraph.Inlines[4]);
        Assert.Equal("12", Assert.IsType<TextContent>(Assert.Single(result.Content)).Text);
        Assert.NotNull(result.Properties.Get(W + "b"));
        Assert.Same(before[5], paragraph.Inlines[5]);
    }

    [Fact]
    public void SetResultReplacesAResultOfSeveralRunsAndASimpleField()
    {
        var paragraph = Complex(" REF x ", "a");
        paragraph.Inlines.Insert(5, Text("b"));
        Assert.True(FieldWriter.SetResult(paragraph, FieldView.InParagraph(paragraph)[0], "ab2"));
        Assert.Equal(6, paragraph.Inlines.Count);

        var simple = new SimpleField(" SEQ Table ");
        simple.Inlines.Add(Text("1"));
        var other = new Paragraph();
        other.Inlines.Add(simple);
        Assert.True(FieldWriter.SetResult(other, FieldView.InParagraph(other)[0], "2"));
        Assert.Equal(" SEQ Table ", simple.Instruction);
        Assert.Equal("2", ((TextContent)((Run)Assert.Single(simple.Inlines)).Content[0]).Text);
    }

    [Fact]
    public void SetResultReturnsFalseWhenEqual()
    {
        var paragraph = Complex(" SEQ Figure ", "3");
        var span = FieldView.InParagraph(paragraph)[0];
        Assert.False(FieldWriter.SetResult(paragraph, span, "3"));
        Assert.Equal(6, paragraph.Inlines.Count);

        var open = new Paragraph();
        open.Inlines.Add(Mark(FieldCharKind.Begin));
        open.Inlines.Add(Code(" SEQ x "));
        Assert.False(FieldWriter.SetResult(open, FieldView.InParagraph(open)[0], "1"));
    }

    [Fact]
    public void MakeWritesNoDirtyAttribute()
    {
        var like = Text("x", bold: true);
        var runs = FieldWriter.Make(" SEQ Figure \\* ARABIC", "1", like);

        Assert.Equal(5, runs.Count);
        var kinds = runs.Cast<Run>().SelectMany(r => r.Content).OfType<FieldChar>().ToList();
        Assert.Equal([FieldCharKind.Begin, FieldCharKind.Separate, FieldCharKind.End], kinds.Select(k => k.Kind));
        Assert.All(kinds, k => Assert.False(k.Dirty));
        Assert.All(runs.Cast<Run>(), r => Assert.NotNull(r.Properties.Get(W + "b")));
        Assert.Equal(" SEQ Figure \\* ARABIC ", ((InstructionText)((Run)runs[1]).Content[0]).Text);

        var paragraph = new Paragraph();
        foreach (var run in runs)
        {
            paragraph.Inlines.Add(run);
        }

        var span = Assert.Single(FieldView.InParagraph(paragraph));
        Assert.True(span.Support.IsSupported);
        Assert.True(span.IsComplete);
    }

    [Fact]
    public void AFieldInATrackedInsertionIsFlagged()
    {
        var paragraph = new Paragraph();
        var insertion = new TrackedChange(TrackedChangeKind.Insert, new RevisionInfo(1, "A", "2026-01-01T00:00:00Z"));
        insertion.Inlines.Add(Mark(FieldCharKind.Begin));
        insertion.Inlines.Add(Code(" SEQ Figure "));
        insertion.Inlines.Add(Mark(FieldCharKind.Separate));
        insertion.Inlines.Add(Text("1"));
        insertion.Inlines.Add(Mark(FieldCharKind.End));
        paragraph.Inlines.Add(insertion);
        foreach (var inline in Complex(" SEQ Table ", "1").Inlines.Skip(1))
        {
            paragraph.Inlines.Add(inline);
        }

        var spans = FieldView.InParagraph(paragraph);

        Assert.Equal(2, spans.Count);
        Assert.True(spans[0].InTrackedChange);
        Assert.False(spans[1].InTrackedChange);
    }

    [Fact]
    public void NestedFieldsKeepTheirParent()
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        paragraph.Inlines.Add(Code(" REF "));
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        paragraph.Inlines.Add(Code(" SEQ Figure "));
        paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
        paragraph.Inlines.Add(Text("1"));
        paragraph.Inlines.Add(Mark(FieldCharKind.End));
        paragraph.Inlines.Add(Code(" \\h "));
        paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
        paragraph.Inlines.Add(Text("Figure 1"));
        paragraph.Inlines.Add(Mark(FieldCharKind.End));

        var spans = FieldView.InParagraph(paragraph);

        Assert.Equal(2, spans.Count);
        Assert.Null(spans[0].Parent);
        Assert.Same(spans[0], spans[1].Parent);
        Assert.Equal(FieldKind.Seq, spans[1].Support.Kind);
        Assert.True(FieldWriter.SetResult(paragraph, spans[0], "Figure 2"));
        Assert.Equal(11, paragraph.Inlines.Count);
    }
}
