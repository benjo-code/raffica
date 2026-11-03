using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;

namespace Raffica.Shell.Tests.Frame;

/// <summary>A whole frame in a realistic window (search, ribbon, two panes, a text box, a dialog host, a status bar) with the Alt field codes attached.</summary>
internal sealed class FieldCodeFixture
{
    private FieldCodeFixture(Window window, RibbonDefinition definition)
    {
        Window = window;
        Definition = definition;
    }

    public Window Window { get; }

    public RibbonDefinition Definition { get; }

    public RibbonView Ribbon { get; private set; } = null!;

    public StatusBar Status { get; private set; } = null!;

    public SearchField Search { get; private set; } = null!;

    public Pane Left { get; private set; } = null!;

    public Pane Right { get; private set; } = null!;

    public TextBox Box { get; private set; } = null!;

    public DialogHost Host { get; private set; } = null!;

    public FieldCodeSession Session { get; private set; } = null!;

    public IDisposable Attachment { get; private set; } = null!;

    public List<string> Invoked { get; } = [];

    public static FieldCodeFixture Create(ThemeMode mode = ThemeMode.Light, AppInk ink = AppInk.Write, RibbonDefinition? definition = null)
    {
        definition ??= SampleRibbon.Create();
        var ribbon = new RibbonView { Definition = definition };
        var search = new SearchField { Source = definition, Width = 300 };
        var left = new Pane { Title = "Navigation", Width = 200, Content = new Button { Content = "Nav" } };
        var right = new Pane { Title = "Comments", Width = 200, Content = new Button { Content = "Comment" } };
        FieldCodeBadge.SetCode(left, "6");
        FieldCodeBadge.SetCode(right, "7");
        var box = new TextBox();
        var status = new StatusBar();
        var frame = new AppFrame
        {
            TitleBar = search,
            TabRow = ribbon.TabRow,
            Band = ribbon.Band,
            LeftPane = left,
            RightPane = right,
            Document = box,
            StatusBar = status,
        };
        var host = new DialogHost { Content = frame };
        var window = ControlHarness.Show(host, mode);
        ShellTheme.Apply(Application.Current!, mode, ink, forceHighContrast: false);
        window.Width = 1400;
        window.Height = 800;
        ControlHarness.Settle(window);

        var session = new FieldCodeSession(definition);
        var fixture = new FieldCodeFixture(window, definition)
        {
            Ribbon = ribbon,
            Status = status,
            Search = search,
            Left = left,
            Right = right,
            Box = box,
            Host = host,
            Session = session,
        };
        ribbon.CommandInvoked += command => fixture.Invoked.Add(command.Id);
        fixture.Attachment = FieldCodeInput.Attach(window, session, ribbon, status);
        return fixture;
    }

    public IEnumerable<FieldCodeBadge> Badges => ControlHarness.Descendants(Window).OfType<FieldCodeBadge>();

    public List<string> BadgeTexts() => Badges.Select(b => b.Text!).ToList();

    public static FieldCodeBadge BadgeFor(Control target)
    {
        var layer = AdornerLayer.GetAdornerLayer(target)!;
        return layer.Children.OfType<FieldCodeBadge>().Single(b => ReferenceEquals(AdornerLayer.GetAdornedElement(b), target));
    }

    public Button TabButton(string letter) =>
        Ribbon.TabRow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is RibbonTabDefinition t && t.Letter == letter);

    /// <summary>Sends one key the way a real keyboard does: key down, the text it types, key up.</summary>
    public void Type(Key key, PhysicalKey physical, string text, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, physical, text);
        Window.KeyTextInput(text);
        Window.KeyRelease(key, modifiers, physical, text);
        ControlHarness.Settle(Window);
    }

    /// <summary>Types the letters and digits of a code (a-z, 0-9) as separate key presses.</summary>
    public void TypeCode(string code)
    {
        foreach (var c in code)
        {
            var (key, physical) = c switch
            {
                >= 'A' and <= 'Z' => ((Key)((int)Key.A + (c - 'A')), (PhysicalKey)((int)PhysicalKey.A + (c - 'A'))),
                _ => ((Key)((int)Key.D0 + (c - '0')), (PhysicalKey)((int)PhysicalKey.Digit0 + (c - '0'))),
            };
            Type(key, physical, c.ToString().ToLowerInvariant());
        }
    }

    public void AltDown(RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        Window.KeyPress(Key.LeftAlt, modifiers, PhysicalKey.AltLeft, null);
        ControlHarness.Settle(Window);
    }

    public void AltUp()
    {
        Window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        ControlHarness.Settle(Window);
    }

    /// <summary>A bare Alt: press and release.</summary>
    public void Alt()
    {
        AltDown();
        AltUp();
    }

    public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, physical, null);
        Window.KeyRelease(key, modifiers, physical, null);
        ControlHarness.Settle(Window);
    }

    /// <summary>The platform tells the window that it lost the focus (the headless backend has no real window switching).</summary>
    public void Deactivate()
    {
        Raise("Deactivated");
    }

    /// <summary>The window loses the focus (Alt+Tab) and gets it back.</summary>
    public void DeactivateAndReactivate()
    {
        Deactivate();
        Raise("Activated");
    }

    // IWindowBaseImpl.Activated/Deactivated are callbacks the window installs on its platform implementation; the test plays the platform.
    private void Raise(string callback)
    {
        var impl = Window.PlatformImpl!;
        var property = impl.GetType().GetProperty(callback) ?? throw new InvalidOperationException($"No {callback} on {impl.GetType()}.");
        ((Action)property.GetValue(impl)!)();
        ControlHarness.Settle(Window);
    }

    /// <summary>Records, for every key event, whether it was handled by the time it reached the end of its route.</summary>
    public List<(RoutedEventKind Kind, Key Key, bool Handled)> WatchKeys()
    {
        var log = new List<(RoutedEventKind, Key, bool)>();
        Window.AddHandler(InputElement.KeyDownEvent, (_, e) => log.Add((RoutedEventKind.Down, e.Key, e.Handled)), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Window.AddHandler(InputElement.KeyUpEvent, (_, e) => log.Add((RoutedEventKind.Up, e.Key, e.Handled)), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        return log;
    }

    internal enum RoutedEventKind
    {
        Down,
        Up,
    }
}
