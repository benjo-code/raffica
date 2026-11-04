using Avalonia;
using Avalonia.Controls;

namespace Raffica.Shell.Frame;

/// <summary>
/// A pane of the app frame (section 6 of the design spec): a 38 px header with the title in 12 px text, the content below with
/// 12 px padding, on the band surface with square edges. <see cref="IsOpen"/> false collapses it. While it holds the F6 focus
/// (<see cref="IsPaneFocused"/>, set by <see cref="PaneFocus"/>) it gets a 1 px ink border inside its edge and a primary title.
/// The document pane (<see cref="IsDocument"/>) has no header, no padding and no band surface of its own.
/// </summary>
public class Pane : ContentControl, IPaneItem
{
    /// <summary>The text in the header.</summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<Pane, string?>(nameof(Title));

    /// <summary>The name in the status bar during F6 navigation; defaults to <see cref="Title"/>.</summary>
    public static readonly StyledProperty<string?> PaneNameProperty = AvaloniaProperty.Register<Pane, string?>(nameof(PaneName));

    /// <summary>False collapses the pane and takes it out of the F6 ring.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<Pane, bool>(nameof(IsOpen), true);

    /// <summary>Whether the pane holds the F6 focus; drawn as the ink border and the primary title.</summary>
    public static readonly StyledProperty<bool> IsPaneFocusedProperty = AvaloniaProperty.Register<Pane, bool>(nameof(IsPaneFocused));

    /// <summary>The document pane: no header, no padding, no band surface.</summary>
    public static readonly StyledProperty<bool> IsDocumentProperty = AvaloniaProperty.Register<Pane, bool>(nameof(IsDocument));

    static Pane()
    {
        IsOpenProperty.Changed.AddClassHandler<Pane>((pane, e) => pane.IsVisible = e.GetNewValue<bool>());
        IsPaneFocusedProperty.Changed.AddClassHandler<Pane>((pane, e) => pane.PseudoClasses.Set(":pane-focused", e.GetNewValue<bool>()));
        IsDocumentProperty.Changed.AddClassHandler<Pane>((pane, e) => pane.PseudoClasses.Set(":document", e.GetNewValue<bool>()));
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? PaneName { get => GetValue(PaneNameProperty); set => SetValue(PaneNameProperty, value); }

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    public bool IsPaneFocused { get => GetValue(IsPaneFocusedProperty); set => SetValue(IsPaneFocusedProperty, value); }

    public bool IsDocument { get => GetValue(IsDocumentProperty); set => SetValue(IsDocumentProperty, value); }

    string IPaneItem.Name => PaneName ?? Title ?? string.Empty;

    bool IPaneItem.IsAvailable => IsOpen && IsVisible;
}
