using System.Globalization;
using Raffica.Core.Updates;

namespace Raffica.Pdf.Updating;

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

    /// <summary>The message of a failed update: the text the applier gave for its failure (already in the UI language), else the sentence of the code.</summary>
    public static string ForFailure(UpdateState state, string? applierMessage) =>
        !string.IsNullOrWhiteSpace(applierMessage) ? applierMessage : ForFailure(state.Error ?? UpdateFailure.InstallFailed, PdfStrings.Culture);

    /// <summary>A size in megabytes with one decimal ("12.3 MB"), in the UI culture.</summary>
    public static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.0", PdfStrings.Culture) + " MB";
}
