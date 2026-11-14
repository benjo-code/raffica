namespace Raffica.Write.Layout;

/// <summary>
/// The Unicode line breaking algorithm (UAX #14, revision 57 of Unicode 18.0.0): rules LB1 to LB31 over the classes of the generated
/// table, without tailoring. It is the library's own so that no Avalonia type is needed, and the official <c>LineBreakTest.txt</c>
/// proves it. The algorithm is a single left to right pass; the rules that look ahead (LB15b, LB15c, LB25, LB28a) read a few units
/// forward, so the pass is linear and allocates only the result.
/// </summary>
public static class LineBreaker
{
    private const int DottedCircle = 0x25CC;

    /// <summary>
    /// Finds the break opportunities of a text. An entry says a line may end before that UTF-16 offset (a mandatory one must end
    /// there). The offsets rise, never fall inside a surrogate pair and never include 0; the one at <c>text.Length</c> is always
    /// present (an empty text has the single entry 0) and is mandatory only when the text ends with a hard line break.
    /// </summary>
    public static IReadOnlyList<BreakOpportunity> Find(ReadOnlySpan<char> text)
    {
        var pass = new Pass(text);
        return pass.Run();
    }

    private enum Break
    {
        No,
        Yes,
        Mandatory,
    }

    private static int Decode(ReadOnlySpan<char> text, int index, out int length)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        length = 1;
        return c;
    }

    private static bool IsQuote(LineBreakClass c) => c is LineBreakClass.Qu or LineBreakClass.QuPi or LineBreakClass.QuPf;

    // The classes after which LB9 does not attach a combining mark: it starts a unit of its own (LB10).
    private static bool IsLineStart(LineBreakClass c) =>
        c is LineBreakClass.Bk or LineBreakClass.Cr or LineBreakClass.Lf or LineBreakClass.Nl or LineBreakClass.Sp or LineBreakClass.Zw;

    // LB15a: what may precede an initial quotation mark that holds the line together with what follows.
    private static bool IsInitialQuoteContext(LineBreakClass c) =>
        c is LineBreakClass.Bk or LineBreakClass.Cr or LineBreakClass.Lf or LineBreakClass.Nl or LineBreakClass.Op or LineBreakClass.Qu
            or LineBreakClass.QuPi or LineBreakClass.QuPf or LineBreakClass.Gl or LineBreakClass.Sp or LineBreakClass.Zw;

    // LB15b: what may follow a final quotation mark that holds the line together with what precedes.
    private static bool EndsFinalQuote(LineBreakClass c) =>
        c is LineBreakClass.Sp or LineBreakClass.Gl or LineBreakClass.Wj or LineBreakClass.Cl or LineBreakClass.Qu or LineBreakClass.QuPi
            or LineBreakClass.QuPf or LineBreakClass.Cp or LineBreakClass.Ex or LineBreakClass.Is or LineBreakClass.Sy or LineBreakClass.Bk
            or LineBreakClass.Cr or LineBreakClass.Lf or LineBreakClass.Nl or LineBreakClass.Zw;

    // One run over a text. A "unit" is the word of rule LB9: a base character with the combining marks attached to it; the
    // fields describe the previous unit and the run before it.
    private ref struct Pass
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly List<BreakOpportunity> _result;
        private bool _hasPrev;
        private LineBreakClass _prev;
        private bool _prevEastAsian;
        private bool _prevDotted;
        private bool _prevPictographicUnassigned;
        private bool _prevQuoteAfterNonEastAsian;
        private bool _hasPrev2;
        private LineBreakClass _prev2;
        private bool _prev2Dotted;
        private bool _hasLastNonSpace;
        private LineBreakClass _lastNonSpace;
        private bool _initialQuote; // LB15a: no break after an initial quotation mark and the spaces that follow it
        private int _number; // LB25: 1 after NU (SY|IS)*, 2 after NU (SY|IS)* (CL|CP)
        private int _regionalIndicators;
        private bool _previousWasJoiner;

        public Pass(ReadOnlySpan<char> text)
        {
            _text = text;
            _result = new List<BreakOpportunity>((text.Length / 4) + 1);
            _prev = LineBreakClass.Al;
            _prev2 = LineBreakClass.Al;
            _lastNonSpace = LineBreakClass.Al;
        }

        public List<BreakOpportunity> Run()
        {
            if (_text.Length == 0)
            {
                _result.Add(new BreakOpportunity(0, false));
                return _result;
            }

            var i = 0;
            while (i < _text.Length)
            {
                var codePoint = Decode(_text, i, out var length);
                var packed = LineBreakData.Get(codePoint);
                var cls = (LineBreakClass)(packed & LineBreakData.ClassMask);
                var eastAsian = (packed & LineBreakData.EastAsian) != 0;
                var pictographicUnassigned = (packed & LineBreakData.PictographicUnassigned) != 0;
                var dotted = codePoint == DottedCircle;
                var rawJoiner = cls == LineBreakClass.Zwj;

                // LB9: a combining mark or joiner after anything but a hard break, a space or a zero width space belongs to the
                // unit before it and is otherwise ignored; there is no break before it.
                if ((cls == LineBreakClass.Cm || cls == LineBreakClass.Zwj) && _hasPrev && !IsLineStart(_prev))
                {
                    _previousWasJoiner = rawJoiner;
                    i += length;
                    continue;
                }

                // LB10: any remaining combining mark or joiner is a letter A.
                if (cls == LineBreakClass.Cm || cls == LineBreakClass.Zwj)
                {
                    cls = LineBreakClass.Al;
                    eastAsian = false;
                    pictographicUnassigned = false;
                    dotted = false;
                }

                if (_hasPrev)
                {
                    var decision = Decide(cls, eastAsian, dotted, i + length);
                    if (decision != Break.No)
                    {
                        _result.Add(new BreakOpportunity(i, decision == Break.Mandatory));
                    }
                }

                // The state after this unit.
                var quoteBefore = IsQuote(cls) && (!_hasPrev || !_prevEastAsian);
                if (cls == LineBreakClass.QuPi && (!_hasPrev || IsInitialQuoteContext(_prev)))
                {
                    _initialQuote = true;
                }
                else if (!(cls == LineBreakClass.Sp && _initialQuote))
                {
                    _initialQuote = false;
                }

                _number = cls switch
                {
                    LineBreakClass.Nu => 1,
                    LineBreakClass.Sy or LineBreakClass.Is when _number == 1 => 1,
                    LineBreakClass.Cl or LineBreakClass.Cp when _number == 1 => 2,
                    _ => 0,
                };
                _regionalIndicators = cls == LineBreakClass.Ri ? (_hasPrev && _prev == LineBreakClass.Ri ? _regionalIndicators + 1 : 1) : 0;

                _hasPrev2 = _hasPrev;
                _prev2 = _prev;
                _prev2Dotted = _prevDotted;
                _hasPrev = true;
                _prev = cls;
                _prevEastAsian = eastAsian;
                _prevDotted = dotted;
                _prevPictographicUnassigned = pictographicUnassigned;
                _prevQuoteAfterNonEastAsian = quoteBefore;
                _previousWasJoiner = rawJoiner;
                if (cls != LineBreakClass.Sp)
                {
                    _hasLastNonSpace = true;
                    _lastNonSpace = cls;
                }

                i += length;
            }

            var last = _hasPrev && _prev is LineBreakClass.Bk or LineBreakClass.Lf or LineBreakClass.Nl or LineBreakClass.Cr;
            _result.Add(new BreakOpportunity(_text.Length, last));
            return _result;
        }

        // The decision of the break before the current unit. Rules run in the order of the annex; the first that applies wins.
        private Break Decide(LineBreakClass cls, bool eastAsian, bool dotted, int after)
        {
            var cur = cls;
            var p = _prev;

            // LB4, LB5: hard breaks (CR LF is one).
            if (p is LineBreakClass.Bk or LineBreakClass.Lf or LineBreakClass.Nl)
            {
                return Break.Mandatory;
            }

            if (p == LineBreakClass.Cr)
            {
                return cur == LineBreakClass.Lf ? Break.No : Break.Mandatory;
            }

            // LB6, LB7.
            if (cur is LineBreakClass.Bk or LineBreakClass.Cr or LineBreakClass.Lf or LineBreakClass.Nl or LineBreakClass.Sp or LineBreakClass.Zw)
            {
                return Break.No;
            }

            // LB8 (after a zero width space and spaces), LB8a.
            if (_hasLastNonSpace && _lastNonSpace == LineBreakClass.Zw)
            {
                return Break.Yes;
            }

            if (_previousWasJoiner)
            {
                return Break.No;
            }

            // LB11, LB12, LB12a.
            if (cur == LineBreakClass.Wj || p == LineBreakClass.Wj || p == LineBreakClass.Gl)
            {
                return Break.No;
            }

            if (cur == LineBreakClass.Gl && p is not (LineBreakClass.Sp or LineBreakClass.Hy or LineBreakClass.Hh))
            {
                return Break.No;
            }

            // LB13, LB14, LB15a.
            if (cur is LineBreakClass.Cl or LineBreakClass.Cp or LineBreakClass.Ex or LineBreakClass.Sy)
            {
                return Break.No;
            }

            if (_hasLastNonSpace && _lastNonSpace == LineBreakClass.Op)
            {
                return Break.No;
            }

            if (_initialQuote)
            {
                return Break.No;
            }

            // LB15b: before a final quotation mark that is followed by a space, closing punctuation and the like or the end.
            if (cur == LineBreakClass.QuPf)
            {
                var next = NextUnit(after, out _);
                if (next is null || EndsFinalQuote(next.Value))
                {
                    return Break.No;
                }
            }

            // LB15c, LB15d.
            if (cur == LineBreakClass.Is)
            {
                if (p == LineBreakClass.Sp && NextUnit(after, out _) == LineBreakClass.Nu)
                {
                    return Break.Yes;
                }

                return Break.No;
            }

            // LB16, LB17.
            if (_hasLastNonSpace && _lastNonSpace is LineBreakClass.Cl or LineBreakClass.Cp && cur == LineBreakClass.Ns)
            {
                return Break.No;
            }

            if (_hasLastNonSpace && _lastNonSpace == LineBreakClass.B2 && cur == LineBreakClass.B2)
            {
                return Break.No;
            }

            // LB18.
            if (p == LineBreakClass.Sp)
            {
                return Break.Yes;
            }

            // LB19, LB19a: quotation marks.
            var curQuote = IsQuote(cur);
            var prevQuote = IsQuote(p);
            if (cur is LineBreakClass.Qu or LineBreakClass.QuPf || p is LineBreakClass.Qu or LineBreakClass.QuPi)
            {
                return Break.No;
            }

            if (curQuote && !_prevEastAsian)
            {
                return Break.No;
            }

            if (curQuote)
            {
                var next = NextUnitDetail(after, out _, out var nextEastAsian);
                if (next is null || !nextEastAsian)
                {
                    return Break.No;
                }
            }

            if (prevQuote && (!eastAsian || _prevQuoteAfterNonEastAsian))
            {
                return Break.No;
            }

            // LB20, LB20a.
            if (cur == LineBreakClass.Cb || p == LineBreakClass.Cb)
            {
                return Break.Yes;
            }

            if (p is LineBreakClass.Hy or LineBreakClass.Hh && cur is LineBreakClass.Al or LineBreakClass.Hl
                && (!_hasPrev2 || _prev2 is LineBreakClass.Bk or LineBreakClass.Cr or LineBreakClass.Lf or LineBreakClass.Nl
                    or LineBreakClass.Sp or LineBreakClass.Zw or LineBreakClass.Cb or LineBreakClass.Gl))
            {
                return Break.No;
            }

            // LB21, LB21a, LB21b, LB22.
            if (cur is LineBreakClass.Ba or LineBreakClass.Hh or LineBreakClass.Hy or LineBreakClass.Ns || p == LineBreakClass.Bb)
            {
                return Break.No;
            }

            if (p is LineBreakClass.Hy or LineBreakClass.Hh && _hasPrev2 && _prev2 == LineBreakClass.Hl && cur != LineBreakClass.Hl)
            {
                return Break.No;
            }

            if (p == LineBreakClass.Sy && cur == LineBreakClass.Hl)
            {
                return Break.No;
            }

            if (cur == LineBreakClass.In)
            {
                return Break.No;
            }

            // LB23, LB23a, LB24.
            var curLetter = cur is LineBreakClass.Al or LineBreakClass.Hl;
            var prevLetter = p is LineBreakClass.Al or LineBreakClass.Hl;
            if ((prevLetter && cur == LineBreakClass.Nu) || (p == LineBreakClass.Nu && curLetter))
            {
                return Break.No;
            }

            if ((p == LineBreakClass.Pr && cur is LineBreakClass.Id or LineBreakClass.Eb or LineBreakClass.Em)
                || (p is LineBreakClass.Id or LineBreakClass.Eb or LineBreakClass.Em && cur == LineBreakClass.Po))
            {
                return Break.No;
            }

            if ((p is LineBreakClass.Pr or LineBreakClass.Po && curLetter) || (prevLetter && cur is LineBreakClass.Pr or LineBreakClass.Po))
            {
                return Break.No;
            }

            // LB25: numbers.
            if (_number != 0)
            {
                if (cur is LineBreakClass.Po or LineBreakClass.Pr)
                {
                    return Break.No;
                }

                if (_number == 1 && cur == LineBreakClass.Nu)
                {
                    return Break.No;
                }
            }

            if (p is LineBreakClass.Po or LineBreakClass.Pr)
            {
                if (cur == LineBreakClass.Nu)
                {
                    return Break.No;
                }

                if (cur == LineBreakClass.Op)
                {
                    var next = NextUnit(after, out var nextAt);
                    if (next == LineBreakClass.Nu)
                    {
                        return Break.No;
                    }

                    if (next == LineBreakClass.Is && NextUnit(nextAt, out _) == LineBreakClass.Nu)
                    {
                        return Break.No;
                    }
                }
            }

            if (cur == LineBreakClass.Nu && p is LineBreakClass.Hy or LineBreakClass.Is)
            {
                return Break.No;
            }

            // LB26, LB27: Korean syllable blocks.
            if ((p == LineBreakClass.Jl && cur is LineBreakClass.Jl or LineBreakClass.Jv or LineBreakClass.H2 or LineBreakClass.H3)
                || (p is LineBreakClass.Jv or LineBreakClass.H2 && cur is LineBreakClass.Jv or LineBreakClass.Jt)
                || (p is LineBreakClass.Jt or LineBreakClass.H3 && cur == LineBreakClass.Jt))
            {
                return Break.No;
            }

            var prevKorean = p is LineBreakClass.Jl or LineBreakClass.Jv or LineBreakClass.Jt or LineBreakClass.H2 or LineBreakClass.H3;
            var curKorean = cur is LineBreakClass.Jl or LineBreakClass.Jv or LineBreakClass.Jt or LineBreakClass.H2 or LineBreakClass.H3;
            if ((prevKorean && cur == LineBreakClass.Po) || (p == LineBreakClass.Pr && curKorean))
            {
                return Break.No;
            }

            // LB28.
            if (prevLetter && curLetter)
            {
                return Break.No;
            }

            // LB28a: orthographic syllables of Brahmic scripts.
            var prevSyllable = p is LineBreakClass.Ak or LineBreakClass.As || _prevDotted;
            var curBase = cur == LineBreakClass.Ak || dotted;
            var curSyllable = curBase || cur == LineBreakClass.As;
            if (p == LineBreakClass.Ap && curSyllable)
            {
                return Break.No;
            }

            if (prevSyllable && cur is LineBreakClass.Vf or LineBreakClass.Vi)
            {
                return Break.No;
            }

            if (p == LineBreakClass.Vi && _hasPrev2 && (_prev2 is LineBreakClass.Ak or LineBreakClass.As || _prev2Dotted) && curBase)
            {
                return Break.No;
            }

            if (prevSyllable && curSyllable && NextUnit(after, out _) == LineBreakClass.Vf)
            {
                return Break.No;
            }

            // LB29, LB30.
            if (p == LineBreakClass.Is && curLetter)
            {
                return Break.No;
            }

            if ((prevLetter || p == LineBreakClass.Nu) && cur == LineBreakClass.Op && !eastAsian)
            {
                return Break.No;
            }

            if (p == LineBreakClass.Cp && !_prevEastAsian && (curLetter || cur == LineBreakClass.Nu))
            {
                return Break.No;
            }

            // LB30a: regional indicators in pairs.
            if (p == LineBreakClass.Ri && cur == LineBreakClass.Ri && _regionalIndicators % 2 == 1)
            {
                return Break.No;
            }

            // LB30b: emoji base and modifier.
            if (cur == LineBreakClass.Em && (p == LineBreakClass.Eb || _prevPictographicUnassigned))
            {
                return Break.No;
            }

            // LB31.
            return Break.Yes;
        }

        // The class of the first unit that starts at or after offset 'from' (combining marks and joiners after the unit that
        // ends there belong to that unit and are skipped), or null at the end of the text.
        private LineBreakClass? NextUnit(int from, out int at) => NextUnitDetail(from, out at, out _);

        private LineBreakClass? NextUnitDetail(int from, out int at, out bool nextEastAsian)
        {
            var j = from;
            while (j < _text.Length)
            {
                var cp = Decode(_text, j, out var len);
                var packed = LineBreakData.Get(cp);
                var c = (LineBreakClass)(packed & LineBreakData.ClassMask);
                if (c is LineBreakClass.Cm or LineBreakClass.Zwj)
                {
                    j += len;
                    continue;
                }

                at = j + len;
                nextEastAsian = (packed & LineBreakData.EastAsian) != 0;
                return c;
            }

            at = _text.Length;
            nextEastAsian = false;
            return null;
        }
    }
}
