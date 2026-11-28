namespace Raffica.Write.Editing;

/// <summary>Why a bookmark name is refused.</summary>
public enum BookmarkNameProblem
{
    /// <summary>The name is empty.</summary>
    Empty,

    /// <summary>The name has more than <see cref="BookmarkNames.MaxLength"/> characters.</summary>
    TooLong,

    /// <summary>The first character is not a letter (A to Z).</summary>
    BadStart,

    /// <summary>A later character is not a letter, a digit or an underscore.</summary>
    BadCharacter,

    /// <summary>A bookmark of that name exists (names compare ignoring case).</summary>
    Duplicate,
}

/// <summary>
/// Word's rule for the name of a bookmark: a letter first, then letters, digits and underscores, at most 40 characters, unique ignoring case. Letters are the ASCII letters, so a name is safe in a field
/// instruction. A name that starts with an underscore is Write's own (<c>_Toc</c>, <c>_Ref</c>): hidden, never listed and never made by the user.
/// </summary>
public static class BookmarkNames
{
    /// <summary>The longest name Word accepts.</summary>
    public const int MaxLength = 40;

    /// <summary>The first problem of <paramref name="name"/>, or null when it is a valid new name. <paramref name="existing"/> holds the names of the bookmarks of the document.</summary>
    public static BookmarkNameProblem? Problem(string name, IReadOnlyCollection<string> existing)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(existing);
        if (name.Length == 0)
        {
            return BookmarkNameProblem.Empty;
        }

        if (name.Length > MaxLength)
        {
            return BookmarkNameProblem.TooLong;
        }

        if (!IsAsciiLetter(name[0]))
        {
            return BookmarkNameProblem.BadStart;
        }

        foreach (var c in name)
        {
            if (!IsAsciiLetter(c) && c is not (>= '0' and <= '9') && c != '_')
            {
                return BookmarkNameProblem.BadCharacter;
            }
        }

        return existing.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) ? BookmarkNameProblem.Duplicate : null;
    }

    /// <summary>Whether the name is one of Write's own (it starts with an underscore): such a name is never listed.</summary>
    public static bool IsHidden(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith('_');
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
