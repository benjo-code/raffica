using Raffica.Shell.Frame;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>The values of the picture commands that take a payload (<c>picture.wrap</c>: a wrap id); a payload that is not one is refused (false), never an exception.</summary>
internal static class PicturePayloads
{
    /// <summary>The wrap ids of the picker, in the order it shows them.</summary>
    public static IReadOnlyList<string> Wraps { get; } = ["inline", "square", "topAndBottom", "inFront", "behind"];

    /// <summary>Whether <paramref name="payload"/> is a value of the command (null is none of them for a value command; the other picture commands take no payload).</summary>
    public static bool IsValid(string id, object? payload) => id switch
    {
        WriteCommandIds.PictureWrap => payload is string wrap && TryWrap(wrap, out _),
        WriteCommandIds.PictureInsert => payload is PictureFile { Bytes.Length: > 0 },
        WriteCommandIds.PictureProperties => payload is PictureProperties { IsEmpty: false },
        _ => payload is null,
    };

    /// <summary>The wrap of a wrap id; false for an id that is none.</summary>
    public static bool TryWrap(string id, out PictureWrap wrap)
    {
        wrap = PictureWrap.Inline;
        switch (id)
        {
            case "inline":
                return true;
            case "square":
                wrap = PictureWrap.Square;
                return true;
            case "topAndBottom":
                wrap = PictureWrap.TopAndBottom;
                return true;
            case "inFront":
                wrap = PictureWrap.InFront;
                return true;
            case "behind":
                wrap = PictureWrap.Behind;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The words of a wrap id in the UI language.</summary>
    public static string WrapLabel(string id) => id switch
    {
        "inline" => WriteStrings.PictureWrapInline,
        "square" => WriteStrings.PictureWrapSquare,
        "topAndBottom" => WriteStrings.PictureWrapTopAndBottom,
        "inFront" => WriteStrings.PictureWrapInFront,
        _ => WriteStrings.PictureWrapBehind,
    };

    /// <summary>The id of a wrap.</summary>
    public static string IdOf(PictureWrap wrap) => wrap switch
    {
        PictureWrap.Square => "square",
        PictureWrap.TopAndBottom => "topAndBottom",
        PictureWrap.InFront => "inFront",
        PictureWrap.Behind => "behind",
        _ => "inline",
    };
}

/// <summary>
/// The picker of Text wrapping (the five wraps). It shows the name of its command, lists its items when it opens and never before, and runs a choice through the window's <c>RunCommand(id, payload)</c>, which validates it.
/// </summary>
/// <param name="run">Runs a value command with its payload; false when it was refused.</param>
internal sealed class PicturePickers(Func<string, object?, bool> run)
{
    private const string Unset = "-";

    /// <summary>How many times a list was asked (a list is read when it opens, never before; tests pin that).</summary>
    internal int ListReads { get; private set; }

    /// <summary>The picker of a command, or null when the command is no picker.</summary>
    public PickerDefinition? For(string commandId) => commandId == WriteCommandIds.PictureWrap
        ? new PickerDefinition(
            () =>
            {
                ListReads++;
                return [.. PicturePayloads.Wraps.Select(id => new PickerItem(id, PicturePayloads.WrapLabel(id)))];
            },
            id => run(WriteCommandIds.PictureWrap, id),
            () => Unset,
            SelectedLabel: () => WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.PictureWrap).Name,
            Width: 150)
        : null;
}

/// <summary>A picture file read for Insert picture: its bytes and the description the user gave (possibly empty). It names no file; the bytes and the text are never put into a message or a log.</summary>
internal sealed record PictureFile(byte[] Bytes, string AltText);
