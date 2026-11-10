using System.Globalization;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>
/// The shell script that replaces the running bundle after the app has quit. Every value is a positional parameter of
/// <c>sh -c</c> (<c>$1</c> to <c>$7</c>), never part of the script text, so spaces and quotes in a path are harmless.
/// </summary>
internal static class MacSwapScript
{
    /// <summary>
    /// Parameters: 1 process id, 2 old bundle, 3 new bundle, 4 backup bundle, 5 opener (<c>/usr/bin/open</c>),
    /// 6 verifier (<c>/usr/bin/codesign</c>), 7 restore flag (<c>1</c> or <c>0</c>).
    /// </summary>
    public const string Text = """
        reopen() {
          # A function has its own positional parameters: opener, bundle, restore flag. The same rule on every path, so a failed
          # swap brings the old version back with the tabs, as the success path does.
          if [ "$3" = "1" ]; then
            "$1" "$2" --args --restore-session
          else
            "$1" "$2"
          fi
        }
        n=0
        while kill -0 "$1" 2>/dev/null && [ "$n" -lt 30 ]; do
          sleep 1
          n=$((n + 1))
        done
        if ! "$6" --verify --deep --strict "$3"; then
          rm -rf "$3"
          reopen "$5" "$2" "$7"
          exit 1
        fi
        if ! mv "$2" "$4"; then
          reopen "$5" "$2" "$7"
          exit 1
        fi
        if ! mv "$3" "$2"; then
          mv "$4" "$2"
          reopen "$5" "$2" "$7"
          exit 1
        fi
        rm -rf "$4"
        reopen "$5" "$2" "$7"
        """;

    /// <summary>The argument list for <c>/bin/sh</c>: the script and its seven positional parameters.</summary>
    public static IReadOnlyList<string> Arguments(
        int processId,
        string oldBundle,
        string newBundle,
        string backupBundle,
        string opener,
        string verifier,
        bool restoreSession) =>
    [
        "-c",
        Text,
        "sh",
        processId.ToString(CultureInfo.InvariantCulture),
        oldBundle,
        newBundle,
        backupBundle,
        opener,
        verifier,
        restoreSession ? "1" : "0",
    ];
}
