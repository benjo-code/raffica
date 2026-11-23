using System.Globalization;
using System.Xml.Linq;
using Raffica.Shell.Frame;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>
/// The pickers of the Layout tab (paper size, margins, orientation, columns, section break and page number): what each shows (the value of the caret's section, or the name of the command), the lazy
/// list it opens with and what a choice runs. Nothing is read when the ribbon is built: the lists are asked when a list opens, and the value comes from the section facts the window already holds. A choice
/// calls <paramref name="run"/> with the command id and the value (<c>WriteWindow.RunCommand(id, payload)</c>, which validates it).
/// </summary>
/// <param name="facts">The facts of the caret's section for a command (the setup shown is read from them).</param>
/// <param name="document">The model of the active document (null without one); read when the list of the page numbers opens.</param>
/// <param name="run">Runs a value command with its payload; false when it was refused.</param>
/// <param name="inches">Whether the margin presets are the inch ones (the region's rule of <see cref="PagePresets.UsesInches"/>); null reads the current region.</param>
internal sealed class SectionPickers(Func<string, SectionCommandFacts> facts, Func<WordDocument?> document, Func<string, object?, bool> run, Func<bool>? inches = null)
{
    private const string Unset = "-";
    private const int TwipsTolerance = 2;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>How many times a list was asked (a list is read when it opens, never before; tests pin that).</summary>
    internal int ListReads { get; private set; }

    /// <summary>The picker of a command, or null when the command is no picker.</summary>
    public PickerDefinition? For(string commandId) => Counted(Create(commandId));

    private bool InchesNow => inches?.Invoke() ?? PagePresets.UsesInches(RegionInfo.CurrentRegion);

    private PickerDefinition? Counted(PickerDefinition? picker)
    {
        if (picker is null)
        {
            return null;
        }

        return picker with
        {
            Items = () =>
            {
                ListReads++;
                return picker.Items();
            },
        };
    }

    private PickerDefinition? Create(string commandId) => commandId switch
    {
        WriteCommandIds.PageSize => new PickerDefinition(
            () => [.. SectionPayloads.Papers.Select(p => new PickerItem(p, p))],
            id => run(WriteCommandIds.PageSize, id),
            () => SizeId(facts(WriteCommandIds.PageSize).Setup) ?? Unset,
            SelectedLabel: () => SizeId(facts(WriteCommandIds.PageSize).Setup) ?? NameOf(WriteCommandIds.PageSize),
            Width: 96),
        WriteCommandIds.PageMargins => new PickerDefinition(
            () => [.. SectionPayloads.MarginPresets.Select(m => new PickerItem(m, MarginLabel(m)))],
            id => run(WriteCommandIds.PageMargins, id),
            () => MarginId(facts(WriteCommandIds.PageMargins).Setup) ?? Unset,
            SelectedLabel: () => MarginId(facts(WriteCommandIds.PageMargins).Setup) is { } id ? MarginLabel(id) : NameOf(WriteCommandIds.PageMargins),
            Width: 110),
        WriteCommandIds.PageOrientation => new PickerDefinition(
            () => [new PickerItem("portrait", WriteStrings.Get("OrientationPortrait")), new PickerItem("landscape", WriteStrings.Get("OrientationLandscape"))],
            id => run(WriteCommandIds.PageOrientation, id),
            () => facts(WriteCommandIds.PageOrientation).Setup is { } s ? (s.Landscape ? "landscape" : "portrait") : Unset,
            SelectedLabel: () => facts(WriteCommandIds.PageOrientation).Setup is { } s ? WriteStrings.Get(s.Landscape ? "OrientationLandscape" : "OrientationPortrait") : NameOf(WriteCommandIds.PageOrientation),
            Width: 110),
        WriteCommandIds.PageColumns => new PickerDefinition(
            () => [new PickerItem("1", WriteStrings.Get("ColumnsOne")), new PickerItem("2", WriteStrings.Get("ColumnsTwo")), new PickerItem("3", WriteStrings.Get("ColumnsThree"))],
            id => run(WriteCommandIds.PageColumns, id),
            () => facts(WriteCommandIds.PageColumns).Setup is { Columns: >= 1 and <= 3 } s ? s.Columns.ToString(CultureInfo.InvariantCulture) : Unset,
            SelectedLabel: () => facts(WriteCommandIds.PageColumns).Setup is { Columns: >= 1 and <= 3 } s ? WriteStrings.Get(s.Columns switch { 1 => "ColumnsOne", 2 => "ColumnsTwo", _ => "ColumnsThree" }) : NameOf(WriteCommandIds.PageColumns),
            Width: 100),
        WriteCommandIds.SectionBreak => Command(
            WriteCommandIds.SectionBreak,
            () =>
            [
                new PickerItem("nextPage", WriteStrings.Get("BreakNextPage")),
                new PickerItem("continuous", WriteStrings.Get("BreakContinuous")),
                new PickerItem("evenPage", WriteStrings.Get("BreakEvenPage")),
                new PickerItem("oddPage", WriteStrings.Get("BreakOddPage")),
            ],
            130),
        WriteCommandIds.InsertPageNumber => Command(WriteCommandIds.InsertPageNumber, PageNumberItems, 120),
        _ => null,
    };

