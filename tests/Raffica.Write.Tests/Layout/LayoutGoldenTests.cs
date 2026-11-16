using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

// The font catalog must not read installed fonts: glyph widths of a fallback face change line and page breaks per machine, and the golden must not.
/// <summary>
/// The layout of every corpus file, recorded per file and mode: the page count and, per page, the SHA-256 of the page text and the number of
/// items. A refactor of the planner must leave it unchanged. <c>RAFFICA_WRITE_GOLDEN=update</c> rewrites the record instead of checking it.
/// </summary>
public sealed class LayoutGoldenTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string GoldenPath => Path.Combine(TestData.Root, "tests", "Raffica.Write.Tests", "Layout", "Golden", "layout-golden.json");

    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    [Fact]
    public async Task EveryCorpusFileLaysOutAsBefore()
    {
        var fonts = PageTestKit.Fonts;
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var docx = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            foreach (var mode in new[] { LayoutMode.Pages, LayoutMode.Flow })
            {
                var layout = DocumentLayout.Create(docx.Model, fonts, new LayoutOptions(mode));
                await layout.LayOutRestAsync(null, CancellationToken.None);
                Assert.True(layout.IsComplete, entry.Id);
                var lines = new List<string> { layout.LaidOutPages.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                for (var i = 0; i < layout.LaidOutPages; i++)
                {
                    var page = layout.Page(i);
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.Text)))[..16];
                    lines.Add(hash + ":" + page.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                actual[entry.Id + "/" + mode] = string.Join(' ', lines);
            }
        }

        if (Environment.GetEnvironmentVariable("RAFFICA_WRITE_GOLDEN") == "update")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath)!);
            File.WriteAllText(GoldenPath, JsonSerializer.Serialize(actual, s_indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
            return;
        }

        var golden = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(GoldenPath))!;
        Assert.Equal(golden.Keys, actual.Keys);
        foreach (var (key, value) in golden)
        {
            Assert.True(value == actual[key], $"{key} differs from the golden record");
        }
    }
}
