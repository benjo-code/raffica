using System.Globalization;
using System.Text.Json;
using Raffica.TestSupport;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class PageComparerTests
{
    private static PageFacts Facts(params string[] pages) => new(pages.Length, pages);

    private static (int Code, string Out, string Err) RunTool(params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = Program.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static string WriteFacts(TempFolder folder, string name, PageFacts facts)
    {
        var path = folder.File(name);
        File.WriteAllText(path, JsonSerializer.Serialize(facts, PageFactsJson.Default.PageFacts));
        return path;
    }

    [Fact]
    public void NormalizeCollapsesWhitespaceAndRemovesSoftHyphens()
    {
        Assert.Equal("a b c", TextNormalizer.Normalize("  a \t\n b\u00A0\u00A0c \r\n"));
        Assert.Equal("hyphenation", TextNormalizer.Normalize("hy\u00ADphen\u00ADation"));
        Assert.Equal("ab", TextNormalizer.Normalize("a\u0001\u0000b"));
        Assert.Equal("\u00E9", TextNormalizer.Normalize("e\u0301"));
        Assert.Equal("", TextNormalizer.Normalize(" \u00A0 "));
    }

    [Theory]
    [InlineData("a\u00A0b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a   b")]
    [InlineData("a  b")]
    [InlineData("a\r\n \u00A0\tb")]
    public void WhitespaceBetweenWordsBecomesOneSpace(string input) => Assert.Equal("a b", TextNormalizer.Normalize(input));

    [Fact]
    public void NormalizeRemovesFormatCharactersWithoutLeavingASpace()
    {
        Assert.Equal("ab", TextNormalizer.Normalize("a\u200Bb"));
        Assert.Equal("ab", TextNormalizer.Normalize("\uFEFFa\uFEFFb"));
        Assert.Equal("ab", TextNormalizer.Normalize("a\u200Eb\u2060"));
        Assert.Equal("a b", TextNormalizer.Normalize("a \u200B b"));
    }

    [Fact]
    public void PagesDifferingOnlyInNoBreakSpaceScoreOne()
    {
        var result = PageComparer.Compare(Facts("one\u00A0two three"), Facts("one two\nthree"));
        Assert.Equal(1.0, result.MeanDice, 9);
        Assert.Equal(1, result.EqualPages);
    }

    [Fact]
    public void IdenticalFactsAreEqualWithDiceOne()
    {
        var result = PageComparer.Compare(Facts("one two", "three"), Facts("one  two", "three"));
        Assert.True(result.PageCountEqual);
        Assert.Equal(2, result.EqualPages);
        Assert.Equal(1.0, result.MeanDice, 9);
        Assert.Null(result.FirstDifferentPage);
    }

    [Fact]
    public void ADifferentWordLowersTheScoreButNotTheCount()
    {
        var result = PageComparer.Compare(Facts("a b c d"), Facts("a b c x"));
        Assert.True(result.PageCountEqual);
        Assert.Equal(0, result.EqualPages);
        Assert.Equal(0.75, result.MeanDice, 9);
        Assert.Equal(1, result.FirstDifferentPage);
    }

    [Fact]
    public void ASurplusPageCountsAsZero()
    {
        var more = PageComparer.Compare(Facts("a"), Facts("a", "b"));
        Assert.False(more.PageCountEqual);
        Assert.Equal((1, 2), (more.ReferencePages, more.CandidatePages));
        Assert.Equal(1, more.EqualPages);
        Assert.Equal(0.5, more.MeanDice, 9);
        Assert.Equal(2, more.FirstDifferentPage);

        var fewer = PageComparer.Compare(Facts("a", "b"), Facts("a"));
        Assert.Equal(0.5, fewer.MeanDice, 9);
        Assert.Equal(2, fewer.FirstDifferentPage);
    }

    [Fact]
    public void TwoEmptyPagesAreEqual()
    {
        var result = PageComparer.Compare(Facts(" "), Facts(""));
        Assert.Equal(1, result.EqualPages);
        Assert.Equal(1.0, result.MeanDice, 9);
        var none = PageComparer.Compare(Facts(), Facts());
        Assert.True(none.PageCountEqual);
        Assert.Equal(1.0, none.MeanDice, 9);
    }

    [Fact]
    public void DiceCountsRepeatedWordsAsAMultiset()
    {
        // {a,a,b} against {a,b,b}: the overlap is a,b = 2 of 3 + 3 words, Dice = 2*2/6.
        Assert.Equal(4.0 / 6.0, PageComparer.Compare(Facts("a a b"), Facts("a b b")).MeanDice, 9);
    }

    [Fact]
    public async Task PdfPageFactsReadsTheTestPdf()
    {
        var facts = await PdfPageFacts.ReadAsync(TestData.Pdf("text-basic.pdf"), TestContext.Current.CancellationToken);
        Assert.Equal(2, facts.PageCount);
        Assert.Contains("Hello,", facts.PageTexts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompareOfAPdfWithItselfIsPerfect()
    {
        var facts = await PdfPageFacts.ReadAsync(TestData.Pdf("text-basic.pdf"), TestContext.Current.CancellationToken);
        var result = PageComparer.Compare(facts, facts);
        Assert.Equal(1.0, result.MeanDice, 9);
        Assert.Equal(2, result.EqualPages);
        var tool = RunTool("compare", "--reference", TestData.Pdf("text-basic.pdf"), "--candidate", TestData.Pdf("text-basic.pdf"), "--require-same-page-count");
        Assert.Equal(0, tool.Code);
    }

    [Fact]
    public void PageFactsRoundTripThroughJson()
    {
        var facts = Facts("one \"two\"", "déjà vu");
        var json = JsonSerializer.Serialize(facts, PageFactsJson.Default.PageFacts);
        var back = JsonSerializer.Deserialize(json, PageFactsJson.Default.PageFacts)!;
        Assert.Equal(facts.PageCount, back.PageCount);
        Assert.Equal(facts.PageTexts, back.PageTexts);
    }

    [Fact]
    public void CompareExitCodes()
    {
        using var folder = new TempFolder();
        var one = WriteFacts(folder, "one.json", Facts("a"));
        var two = WriteFacts(folder, "two.json", Facts("a", "b"));

        var plain = RunTool("compare", "--reference", one, "--candidate", two);
        Assert.Equal(0, plain.Code);
        Assert.Contains("0.5", plain.Out, StringComparison.Ordinal);

        Assert.Equal(1, RunTool("compare", "--reference", one, "--candidate", two, "--require-same-page-count").Code);
        Assert.Equal(0, RunTool("compare", "--reference", one, "--candidate", one, "--require-same-page-count").Code);
        Assert.Equal(2, RunTool("compare", "--reference", one, "--candidate", folder.File("missing.json")).Code);
        File.WriteAllText(folder.File("bad.json"), "{ not json");
        Assert.Equal(2, RunTool("compare", "--reference", one, "--candidate", folder.File("bad.json")).Code);
        Assert.Equal(2, RunTool("compare", "--reference", one).Code);
    }

    [Theory]
    [InlineData("""{"pageCount":1,"pageTexts":[null]}""")]
    [InlineData("""{"pageCount":1}""")]
    [InlineData("""{"pageCount":1,"pageTexts":null}""")]
    [InlineData("""{"pageCount":2,"pageTexts":["a"]}""")]
    [InlineData("""{"pageCount":0,"pageTexts":["a"]}""")]
    [InlineData("null")]
    [InlineData("[]")]
    public void MalformedFactsAreExitTwoWithOneLine(string json)
    {
        using var folder = new TempFolder();
        var good = WriteFacts(folder, "good.json", Facts("a"));
        var bad = folder.File("bad.json");
        File.WriteAllText(bad, json);
        foreach (var args in new[] { new[] { "compare", "--reference", bad, "--candidate", good }, ["compare", "--reference", good, "--candidate", bad, "--require-same-page-count"] })
        {
            var result = RunTool(args);
            Assert.Equal(2, result.Code);
            Assert.Single(result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain(folder.Path, result.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMissingOutputFolderIsExitTwo()
    {
        using var folder = new TempFolder();
        var facts = WriteFacts(folder, "facts.json", Facts("a"));
        var missing = folder.File("no-such-folder/out.json");
        var root = MakeRoot(folder, ("a", ["tables"]));

        var facts2 = RunTool("pdf-facts", TestData.Pdf("text-basic.pdf"), "--out", missing);
        Assert.Equal(2, facts2.Code);
        Assert.DoesNotContain(folder.Path, facts2.Err, StringComparison.Ordinal);

        var table = RunTool("table", "--root", root, "--out", missing);
        Assert.Equal(2, table.Code);
        Assert.DoesNotContain(folder.Path, table.Err, StringComparison.Ordinal);
        Assert.True(File.Exists(facts));
    }

    [Fact]
    public void ATableOfAMissingOrBrokenManifestIsExitTwo()
    {
        using var folder = new TempFolder();
        var empty = folder.File("empty");
        Directory.CreateDirectory(empty);
        Assert.Equal(2, RunTool("table", "--root", empty, "--out", folder.File("t.md")).Code);

        var broken = folder.File("broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "corpus.json"), "{ not json");
        Assert.Equal(2, RunTool("table", "--root", broken, "--out", folder.File("t.md")).Code);

        var wrongSchema = folder.File("wrong");
        Directory.CreateDirectory(wrongSchema);
        File.WriteAllText(Path.Combine(wrongSchema, "corpus.json"), """{"schema":99,"files":[]}""");
        Assert.Equal(2, RunTool("table", "--root", wrongSchema, "--out", folder.File("t.md")).Code);
    }

    private static string MakeRoot(TempFolder folder, params (string Id, string[] Features)[] entries)
    {
        var root = folder.File("root");
        Directory.CreateDirectory(root);
        var manifest = new CorpusManifest();
        foreach (var (id, features) in entries)
        {
            Directory.CreateDirectory(Path.Combine(root, id));
            DocxBuilder.WithBody(Path.Combine(root, id, id + ".docx"), "<w:p><w:r><w:t>Plain</w:t></w:r></w:p>");
            manifest.Files.Add(new CorpusEntry { Id = id, Path = id + "/" + id + ".docx", Source = "generated", License = "GPL-3.0-or-later", Features = [.. features] });
        }

        manifest.Save(root);
        return root;
    }

    [Fact]
    public void TableLeavesWordColumnsEmptyWithoutAPdf()
    {
        using var folder = new TempFolder();
        var root = MakeRoot(folder, ("zeta", ["tables"]), ("alpha", ["tracked-changes"]));
        var outPath = folder.File("table.md");
        var result = RunTool("table", "--root", root, "--out", outPath);
        Assert.Equal(0, result.Code);
        var text = File.ReadAllText(outPath);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain(folder.Path, text, StringComparison.Ordinal);
        var rows = text.Split('\n').Where(l => l.StartsWith("| alpha ", StringComparison.Ordinal) || l.StartsWith("| zeta ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("| alpha ", rows[0], StringComparison.Ordinal);
        Assert.EndsWith("| – | – | – |", rows[0], StringComparison.Ordinal);
        Assert.EndsWith("| – | – | – |", rows[1], StringComparison.Ordinal);
        Assert.Contains("| true | true | true | – |", rows[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TableIsDeterministic()
    {
        using var folder = new TempFolder();
        var root = MakeRoot(folder, ("b", ["tables"]), ("a", ["tracked-changes"]));
        RunTool("table", "--root", root, "--out", folder.File("one.md"));
        RunTool("table", "--root", root, "--out", folder.File("two.md"));
        Assert.Equal(File.ReadAllBytes(folder.File("one.md")), File.ReadAllBytes(folder.File("two.md")));
    }

    [Fact]
    public async Task TableShowsWordPageCountsWhenThePdfExists()
    {
        using var folder = new TempFolder();
        var root = MakeRoot(folder, ("a", ["tracked-changes"]));
        File.Copy(TestData.Pdf("text-basic.pdf"), Path.Combine(root, "a", "word.pdf"));
        var outPath = folder.File("table.md");
        Assert.Equal(0, RunTool("table", "--root", root, "--out", outPath).Code);
        var row = (await File.ReadAllLinesAsync(outPath, TestContext.Current.CancellationToken)).Single(l => l.StartsWith("| a ", StringComparison.Ordinal));
        Assert.EndsWith("| 2 | – | – |", row, StringComparison.Ordinal);
    }
}
