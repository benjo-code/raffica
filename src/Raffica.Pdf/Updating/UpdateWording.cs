using System.Globalization;
using Raffica.Core.Updates;

namespace Raffica.Pdf.Updating;

/// <summary>What an applier (the Windows installer and its helper, the AppImage updater) reports when it could not install; <see cref="UpdateWording.ForApplier"/> words each one.</summary>
internal enum UpdateApplyProblem
{
    /// <summary>The signed release information of the staging folder did not pass the check, or the folder is not one of the updates.</summary>
    Rejected,

    /// <summary>The staged or copied file does not have the size and SHA-256 of the signed manifest.</summary>
    Checksum,

    /// <summary>A full disk or any other failure to write the update; the running version is unchanged.</summary>
    Storage,

    /// <summary>The folder cannot be written (a write-protected folder or mount); the user installs by hand.</summary>
    FolderWriteProtected,

    /// <summary>The installer could not be started or the file could not be put in place; the running version is unchanged.</summary>
    InstallFailed,

    /// <summary>The file is replaced but the new version could not be started.</summary>
    NotStarted,

    /// <summary>The setup program ended without an error but another version is installed.</summary>
    VersionMismatch,

    /// <summary>The setup program of the helper failed or was declined (the permission prompt); files may have been replaced.</summary>
    HelperInstallFailed,

    /// <summary>This program cannot be proven to be the installation to update.</summary>
    NoInstaller,

    /// <summary>The program runs from a mounted disk image or a copy macOS moved aside, so it cannot replace itself; the user moves it to Applications or installs by hand.</summary>
    RunsFromDownload,
}

/// <summary>
/// The words of the update UI. Core's <see cref="UpdateFailure"/> codes and the text of an applier are never shown as they are: every code has a
/// sentence in both languages here, and the sentences say what was or was not changed and what was checked (the signature, the size and the
/// SHA-256 of the file), never that an update is "safe" or "guaranteed" (<c>NoUiTextOverpromisesUpdates</c>).
/// </summary>
internal static class UpdateWording
{
    /// <summary>The sentence of a failure code in a given language (the UI language is not touched).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The code has no wording.</exception>
    public static string ForFailure(string error, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return PdfStrings.GetIn(KeyOf(error), culture);
    }

    /// <summary>The resource key of the sentence of a failure code.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The code has no wording.</exception>
    public static string KeyOf(string? error) => error switch
    {
        UpdateFailure.Unreachable => "UpdateFailureUnreachable",
        UpdateFailure.Withdrawn => "UpdateFailureWithdrawn",
        UpdateFailure.Superseded => "UpdateFailureSuperseded",
        UpdateFailure.Rejected => "UpdateFailureRejected",
        UpdateFailure.Checksum => "UpdateFailureChecksum",
        UpdateFailure.Storage => "UpdateFailureStorage",
        UpdateFailure.NoInstaller => "UpdateFailureNoInstaller",
        UpdateFailure.InstallFailed => "UpdateFailureInstallFailed",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "This update failure has no text."),
    };

    /// <summary>
    /// The sentence an applier gives for its failure, in <paramref name="culture"/> (what <see cref="Raffica.Core.Updates.ApplyResult.Message"/> carries; the UI shows it as it is).
    /// <paramref name="folder"/> names the folder of <see cref="UpdateApplyProblem.FolderWriteProtected"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The problem has no wording.</exception>
    public static string ForApplier(UpdateApplyProblem problem, CultureInfo culture, string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var text = problem switch
        {
            UpdateApplyProblem.Rejected => PdfStrings.GetIn("UpdateFailureRejected", culture),
            UpdateApplyProblem.Checksum => PdfStrings.GetIn("UpdateFailureChecksum", culture),
            UpdateApplyProblem.Storage => PdfStrings.GetIn("UpdateFailureStorage", culture),
            UpdateApplyProblem.FolderWriteProtected => PdfStrings.GetIn("UpdateFailureFolderWriteProtectedFormat", culture),
            UpdateApplyProblem.InstallFailed => PdfStrings.GetIn("UpdateFailureInstallFailed", culture),
            UpdateApplyProblem.NotStarted => PdfStrings.GetIn("UpdateFailureNotStarted", culture),
            UpdateApplyProblem.VersionMismatch => PdfStrings.GetIn("UpdateFailureVersionMismatch", culture),
            UpdateApplyProblem.HelperInstallFailed => PdfStrings.GetIn("UpdateFailureHelperInstallFailed", culture),
            UpdateApplyProblem.NoInstaller => PdfStrings.GetIn("UpdateFailureNoInstaller", culture),
            UpdateApplyProblem.RunsFromDownload => PdfStrings.GetIn("UpdateFailureRunsFromDownload", culture),
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "This applier problem has no text."),
        };
        return text.Replace("{0}", folder ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The message of a failed update: the text the applier gave for its failure (already in the UI language), else the sentence of the code.</summary>
    public static string ForFailure(UpdateState state, string? applierMessage) =>
        !string.IsNullOrWhiteSpace(applierMessage) ? applierMessage : ForFailure(state.Error ?? UpdateFailure.InstallFailed, PdfStrings.Culture);

    /// <summary>A size in megabytes with one decimal ("12.3 MB"), in the UI culture.</summary>
    public static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.0", PdfStrings.Culture) + " MB";
}
