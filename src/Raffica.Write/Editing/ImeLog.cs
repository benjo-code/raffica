using System.Text;

namespace Raffica.Write.Editing;

/// <summary>
/// The log of the input method client, for the owner's checks on real systems (<c>Docs/write-ime-check.md</c>): when the environment variable
/// <see cref="Variable"/> names a file, one line per event is appended to it: <c>preedit len=&lt;n&gt;</c> (the preedit was set or changed; <c>n</c> counts UTF-16 code
/// units), <c>commit len=&lt;n&gt;</c> (text arrived as the result of a composition) and <c>cancel</c> (a composition ended without text). The lengths only: the text
/// of a composition is what the user typed and never goes into a log. UTF-8 without a byte order mark, one line per event; the file is opened (append, readable by
/// others) by the first event and not before, and an error writing it is swallowed: a log must never take the editor down. Nothing exists without the variable.
/// </summary>
internal sealed class ImeLog(Func<string?> path)
{
    /// <summary>The name of the environment variable that holds the path of the log file.</summary>
    public const string Variable = "RAFFICA_WRITE_IME_LOG";

    private static readonly UTF8Encoding s_utf8 = new(false);

    /// <summary>The preedit was set or changed to a text of <paramref name="length"/> code units.</summary>
    public void Preedit(int length) => Write("preedit len=" + length.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A composition ended with text of <paramref name="length"/> code units.</summary>
    public void Commit(int length) => Write("commit len=" + length.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A composition ended without text.</summary>
    public void Cancel() => Write("cancel");

    private void Write(string line)
    {
        try
        {
            if (path() is not { Length: > 0 } file)
            {
                return;
            }

            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = s_utf8.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never crash for a log: a path that cannot be written is no reason to lose a composition.
        }
    }
}
