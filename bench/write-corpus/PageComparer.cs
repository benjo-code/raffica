namespace WriteCorpus;

/// <summary>The numbers of one comparison of two paginated documents; <see cref="PageComparer"/> makes it.</summary>
/// <param name="PageCountEqual">Both documents have the same number of pages.</param>
/// <param name="ReferencePages">Pages of the reference.</param>
/// <param name="CandidatePages">Pages of the candidate.</param>
/// <param name="EqualPages">Pages (paired by index) whose normalised text is identical.</param>
/// <param name="MeanDice">Mean of the per-page Dice scores, divided by the larger page count, so a surplus page lowers it.</param>
/// <param name="FirstDifferentPage">The first page (1-based) whose normalised text differs or that exists in one document only; <see langword="null"/> when there is none.</param>
internal sealed record PageComparison(bool PageCountEqual, int ReferencePages, int CandidatePages, int EqualPages, double MeanDice, int? FirstDifferentPage);

/// <summary>Compares the page facts of a reference (Word's PDF) with a candidate (Write's output).</summary>
internal static class PageComparer
{
    /// <summary>
    /// Pairs the pages by index. A page's score is the Dice coefficient over the multiset of its normalised words
    /// (2 times the shared words, counted with multiplicity, over the sum of both word counts; 1.0 for two pages without words).
    /// A page that exists in one document only scores 0, and <see cref="PageComparison.MeanDice"/> divides by the larger page count
    /// (1.0 when both have no pages).
    /// </summary>
    public static PageComparison Compare(PageFacts reference, PageFacts candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        int referencePages = reference.PageTexts.Count, candidatePages = candidate.PageTexts.Count;
        var paired = Math.Min(referencePages, candidatePages);
        var larger = Math.Max(referencePages, candidatePages);
        var equal = 0;
        var sum = 0.0;
        int? first = null;
        for (var i = 0; i < paired; i++)
        {
            var a = TextNormalizer.Normalize(reference.PageTexts[i]);
            var b = TextNormalizer.Normalize(candidate.PageTexts[i]);
            if (string.Equals(a, b, StringComparison.Ordinal))
            {
                equal++;
                sum += 1.0;
            }
            else
            {
                sum += Dice(a, b);
                first ??= i + 1;
            }
        }

        if (larger > paired)
        {
            first ??= paired + 1;
        }

        return new PageComparison(referencePages == candidatePages, referencePages, candidatePages, equal, larger == 0 ? 1.0 : sum / larger, first);
    }

    private static double Dice(string a, string b)
    {
        var wordsA = Words(a);
        var wordsB = Words(b);
        if (wordsA.Length == 0 && wordsB.Length == 0)
        {
            return 1.0;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var word in wordsA)
        {
            counts[word] = counts.GetValueOrDefault(word) + 1;
        }

        var shared = 0;
        foreach (var word in wordsB)
        {
            if (counts.TryGetValue(word, out var left) && left > 0)
            {
                counts[word] = left - 1;
                shared++;
            }
        }

        return 2.0 * shared / (wordsA.Length + wordsB.Length);
    }

    private static string[] Words(string normalized) => normalized.Length == 0 ? [] : normalized.Split(' ');
}
