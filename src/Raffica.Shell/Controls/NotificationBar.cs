using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Controls;

/// <summary>The three kinds of notification. None of them is red: the icon tells them apart.</summary>
public enum NotificationSeverity
{
    /// <summary>A circle with an i, in the secondary text colour.</summary>
    Info,

    /// <summary>A triangle in the warning icon colour (the icon only, never a fill or text).</summary>
    Warning,

    /// <summary>An octagon in the primary text colour.</summary>
    Error,
}

/// <summary>
/// The neutral bar under the ribbon (section 6 of the design spec): an icon, one sentence with an optional bold lead-in, at most
/// two actions and a close button. 40 px high. The sentence comes from <see cref="LeadIn"/> and <see cref="Message"/>; the
/// inherited <c>Content</c> is not shown.
/// </summary>
public class NotificationBar : ContentControl
{
    private const int MaxActions = 2;

    public static readonly StyledProperty<NotificationSeverity> SeverityProperty =
        AvaloniaProperty.Register<NotificationBar, NotificationSeverity>(nameof(Severity));

    /// <summary>The bold opening phrase, for example "Font missing.". Null or empty: no lead-in.</summary>
    public static readonly StyledProperty<string?> LeadInProperty =
        AvaloniaProperty.Register<NotificationBar, string?>(nameof(LeadIn));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<NotificationBar, string?>(nameof(Message));

    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<NotificationBar, bool>(nameof(IsClosable), defaultValue: true);

    private TextBlock? _text;
    private Panel? _actionsPanel;
    private Button? _close;
    private StrokeIcon? _icon;
    private bool _closed;

    public NotificationBar()
    {
        Actions.CollectionChanged += OnActionsChanged;
        UpdateSeverityClasses();
    }

    /// <summary>Raised once, when the bar has been closed.</summary>
    public event EventHandler? Closed;

    public NotificationSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string? LeadIn
    {
        get => GetValue(LeadInProperty);
        set => SetValue(LeadInProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsClosable
    {
        get => GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>The action buttons, at most two (usually a boxed button; add the class <c>boxed</c> to it).</summary>
    public AvaloniaList<Button> Actions { get; } = [];

    /// <summary>Closes the bar: it leaves the layout and <see cref="Closed"/> is raised (once).</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        SetCurrentValue(IsVisibleProperty, false);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_close is not null)
        {
            _close.Click -= OnCloseClick;
        }

        _text = e.NameScope.Find<TextBlock>("PART_Text");
        _actionsPanel = e.NameScope.Find<Panel>("PART_Actions");
        _close = e.NameScope.Find<Button>("PART_Close");
        _icon = e.NameScope.Find<StrokeIcon>("PART_Icon");

        if (_close is not null)
        {
            AutomationProperties.SetName(_close, ShellStrings.Close);
            _close.Click += OnCloseClick;
        }

        RebuildText();
        RebuildActions();
        UpdateIconName();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeverityProperty)
        {
            UpdateSeverityClasses();
            UpdateIconName();
        }
        else if (change.Property == LeadInProperty || change.Property == MessageProperty)
        {
            RebuildText();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Actions.Count > MaxActions)
        {
            // Take the surplus action back out so the bar stays valid, then report the misuse.
            while (Actions.Count > MaxActions)
            {
                Actions.RemoveAt(Actions.Count - 1);
            }

            throw new InvalidOperationException($"A notification bar holds at most {MaxActions} actions.");
        }

        RebuildActions();
    }

    private void RebuildActions()
    {
        if (_actionsPanel is null)
        {
            return;
        }

        _actionsPanel.Children.Clear();
        _actionsPanel.Children.AddRange(Actions);
    }

    private void RebuildText()
    {
        if (_text?.Inlines is not { } inlines)
        {
            return;
        }

        inlines.Clear();
        if (!string.IsNullOrEmpty(LeadIn))
        {
            inlines.Add(new Run(LeadIn + " ") { FontWeight = FontWeight.SemiBold });
        }

        inlines.Add(new Run(Message));
    }

    private void UpdateSeverityClasses()
    {
        PseudoClasses.Set(":info", Severity == NotificationSeverity.Info);
        PseudoClasses.Set(":warning", Severity == NotificationSeverity.Warning);
        PseudoClasses.Set(":error", Severity == NotificationSeverity.Error);
    }

    private void UpdateIconName()
    {
        if (_icon is null)
        {
            return;
        }

        AutomationProperties.SetName(_icon, Severity switch
        {
            NotificationSeverity.Warning => ShellStrings.NotificationWarning,
            NotificationSeverity.Error => ShellStrings.NotificationError,
            _ => ShellStrings.NotificationInfo,
        });
    }
}
