using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

public sealed class CommandIconTests
{
    // The same action in two places may share a glyph: the Pages tab repeats the history of the Home tab.
    private static readonly string[][] SameActionGroups =
    [
        [PdfCommandIds.Undo, PdfCommandIds.PagesUndo],
        [PdfCommandIds.Redo, PdfCommandIds.PagesRedo],
    ];

    [Fact]
    public void NoGlyphServesTwoMeanings()
    {
        foreach (var group in PdfCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            var ids = group.Select(c => c.Id).Order().ToArray();
            Assert.True(
                SameActionGroups.Any(a => a.Order().SequenceEqual(ids)),
                $"Icon {group.Key} serves {string.Join(", ", ids)}");
        }
    }

    [AvaloniaFact]
    public void EveryCommandIconResolvesToAGeometry()
    {
        foreach (var command in PdfCommandCatalog.Commands)
        {
            Assert.True(
                Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value),
                command.Id + " " + command.Icon);
            Assert.IsAssignableFrom<Geometry>(value);
        }
    }
}
