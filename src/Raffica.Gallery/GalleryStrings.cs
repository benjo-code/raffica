using System.Globalization;
using Raffica.Shell.Localization;

namespace Raffica.Gallery;

/// <summary>The labels and sample texts of the gallery itself (English source, German translation); the shell controls bring their own.</summary>
public static class GalleryStrings
{
    private static readonly StringTable Table = new("Raffica.Gallery.Strings", typeof(GalleryStrings).Assembly);

    /// <summary>The UI language; set once at startup, a change applies after a restart.</summary>
    public static CultureInfo Culture
    {
        get => Table.Culture;
        set => Table.Culture = value;
    }

    public static string WindowTitle => Table.Get("WindowTitle");
    public static string SectionButtons => Table.Get("SectionButtons");
    public static string StateRest => Table.Get("StateRest");
    public static string StateHover => Table.Get("StateHover");
    public static string StatePressedOn => Table.Get("StatePressedOn");
    public static string StateFocus => Table.Get("StateFocus");
    public static string StateDisabled => Table.Get("StateDisabled");
    public static string RowButton => Table.Get("RowButton");
    public static string RowBoxed => Table.Get("RowBoxed");
    public static string RowPrimary => Table.Get("RowPrimary");
    public static string RowDestructive => Table.Get("RowDestructive");
    public static string RowIcon => Table.Get("RowIcon");
    public static string RowToggle => Table.Get("RowToggle");
    public static string RowCheckBox => Table.Get("RowCheckBox");
    public static string RowRadio => Table.Get("RowRadio");
    public static string RowSegmented => Table.Get("RowSegmented");
    public static string SampleInsert => Table.Get("SampleInsert");
    public static string SampleBrowse => Table.Get("SampleBrowse");
    public static string SampleApply => Table.Get("SampleApply");
    public static string SampleTrackChanges => Table.Get("SampleTrackChanges");
    public static string SamplePortrait => Table.Get("SamplePortrait");
    public static string SampleMarks => Table.Get("SampleMarks");
    public static string SamplePages => Table.Get("SamplePages");
    public static string SampleResults => Table.Get("SampleResults");
    public static string IconBold => Table.Get("IconBold");
}