    // A picker that has no selection of its own (a break, a page number): it shows the name of the command and runs the choice.
    private PickerDefinition Command(string id, Func<IReadOnlyList<PickerItem>> items, double width) => new(
        items,
        choice => run(id, choice),
        () => Unset,
        SelectedLabel: () => NameOf(id),
        Width: width);

    private static string NameOf(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id).Name;

    private static string MarginLabel(string id) => WriteStrings.Get("Margin" + id);

    // The paper whose width and height (either way round, so a landscape section matches too) are the section's.
    private static string? SizeId(SectionPageSetup? setup)
    {
        if (setup is null)
        {
            return null;
        }

        foreach (var name in SectionPayloads.Papers)
        {
            var (w, h) = PagePresets.Size(name);
            if ((Near(setup.WidthTwips, w) && Near(setup.HeightTwips, h)) || (Near(setup.WidthTwips, h) && Near(setup.HeightTwips, w)))
            {
                return name;
            }
        }

        return null;
    }

    private string? MarginId(SectionPageSetup? setup)
    {
        if (setup is null)
        {
            return null;
        }

        var inch = InchesNow;
        foreach (var name in SectionPayloads.MarginPresets)
        {
            var m = PagePresets.Margins(name, inch);
            if (Near(setup.TopTwips, m.Top) && Near(setup.BottomTwips, m.Bottom) && Near(setup.LeftTwips, m.Left) && Near(setup.RightTwips, m.Right))
            {
                return name;
            }
        }

        return null;
    }

    private static bool Near(int a, int b) => Math.Abs(a - b) <= TwipsTolerance;

    // Place, alignment and look: "Footer, Center: Page 1 of N". The looks that count pages say, when the numbers of the document are not 1 to N, that N counts every page.
    private List<PickerItem> PageNumberItems()
    {
        var hint = document() is { } model && NumbersAreNotOneToN(model);
        var items = new List<PickerItem>();
        foreach (var place in new[] { "footer", "header" })
        {
            foreach (var align in new[] { "left", "center", "right" })
            {
                foreach (var look in new[] { "plain", "page", "pageOfTotal", "slash" })
                {
                    var label = WriteStrings.Fill(
                        WriteStrings.Get("PageNumberItemFormat"),
                        WriteStrings.Get(place == "footer" ? "PageNumberPlaceFooter" : "PageNumberPlaceHeader"),
                        WriteStrings.Get("PageNumberAlign" + char.ToUpperInvariant(align[0]) + align[1..]),
                        LookLabel(look));
                    if (hint && look is "pageOfTotal" or "slash")
                    {
                        label += " (" + WriteStrings.PageNumberCountsEveryPage + ")";
                    }

                    items.Add(new PickerItem(place + ":" + align + ":" + look, label));
                }
            }
        }

        return items;
    }

    private static string LookLabel(string look) => look switch
    {
        "page" => WriteStrings.PageNumberWordPage + " 1",
        "pageOfTotal" => WriteStrings.PageNumberWordPage + " 1 " + WriteStrings.PageNumberWordOf + " N",
        "slash" => "1 / N",
        _ => "1",
    };

    // The numbers are not 1 to N when a section restarts or changes the format of its page numbers.
    private static bool NumbersAreNotOneToN(WordDocument model)
    {
        foreach (var info in SectionList.Of(model))
        {
            if (info.Properties.Get(W + "pgNumType") is { } numbering && (numbering.Attribute(W + "start") is not null || numbering.Attribute(W + "fmt") is { Value: not "decimal" }))
            {
                return true;
            }
        }

        return false;
    }
}
