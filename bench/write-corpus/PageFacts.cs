using System.Text.Json;
using System.Text.Json.Serialization;

namespace WriteCorpus;

/// <summary>
/// What the comparison needs of a paginated document: the number of pages and the text of each page (raw, as read; the comparer
/// normalises). <see cref="PageCount"/> equals the number of <see cref="PageTexts"/>.
/// </summary>
public sealed record PageFacts(int PageCount, IReadOnlyList<string> PageTexts)
{
    /// <summary>
    /// Reads facts from a JSON file written by <c>pdf-facts --out</c>. Throws <see cref="JsonException"/> when the JSON is not facts: a missing or
    /// null <c>pageTexts</c>, a null page text, or a <c>pageCount</c> that is not equal to the number of page texts.
    /// </summary>
    internal static PageFacts Load(string path)
    {
        using var stream = File.OpenRead(path);
        var facts = JsonSerializer.Deserialize(stream, PageFactsJson.Default.PageFacts) ?? throw new JsonException("The facts file is empty.");
        // The nullable annotations do not hold for a file that omits or nulls a value, so check what the type promises.
        if ((object?)facts.PageTexts is null || facts.PageTexts.Any(text => (object?)text is null))
        {
            throw new JsonException("The page texts are missing or contain a null.");
        }

        return facts.PageCount == facts.PageTexts.Count ? facts : throw new JsonException("The page count does not match the page texts.");
    }
}

/// <summary>Source-generated serialisation of <see cref="PageFacts"/> (Native AOT, no reflection).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, IndentSize = 2, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PageFacts))]
internal sealed partial class PageFactsJson : JsonSerializerContext;
