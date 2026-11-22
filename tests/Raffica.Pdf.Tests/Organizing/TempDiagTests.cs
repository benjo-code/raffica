using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

// THROWAWAY diagnostic (never merged): repeats the click of FocusStaysOnItsRowAfterSelectAllAndMoves and says what was different when the wrong row got the focus.
public sealed class TempDiagTests
{
    private static string Box(OrganizeListFixture f, int index)
    {
        if (f.List.ContainerOf(index) is not { } c)
        {
            return index + ":none";
        }

        var top = c.TranslatePoint(default, f.Window);
        return string.Create(CultureInfo.InvariantCulture, $"{index}:y={top?.Y:F2},h={c.Bounds.Height:F2}");
    }

    [AvaloniaFact]
    public async Task Diagnose()
    {
        var lines = new List<string>();
        int wrong = 0, n = 80;
        for (var i = 0; i < n; i++)
        {
            using var f = await OrganizeListFixture.OpenAsync("thousand-pages.pdf");
            var list = f.List.List;
            list.ScrollIntoView(500);
            ViewFixture.Pump();
            var boxesBefore = $"{Box(f, 499)} {Box(f, 500)} {Box(f, 501)}";
            var p = f.Center(f.Item(500));
            var hitBefore = f.Window.InputHitTest(p) as Visual;
            var hitIndexBefore = hitBefore?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().Select(x => list.IndexFromContainer(x)).FirstOrDefault(-2);
            var scrollBefore = list.Scroll as ScrollViewer;
            var offsetBefore = scrollBefore?.Offset.Y ?? double.NaN;

            f.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
            f.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
            ViewFixture.Pump();
            var focused = NavList.FocusedIndex(list);
            if (focused != 500)
            {
                wrong++;
                var boxesAfter = $"{Box(f, 499)} {Box(f, 500)} {Box(f, 501)}";
                var offsetAfter = (list.Scroll as ScrollViewer)?.Offset.Y ?? double.NaN;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"iter {i}: focus={focused} point=({p.X:F2},{p.Y:F2}) hitBefore={hitIndexBefore} offset {offsetBefore:F2}->{offsetAfter:F2} before[{boxesBefore}] after[{boxesAfter}] scale={f.Window.RenderScaling}"));
            }
        }

        lines.Add($"n={n} wrong={wrong} os={System.Runtime.InteropServices.RuntimeInformation.OSDescription} cpus={Environment.ProcessorCount}");
        Assert.Fail("DIAGNOSTIC RESULT: " + string.Join(" | ", lines));
    }
}
