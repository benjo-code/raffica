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

    /// <summary>
    /// <see cref="XmlEquivalence"/> ignores prefixes, but Word reads <c>mc:Ignorable</c> and <c>mc:Choice/@Requires</c> as prefix
    /// names, so a renamed or dropped prefix would make Word report a corrupt file. For every part the save rewrote, the set of
    /// <c>prefix=namespace</c> bindings declared anywhere in it, the values of <c>mc:Ignorable</c> and the values of
    /// <c>Requires</c> must be the same before and after.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public async Task NamespacePrefixBindingsAndMarkupCompatibilityNamesSurviveTheSave(string id)
    {
        var entry = CorpusManifest.Load(Root).Files.Single(e => e.Id == id);
        var original = CorpusCheck.Resolve(Root, entry.Path);
        Assert.NotNull(original);
        using var folder = new TempFolder();
        var saved = folder.File("saved.docx");
        using (var file = DocxFile.Open(original))
        {
            await file.SaveAsync(saved, Cancel);
        }

        var diff = PartComparer.Diff(original, saved);
        Assert.NotEmpty(diff.Changed);
        foreach (var name in diff.Changed)
        {
            var before = PrefixFacts(PartComparer.ReadPart(original, name));
            var after = PrefixFacts(PartComparer.ReadPart(saved, name));
            Assert.True(before.SetEquals(after), $"{id} {name}: prefix facts differ (only before: {string.Join(" ", before.Except(after))}; only after: {string.Join(" ", after.Except(before))})");
        }
    }

    [Fact]
    public void ThePrefixFactsSeeARenamedPrefixAndAChangedIgnorableList()
    {
        const string Before = "<w:d xmlns:w=\"urn:w\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"urn:w14\" mc:Ignorable=\"w14\"><mc:AlternateContent><mc:Choice Requires=\"w14\"/></mc:AlternateContent></w:d>";
        const string Renamed = "<x:d xmlns:x=\"urn:w\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"urn:w14\" mc:Ignorable=\"w14\"><mc:AlternateContent><mc:Choice Requires=\"w14\"/></mc:AlternateContent></x:d>";
        const string Ignorable = "<w:d xmlns:w=\"urn:w\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"urn:w14\" mc:Ignorable=\"w14 w15\"><mc:AlternateContent><mc:Choice Requires=\"w14\"/></mc:AlternateContent></w:d>";
        var facts = PrefixFacts(System.Text.Encoding.UTF8.GetBytes(Before));

        Assert.True(facts.SetEquals(PrefixFacts(System.Text.Encoding.UTF8.GetBytes(Before))));
        Assert.False(facts.SetEquals(PrefixFacts(System.Text.Encoding.UTF8.GetBytes(Renamed))));
        Assert.False(facts.SetEquals(PrefixFacts(System.Text.Encoding.UTF8.GetBytes(Ignorable))));
    }

    private static HashSet<string> PrefixFacts(byte[] xml)
    {
        var facts = new HashSet<string>(StringComparer.Ordinal);
        var document = XDocument.Load(new MemoryStream(xml));
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    facts.Add($"bind {(attribute.Name.Namespace == XNamespace.None ? string.Empty : attribute.Name.LocalName)}={attribute.Value}");
                }
                else if (attribute.Name.LocalName == "Ignorable" && attribute.Name.Namespace.NamespaceName.EndsWith("markup-compatibility/2006", StringComparison.Ordinal))
                {
                    facts.Add($"ignorable {attribute.Value}");
                }
                else if (attribute.Name.LocalName == "Requires" && element.Name.LocalName == "Choice")
                {
                    facts.Add($"requires {attribute.Value}");
                }
            }
        }

        return facts;
    }

    [Fact]
    public void EveryEntryIsInTheManifestAndTheCorpusCheckPasses()
    {
        Assert.NotEmpty(CorpusManifest.Load(Root).Files);
        Assert.Empty(CorpusCheck.Run(Root));
    }
}
