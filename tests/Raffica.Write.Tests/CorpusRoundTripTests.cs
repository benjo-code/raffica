using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests;

/// <summary>
/// The round trip on the compatibility corpus (<c>testdata/write-corpus</c>): every file opens, saves, keeps the parts the model
/// did not load byte for byte, keeps the loaded parts equivalent and reads back to the same model.
/// </summary>
public sealed class CorpusRoundTripTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    public static TheoryData<string> Ids()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task EveryCorpusFileRoundTrips(string id)
    {
        var entry = CorpusManifest.Load(Root).Files.Single(e => e.Id == id);
        var original = CorpusCheck.Resolve(Root, entry.Path);
        Assert.NotNull(original);
        using var folder = new TempFolder();
        var saved = folder.File("saved.docx");
        XElement? first;
        using (var file = DocxFile.Open(original))
        {
            first = ModelSnapshot.Of(file.Model);
            await file.SaveAsync(saved, Cancel);
        }

        var verdict = PackageVerifier.Compare(original, saved);
        var diff = PartComparer.Diff(original, saved);
        Assert.True(verdict.UnloadedIdentical, $"{id}: a part the model did not load changed (added {string.Join(",", diff.Added)}; removed {string.Join(",", diff.Removed)}; changed {string.Join(",", diff.Changed)})");
        Assert.True(verdict.LoadedEquivalent, $"{id}: a loaded part is not equivalent (changed {string.Join(",", diff.Changed)})");

        using var reopened = DocxFile.Open(saved);
        var second = ModelSnapshot.Of(reopened.Model);
        Assert.True(XmlEquivalence.AreEqual(first, second), $"{id}: {XmlEquivalence.Explain(first, second)}");
    }

    [Fact]
    public void EveryEntryIsInTheManifestAndTheCorpusCheckPasses()
    {
        Assert.NotEmpty(CorpusManifest.Load(Root).Files);
        Assert.Empty(CorpusCheck.Run(Root));
    }
}
