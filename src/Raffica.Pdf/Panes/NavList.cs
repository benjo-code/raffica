using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Raffica.Pdf.Panes;

/// <summary>What the list of a navigation pane does with the keyboard focus, shared by the pages pane and the outline pane.</summary>
internal static class NavList
{
    /// <summary>The index of the row that has the keyboard focus, or -1 when the focus is not on a row of the list.</summary>
    public static int FocusedIndex(ListBox list)
    {
        if (TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement() is Visual focused
            && focused.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault(item => list.IndexFromContainer(item) >= 0) is { } item)
        {
            return list.IndexFromContainer(item);
        }

        return -1;
    }

    /// <summary>
    /// The row to act on for a key: the row that has the focus, which also becomes the selected row; when the focus is somewhere else
    /// in the list (the list itself), the selected row.
    /// </summary>
    public static int RowForKey(ListBox list)
    {
        var focused = FocusedIndex(list);
        if (focused >= 0 && focused != list.SelectedIndex)
        {
            list.SelectedIndex = focused;
        }

        return focused >= 0 ? focused : list.SelectedIndex;
    }

    /// <summary>
    /// When the keyboard focus enters the list from outside (F6, Tab), puts it on the row <paramref name="target"/> returns, selected and
    /// scrolled into view, instead of on whichever row came first. A click or a move inside the list is left alone. The move is posted:
    /// the code that asked for the focus (F6) must see its own request succeed, and moves the focus on itself if it is changed meanwhile.
    /// </summary>
    public static void EnterAt(ListBox list, FocusChangedEventArgs e, Func<int> target)
    {
        if (e.NavigationMethod != NavigationMethod.Tab || (e.OldFocusedElement is Visual old && list.IsVisualAncestorOf(old)))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var row = target();
                var focused = FocusedIndex(list);
                if (focused < 0 || row < 0 || row >= list.ItemCount)
                {
                    return;
                }

                list.SelectedIndex = row;
                if (focused == row)
                {
                    return;
                }

                list.ScrollIntoView(row);
                if (list.ContainerFromIndex(row) is null)
                {
                    list.UpdateLayout();
                }

                list.ContainerFromIndex(row)?.Focus(NavigationMethod.Tab);
            },
            DispatcherPriority.Input);
    }
}
