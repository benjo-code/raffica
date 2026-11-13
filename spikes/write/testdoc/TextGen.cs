using System.Text;

namespace TestDoc;

/// <summary>Seeded generator of Latin-like words, sentences and paragraphs. No network, no unseeded randomness.</summary>
public sealed class TextGen
{
    const int Seed = 4242;
    readonly Random _rng = new(Seed);
    readonly string[] _vocab;

    public TextGen()
    {
        string[] onsets = { "b", "c", "d", "f", "g", "l", "m", "n", "p", "qu", "r", "s", "t", "v", "st", "tr", "pr", "cl", "gr", "" };
        string[] vowels = { "a", "e", "i", "o", "u", "ae", "io", "ia", "ei" };
        string[] codas = { "", "", "", "m", "n", "r", "s", "t", "x", "ns", "nt", "rt" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        while (list.Count < 900)
        {
            int syl = 1 + _rng.Next(4);
            var sb = new StringBuilder();
            for (int i = 0; i < syl; i++)
            {
                sb.Append(onsets[_rng.Next(onsets.Length)]).Append(vowels[_rng.Next(vowels.Length)]);
                if (i == syl - 1 || _rng.Next(4) == 0) sb.Append(codas[_rng.Next(codas.Length)]);
            }
            if (seen.Add(sb.ToString())) list.Add(sb.ToString());
        }
        _vocab = list.ToArray();
    }

    public int Next(int maxExclusive) => _rng.Next(maxExclusive);

    /// <summary>A word with a skewed pick, so frequent words repeat like in running text.</summary>
    public string Word()
    {
        double u = _rng.NextDouble();
        return _vocab[(int)(u * u * _vocab.Length)];
    }

    public string Title(int words)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < words; i++) { if (i > 0) sb.Append(' '); sb.Append(Word()); }
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    /// <summary>Sentences until at least <paramref name="minWords"/> words; returns the sentences and the word count.</summary>
    public List<string> Sentences(int minWords, out int count)
    {
        var res = new List<string>();
        count = 0;
        while (count < minWords)
        {
            int n = 8 + _rng.Next(13);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(i % 7 == 6 ? ", " : " ");
                sb.Append(Word());
            }
            sb[0] = char.ToUpperInvariant(sb[0]);
            sb.Append('.');
            res.Add(sb.ToString());
            count += n;
        }
        return res;
    }

    public string Cell(bool numeric) => numeric ? (_rng.NextDouble() * 1000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : Word();
}
