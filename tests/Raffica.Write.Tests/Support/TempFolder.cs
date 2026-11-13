namespace Raffica.Write.Tests.Support;

/// <summary>A folder under the system temp folder that is deleted when disposed.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "raffica-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>A copy of the committed thesis document in this folder.</summary>
    public string CopyThesis(string name = "thesis.docx")
    {
        var target = File(name);
        System.IO.File.Copy(ThesisPath, target);
        return target;
    }

    public static string ThesisPath => System.IO.Path.Combine(Raffica.TestSupport.TestData.Root, "spikes", "write", "testdoc", "thesis.docx");

    public string[] Names() => Directory.GetFileSystemEntries(Path).Select(System.IO.Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            try
            {
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A dangling link or a file that went away: nothing to reset.
            }
        }

        Directory.Delete(Path, recursive: true);
    }
}
