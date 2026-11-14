using System.Reflection;

namespace Raffica.Write.Layout;

/// <summary>One TeX pattern set (Liang's patterns): the letters of each pattern with the digit that sits before each letter and after the last one.</summary>
internal sealed class PatternSet
{
    private readonly Dictionary<string, byte[]> _patterns;
    private readonly Dictionary<string, byte[]>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    private PatternSet(Dictionary<string, byte[]> patterns, int longest, int left, int right)
    {
        _patterns = patterns;
        _lookup = patterns.GetAlternateLookup<ReadOnlySpan<char>>();
        Longest = longest;
        LeftMin = left;
        RightMin = right;
    }

    /// <summary>The length of the longest pattern, in letters (the dots at the word ends count).</summary>
    public int Longest { get; }

    /// <summary>The fewest letters that stay before the first and after the last hyphen.</summary>
    public int LeftMin { get; }

    public int RightMin { get; }

    public int Count => _patterns.Count;

    public bool TryGet(ReadOnlySpan<char> letters, out byte[] digits) => _lookup.TryGetValue(letters, out digits!);

    public static PatternSet Parse(TextReader reader, int left, int right)
    {
        var patterns = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var longest = 0;
        var letters = new char[64];
        var digits = new byte[65];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '%')
            {
                continue;
            }

            var n = 0;
            Array.Clear(digits);
            foreach (var c in line)
            {
                if (c is >= '0' and <= '9')
                {
                    digits[n] = (byte)(c - '0');
                }
                else if (n < letters.Length)
                {
                    letters[n++] = c;
                }
            }

            if (n == 0)
            {
                continue;
            }

            patterns[new string(letters, 0, n)] = digits.AsSpan(0, n + 1).ToArray();
            longest = Math.Max(longest, n);
        }

        return new PatternSet(patterns, longest, left, right);
    }
}

/// <summary>
/// The pattern sets of the shipped languages (hyph-utf8, see third-party/hyph-utf8). A set is read from its embedded resource the
/// first time a word of its language is hyphenated, never before.
/// </summary>
internal sealed class HyphenationData
{
    private static readonly Assembly s_assembly = typeof(HyphenationData).Assembly;

    private readonly object _gate = new();
    private readonly Dictionary<string, PatternSet> _sets = new(StringComparer.Ordinal);

    /// <summary>How many sets have been read so far.</summary>
    public int Loaded
    {
        get
        {
            lock (_gate)
            {
                return _sets.Count;
            }
        }
    }

    /// <summary>The pattern set key of a language tag (en-US, en-GB, de-DE, any case), or null for a language with no patterns.</summary>
    public static string? KeyFor(string? language)
    {
        if (language is null)
        {
            return null;
        }

        if (language.Equals("en-US", StringComparison.OrdinalIgnoreCase))
        {
            return "hyph-en-us";
        }

        if (language.Equals("en-GB", StringComparison.OrdinalIgnoreCase))
        {
            return "hyph-en-gb";
        }

        return language.Equals("de-DE", StringComparison.OrdinalIgnoreCase) ? "hyph-de-1996" : null;
    }

    public PatternSet? Get(string? language)
    {
        var key = KeyFor(language);
        if (key is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_sets.TryGetValue(key, out var loaded))
            {
                return loaded;
            }

            using var stream = s_assembly.GetManifestResourceStream($"Raffica.Write.Layout.Patterns.{key}.pat.txt");
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var german = key == "hyph-de-1996";
            var set = PatternSet.Parse(reader, left: 2, right: german ? 2 : 3);
            _sets[key] = set;
            return set;
        }
    }
}
