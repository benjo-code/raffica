using Raffica.Core.Saving;
using Raffica.Pdf.Organizing;

namespace Raffica.Pdf.Documents;

/// <summary>
/// Finds the other tab that has a chosen write target open. PDFium opens files with share-delete, so a replace succeeds under an open tab and two
/// tabs would end with one path (a stale one could later write old content back): every flow that writes to a path the user picked asks this first,
/// after the picker and after the last re-check, immediately before the write.
/// </summary>
internal static class OpenTargetGuard
{
    /// <summary>
    /// The other open tab (never <paramref name="self"/>) whose path is <paramref name="target"/> or another name of the same file, or null.
    /// The spelling is compared first (<see cref="JobRunner.SamePath"/>); the file system answers for links, hard links and short names, off the UI thread.
    /// <paramref name="sameFile"/> replaces the file system question (tests); null: <see cref="FileIdentity.AreSameFile"/>.
    /// A null answer of <see cref="FileIdentity.AreSameFile"/> counts as the same file on Windows, Linux and macOS (a refused write costs a click, a replaced open file costs data);
    /// <paramref name="identitySupported"/> replaces that OS test (tests); on other systems the answer is "another file" and only the spelling decides.
    /// </summary>
    public static async Task<PdfTab?> FindOtherTabAsync(Workspace workspace, PdfTab self, string target, CancellationToken token, Func<string, string, bool?>? sameFile = null, bool? identitySupported = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentException.ThrowIfNullOrEmpty(target);
        var others = workspace.Tabs.Where(tab => !ReferenceEquals(tab, self) && tab.Path is not null).Select(tab => (Tab: tab, Path: tab.Path!)).ToList();
        foreach (var (tab, path) in others)
        {
            if (JobRunner.SamePath(target, path))
            {
                return tab;
            }
        }

        var identity = sameFile ?? FileIdentity.AreSameFile;

        // FileIdentity answers null for "cannot tell". On Windows, Linux and macOS that is a real failure (the file could not be asked), which counts as the same file.
        // On any other OS it is the answer for every pair (no identity call exists there): counting it as the same file would refuse every target, so
        // only the spelling decides there.
        var nullMeansSame = identitySupported ?? (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        if (others.Count == 0)
        {
            return null;
        }

        return await Task.Run(
            () =>
            {
                foreach (var (tab, path) in others)
                {
                    token.ThrowIfCancellationRequested();
                    if (identity(target, path) ?? nullMeansSame)
                    {
                        return tab;
                    }
                }

                return null;
            },
            token);
    }

    /// <summary>The note that says the target is taken (the file name is filled in).</summary>
    public static string NoteFor(string target) => PdfStrings.Fill(PdfStrings.TargetOpenInOtherTabFormat, Path.GetFileName(target));
}
