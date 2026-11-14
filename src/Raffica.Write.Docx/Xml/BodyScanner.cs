namespace Raffica.Write.Docx.Xml;

/// <summary>
/// A forward scan over the bytes of a main document part that finds where the direct children of <c>w:body</c> start and end, without
/// building anything: it tracks tags, comments, processing instructions, CDATA and quoted attribute values, and nothing else. It is
/// only as exact as a prefix open needs; anything it does not understand makes <see cref="TryEnterBody"/> or
/// <see cref="NextChild"/> report failure and the caller reads the whole part.
/// </summary>
internal sealed class BodyScanner(byte[] xml, int limit)
{
    private int _position;
    private int _depth;
    private bool _inBody;

    /// <summary>Moves to the first byte after the start tag of <c>w:body</c> (a child of the root). False when the part has no such element before <c>limit</c>.</summary>
    public bool TryEnterBody()
    {
        while (_position < limit)
        {
            var kind = NextToken(out _, out var end, out var name, out var selfClosed);
            switch (kind)
            {
                case Token.None:
                case Token.Bad:
                    return false;
                case Token.Start:
                    if (_depth == 1 && IsLocal(name, "body"u8))
                    {
                        if (selfClosed)
                        {
                            return false;
                        }

                        _inBody = true;
                        _depth = 2;
                        _position = end;
                        return true;
                    }

                    if (!selfClosed)
                    {
                        _depth++;
                    }

                    break;
                case Token.End:
                    _depth--;
                    break;
            }

            _position = end;
        }

        return false;
    }

    /// <summary>
    /// Reads the next direct child of the body completely. False at the end of the blocks (the scan reached <c>limit</c> or the end tag
    /// of the body) and when the bytes could not be followed (<paramref name="failed"/>).
    /// </summary>
    public bool NextChild(out int start, out int end, out bool failed)
    {
        start = end = 0;
        failed = false;
        if (!_inBody)
        {
            failed = true;
            return false;
        }

        while (_position < limit)
        {
            var kind = NextToken(out var tokenStart, out var tokenEnd, out _, out var selfClosed);
            if (kind == Token.Bad)
            {
                failed = true;
                return false;
            }

            if (kind is Token.None or Token.End || tokenStart >= limit)
            {
                return false;
            }

            _position = tokenEnd;
            if (kind == Token.Skipped)
            {
                continue;
            }

            // A child element: read on to its end tag.
            start = tokenStart;
            var depth = selfClosed ? 0 : 1;
            while (depth > 0)
            {
                var inner = NextToken(out _, out var innerEnd, out _, out var innerSelfClosed);
                if (inner is Token.Bad or Token.None)
                {
                    failed = true;
                    return false;
                }

                _position = innerEnd;
                if (inner == Token.Start && !innerSelfClosed)
                {
                    depth++;
                }
                else if (inner == Token.End)
                {
                    depth--;
                }
            }

            end = _position;
            return true;
        }

        return false;
    }

    private enum Token
    {
        None,
        Bad,
        Start,
        End,
        Skipped,
    }

    /// <summary>Finds the next tag, comment, processing instruction or CDATA section at or after the position. Comments, instructions and CDATA are <see cref="Token.Skipped"/>.</summary>
    private Token NextToken(out int start, out int end, out ReadOnlySpan<byte> name, out bool selfClosed)
    {
        name = default;
        selfClosed = false;
        start = end = _position;
        var span = xml.AsSpan(0, limit);
        var offset = span[_position..].IndexOf((byte)'<');
        if (offset < 0)
        {
            return Token.None;
        }

        start = _position + offset;
        if (start + 1 >= limit)
        {
            return Token.Bad;
        }

        var next = xml[start + 1];
        if (next == (byte)'?')
        {
            return Until(span, start, "?>"u8, out end);
        }

        if (next == (byte)'!')
        {
            if (span[start..].StartsWith("<!--"u8))
            {
                return Until(span, start, "-->"u8, out end);
            }

            return span[start..].StartsWith("<![CDATA["u8) ? Until(span, start, "]]>"u8, out end) : Token.Bad;
        }

        var closing = next == (byte)'/';
        var nameStart = start + (closing ? 2 : 1);
        var i = nameStart;
        while (i < limit && xml[i] is not ((byte)'>' or (byte)'/' or (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
        {
            i++;
        }

        if (i == nameStart || i >= limit)
        {
            return Token.Bad;
        }

        name = span[nameStart..i];

        // The end of the tag: the first '>' outside a quoted attribute value (a value can hold '>' but never '<').
        byte quote = 0;
        for (; i < limit; i++)
        {
            var b = xml[i];
            if (quote != 0)
            {
                if (b == quote)
                {
                    quote = 0;
                }
            }
            else if (b is (byte)'"' or (byte)'\'')
            {
                quote = b;
            }
            else if (b == (byte)'>')
            {
                break;
            }
        }

        if (i >= limit)
        {
            return Token.Bad;
        }

        end = i + 1;
        selfClosed = !closing && xml[i - 1] == (byte)'/';
        return closing ? Token.End : Token.Start;
    }

    private static Token Until(ReadOnlySpan<byte> span, int start, ReadOnlySpan<byte> terminator, out int end)
    {
        var found = span[start..].IndexOf(terminator);
        if (found < 0)
        {
            end = start;
            return Token.Bad;
        }

        end = start + found + terminator.Length;
        return Token.Skipped;
    }

    private static bool IsLocal(ReadOnlySpan<byte> name, ReadOnlySpan<byte> local)
    {
        var colon = name.LastIndexOf((byte)':');
        return (colon < 0 ? name : name[(colon + 1)..]).SequenceEqual(local);
    }
}
