using Avalonia.Controls;
using Avalonia.Layout;

namespace Raffica.Shell.Controls;

/// <summary>
/// The footer of a dialog card in one order everywhere (the Windows convention, right-aligned by the dialog): the primary action first, then the
/// secondary actions, Cancel last. The primary button is the default (Enter) and the cancel button is the cancel (Esc). A card with only a way
/// to dismiss it (a notice, a question whose safe answer is the cancel) passes no primary: the cancel button is then both.
/// </summary>
public static class CardFooter
{
    /// <summary>The gap between two footer buttons.</summary>
    public const double Spacing = 8;

    /// <summary>Builds the footer panel for <see cref="Dialog.Footer"/> and marks the default and the cancel button.</summary>
    /// <param name="primary">The main action, or <see langword="null"/> when the card has none.</param>
    /// <param name="secondary">The other actions, in the order they are shown (may be <see langword="null"/>).</param>
    /// <param name="cancel">The button that dismisses the card without a decision, shown last, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">There is no button at all.</exception>
    public static StackPanel Build(Button? primary, IReadOnlyList<Button>? secondary, Button? cancel)
    {
        if (primary is null && cancel is null && (secondary is null || secondary.Count == 0))
        {
            throw new ArgumentException("A card footer needs at least one button.", nameof(primary));
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing };
        if (primary is not null)
        {
            panel.Children.Add(primary);
        }

        foreach (var button in secondary ?? [])
        {
            panel.Children.Add(button);
        }

        if (cancel is not null)
        {
            panel.Children.Add(cancel);
            cancel.IsCancel = true;
        }

        // The default is the primary; a card without one answers Enter with the cancel button, and a card with secondary actions only with the first.
        var defaultButton = primary ?? cancel ?? secondary![0];
        defaultButton.IsDefault = true;
        return panel;
    }

    /// <summary>A footer with one button: it closes the card, so it is the default and the cancel.</summary>
    public static StackPanel Only(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return Build(null, null, button);
    }
}
