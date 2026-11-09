using System.Text.RegularExpressions;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Saving;

public sealed partial class PathComparisonGuardTests
{
    [GeneratedRegex(@"IsWindows\(\)\s*\?\s*(StringComparison|StringComparer)\.OrdinalIgnoreCase")]
    private static partial Regex WindowsCaseChoice();

    [Fact]
    public void NoSourceFileChoosesPathCaseByWindowsCheck()
    {
        var src = Path.Combine(TestData.Root, "src");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (WindowsCaseChoice().IsMatch(lines[i]))
                {
                    hits.Add($"{Path.GetRelativePath(TestData.Root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(hits.Count == 0, "Use PathRules (Raffica.Core.Saving) instead of choosing the path case by IsWindows(): " + string.Join(", ", hits));
    }
}
