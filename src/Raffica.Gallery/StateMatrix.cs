using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Raffica.Gallery;

/// <summary>Forces control states without a pointer or the keyboard, so a gallery row can show hover, pressed and focus side by side.</summary>
public static class StateMatrix
{
    /// <summary>
    /// Sets the given pseudo-classes (<c>:pointerover</c>, <c>:pressed</c>, <c>:focus-visible</c>, with or without the colon) on
    /// <paramref name="control"/>. <c>:focus-visible</c> also shows the focus adorner, which the framework otherwise only
    /// adds when the control really has the keyboard focus.
    /// </summary>
    public static void Force(Control control, params string[] pseudoClasses)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(pseudoClasses);

        var names = pseudoClasses.Select(name => name.StartsWith(':') ? name : ":" + name).ToArray();

        // A control that is not shown yet gets its states once it is loaded (the framework resets the pseudo-classes while it attaches and templates the control).
        if (control.IsLoaded)
        {
            SetAll(control, names);
            return;
        }

        control.Loaded += OnLoaded;

        void OnLoaded(object? sender, RoutedEventArgs e)
        {
            control.Loaded -= OnLoaded;
            SetAll(control, names);
        }
    }

    private static void SetAll(Control control, string[] names)
    {
        var classes = (IPseudoClasses)control.Classes;
        foreach (var pseudo in names)
        {
            classes.Add(pseudo);
            if (pseudo == ":focus-visible")
            {
                ShowFocusAdorner(control);
            }
        }
    }

    private static void ShowFocusAdorner(Control control)
    {
        if (control.IsAttachedToVisualTree())
        {
            Attach(control);
        }
        else
        {
            control.AttachedToVisualTree += OnAttached;
        }

        static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            var control = (Control)sender!;
            control.AttachedToVisualTree -= OnAttached;
            Attach(control);
        }
    }

    private static void Attach(Control control)
    {
        if (AdornerLayer.GetAdornerLayer(control) is not { } layer)
        {
            return;
        }

        var template = control.FocusAdorner ?? layer.DefaultFocusAdorner;
        if (template?.Build() is not { } adorner)
        {
            return;
        }

        AdornerLayer.SetAdornedElement(adorner, control);
        AdornerLayer.SetIsClipEnabled(adorner, false);
        layer.Children.Add(adorner);
    }
}
