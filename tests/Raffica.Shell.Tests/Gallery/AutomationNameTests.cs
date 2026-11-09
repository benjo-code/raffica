using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Gallery.Sections;
using Raffica.Shell.Controls;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Gallery;

public sealed class AutomationNameTests
{
    /// <summary>The interactive controls below <paramref name="root"/> (visual tree, open popups and the logical tree of menus) whose automation peer has no name.</summary>
    internal static List<string> Unnamed(Control root)
    {
        var controls = ControlHarness.Descendants(root).OfType<Control>()
            .Concat(root.GetLogicalDescendants().OfType<Control>())
            .Append(root)
            .Where(IsInteractive)
            .Distinct();
        var problems = new List<string>();
        foreach (var control in controls)
        {
            if (!HasName(control))
            {
                problems.Add($"{control.GetType().Name} (content: {ContentOf(control)?.GetType().Name ?? "-"})");
            }
        }

        return problems;
    }

    private static object? ContentOf(Control c) => c switch
    {
        ContentControl cc => cc.Content,
        MenuItem mi => mi.Header,
        _ => null,
    };

    /// <summary>
    /// An explicit automation name, or a peer name that really is text. A peer falls back to <c>ToString()</c> of a non-text content,
    /// which for an icon is its type name: that is no name, so it does not count.
    /// </summary>
    private static bool HasName(Control control)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
        {
            return true;
        }

        var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var content = ContentOf(control);
        return content is null || content is string || name != content.ToString();
    }

    // The line buttons inside a slider or a scroll bar are parts of that control (the slider or the bar carries the name), not controls of their own.
    private static bool IsInteractive(Control c) =>
        c is Button or ToggleButton or CheckBox or RadioButton or TextBox or ComboBox or MenuItem or ListBoxItem or TabItem
        && !(c is RepeatButton && c.FindAncestorOfType<RangeBase>() is not null);

    [AvaloniaFact]
    public void EveryInteractiveControlInTheGalleryHasAName()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        foreach (var section in GallerySections.Names)
        {
            var window = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", section, "--open-menu", "--open-dialog"]));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Empty(Unnamed(window));
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnIconButtonWithoutAnAutomationNameIsFound()
    {
        var button = new Button { Content = new StrokeIcon { Size = 16 } };
        button.Classes.Add("icon");
        var window = ControlHarness.Show(new StackPanel { Children = { button } }, ThemeMode.Light);

        Assert.Single(Unnamed(window));

        AutomationProperties.SetName(button, "Bold");
        Assert.Empty(Unnamed(window));
        window.Close();
    }
}
