using Avalonia.Input;
using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>The catalog against the shortcuts macOS reserves for itself, and the per-system shortcut table.</summary>
public sealed class ReservedShortcutTests
{
    private const KeyModifiers Meta = KeyModifiers.Meta;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Ctrl = KeyModifiers.Control;

    [Fact]
    public void NoCatalogShortcutClashesWithAReservedMacShortcutOnMacOs() =>
        Assert.Empty(ReservedShortcuts.ClashesIn(true));

    [Fact]
    public void TheUnmovedCatalogHasExactlyOneClash()
    {
        var clashes = PdfCommandCatalog.Commands
            .Where(c => c.DefaultShortcut is { } shortcut
                && ReservedShortcuts.MacOs.Any(r => shortcut.Matches(r.Key, r.Modifiers, Meta)))
            .Select(c => c.Id)
            .ToList();

        Assert.Equal([PdfCommandIds.OcrRun], clashes);
    }

    [Fact]
    public void OcrRunIsCommandShiftRonMacAndCtrlShiftQElsewhere()
    {
        var ocr = PdfCommandCatalog.Commands.Single(c => c.Id == PdfCommandIds.OcrRun);

        Assert.Equal("Meta+Shift+R", ocr.ShortcutFor(true)!.Text(true));
        Assert.Equal("Ctrl+Shift+Q", ocr.ShortcutFor(false)!.Text(false));
    }

    [Fact]
    public void FindUsesTheMacTableOnMac()
    {
        Assert.Equal(PdfCommandIds.OcrRun, PdfCommandCatalog.Find(Key.R, Meta | Shift, Meta, macOs: true)?.Id);
        Assert.Null(PdfCommandCatalog.Find(Key.Q, Meta | Shift, Meta, macOs: true));
        Assert.Equal(PdfCommandIds.OcrRun, PdfCommandCatalog.Find(Key.Q, Ctrl | Shift, Ctrl, macOs: false)?.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoTwoCommandsShareAShortcutOnEitherSystem(bool macOs)
    {
        var texts = PdfCommandCatalog.Commands
            .Select(c => (c.Id, Shortcut: c.ShortcutFor(macOs)))
            .Where(x => x.Shortcut is not null)
            .GroupBy(x => x.Shortcut!.Text(macOs))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + ": " + string.Join(", ", g.Select(x => x.Id)))
            .ToList();

        Assert.Empty(texts);
    }
}
