using System.Text;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class LineBreakConformanceTests
{
    private const char NoBreak = '×';
    private const char Break = '÷';

    [Fact]
    public void EveryLineOfLineBreakTestPasses()
    {
        var path = Path.Combine(Raffica.TestSupport.TestData.Root, "third-party", "unicode", "LineBreakTest.txt");
        var ran = 0;
        var failures = new List<string>();
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var text = new StringBuilder();
            var expected = new List<int>();
            foreach (var token in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length == 1 && token[0] == NoBreak)
                {
                    continue;
                }

                if (token.Length == 1 && token[0] == Break)
                {
                    if (text.Length > 0)
                    {
                        expected.Add(text.Length);
                    }

                    continue;
                }

                text.Append(char.ConvertFromUtf32(Convert.ToInt32(token, 16)));
            }

            var found = LineBreaker.Find(text.ToString()).Select(b => b.Offset).ToList();
            ran++;
            if (!found.SequenceEqual(expected))
            {
                failures.Add($"{line} -> found [{string.Join(",", found)}] expected [{string.Join(",", expected)}]");
            }
        }

        TestContext.Current.SendDiagnosticMessage($"LineBreakTest.txt: {ran} lines ran, {failures.Count} failed");
        Assert.True(ran >= 15000, $"only {ran} lines were read from LineBreakTest.txt");
        Assert.True(failures.Count == 0, $"{failures.Count} of {ran} lines failed, first: {string.Join(" | ", failures.Take(5))}");
    }
}
