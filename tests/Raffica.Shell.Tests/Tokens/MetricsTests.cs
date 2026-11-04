using System.Xml.Linq;
using Xunit;

namespace Raffica.Shell.Tests.Tokens;

/// <summary>The sizes, spacing and radii of appendix A section 4 of the spec, read from Metrics.axaml.</summary>
public sealed class MetricsTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static readonly TheoryData<string, string, string> Rows = new()
    {
        { "Raffica.Space.2", "Double", "2" },
        { "Raffica.Space.4", "Double", "4" },
        { "Raffica.Space.6", "Double", "6" },
        { "Raffica.Space.8", "Double", "8" },
        { "Raffica.Space.10", "Double", "10" },
        { "Raffica.Space.12", "Double", "12" },
        { "Raffica.Space.16", "Double", "16" },
        { "Raffica.Space.24", "Double", "24" },
        { "Raffica.Radius.Surface", "CornerRadius", "0" },
        { "Raffica.Radius.Checkbox", "CornerRadius", "2" },
        { "Raffica.Radius.Control", "CornerRadius", "3" },
        { "Raffica.Radius.Overlay", "CornerRadius", "3" },
        { "Raffica.Size.Button", "Double", "26" },
        { "Raffica.Size.Dropdown", "Double", "26" },
        { "Raffica.Size.HitTargetMin", "Double", "24" },
        { "Raffica.Size.RowList", "Double", "32" },
        { "Raffica.Size.RowFile", "Double", "40" },
        { "Raffica.Size.DialogWidth", "Double", "520" },
        { "Raffica.Size.DialogHeader", "Double", "44" },
        { "Raffica.Size.DialogFooter", "Double", "52" },
        { "Raffica.Size.NotificationBar", "Double", "40" },
        { "Raffica.Size.Icon.Small", "Double", "16" },
        { "Raffica.Size.Icon.Large", "Double", "20" },
        { "Raffica.Size.TitleBar", "Double", "36" },
        { "Raffica.Size.TabRow", "Double", "32" },
        { "Raffica.Size.Ribbon", "Double", "82" },
        { "Raffica.Size.RibbonCompact", "Double", "40" },
        { "Raffica.Size.StatusBar", "Double", "30" },
        { "Raffica.Size.PanelHeader", "Double", "38" },
        { "Raffica.Size.SearchField", "Double", "28" },
        { "Raffica.Size.CaptionButton", "Double", "46" },
        { "Raffica.Size.DocumentTab", "Double", "28" },
        { "Raffica.Size.DocumentTabMax", "Double", "240" },
        { "Raffica.Size.TileLarge", "Double", "54" },
        { "Raffica.Size.TileLargeMin", "Double", "48" },
        { "Raffica.Size.FieldCode", "Double", "18" },
        { "Raffica.Size.StatusCell", "Double", "20" },
        { "Raffica.Size.ZoomButton", "Double", "24" },
        { "Raffica.Size.ZoomSlider", "Double", "96" },
        { "Raffica.Size.StatusCellGap", "Double", "16" },
        { "Raffica.Rule.Width", "Double", "1" },
        { "Raffica.Focus.RingWidth", "Double", "2" },
        { "Raffica.Focus.RingOffset", "Double", "2" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void MetricHasItsPinnedValue(string key, string type, string value)
    {
        var element = Load().Elements().Single(e => (string?)e.Attribute(X + "Key") == key);

        Assert.Equal(type, element.Name.LocalName);
        Assert.Equal(value, element.Value.Trim());
    }

    [Fact]
    public void NoMetricOutsideTheTableExists()
    {
        var known = Rows.Select(r => r.Data.Item1).ToHashSet();
        var keys = Load().Elements().Select(e => (string)e.Attribute(X + "Key")!);

        Assert.Empty(keys.Except(known));
    }

    private static XElement Load() => XDocument.Load(TokenFile.StylesPath("Metrics.axaml")).Root!;
}
