using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class CloneTests
{
    private static string Thesis => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    private static XElement Written(Story template, Block block)
    {
        var story = new Story(template.Root);
        story.Blocks.Add(block);
        using var output = new MemoryStream();
        DocumentXml.Write(story, output);
        output.Position = 0;
        return XElement.Load(output);
    }

    [Fact]
    public void CloneWritesTheSameXml()
    {
        using var file = DocxFile.Open(Thesis);
        var body = file.Model.Body;
        Assert.NotEmpty(body.Blocks);
        foreach (var block in body.Blocks)
        {
            var difference = XmlEquivalence.Explain(Written(body, block), Written(body, block.Clone()));
            Assert.True(difference is null, difference);
        }
    }

    [Fact]
    public void CloneSharesNoMutableObjects()
    {
        using var file = DocxFile.Open(Thesis);
        var checkedRuns = 0;
        foreach (var block in file.Model.Body.Blocks)
        {
            var clone = block.Clone();
            var originals = ModelWalk.Blocks([block]).ToList();
            var copies = ModelWalk.Blocks([clone]).ToList();
            Assert.Equal(originals.Count, copies.Count);
            for (var i = 0; i < originals.Count; i++)
            {
                Assert.NotSame(originals[i], copies[i]);
                switch (originals[i])
                {
                    case UnknownBlock unknown:
                        Assert.Same(unknown.Content, ((UnknownBlock)copies[i]).Content);
                        break;
                    case Paragraph paragraph:
                        var copy = (Paragraph)copies[i];
                        Assert.NotSame(paragraph.Properties, copy.Properties);
                        var inlines = ModelWalk.Inlines(paragraph.Inlines).ToList();
                        var inlineCopies = ModelWalk.Inlines(copy.Inlines).ToList();
                        Assert.Equal(inlines.Count, inlineCopies.Count);
                        for (var j = 0; j < inlines.Count; j++)
                        {
                            Assert.NotSame(inlines[j], inlineCopies[j]);
                            if (inlines[j] is Run run)
                            {
                                Assert.NotSame(run.Properties, ((Run)inlineCopies[j]).Properties);
                                checkedRuns++;
                            }
                            else if (inlines[j] is UnknownInline unknownInline)
                            {
                                Assert.Same(unknownInline.Content, ((UnknownInline)inlineCopies[j]).Content);
                            }
                        }

                        break;
                }
            }
        }

        Assert.True(checkedRuns > 0);
    }

    [Fact]
    public void ACloneIsIndependentOfTheOriginal()
    {
        var paragraph = new Paragraph();
        var run = new Run();
        run.Content.Add(new TextContent("abc"));
        run.Properties.SetFlag(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "b", true);
        paragraph.Inlines.Add(run);
        var clone = paragraph.Clone();
        ((TextContent)run.Content[0]).Text = "changed";
        run.Properties.SetFlag(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "b", false);
        var copiedRun = (Run)clone.Inlines[0];
        Assert.Equal("abc", ((TextContent)copiedRun.Content[0]).Text);
        Assert.True(copiedRun.Properties.Bold);
    }

    [Fact]
    public void DirtyFollowsTheRevision()
    {
        var edits = new DocumentEdits();
        Assert.False(edits.IsDirty);
        edits.Raise();
        Assert.True(edits.IsDirty);
        edits.MarkSaved();
        Assert.False(edits.IsDirty);
        var saved = edits.SavedRevision;
        edits.Raise();
        edits.Raise();
        edits.Restore(saved);
        Assert.False(edits.IsDirty);
        edits.Restore(saved + 1);
        Assert.True(edits.IsDirty);
        Assert.False(new WordDocument(new Story(new OoxmlRoot("document", []))).Edits.IsDirty);
    }
}
