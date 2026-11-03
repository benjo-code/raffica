using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Controls;

/// <summary>
/// A modal dialog (section 6 of the design spec): a header with the title and a close button, the body (the content) and a
/// footer on the band surface with the action buttons. Show it in a <see cref="DialogHost"/>. The dialog never closes itself:
/// Esc and the close button raise <see cref="CancelRequested"/>, and the app decides. Tab and Shift+Tab cycle inside the dialog.
/// </summary>
public class Dialog : ContentControl
{
    /// <summary>The title in the header (15 px, 600).</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Dialog, string?>(nameof(Title));

    /// <summary>The action buttons in the footer, usually a horizontal stack with the cancel and the primary button on the right. Null hides the footer.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<Dialog, object?>(nameof(Footer));

    private Button? _close;

    public Dialog()
    {
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised by Esc and by the close button.</summary>
    public event EventHandler? CancelRequested;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>Moves the focus to the first control of the body, else of the footer, else to the close button.</summary>
    internal void FocusFirst()
    {
        var candidates = FocusableControls();
        var first = candidates.FirstOrDefault(c => !ReferenceEquals(c, _close)) ?? candidates.FirstOrDefault();
        first?.Focus(NavigationMethod.Tab);
    }

    internal void RaiseCancelRequested() => CancelRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_close is not null)
        {
            _close.Click -= OnCloseClick;
        }

        _close = e.NameScope.Find<Button>("PART_Close");
        if (_close is not null)
        {
            AutomationProperties.SetName(_close, ShellStrings.Close);
            _close.Click += OnCloseClick;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            AutomationProperties.SetName(this, Title);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => RaiseCancelRequested();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            // An open select consumes Esc to close its list first.
            if (e.Source is Visual source && source.FindAncestorOfType<ComboBox>(includeSelf: true) is { IsDropDownOpen: true })
            {
                return;
            }

            e.Handled = true;
            RaiseCancelRequested();
        }
        else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            e.Handled = true;
            MoveFocus(backwards: e.KeyModifiers == KeyModifiers.Shift);
        }
    }

    private void MoveFocus(bool backwards)
    {
        var candidates = FocusableControls();
        if (candidates.Count == 0)
        {
            return;
        }

        var current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var index = candidates.FindIndex(c => ReferenceEquals(c, current));
        var next = index < 0
            ? (backwards ? candidates.Count - 1 : 0)
            : (index + (backwards ? candidates.Count - 1 : 1)) % candidates.Count;
        candidates[next].Focus(NavigationMethod.Tab);
    }

    /// <summary>The controls Tab can reach inside the dialog, in visual order.</summary>
    private List<InputElement> FocusableControls() =>
        this.GetVisualDescendants()
            .OfType<InputElement>()
            .Where(c => c.Focusable && c.IsTabStop && c.IsEffectivelyEnabled && c.IsEffectivelyVisible)
            .ToList();
}
