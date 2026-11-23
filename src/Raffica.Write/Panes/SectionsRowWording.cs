using System.Globalization;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Panes;

/// <summary>The words of the Sections pane in the UI language: one line for each fact of a row. They name the section and its settings, never the text of the document.</summary>
internal static class SectionsRowWording
{
    private static readonly string[] s_papers = ["A4", "Letter", "A3", "A5", "Legal"];

    /// <summary>"Section 2" (counted from 1).</summary>
    public static string Title(SectionRow row) => WriteStrings.Fill(WriteStrings.SectionTitleFormat, row.Index + 1);

    /// <summary>"Starts on page 4" (the page index counted from 1), or the honest line while the layout has not reached that page.</summary>
    public static string Start(SectionRow row) =>
        row.IsEmpty ? WriteStrings.SectionEmptyFinal : row.FirstPage < 0 ? WriteStrings.SectionPageNotLaidOut : WriteStrings.Fill(WriteStrings.SectionStartsOnPageFormat, (row.FirstPage + 1).ToString(WriteStrings.Culture));

    /// <summary>"Page numbers start at 5, i ii iii" or "Page numbers continue" (with the format when the section sets one other than 1 2 3).</summary>
    public static string Numbers(SectionRow row)
    {
        if (row.NumberStart is { } start)
        {
            return WriteStrings.Fill(WriteStrings.SectionNumbersStartFormat, start.ToString(WriteStrings.Culture), FormatWords(row.NumberFormat ?? PageNumberFormat.Arabic));
        }

        return row.NumberFormat is { } format && format != PageNumberFormat.Arabic
            ? WriteStrings.Fill(WriteStrings.SectionNumbersContinueFormat, FormatWords(format))
            : WriteStrings.SectionNumbersContinue;
    }

    /// <summary>"A4, Portrait, 2 columns": the paper by name when it is one of the five the page setup offers (within one twip), else "Custom".</summary>
    public static string Setup(SectionRow row)
    {
        var setup = row.Setup;
        var upright = (Width: Math.Min(setup.WidthTwips, setup.HeightTwips), Height: Math.Max(setup.WidthTwips, setup.HeightTwips));
        var paper = s_papers.FirstOrDefault(name =>
        {
            var (w, h) = PagePresets.Size(name);
            return Math.Abs(w - upright.Width) <= 1 && Math.Abs(h - upright.Height) <= 1;
        }) ?? WriteStrings.PsCustom;
        var columns = setup.Columns == 1 ? WriteStrings.SectionColumnsOne : WriteStrings.Fill(WriteStrings.SectionColumnsFormat, setup.Columns.ToString(CultureInfo.CurrentCulture));
        return WriteStrings.Fill(WriteStrings.SectionSetupFormat, paper, setup.Landscape ? WriteStrings.OrientationLandscape : WriteStrings.OrientationPortrait, columns);
    }

    /// <summary>The label of the header switch: own, or the same as the section before.</summary>
    public static string Header(SectionRow row) => row.HeaderOwn ? WriteStrings.SectionHeaderOwn : WriteStrings.SectionHeaderSame;

    /// <summary>The label of the footer switch.</summary>
    public static string Footer(SectionRow row) => row.FooterOwn ? WriteStrings.SectionFooterOwn : WriteStrings.SectionFooterSame;

    /// <summary>
    /// What Remove marker does: when what the section shows would change, the sentence names each group that changes ("Section 3 takes over the page setup and the header of section 2."), else a plain description of the merge.
    /// </summary>
    public static string RemoveTip(SectionRow row, SectionRow previous)
    {
        var changes = row.ChangesWhenMerged(previous);
        if (changes == MergeChanges.None)
        {
            return WriteStrings.Fill(WriteStrings.SectionRemoveTipFormat, row.Index + 1, previous.Index + 1);
        }

        var groups = new List<string>();
        AddIf(groups, changes, MergeChanges.PageSetup, WriteStrings.SectionChangePageSetup);
        AddIf(groups, changes, MergeChanges.Numbering, WriteStrings.SectionChangeNumbering);
        AddIf(groups, changes, MergeChanges.FirstPage, WriteStrings.SectionChangeFirstPage);
        AddIf(groups, changes, MergeChanges.Headers, WriteStrings.SectionChangeHeader);
        AddIf(groups, changes, MergeChanges.Footers, WriteStrings.SectionChangeFooter);
        return WriteStrings.Fill(WriteStrings.SectionTakesOverFormat, row.Index + 1, previous.Index + 1, Join(groups));
    }

    private static void AddIf(List<string> groups, MergeChanges changes, MergeChanges flag, string words)
    {
        if ((changes & flag) != 0)
        {
            groups.Add(words);
        }
    }

    // "a", "a and b", "a, b and c" in the UI language.
    private static string Join(List<string> groups) => groups.Count switch
    {
        1 => groups[0],
        _ => WriteStrings.Fill(WriteStrings.SectionListTwoFormat, string.Join(", ", groups.Take(groups.Count - 1)), groups[^1]),
    };

    /// <summary>The name of a control for assistive technology: the control's words and the section it acts on.</summary>
    public static string ButtonName(string words, SectionRow row) => WriteStrings.Fill(WriteStrings.SectionButtonNameFormat, words, row.Index + 1);

    private static string FormatWords(PageNumberFormat format) => format switch
    {
        PageNumberFormat.LowerRoman => WriteStrings.NumPagesFormatLowerRoman,
        PageNumberFormat.UpperRoman => WriteStrings.NumPagesFormatUpperRoman,
        PageNumberFormat.LowerLetter => WriteStrings.NumPagesFormatLowerLetter,
        PageNumberFormat.UpperLetter => WriteStrings.NumPagesFormatUpperLetter,
        _ => WriteStrings.NumPagesFormatArabic,
    };
}
