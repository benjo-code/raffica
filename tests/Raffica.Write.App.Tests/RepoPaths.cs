namespace Raffica.WriteApp.Tests;

/// <summary>Finds the repository root, for tests that read source files and the corpus.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>A file of the compatibility corpus (<c>testdata/write-corpus/&lt;id&gt;/&lt;id&gt;.docx</c>).</summary>
    public static string Corpus(string id) => Combine("testdata", "write-corpus", id, id + ".docx");

    /// <summary>
    /// The built recovery harness (<c>tests/Raffica.Write.RecoveryHarness</c>), in the same configuration as this test assembly. The test project references it without using its
    /// assembly, so a build of the solution or of the tests builds it first.
    /// </summary>
    public static string RecoveryHarness
    {
        get
        {
            var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var configuration = output.Parent?.Name ?? "Release";
            var name = "Raffica.Write.RecoveryHarness" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
            return Combine("tests", "Raffica.Write.RecoveryHarness", "bin", configuration, output.Name, name);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Raffica.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Raffica.slnx not found above " + AppContext.BaseDirectory);
    }
}
