using System.Text.Json;
using System.Text.Json.Serialization;

namespace WriteCorpus;

/// <summary>
/// What the comparison needs of a paginated document: the number of pages and the text of each page (raw, as read; the comparer
/// normalises). <see cref="PageCount"/> equals the number of <see cref="PageTexts"/>.
/// </summary>
public sealed record PageFacts(int PageCount, IReadOnlyList<string> PageTexts)
{
    /// <summary>Reads facts from a JSON file written by <c>pdf-facts --out</c>; throws <see cref="JsonException"/> for anything else.</summary>
    internal static PageFacts Load(string path)
    {
        using var stream = File.OpenRead(path);
        var facts = JsonSerializer.Deserialize(stream, PageFactsJson.Default.PageFacts) ?? throw new JsonException("The facts file is empty.");
        return facts.PageCount == facts.PageTexts.Count ? facts : throw new JsonException("The page count does not match the page texts.");
    }
}

/// <summary>Source-generated serialisation of <see cref="PageFacts"/> (Native AOT, no reflection).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, IndentSize = 2, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PageFacts))]
internal sealed partial class PageFactsJson : JsonSerializerContext;
