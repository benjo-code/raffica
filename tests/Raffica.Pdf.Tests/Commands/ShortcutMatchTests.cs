using Avalonia.Input;
using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>The matching of key presses to catalog commands, without a window: AltGr, Cmd on macOS, Shift variants and the held-key flag.</summary>
public sealed class ShortcutMatchTests
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const KeyModifiers Meta = KeyModifiers.Meta;

    private static string? Find(Key key, KeyModifiers modifiers, KeyModifiers command = Ctrl) =>
        PdfCommandCatalog.Find(key, modifiers, command)?.Id;

    [Fact]
    public void AltGrAndAltNeverMatchAnyShortcut()
    {
        // AltGr arrives as Ctrl+Alt on Windows; typing "@" or "{" must never run a command.
        foreach (var command in PdfCommandCatalog.Commands.Where(c => c.Shortcut is not null))
        {
            foreach (var key in command.Shortcut!.Keys)
            {
                foreach (var modifiers in new[] { Ctrl | Alt, Ctrl | Alt | Shift, Alt, Alt | Shift, Meta | Alt })
                {
                    Assert.Null(Find(key, modifiers));
                    Assert.Null(Find(key, modifiers, Meta));
                }
            }
        }
    }

    [Fact]
    public void ShiftSeparatesTheVariants()
    {
        Assert.Null(Find(Key.D, Ctrl));
        Assert.Equal("page.dark", Find(Key.D, Ctrl | Shift));
        Assert.Equal("zoom.fitwidth", Find(Key.D1, Ctrl));
        Assert.Equal("panes.pages", Find(Key.D1, Ctrl | Shift));
        Assert.Equal("zoom.fitpage", Find(Key.D2, Ctrl));
        Assert.Equal("panes.outline", Find(Key.D2, Ctrl | Shift));
        Assert.Equal("tabs.next", Find(Key.Tab, Ctrl));
        Assert.Equal("tabs.previous", Find(Key.Tab, Ctrl | Shift));
        Assert.Equal("find.next", Find(Key.F3, KeyModifiers.None));
        Assert.Equal("find.previous", Find(Key.F3, Shift));
        Assert.Null(Find(Key.F3, Ctrl));
    }

    [Fact]
    public void ZoomKeysAcceptShiftAndTheNumberPad()
    {
        Assert.Equal("zoom.in", Find(Key.OemPlus, Ctrl));
        Assert.Equal("zoom.in", Find(Key.OemPlus, Ctrl | Shift));
        Assert.Equal("zoom.in", Find(Key.Add, Ctrl));
        Assert.Equal("zoom.out", Find(Key.OemMinus, Ctrl));
        Assert.Equal("zoom.out", Find(Key.Subtract, Ctrl));
        Assert.Null(Find(Key.OemPlus, KeyModifiers.None));
        Assert.Equal("zoom.actual", Find(Key.NumPad0, Ctrl));
        Assert.Equal("zoom.fitwidth", Find(Key.NumPad1, Ctrl));
    }

    [Fact]
    public void OnMacTheCommandModifierIsMetaAndControlDoesNotCount()
    {
        Assert.Equal("file.open", Find(Key.O, Meta, Meta));
        Assert.Null(Find(Key.O, Ctrl, Meta));
        Assert.Null(Find(Key.O, Ctrl | Meta, Meta));
        Assert.Equal("page.dark", Find(Key.D, Meta | Shift, Meta));
        Assert.Equal("zoom.in", Find(Key.OemPlus, Meta, Meta));

        // Ctrl+Tab keeps the Control key on every platform; Cmd+Tab belongs to the system.
        Assert.Equal("tabs.next", Find(Key.Tab, Ctrl, Meta));
        Assert.Null(Find(Key.Tab, Meta, Meta));

        // Elsewhere Meta (the Windows key) is no command key.
        Assert.Null(Find(Key.O, Meta, Ctrl));
        Assert.Null(Find(Key.O, Ctrl | Meta, Ctrl));
    }

    [Fact]
    public void OnlyZoomAndTheHitKeysRepeatWhenHeld()
    {
        var repeating = PdfCommandCatalog.Commands.Where(c => c.Shortcut is { AllowsRepeat: true }).Select(c => c.Id).Order().ToArray();
        Assert.Equal(["find.next", "find.previous", "zoom.in", "zoom.out"], repeating);
    }
}
