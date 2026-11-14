#:property PublishAot=false

// Generates the line break table of Raffica Write from the Unicode data files in third-party/unicode.
//   dotnet run tools/gen-linebreak.cs              writes src/Raffica.Write.Layout/Text/LineBreakData.g.cs
//   dotnet run tools/gen-linebreak.cs -- --check   builds the table in memory and fails (exit 1) when the committed file differs
// Run from the repository root (--root <dir> names another one). The tool never uses the network. The class numbers come from
// src/Raffica.Write.Layout/Text/LineBreakClass.cs. Rule LB1 is applied here (AI, SG, XX -> AL; SA -> CM for Mn and Mc, else AL;
// CJ -> NS) and the quotation marks are split by general category (Pi, Pf), so the table holds the resolved class.
// One byte per code point: bits 0-5 the class, bit 6 East_Asian_Width F, W or H, bit 7 Extended_Pictographic and Cn (rule LB30b).

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

const int MaxCodePoint = 0x10FFFF;
const byte EastAsianBit = 0x40;
const byte PictographicUnassignedBit = 0x80;

var check = false;
var root = ".";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--check")
    {
        check = true;
    }
    else if (args[i] == "--root" && i + 1 < args.Length)
    {
        root = args[++i];
    }
    else
    {
        Console.Error.WriteLine("usage: dotnet run tools/gen-linebreak.cs -- [--check] [--root <dir>]");
        return 2;
    }
}

var dataDir = Path.Combine(root, "third-party", "unicode");
var classFile = Path.Combine(root, "src", "Raffica.Write.Layout", "Text", "LineBreakClass.cs");
var outputFile = Path.Combine(root, "src", "Raffica.Write.Layout", "Text", "LineBreakData.g.cs");
foreach (var required in new[] { classFile, Path.Combine(dataDir, "README.md") })
{
    if (!File.Exists(required))
    {
        Console.Error.WriteLine($"missing {required} (run from the repository root)");
        return 2;
    }
}

var readme = File.ReadAllText(Path.Combine(dataDir, "README.md"));
var version = Regex.Match(readme, @"^Version:\s*(\S+)\s*$", RegexOptions.Multiline).Groups[1].Value;
var retrieved = Regex.Match(readme, @"^Retrieved:\s*(\d{4}-\d{2}-\d{2})\s*$", RegexOptions.Multiline).Groups[1].Value;
if (version.Length == 0 || retrieved.Length == 0)
{
    Console.Error.WriteLine("third-party/unicode/README.md needs the lines 'Version: x.y.z' and 'Retrieved: yyyy-mm-dd'");
    return 2;
}

var classNumbers = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
foreach (Match m in Regex.Matches(File.ReadAllText(classFile), @"^\s*(\w+)\s*=\s*(\d+),", RegexOptions.Multiline))
{
    classNumbers[m.Groups[1].Value] = byte.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
}

if (classNumbers.Count == 0 || classNumbers.Values.Max() > 0x3F)
{
    Console.Error.WriteLine("LineBreakClass.cs holds no classes or a number above 63");
    return 2;
}

byte Number(string name)
{
    if (!classNumbers.TryGetValue(name, out var n))
    {
        throw new InvalidDataException($"class {name} is not in LineBreakClass.cs");
    }

    return n;
}

