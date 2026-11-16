namespace Raffica.Write.Layout;

/// <summary>Finds an installed font by family name. It is asked only for a family that is neither shipped nor a metric alias.</summary>
public interface ISystemFonts
{
    /// <summary>The installed face of <paramref name="family"/> in the given style, or null when that family is not installed.</summary>
    FontFace? Match(string family, bool bold, bool italic);

    /// <summary>The names of the installed font families; names only, no face is read. A source that cannot list its fonts answers none.</summary>
    IEnumerable<string> Families() => [];
}
