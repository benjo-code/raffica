using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Raffica.Shell.Controls;

/// <summary>
/// A row of mutually exclusive choices (the navigation view switch, the theme choice): a <see cref="ListBox"/> with
/// single selection that always keeps one item selected. For assistive technology it is a selection container whose items
/// are radio buttons.
/// </summary>
public class SegmentedControl : ListBox
{
    public SegmentedControl()
    {
        SelectionMode = SelectionMode.Single | SelectionMode.AlwaysSelected;
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<SegmentedControlItem>(item, out recycleKey);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new SegmentedControlItem();
}

/// <summary>One choice of a <see cref="SegmentedControl"/>.</summary>
public class SegmentedControlItem : ListBoxItem
{
    protected override AutomationPeer OnCreateAutomationPeer() => new SegmentedControlItemAutomationPeer(this);

    private sealed class SegmentedControlItemAutomationPeer(SegmentedControlItem owner) : ListItemAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;
    }
}
