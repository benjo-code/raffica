using Raffica.Core.Printing;

namespace Raffica.Pdf.Platform.Linux;

/// <summary>Maps the choices of the print dialog to CUPS job options (IPP attribute names and values).</summary>
internal static class CupsOptions
{
    /// <summary>
    /// Builds the options. Every value comes from a number, an enumeration or the normalized page ranges of the
    /// <see cref="PrintPlan"/>, never from text the user typed.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Build(PrintSettings settings, PrintPlan plan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);
        settings = settings.Normalized();

        var options = new List<(string, string)>
        {
            ("copies", settings.Copies.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("multiple-document-handling", settings.Collate ? "separate-documents-collated-copies" : "separate-documents-uncollated-copies"),
            ("print-scaling", settings.Scaling == PrintScaling.ActualSize ? "none" : "fit"),
        };

        if (!plan.IsWholeDocument)
        {
            options.Add(("page-ranges", plan.CupsPageRanges));
        }

        switch (settings.Duplex)
        {
            case PrintDuplex.Off:
                options.Add(("sides", "one-sided"));
                break;
            case PrintDuplex.LongEdge:
                options.Add(("sides", "two-sided-long-edge"));
                break;
            case PrintDuplex.ShortEdge:
                options.Add(("sides", "two-sided-short-edge"));
                break;
            default:
                break;
        }

        switch (settings.Orientation)
        {
            case PrintOrientation.Portrait:
                options.Add(("orientation-requested", "3"));
                break;
            case PrintOrientation.Landscape:
                options.Add(("orientation-requested", "4"));
                break;
            default:
                break;
        }

        return options;
    }

    /// <summary>
    /// Whether a name can be a CUPS destination: 1 to 127 printable characters without white space, <c>/</c> or <c>#</c>
    /// (the rule of CUPS itself). The name goes to libcups as a separate argument, never into a command line; the check
    /// keeps odd text out of the queue lookup all the same.
    /// </summary>
    internal static bool IsValidPrinterName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 127)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '/' or '#')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The job title for the queue: control characters removed, at most 255 characters, never empty.</summary>
    internal static string SanitizeTitle(string? title)
    {
        var clean = new string((title ?? string.Empty).Where(c => !char.IsControl(c)).Take(255).ToArray()).Trim();
        return clean.Length > 0 ? clean : "Raffica";
    }
}
