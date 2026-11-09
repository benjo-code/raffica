using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Raffica.Shell.Controls;

/// <summary>
/// The root that wraps the app content and shows one <see cref="Dialog"/> over it (an in-window overlay, not an OS window, so the
/// dimmed app stays visible behind). While a dialog is open the scrim covers the content, pointer input hits the scrim, keys that
/// start outside the dialog are swallowed, and the focus moves into the dialog; <see cref="Close"/> hands it back to the control
/// that had it before.
/// </summary>
public class DialogHost : ContentControl
{
    /// <summary>The dialog on show, or null.</summary>
    public static readonly StyledProperty<Dialog?> ActiveDialogProperty =
        AvaloniaProperty.Register<DialogHost, Dialog?>(nameof(ActiveDialog));

    /// <summary>The room kept free around a card: it is at most the host's height less this (24 DIP above and below).</summary>
    public const double CardMargin = 48;

    /// <summary>The least height the cap leaves a card (header, footer and a few rows of the body), so a very small host does not shrink it to a sliver (the margin gives way there: the card is then as tall as the host, never taller).</summary>
    public const double MinCardHeight = 200;

    private InputElement? _opener;

    public DialogHost()
    {
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public Dialog? ActiveDialog => GetValue(ActiveDialogProperty);

    public bool IsOpen => ActiveDialog is not null;

    /// <exception cref="InvalidOperationException">A dialog is already open.</exception>
    public void Show(Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (IsOpen)
        {
            throw new InvalidOperationException("A dialog is already open in this host.");
        }

        _opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
        CapHeight(dialog, Bounds.Height);
        SetValue(ActiveDialogProperty, dialog);
        PseudoClasses.Set(":open", true);

        if (this.IsAttachedToVisualTree())
        {
            ApplyTemplate();
            UpdateLayout();
            dialog.FocusFirst();
        }
        else
        {
            void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
            {
                AttachedToVisualTree -= OnAttached;
                Dispatcher.UIThread.Post(() => ActiveDialog?.FocusFirst(), DispatcherPriority.Loaded);
            }

            AttachedToVisualTree += OnAttached;
        }
    }

    /// <summary>Closes the dialog (if one is open) and gives the focus back to the control that had it before.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        SetValue(ActiveDialogProperty, null);
        PseudoClasses.Set(":open", false);
        var opener = _opener;
        _opener = null;
        if (opener is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && opener.IsAttachedToVisualTree())
        {
            opener.Focus(NavigationMethod.Unspecified);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (ActiveDialog is { } dialog)
        {
            CapHeight(dialog, e.NewSize.Height);
        }
    }

    // The card never grows past the host (less the margin, but not below MinCardHeight): its body scrolls instead. A host that is not laid out yet has no height to cap by.
    private static void CapHeight(Dialog dialog, double hostHeight) =>
        dialog.MaxHeight = hostHeight > 0 ? Math.Max(MinCardHeight, hostHeight - CardMargin) : double.PositiveInfinity;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ActiveDialog is not { } dialog || e.Handled)
        {
            return;
        }

        if (e.Source is Visual source && (ReferenceEquals(source, dialog) || dialog.IsVisualAncestorOf(source)))
        {
            return;
        }

        // The key started in the content behind the dialog (or nowhere): nothing there may react to it.
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            dialog.RaiseCancelRequested();
        }
        else
        {
            dialog.FocusFirst();
        }
    }
}
