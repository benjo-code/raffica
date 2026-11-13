using System.Security.Cryptography;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class CorpusToolTests
{
    private const string RevisionsBody =
        "<w:p><w:ins w:id=\"1\" w:author=\"A\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:t>new</w:t></w:r></w:ins>"
        + "<w:r><w:commentReference w:id=\"0\"/></w:r></w:p>"
        + "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>x</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    private const string PlainBody = "<w:p><w:r><w:t>Plain</w:t></w:r></w:p>";

    private static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>A root with one entry per body, each consistent (hash, licence, features as detected).</summary>
    private static CorpusManifest MakeRoot(TempFolder folder, params (string Id, string Body)[] files)
    {
        var manifest = new CorpusManifest();
        File.WriteAllText(folder.File("LICENSE-test.txt"), "licence text");
        foreach (var (id, body) in files)
        {
            Directory.CreateDirectory(folder.File(id));
            var path = folder.File(id + "/" + id + ".docx");
            DocxBuilder.WithBody(path, body);
            manifest.Files.Add(new CorpusEntry
            {
                Id = id,
                Path = id + "/" + id + ".docx",
                Sha256 = Sha(path),
                Source = "https://example.org/" + id,
                License = "MIT",
                LicenseFile = "LICENSE-test.txt",
                Retrieved = "2026-11-13",
                Features = [.. FeatureDetector.Detect(path).Order(StringComparer.Ordinal)],
            });
        }

        manifest.Save(folder.Path);
        return manifest;
    }

    private static IReadOnlyList<string> Check(TempFolder folder) => CorpusCheck.Run(folder.Path, requireCoverage: false);

    [Fact]
    public void CheckAcceptsAConsistentRoot()
    {
        using var folder = new TempFolder();
        MakeRoot(folder, ("revisions", RevisionsBody), ("plain", PlainBody));
        Assert.Empty(Check(folder));
    }

    [Fact]
    public void CheckRejectsAnUnknownLicence()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("plain", PlainBody));
        manifest.Files[0].License = "Proprietary";
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("licence", StringComparison.Ordinal) && p.Contains("plain", StringComparison.Ordinal));

        manifest.Files[0].License = "MIT";
        manifest.Files[0].LicenseFile = "";
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("licenseFile", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckRejectsAHashMismatch()
    {
        using var folder = new TempFolder();
        MakeRoot(folder, ("plain", PlainBody));
        File.AppendAllText(folder.File("plain/plain.docx"), "x");
        Assert.Contains(Check(folder), p => p.Contains("hash", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckRejectsAFileMissingFromTheManifest()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("plain", PlainBody), ("extra", PlainBody));
        manifest.Files.RemoveAt(1);
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("extra/extra.docx", StringComparison.Ordinal) && p.Contains("not in the manifest", StringComparison.Ordinal));

        // An entry without its file is the reverse.
        manifest.Files.Add(new CorpusEntry { Id = "gone", Path = "gone/gone.docx", Sha256 = new string('0', 64), Source = "s", License = "MIT", LicenseFile = "LICENSE-test.txt", Retrieved = "2026-11-13" });
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("gone/gone.docx", StringComparison.Ordinal) && p.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckRejectsAFeatureTagThatIsNotInTheFile()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("plain", PlainBody));
        manifest.Files[0].Features.Add("tables");
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("tables", StringComparison.Ordinal) && p.Contains("plain", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckReportsAFeatureNoFileCovers()
    {
        using var folder = new TempFolder();
        MakeRoot(folder, ("revisions", RevisionsBody));
        var problems = CorpusCheck.Run(folder.Path);
        Assert.Contains(problems, p => p.Contains("tables", StringComparison.Ordinal) && p.Contains("no file", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("tracked-changes", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("toc", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckRejectsAMissingSourceOrDate()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("plain", PlainBody));
        manifest.Files[0].Source = "";
        manifest.Files[0].Retrieved = "13.11.2026";
        manifest.Save(folder.Path);
        var problems = Check(folder);
        Assert.Contains(problems, p => p.Contains("source", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("retrieved", StringComparison.Ordinal));
    }

    [Fact]
    public void FeatureDetectorFindsRevisionsCommentsAndFields()
    {
        using var folder = new TempFolder();
        var path = folder.File("revisions.docx");
        DocxBuilder.WithBody(path, RevisionsBody);
        var features = FeatureDetector.Detect(path);
        Assert.Contains("tracked-changes", features);
        Assert.Contains("comments", features);
        Assert.Contains("fields", features);
        Assert.Contains("toc", features);
        Assert.Contains("paragraphs-runs", features);
        Assert.DoesNotContain("tables", features);
        Assert.DoesNotContain("equations", features);
    }

    [Fact]
    public void FeatureDetectorFindsStructureFeatures()
    {
        using var folder = new TempFolder();
        var path = folder.File("structure.docx");
        DocxBuilder.WithBody(
            path,
            "<w:tbl><w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>"
            + "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"b\"/><w:hyperlink w:anchor=\"b\"><w:r><w:t>l</w:t></w:r></w:hyperlink></w:p>"
            + "<w:p><w:pPr><w:bidi/></w:pPr></w:p>"
            + "<w:sdt><w:sdtContent><w:p/></w:sdtContent></w:sdt>"
            + "<w:p><w:r><w:drawing><wp:anchor/></w:drawing></w:r></w:p>");
        var features = FeatureDetector.Detect(path);
        foreach (var tag in new[] { "tables", "merged-cells", "bookmarks", "hyperlinks", "rtl-text", "content-controls", "pictures-floating" })
        {
            Assert.Contains(tag, features);
        }

        Assert.DoesNotContain("pictures-inline", features);
        Assert.DoesNotContain("footnotes", features);
    }

    [Fact]
    public void ARightToLeftSwitchThatIsOffDoesNotCount()
    {
        using var folder = new TempFolder();
        var path = folder.File("ltr.docx");
        DocxBuilder.WithBody(path, "<w:p><w:pPr><w:bidi w:val=\"0\"/><w:rPr><w:rtl w:val=\"false\"/></w:rPr></w:pPr></w:p>");
        Assert.DoesNotContain("rtl-text", FeatureDetector.Detect(path));
    }

    [Fact]
    public void RunReportsAFailingEntry()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("good", PlainBody), ("bad", PlainBody));
        File.WriteAllText(folder.File("bad/bad.docx"), "this is not a package");
        manifest.Files[1].Sha256 = Sha(folder.File("bad/bad.docx"));
        manifest.Save(folder.Path);

        var output = new StringWriter();
        var code = CorpusRun.Run(folder.Path, output);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEqual(0, code);
        Assert.Equal("id,opens,saves,unloadedIdentical,loadedEquivalent,ms", lines[0]);
        Assert.StartsWith("good,true,true,true,true,", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("bad,false,false,false,false,", lines[2], StringComparison.Ordinal);
        Assert.DoesNotContain(folder.Path, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunPassesOnTheThesis()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("thesis"));
        var path = folder.File("thesis/thesis.docx");
        File.Copy(TempFolder.ThesisPath, path);
        var manifest = new CorpusManifest();
        manifest.Files.Add(new CorpusEntry { Id = "thesis", Path = "thesis/thesis.docx", Sha256 = Sha(path), Source = "s", License = "GPL-3.0-or-later", LicenseFile = "L", Retrieved = "2026-11-13" });
        manifest.Save(folder.Path);

        var output = new StringWriter();
        Assert.Equal(0, CorpusRun.Run(folder.Path, output));
        Assert.Contains("thesis,true,true,true,true,", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddCopiesTheFileAndAppendsAConsistentEntry()
    {
        using var folder = new TempFolder();
        var source = folder.File("source.docx");
        DocxBuilder.WithBody(source, RevisionsBody);
        var license = folder.File("LICENSE-in.txt");
        File.WriteAllText(license, "licence text");
        var root = folder.File("root");
        Directory.CreateDirectory(root);

        var code = Program.Run(["add", source, "--root", root, "--id", "revisions", "--source", "https://example.org/r", "--license", "CC0-1.0", "--license-file", license], new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(root, "revisions", "revisions.docx")));
        var manifest = CorpusManifest.Load(root);
        var entry = Assert.Single(manifest.Files);
        Assert.Contains("tracked-changes", entry.Features);
        Assert.Empty(CorpusCheck.Run(root, requireCoverage: false));

        // A second add with the same id or a licence outside the list is refused and changes nothing.
        Assert.Equal(1, Program.Run(["add", source, "--root", root, "--id", "revisions", "--source", "s", "--license", "CC0-1.0", "--license-file", license], new StringWriter(), new StringWriter()));
        Assert.Equal(1, Program.Run(["add", source, "--root", root, "--id", "other", "--source", "s", "--license", "Proprietary", "--license-file", license], new StringWriter(), new StringWriter()));
        Assert.Single(CorpusManifest.Load(root).Files);
    }

    private static (int Code, string Output, string Error) Invoke(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("{ \"schema\": 1, \"files\": null }")]
    [InlineData("{ \"files\": [] }")]
    [InlineData("{ \"schema\": 2, \"files\": [] }")]
    [InlineData("{ \"schema\": 1, \"files\": [ { \"id\": null, \"path\": \"a/a.docx\" } ] }")]
    [InlineData("{ \"schema\": 1, \"files\": [ { \"id\": \"a\", \"path\": null } ] }")]
    [InlineData("{ \"schema\": 1, \"files\": [ { \"id\": \"a\", \"sha256\": null } ] }")]
    [InlineData("{ \"schema\": 1, \"files\": [ { \"id\": \"a\", \"features\": null } ] }")]
    [InlineData("{ \"schema\": 1, \"files\": [ null ] }")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void AMalformedManifestEndsInExitOneWithoutACrash(string manifest)
    {
        using var folder = new TempFolder();
        File.WriteAllText(CorpusManifest.PathIn(folder.Path), manifest);
        foreach (var command in new[] { "check", "run" })
        {
            var (code, _, error) = Invoke(command, "--root", folder.Path);
            Assert.Equal(1, code);
            Assert.DoesNotContain(folder.Path, error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMissingManifestIsAProblemNotACrash()
    {
        using var folder = new TempFolder();
        Assert.Equal(1, Invoke("check", "--root", folder.Path).Code);
        Assert.Equal(1, Invoke("run", "--root", folder.Path).Code);
    }

    [Theory]
    [InlineData("a\0b/x.docx")]
    [InlineData("../../x.docx")]
    [InlineData("..\\..\\x.docx")]
    [InlineData("/etc/x.docx")]
    [InlineData("C:\\x.docx")]
    [InlineData("")]
    public void AnEntryWithAnUnusablePathIsReportedAndTheOthersStillRun(string badPath)
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("bad", PlainBody), ("good", PlainBody));
        manifest.Files[0].Path = badPath;
        manifest.Files[0].LicenseFile = "a\0b";
        manifest.Save(folder.Path);

        var check = Invoke("check", "--root", folder.Path, "--allow-uncovered");
        Assert.Equal(1, check.Code);
        Assert.Contains("bad", check.Output, StringComparison.Ordinal);

        var run = Invoke("run", "--root", folder.Path);
        Assert.Equal(1, run.Code);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.StartsWith("bad,false,false,false,false,", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("good,true,true,true,true,", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ANameThatOnlyStartsWithTwoDotsIsInsideTheRoot()
    {
        Assert.NotNull(CorpusCheck.Resolve(Path.GetTempPath(), "..foo/x.docx"));
        Assert.NotNull(CorpusCheck.Resolve(Path.GetTempPath(), "a/../b/x.docx"));
        Assert.Null(CorpusCheck.Resolve(Path.GetTempPath(), "a/../../x.docx"));
        Assert.Null(CorpusCheck.Resolve(Path.GetTempPath(), ".."));
    }

    [Fact]
    public void CheckRejectsAPathOutsideTheRoot()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("plain", PlainBody));
        manifest.Files[0].Path = "../plain.docx";
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("not inside the corpus root", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckRejectsADuplicateId()
    {
        using var folder = new TempFolder();
        var manifest = MakeRoot(folder, ("plain", PlainBody), ("other", PlainBody));
        manifest.Files[1].Id = "plain";
        manifest.Save(folder.Path);
        Assert.Contains(Check(folder), p => p.Contains("plain", StringComparison.Ordinal) && p.Contains("used twice", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckRejectsAManifestWithoutASchema()
    {
        using var folder = new TempFolder();
        File.WriteAllText(CorpusManifest.PathIn(folder.Path), "{ \"files\": [] }");
        Assert.Contains(Check(folder), p => p.Contains("cannot be read", StringComparison.Ordinal));
    }

    private static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f) + (File.Exists(f) ? ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))) : "")).Order(StringComparer.Ordinal)];

    [Fact]
    public void AFailingAddLeavesTheRootUnchanged()
    {
        using var folder = new TempFolder();
        var source = folder.File("source.docx");
        DocxBuilder.WithBody(source, PlainBody);
        var license = folder.File("LICENSE-in.txt");
        File.WriteAllText(license, "licence text");
        var root = folder.File("root");
        Directory.CreateDirectory(Path.Combine(root, "x"));
        File.WriteAllText(Path.Combine(root, "x", "x.docx"), "an orphan that is in the way");
        File.WriteAllText(CorpusManifest.PathIn(root), "{\n  \"schema\": 1,\n  \"files\": []\n}\n");
        var before = Snapshot(root);

        var (code, _, error) = Invoke("add", source, "--root", root, "--id", "x", "--source", "s", "--license", "MIT", "--license-file", license);

        Assert.Equal(1, code);
        Assert.Equal(before, Snapshot(root));
        Assert.DoesNotContain(root, error, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageErrorsExitWithTwo()
    {
        Assert.Equal(2, Program.Run([], new StringWriter(), new StringWriter()));
        Assert.Equal(2, Program.Run(["check"], new StringWriter(), new StringWriter()));
        Assert.Equal(2, Program.Run(["frobnicate"], new StringWriter(), new StringWriter()));
    }
}
