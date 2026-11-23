using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Raffica.Write.Commands;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Settings;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// The section markers of the page view: one chip for each section after the first whose first block is on a page in view, at the right edge of that page at the height where the section starts. The chips are
/// not drawn by the page renderer and are not part of a laid out page (so print and PDF export never get them): they are controls in a layer over the pages (<see cref="SectionChipLayer"/>) that the view updates
/// when what it shows changes (scroll, zoom, layout, edit), never in the render or arrange pass. A chip opens a popover with the controls of the pane's row and Remove marker. The writing view has none (one strip,
/// no section start to name); a document that is still opening (a prefix) has none and the document must have been drawn once. Nothing of this exists before the first sharp page; the sections are read once for a
/// revision of the model, and a change of the model reads again after the typing pauses (one read for a burst of keys, posted at the background priority), while a scroll or a zoom only looks the few sections
/// in view up. A chip is not a tab stop (Tab belongs to the page view): the keyboard reaches the same controls through the Sections pane, the section commands and Remove section; assistive technology reaches the chip
/// itself, which has a name and a click.
/// </summary>
internal sealed partial class PageView
{
    private SectionMarkers? _sectionMarkers;
    private SectionChipLayer? _chipLayer;
    private bool _chipRefreshPosted;
    private FormatDisplayRefresh? _chipRefresh;

    /// <summary>The markers' source; null until the page view has drawn page 1 of a document that is fully open (tests).</summary>
    internal SectionMarkers? Markers => _sectionMarkers;

    /// <summary>The layer of the chips; null until the first marker exists (tests).</summary>
    internal SectionChipLayer? ChipLayer => _chipLayer;

    /// <summary>
    /// What a control of the popover runs: the window's route (Number from here asks its card first); the view's own <see cref="RunSectionRow"/> where there is no window (Number from here then does nothing).
    /// </summary>
    internal Func<SectionRowAction, bool>? SectionRowRunner { get; set; }

    // Brings the chips up to date with what the view shows. Called when the view changed (never inside the render or arrange pass).
    private void RefreshSectionChips()
    {
        if (_released)
        {
            return;
        }

        if (!ChipsAllowed(out var layout, out var scroller))
        {
            ClearChips();
            return;
        }

        _sectionMarkers ??= new SectionMarkers();
        if (!_sectionMarkers.IsCurrent(_tab.Docx.Model))
        {
            PostChipRefresh();
            return;
        }

        PlaceChips(layout, scroller);
    }

    // Only the page view of a fully open document shows chips, and only once page 1 was drawn.
    private bool ChipsAllowed(out DocumentLayout layout, out PageScroller scroller)
    {
        layout = _builtLayout!;
        scroller = _scroller!;
        return _mode == WriteView.Page && !_tab.Docx.IsPrefix && FirstSharpPageShown && _builtLayout is not null && _scroller is { Count: > 0 };
    }

    // The first read of the sections is posted at the background priority (the chips appear with the first frames). A change of the model after that is read once when the typing pauses (the read of every section
    // is the only part that costs more than a lookup, so it never runs per key).
    private void PostChipRefresh()
    {
        if (_sectionMarkers is { Reads: > 0 })
        {
            (_chipRefresh ??= new FormatDisplayRefresh(ReadAndPlaceChips, FormatDisplayRefresh.DefaultDelay)).Request();
            return;
        }

        if (_chipRefreshPosted)
        {
            return;
        }

        _chipRefreshPosted = true;
        _dispatcher.Post(
            () =>
            {
                _chipRefreshPosted = false;
                ReadAndPlaceChips();
            },
            DispatcherPriority.Background);
    }

    /// <summary>Runs the read that waits for a pause in typing now (tests do not wait for the delay).</summary>
    internal void FlushSectionChips() => _chipRefresh?.Flush();

    private void ReadAndPlaceChips()
    {
        if (_released)
        {
            return;
        }

        if (!ChipsAllowed(out var layout, out var scroller))
        {
            ClearChips();
            return;
        }

        _ = (_sectionMarkers ??= new SectionMarkers()).Sections(_tab.Docx.Model);
        PlaceChips(layout, scroller);
    }

    private void PlaceChips(DocumentLayout layout, PageScroller scroller)
    {
        var (first, last) = scroller.Visible(_offsetY, _viewport.Height);
        var markers = _sectionMarkers!.InView(_tab.Docx.Model, layout, first, last);
        if (markers.Count == 0)
        {
            ClearChips();
            return;
        }

        var layer = EnsureChipLayer();
        var left = LeftShift(scroller);
        var places = new List<ChipPlace>(markers.Count);
        foreach (var marker in markers)
        {
            if ((uint)marker.Page >= (uint)scroller.Count)
            {
                continue;
            }

            var page = scroller.RectOf(marker.Page).Translate(new Vector(left, -_offsetY));
            places.Add(new ChipPlace(marker, page.Left, page.Top, page.Right, page.Bottom, page.Top + (marker.Y * scroller.Scale), marker.MarginPoints * scroller.Scale));
        }

        layer.Sync(places, _viewport.Width - (_vertical.IsVisible ? _vertical.DesiredSize.Width : 0));
    }

    private void ClearChips()
    {
        _chipLayer?.CloseFlyouts();
        if (_chipLayer is { Count: > 0 } layer)
        {
            layer.Sync([], 0);
        }
    }

    private SectionChipLayer EnsureChipLayer()
    {
        if (_chipLayer is { } existing)
        {
            return existing;
        }

        var layer = new SectionChipLayer { PopoverFactory = OpenSectionPopover };
        VisualChildren.Insert(VisualChildren.IndexOf(_vertical), layer);
        LogicalChildren.Add(layer);
        _chipLayer = layer;
        InvalidateMeasure();
        return layer;
    }

    // The popover is made from the document as it is when it opens; a chip whose section is not in the document any more (a marker removed meanwhile) opens nothing and the note says why.
    private StackPanel? OpenSectionPopover(SectionMarker marker, Action close)
    {
        if (_released || _tab.Docx.IsPrefix)
        {
            return null;
        }

        var model = _tab.Docx.Model;
        var sections = SectionList.Of(model);
        if (marker.Section < 1 || marker.Section >= sections.Count || !ReferenceEquals(sections[marker.Section].Properties, marker.Identity))
        {
            StructureNote?.Invoke(WriteStrings.NoteSectionRowStale);
            return null;
        }

        var row = SectionsSource.RowFor(model, sections, marker.Section, marker.Page);
        var previous = SectionsSource.RowFor(model, sections, marker.Section - 1, -1);
        return SectionPopover.Build(row, previous, SectionBlockedReason(_tab), action => (SectionRowRunner ?? RunSectionRow)(action), close);
    }
}
