using System.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Why a picture command is refused. The caller words it for the user in the language of the interface; no member names the picture, its bytes or its alt text.</summary>
public enum PictureRefusal
{
    /// <summary>There is no paragraph at the place (or it is not in the document).</summary>
    NoPosition,

    /// <summary>The document is opened as a prefix (the first blocks only) and cannot be edited that way.</summary>
    PrefixDocument,

    /// <summary>The place is in a header or a footer (pictures are inserted into the body and its table cells only).</summary>
    HeaderOrFooter,

    /// <summary>The place is in a footnote or an endnote.</summary>
    Note,

    /// <summary>The place is in a comment.</summary>
    Comment,

    /// <summary>The place is in a plain-text, combo box, drop-down list or date content control, which holds text only.</summary>
    RestrictedControl,

    /// <summary>The place is in a table nested in a cell of another table.</summary>
    NestedTable,

    /// <summary>The identity names no plain picture in the body any more (the document changed, or the drawing is a text box, a shape or a chart).</summary>
    NoPicture,

    /// <summary>The picture is turned by an angle: its size is not edited.</summary>
    Rotated,

    /// <summary>A floating picture in a table cell, or a move into one: floats in cells are laid out as inline boxes, so they are not offered.</summary>
    FloatInCell,

    /// <summary>The command moves a floating picture and the picture is inline.</summary>
    NotFloating,

    /// <summary>The picture is placed by an alignment keyword (left, centred, right ...), not an offset: it is not nudged by points.</summary>
    AlignedPosition,

    /// <summary>The move target is in another list of blocks than the picture (another content control, a cell, another story).</summary>
    AnotherPlace,

    /// <summary>The bytes are no PNG, JPEG, GIF, BMP or TIFF, or the header is cut short, unreadable or states no size.</summary>
    NotAnImage,

    /// <summary>The file is larger than 50 MiB.</summary>
    TooLarge,

    /// <summary>The picture has more than 32,000,000 pixels.</summary>
    TooManyPixels,

    /// <summary>The pictures of the document together would hold more than the store takes (256 MiB).</summary>
    TooMuchMedia,

    /// <summary>The size is zero, negative or larger than Word's limit (1584 points).</summary>
    BadSize,

    /// <summary>The alt text is longer than 4096 characters.</summary>
    AltTextTooLong,

    /// <summary>The command would change nothing (the same size, wrap, alt text or position).</summary>
    NothingToChange,

    /// <summary>The picture's original size cannot be told (its bytes are not at hand or are unreadable).</summary>
    OriginalSizeUnknown,
}

/// <summary>The limits and the rules of where a picture may stand and what bytes it may be made from.</summary>
public static class PictureRules
{
    /// <summary>The most bytes a picture file may have (50 MiB).</summary>
    public const long MaxBytes = MediaStore.MaxEntryBytes;

    /// <summary>The most pixels a picture may have (32,000,000), the header says how many.</summary>
    public const long MaxPixels = 32_000_000;

    /// <summary>The longest alt text (4096 characters).</summary>
    public const int MaxAltTextLength = 4096;

    /// <summary>The largest side of a picture's frame: Word's limit of 1584 points, 20,116,800 EMU.</summary>
    public const long MaxExtentEmu = 20_116_800;

    /// <summary>The largest distance a floating picture is moved from what its position is measured from, either way (twice the largest side).</summary>
    public const long MaxOffsetEmu = 2 * MaxExtentEmu;

    /// <summary>EMU of one pixel at 96 DPI.</summary>
    public const long EmuPerPixel = 9525;

    /// <summary>EMU of one point.</summary>
    public const long EmuPerPoint = 12700;

    /// <summary>
    /// The reason a picture cannot be inserted at <paramref name="caret"/>, or null when the place takes one: the body and the cells of its tables (not nested ones), not inside a header, a footer, a note, a comment or
    /// a content control of the kinds text, combo box, drop-down list or date (a paragraph of an inline control of those kinds is judged by <see cref="TextEditor.RefusalForPicture"/>, which knows the offset).
    /// A prefix document is refused first.
    /// </summary>
    public static PictureRefusal? Refusal(WordDocument document, TextPosition caret, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (prefixDocument)
        {
            return PictureRefusal.PrefixDocument;
        }

        if (caret.Paragraph is null)
        {
            return PictureRefusal.NoPosition;
        }

        if (StoryRefusal(caret.Story) is { } story)
        {
            return story;
        }

        if (TextEditor.FindIn(document.Body.Blocks, caret.Paragraph) is not { } found)
        {
            return PictureRefusal.NoPosition;
        }

        if (TextEditor.InSingleParagraphControl(document.Body.Blocks, found.Container))
        {
            return PictureRefusal.RestrictedControl;
        }

        return TextEditor.TableDepthOf(document.Body.Blocks, found.Container) > 1 ? PictureRefusal.NestedTable : null;
    }

    /// <summary>The reason a story takes no picture (a header, footer, note or comment: the kind says it), null for the body, <see cref="PictureRefusal.NoPosition"/> for any other.</summary>
    public static PictureRefusal? StoryRefusal(StoryRef story) => story.Kind switch
    {
        StoryKind.Body => null,
        StoryKind.HeaderFooter => PictureRefusal.HeaderOrFooter,
        StoryKind.Note => PictureRefusal.Note,
        StoryKind.Comment => PictureRefusal.Comment,
        _ => PictureRefusal.NoPosition,
    };

    /// <summary>
    /// The gate every picture file passes before the document is touched: not empty and at most 50 MiB, a header of a PNG, JPEG, GIF, BMP or TIFF that states a size, and at most 32,000,000 pixels by that header.
    /// Returns the facts of the header, or the reason. A header that states a size the data does not hold is accepted (the layout shows a marked box for a picture it cannot decode); the bytes are never read
    /// past the header here and appear in no message.
    /// </summary>
    public static (ImageHeaderInfo? Info, PictureRefusal? Refusal) CheckBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBytes)
        {
            return (null, PictureRefusal.TooLarge);
        }

        if (ImageHeaderProbe.Read(bytes) is not { } info)
        {
            return (null, PictureRefusal.NotAnImage);
        }

        return (long)info.Width * info.Height > MaxPixels ? (null, PictureRefusal.TooManyPixels) : (info, null);
    }

    /// <summary>The alt text without the characters XML cannot hold, or the refusal when it is longer than <see cref="MaxAltTextLength"/> (measured before the cleaning). Null becomes null.</summary>
    public static (string? Text, PictureRefusal? Refusal) CheckAltText(string? altText)
    {
        if (altText is null)
        {
            return (null, null);
        }

        if (altText.Length > MaxAltTextLength)
        {
            return (null, PictureRefusal.AltTextTooLong);
        }

        var clean = new System.Text.StringBuilder(altText.Length);
        for (var i = 0; i < altText.Length; i++)
        {
            var c = altText[i];
            if (char.IsHighSurrogate(c) && i + 1 < altText.Length && char.IsLowSurrogate(altText[i + 1]))
            {
                clean.Append(c).Append(altText[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                clean.Append(c);
            }
        }

        return (clean.ToString(), null);
    }
}
