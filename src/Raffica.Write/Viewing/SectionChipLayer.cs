using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Raffica.Write.Viewing;

/// <summary>Where one marker's page is in the view, as the layer needs it to place the chip (DIPs, in the coordinates of the view).</summary>
/// <param name="Marker">The marker.</param>
/// <param name="PageLeft">The left edge of the marker's page as drawn.</param>
/// <param name="PageTop">The top edge of the page as drawn.</param>
/// <param name="PageRight">The right edge of the page as drawn.</param>
/// <param name="PageBottom">The bottom edge of the page as drawn.</param>
/// <param name="Y">The top of the section's first block as drawn.</param>
/// <param name="MarginDips">The room between the text area and the right edge of the page (the smaller right margin of the section and the one before it, at the zoom).</param>
internal readonly record struct ChipPlace(SectionMarker Marker, double PageLeft, double PageTop, double PageRight, double PageBottom, double Y, double MarginDips);

/// <summary>
/// The layer of the section chips over the pages of the page view: one small button for each marker in view, a tab at the right edge of the page at the height where the section starts. It is made on the first
/// marker and holds only the chips in view (a document of a thousand sections has a handful of them). The chips are controls laid over the pages, so they have an accessible name and a click of their own and
/// never take part in the layout of the document or in print and export; the layer is changed only outside the render and arrange passes (by the view, when what it shows changed). The empty parts of the
/// layer take no pointer, so the page under it is clicked as before. A chip keeps the size of its text at every zoom (it is a control, not a part of the page): it stands inside the right margin when the
/// margin has room, in a short form (the title only) when it has less, on the desk beside the page when the margin has none, and is not shown when neither has room, so it never covers the text area.
/// </summary>
internal sealed class SectionChipLayer : Canvas
{
    /// <summary>The space between a chip and the edge of the page (DIPs).</summary>
    internal const double Gap = 4;

    private readonly Dictionary<int, Chip> _chips = [];
    private int _stamp;

    public SectionChipLayer()
    {
        ClipToBounds = true;
    }

    /// <summary>Makes the content of the popover of a marker as the document is when it opens; null (the popover does not open) when the marker does not belong to the document any more.</summary>
    internal Func<SectionMarker, Action, Control?>? PopoverFactory { get; set; }

    /// <summary>How many times the layer was brought up to date (tests: a render pass must not).</summary>
    internal int Syncs { get; private set; }

    /// <summary>How many chips were made (tests: chips are reused while the view scrolls).</summary>
    internal int ChipsMade { get; private set; }

    /// <summary>The chips as they are now, by section index (tests).</summary>
    internal IEnumerable<(int Section, Button Button, bool Shown, bool Compact)> Chips => _chips.Select(c => (c.Key, c.Value.Button, c.Value.Button.IsVisible, !c.Value.Line.IsVisible));

    /// <summary>How many chips the layer holds.</summary>
    internal int Count => _chips.Count;

    /// <summary>Closes every popover that is open (the view is let go of or leaves the page view).</summary>
    internal void CloseFlyouts()
    {
        foreach (var chip in _chips.Values)
        {
            chip.Button.Flyout?.Hide();
        }
    }

    /// <summary>The chip of a section, or null.</summary>
    internal Button? ChipOf(int section) => _chips.TryGetValue(section, out var chip) ? chip.Button : null;

    /// <summary>Shows exactly these chips: reuses the control of a section that stays, makes the ones that are new and lets go of the rest.</summary>
    internal void Sync(IReadOnlyList<ChipPlace> places, double usableRight)
    {
        Syncs++;
        _stamp++;
        var slots = new List<Slot>(places.Count);
        foreach (var place in places)
        {
            var marker = place.Marker;
            if (!_chips.TryGetValue(marker.Section, out var chip))
            {
                chip = Make(marker.Section);
                _chips[marker.Section] = chip;
                Children.Add(chip.Button);
                ChipsMade++;
            }

            chip.Marker = marker;
            chip.Stamp = _stamp;
            if (chip.Title.Text != marker.Title)
            {
                chip.Title.Text = marker.Title;
            }

            if (chip.Line.Text != marker.Numbers)
            {
                chip.Line.Text = marker.Numbers;
            }

            AutomationProperties.SetName(chip.Button, marker.Name);
            AutomationProperties.SetHelpText(chip.Button, marker.Help);
            if (Place(chip, place, usableRight) is { } slot)
            {
                slots.Add(slot);
            }
        }

        Stack(slots);

        List<int>? gone = null;
        foreach (var (section, chip) in _chips)
        {
            if (chip.Stamp != _stamp)
            {
                (gone ??= []).Add(section);
                chip.Button.Flyout?.Hide();
                Children.Remove(chip.Button);
            }
        }

        if (gone is not null)
        {
            foreach (var section in gone)
            {
                _chips.Remove(section);
            }
        }
    }