// Reads "XXXX..YYYY ; value # comment" lines and calls apply(first, last, value).
void Read(string file, Action<int, int, string> apply, string header)
{
    var lines = File.ReadAllLines(Path.Combine(dataDir, file));
    if (lines.Length == 0 || !lines[0].Contains(header, StringComparison.Ordinal))
    {
        throw new InvalidDataException($"{file} does not start with '{header}' (expected Unicode {version})");
    }

    foreach (var raw in lines)
    {
        var hash = raw.IndexOf('#', StringComparison.Ordinal);
        var line = (hash >= 0 ? raw[..hash] : raw).Trim();
        if (line.Length == 0)
        {
            continue;
        }

        var parts = line.Split(';', 2);
        var range = parts[0].Trim();
        var dots = range.IndexOf("..", StringComparison.Ordinal);
        var first = int.Parse(dots < 0 ? range : range[..dots], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var last = dots < 0 ? first : int.Parse(range[(dots + 2)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        apply(first, last, parts[1].Trim());
    }
}

static void AppendBytes(StringBuilder sb, IEnumerable<byte> data)
{
    var column = 0;
    foreach (var value in data)
    {
        if (column == 0)
        {
            sb.Append("        ");
        }

        sb.Append("0x").Append(value.ToString("X2", CultureInfo.InvariantCulture)).Append(',');
        column++;
        if (column == 16)
        {
            sb.Append('\n');
            column = 0;
        }
        else
        {
            sb.Append(' ');
        }
    }

    if (column != 0)
    {
        sb.Length -= 1;
        sb.Append('\n');
    }
}

try
{
    // Defaults: Line_Break XX, East_Asian_Width N, general category Cn, not Extended_Pictographic.
    var lineBreak = new string[MaxCodePoint + 1];
    var generalCategory = new string[MaxCodePoint + 1];
    var wide = new bool[MaxCodePoint + 1];
    var pictographic = new bool[MaxCodePoint + 1];
    Array.Fill(lineBreak, "XX");
    Array.Fill(generalCategory, "Cn");

    Read("LineBreak.txt", (a, b, v) => Array.Fill(lineBreak, v, a, b - a + 1), $"LineBreak-{version}.txt");
    Read("DerivedGeneralCategory.txt", (a, b, v) => Array.Fill(generalCategory, v, a, b - a + 1), $"DerivedGeneralCategory-{version}.txt");
    Read("EastAsianWidth.txt", (a, b, v) => Array.Fill(wide, v is "F" or "W" or "H", a, b - a + 1), $"EastAsianWidth-{version}.txt");
    Read(
        "emoji-data.txt",
        (a, b, v) =>
        {
            if (v == "Extended_Pictographic")
            {
                Array.Fill(pictographic, true, a, b - a + 1);
            }
        },
        "emoji-data.txt");

    var values = new byte[MaxCodePoint + 1];
    for (var cp = 0; cp <= MaxCodePoint; cp++)
    {
        var original = lineBreak[cp];
        var gc = generalCategory[cp];
        var resolved = original switch
        {
            "AI" or "SG" or "XX" => "AL",
            "SA" => gc is "Mn" or "Mc" ? "CM" : "AL",
            "CJ" => "NS",
            "QU" => gc == "Pi" ? "QuPi" : gc == "Pf" ? "QuPf" : "QU",
            _ => original,
        };
        var value = Number(resolved);
        if (wide[cp])
        {
            value |= EastAsianBit;
        }

        if (pictographic[cp] && gc == "Cn")
        {
            value |= PictographicUnassignedBit;
        }

        values[cp] = value;
    }

    // Two stages: a block index per block of 2^shift code points and the distinct blocks. The largest block size whose
    // block count fits an index byte is used.
    var shift = 8;
    List<byte[]> blocks = [];
    var blockOfIndex = Array.Empty<int>();
    for (; shift >= 4; shift--)
    {
        var size = 1 << shift;
        var count = (MaxCodePoint + 1) >> shift;
        blocks = [];
        blockOfIndex = new int[count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var b = 0; b < count; b++)
        {
            var block = values.AsSpan(b * size, size).ToArray();
            var key = Convert.ToBase64String(block);
            if (!seen.TryGetValue(key, out var id))
            {
                id = blocks.Count;
                seen[key] = id;
                blocks.Add(block);
            }

            blockOfIndex[b] = id;
        }

        if (blocks.Count <= 256)
        {
            break;
        }
    }

    if (blocks.Count > 256)
    {
        throw new InvalidDataException("the table does not fit two byte stages");
    }

    var o = new StringBuilder();
    o.Append("// <auto-generated>\n");
    o.Append("// Generated by tools/gen-linebreak.cs from the Unicode ").Append(version).Append(" data files in third-party/unicode (retrieved ").Append(retrieved).Append(").\n");
    o.Append("// Do not edit; run `dotnet run tools/gen-linebreak.cs`. CI runs it with --check.\n");
    o.Append("// Unicode data files: Copyright (c) 1991-2026 Unicode, Inc., Unicode License v3 (third-party/unicode/LICENSE-Unicode.txt).\n");
    o.Append("// </auto-generated>\n\n");
    o.Append("namespace Raffica.Write.Layout;\n\n");
    o.Append("/// <summary>The line break class of every code point (UAX #14, Unicode ").Append(version).Append("), resolved by rule LB1, with two facts the rules need.</summary>\n");
    o.Append("internal static class LineBreakData\n{\n");
    o.Append("    /// <summary>The Unicode version of the table.</summary>\n");
    o.Append("    public const string UnicodeVersion = \"").Append(version).Append("\";\n\n");
    o.Append("    /// <summary>Bits 0-5 of a value: the <see cref=\"LineBreakClass\"/>.</summary>\n    public const byte ClassMask = 0x3F;\n\n");
    o.Append("    /// <summary>The code point has East_Asian_Width F, W or H.</summary>\n    public const byte EastAsian = 0x40;\n\n");
    o.Append("    /// <summary>The code point is Extended_Pictographic and unassigned (general category Cn).</summary>\n    public const byte PictographicUnassigned = 0x80;\n\n");
    o.Append("    private const int BlockShift = ").Append(shift.ToString(CultureInfo.InvariantCulture)).Append(";\n");
    o.Append("    private const int BlockMask = (1 << BlockShift) - 1;\n\n");
    o.Append("    /// <summary>The packed value of a code point (0 to 0x10FFFF); a value outside that range reads as AL.</summary>\n");
    o.Append("    public static byte Get(int codePoint)\n    {\n");
    o.Append("        if ((uint)codePoint > 0x10FFFF)\n        {\n            return 0;\n        }\n\n");
    o.Append("        return Blocks[(Index[codePoint >> BlockShift] << BlockShift) | (codePoint & BlockMask)];\n    }\n\n");
    o.Append("    // Static data in the assembly: no initialization work at startup.\n");
    o.Append("    private static ReadOnlySpan<byte> Index =>\n    [\n");
    AppendBytes(o, blockOfIndex.Select(id => (byte)id));
    o.Append("    ];\n\n");
    o.Append("    private static ReadOnlySpan<byte> Blocks =>\n    [\n");
    AppendBytes(o, blocks.SelectMany(b => b));
    o.Append("    ];\n}\n");

    var text = o.ToString();
    if (!check)
    {
        File.WriteAllText(outputFile, text, new UTF8Encoding(false));
        Console.WriteLine($"wrote {outputFile}: Unicode {version}, {blocks.Count} blocks of {1 << shift}");
        return 0;
    }

    var current = File.Exists(outputFile) ? File.ReadAllText(outputFile).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
    if (current != text)
    {
        Console.Error.WriteLine($"{outputFile} is stale: run `dotnet run tools/gen-linebreak.cs` and commit the result");
        return 1;
    }

    Console.WriteLine("the line break data is current");
    return 0;
}
catch (Exception e) when (e is InvalidDataException or IOException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
