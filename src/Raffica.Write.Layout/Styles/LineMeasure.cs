using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// Measures how a text lies at the start of a paragraph's first line, through the layout's own definitions (<see cref="StyleResolver"/> for the indents, tab stops and run formatting, <see cref="ParagraphShaper"/> for the width): what a
/// field refresh needs to place the tab stop after the number of an entry of a table of contents. The styles are read once for a document revision and number of styles. Not thread-safe: the owner calls it on one thread.
/// </summary>
public sealed class LineMeasure(WordDocument document, FontCatalog fonts)
{
    private readonly WordDocument _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly FontCatalog _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    private StyleResolver? _resolver;
    private int _revision;
    private int _styles;

    /// <summary>
    /// The geometry of <paramref name="text"/> on the first line of <paramref name="paragraph"/> (its style and direct formatting; it need not be in the document, and its own text is not read). The text has the formatting the
    /// paragraph style gives a run without direct formatting. Capitals are measured full size, so a width is never short.
    /// </summary>
    public LineGeometry Measure(Paragraph paragraph, string text)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(text);
        var resolver = Current();
        var owner = resolver.ForParagraph(paragraph);
        var run = resolver.ForRun(owner, new Run());
        var start = owner.LeftIndent + owner.FirstLineIndent - owner.HangingIndent;
        var width = 0f;
        if (text.Length > 0)
        {
            var size = float.IsFinite(run.SizePoints) && run.SizePoints > 0f ? Math.Min(run.SizePoints, 2000f) : 1f;
            var shown = run.Caps || run.SmallCaps ? text.ToUpperInvariant() : text;
            var pieces = new List<Piece>(1);
            using var shaper = new ParagraphShaper(_fonts);
            shaper.AddText(pieces, run, size, 0f, shown, 0, false);
            foreach (var piece in pieces)
            {
                width += piece.Width;
            }
        }

        var stops = new List<LineStop>(owner.TabStops.Count + 1);
        foreach (var stop in owner.TabStops)
        {
            if (stop.Alignment != TabAlignment.Bar)
            {
                stops.Add(new LineStop(Twips(stop.Position), stop.Alignment == TabAlignment.Right));
            }
        }

        if (owner.HangingIndent > 0f)
        {
            stops.Add(new LineStop(Twips(owner.LeftIndent), false));
        }

        stops.Sort((a, b) => a.Position.CompareTo(b.Position));
        return new LineGeometry(Twips(owner.LeftIndent), (int)MathF.Ceiling(((start + width) * 20f) - 0.01f), stops);
    }

    private static int Twips(float points) => (int)MathF.Round(points * 20f);

    // A style made in place (the TOC styles an insert adds) raises no revision before it is asked for, so the number of styles is part of the key.
    private StyleResolver Current()
    {
        var revision = _document.Edits.Stamp.Revision;
        var styles = _document.Styles?.Styles.Count ?? 0;
        if (_resolver is null || revision != _revision || styles != _styles)
        {
            _revision = revision;
            _styles = styles;
            _resolver = new StyleResolver(_document.Styles, _document.Settings, _document.Numbering);
        }

        return _resolver;
    }
}