    // A chip on its page: the button, where it wants to stand (y) and how high it is.
    private readonly record struct Slot(Button Button, double Y, double Height, double PageTop, double PageBottom);

    // Chips on one page that would cover each other are put under each other (the first one that does not fit at the bottom of the page moves the ones above it up), so each stays clickable.
    private static void Stack(List<Slot> slots)
    {
        const double Space = 2;
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var y = slot.Y;
            if (i > 0 && slots[i - 1].PageTop == slot.PageTop)
            {
                y = Math.Max(y, slots[i - 1].Y + slots[i - 1].Height + Space);
            }

            slots[i] = slot with { Y = Math.Max(slot.PageTop, Math.Min(y, slot.PageBottom - slot.Height)) };
        }

        for (var i = slots.Count - 2; i >= 0; i--)
        {
            if (slots[i].PageTop == slots[i + 1].PageTop && slots[i].Y + slots[i].Height + Space > slots[i + 1].Y)
            {
                slots[i] = slots[i] with { Y = Math.Max(slots[i].PageTop, slots[i + 1].Y - slots[i].Height - Space) };
            }
        }

        foreach (var slot in slots)
        {
            SetTop(slot.Button, slot.Y);
        }
    }

    private static Slot? Place(Chip chip, ChipPlace place, double usableRight)
    {
        // Both sizes are asked (the second line shown, then hidden): the chip is small and only the ones in view are placed.
        var button = chip.Button;
        chip.Line.IsVisible = true;
        button.Measure(Size.Infinity);
        var full = button.DesiredSize;
        chip.Line.IsVisible = false;
        button.Measure(Size.Infinity);
        var compact = button.DesiredSize;
        var inside = place.MarginDips - (2 * Gap);
        var desk = usableRight - place.PageRight - Gap;

        double x;
        bool useFull;
        if (full.Width <= inside)
        {
            (x, useFull) = (place.PageRight - Gap - full.Width, true);
        }
        else if (compact.Width <= inside)
        {
            (x, useFull) = (place.PageRight - Gap - compact.Width, false);
        }
        else if (full.Width <= desk)
        {
            (x, useFull) = (place.PageRight + Gap, true);
        }
        else if (compact.Width <= desk)
        {
            (x, useFull) = (place.PageRight + Gap, false);
        }
        else
        {
            button.IsVisible = false;
            return null;
        }

        chip.Line.IsVisible = useFull;
        button.IsVisible = true;
        var height = useFull ? full.Height : compact.Height;
        SetLeft(button, x);
        return new Slot(button, place.Y, height, place.PageTop, place.PageBottom);
    }

    private Chip Make(int section)
    {
        var title = new TextBlock();
        title.Classes.Add("chip-title");
        var line = new TextBlock();
        line.Classes.Add("chip-line");
        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.Children.Add(title);
        content.Children.Add(line);
        var button = new Button { Name = "PART_SectionChip" + section, Content = content, Focusable = false, IsTabStop = false };
        button.Classes.Add("boxed");
        button.Classes.Add("section-chip");
        // Any button on the chip is the chip's: a right or middle press must not reach the page under it (it would place the caret or open the page's menu).
        button.AddHandler(PointerPressedEvent, static (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);
        var flyout = new Flyout { Placement = PlacementMode.Bottom };
        var chip = new Chip(button, title, line);
        flyout.Opening += (_, _) => OnOpening(chip, flyout, PopoverFactory);
        button.Flyout = flyout;
        return chip;
    }

    // The content is made when the popover opens, from the document as it is then.
    private static void OnOpening(Chip chip, Flyout flyout, Func<SectionMarker, Action, Control?>? factory)
    {
        // A marker that is not in the document any more opens the line that says so (the view has put the same words in the window's note).
        flyout.Content = (chip.Marker is { } marker ? factory?.Invoke(marker, flyout.Hide) : null) ?? new TextBlock { Text = WriteStrings.NoteSectionRowStale, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Width = SectionPopover.Width };
    }

    private sealed class Chip(Button button, TextBlock title, TextBlock line)
    {
        public Button Button => button;

        public TextBlock Title => title;

        public TextBlock Line => line;

        public SectionMarker? Marker { get; set; }

        public int Stamp { get; set; }
    }
}
