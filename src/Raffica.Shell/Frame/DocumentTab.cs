using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// An open document in the title bar. Two tabs are the same only if they are the same instance, so two untitled documents stay apart
/// (this holds although <see cref="IsDirty"/> can change). <see cref="IsDirty"/> says the document has unsaved changes; <see cref="Title"/> and
/// <see cref="Path"/> change when the document is saved under another name.
/// </summary>
public sealed record DocumentTab : INotifyPropertyChanged
{
    public DocumentTab(string title, string? path = null)
    {
        Title = title;
        Path = path;
    }

    /// <summary>Raised when <see cref="IsDirty"/>, <see cref="Title"/> or <see cref="Path"/> changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The name shown on the tab.</summary>
    public string Title
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }
    }

    /// <summary>The full path shown as the tooltip, or null.</summary>
    public string? Path
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path)));
            }
        }
    }

    /// <summary>Whether the document has unsaved changes; the tab then shows a mark before its close button.</summary>
    public bool IsDirty
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            }
        }
    }

    public bool Equals(DocumentTab? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>Shortens a text in the middle ("Quarterly-plan...v12.docx") so that both the start and the end of a file name stay readable.</summary>
public static class MiddleEllipsis
{
    private const string Ellipsis = "…";

    /// <summary>The text itself if <paramref name="measure"/> says it fits in <paramref name="maxWidth"/>, otherwise the longest start + ellipsis + end that does (just the ellipsis when nothing else fits).</summary>
    public static string Fit(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        if (text.Length == 0 || measure(text) <= maxWidth)
        {
            return text;
        }

        // The number of kept characters: the start takes the extra one of an odd count.
        string Candidate(int kept) => text[..((kept + 1) / 2)] + Ellipsis + text[^(kept / 2)..];

        var low = 0;
        var high = text.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (measure(Candidate(mid)) <= maxWidth)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Candidate(low);
    }

    /// <summary>The fewest characters of the name that stay visible beside an extension; with less room the extension goes first.</summary>
    public const int MinKeptWithExtension = 8;

    /// <summary>
    /// A file name that does not fit: the name is shortened in the middle before its extension (<c>Quarterly-pl…v12.pdf</c>), and the extension stays only while at least
    /// <see cref="MinKeptWithExtension"/> characters of the name stay beside it; with less room the extension is dropped and the name alone takes the room
    /// (<c>Quarte…L-v12</c>), because the start and the end of the name say more than ".pdf". The extension is the last dot with one to five letters or digits after it
    /// (not a leading dot, no space); a text without one is shortened in the middle as a whole.
    /// </summary>
    public static string FitFileName(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        if (text.Length == 0 || measure(text) <= maxWidth)
        {
            return text;
        }

        var dot = text.LastIndexOf('.');
        var extension = dot > 0 && IsExtension(text.AsSpan(dot + 1)) ? text[dot..] : string.Empty;
        var name = text[..(text.Length - extension.Length)];
        if (extension.Length > 0)
        {
            var shortened = Fit(name, maxWidth - measure(extension), measure);
            if (shortened.Length - 1 >= MinKeptWithExtension && measure(shortened + extension) <= maxWidth)
            {
                return shortened + extension;
            }
        }

        return Fit(name, maxWidth, measure);
    }

    private static bool IsExtension(ReadOnlySpan<char> text)
    {
        if (text.Length is 0 or > 5)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}


/// <summary>A single-line file name that is shortened before its extension when it does not fit (<see cref="MiddleEllipsis.FitFileName"/>). <see cref="DisplayText"/> is what is drawn.</summary>
public sealed class MiddleTrimTextBlock : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MiddleTrimTextBlock, string?>(nameof(Text));

    private string _display = string.Empty;

    static MiddleTrimTextBlock()
    {
        AffectsMeasure<MiddleTrimTextBlock>(TextProperty, TextElement.FontSizeProperty, TextElement.FontFamilyProperty, TextElement.FontWeightProperty);
        AffectsRender<MiddleTrimTextBlock>(TextProperty, TextElement.ForegroundProperty);
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>The text as drawn: <see cref="Text"/>, or its middle-shortened form after layout.</summary>
    public string DisplayText => _display;

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? string.Empty;
        var full = Format(text);
        _display = text;
        return new Size(Math.Min(full.WidthIncludingTrailingWhitespace, availableSize.Width), full.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var display = MiddleEllipsis.FitFileName(Text ?? string.Empty, finalSize.Width, s => Format(s).WidthIncludingTrailingWhitespace);
        if (display != _display)
        {
            _display = display;
            InvalidateVisual();
        }

        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var formatted = Format(_display);
        context.DrawText(formatted, new Point(0, Math.Max(0, (Bounds.Height - formatted.Height) / 2)));
    }

    private FormattedText Format(string text) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, TextElement.GetFontWeight(this)),
        TextElement.GetFontSize(this),
        TextElement.GetForeground(this));
}

/// <summary>
/// The view of one <see cref="DocumentTab"/> in <see cref="DocumentTabs"/>: 28 px high, square, at least 96 and at most 240 px wide
/// (the cap comes from the strip). The title is shortened in the middle, the close button is 24 x 24 and always there. The selected
/// tab is <c>surface.band</c> with a 3 px ink top edge and a 1 px rule on both sides; the others are transparent.
/// </summary>
public sealed class DocumentTabItem : Border
{
    private readonly MiddleTrimTextBlock _title = new() { Name = "PART_Title", VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Border _topEdge = new() { Name = "PART_TopEdge", IsHitTestVisible = false };
    private readonly Ellipse _dirtyMark = new() { Name = "PART_DirtyMark", Width = 6, Height = 6, Opacity = 0, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _close;
    private readonly Action<DocumentTab> _select;

    public DocumentTabItem(DocumentTab model, Action<DocumentTab> select, Action<DocumentTab> close)
    {
        Model = model;
        _select = select;
        Classes.Add("document-tab");
        Focusable = true;
        _title.Text = model.Title;
        ToolTip.SetTip(this, string.IsNullOrEmpty(model.Path) ? model.Title : model.Path);
        _dirtyMark.Classes.Add("document-tab-dirty");
        UpdateDirty();
        _close = new Button { Name = "PART_Close", Content = RibbonButton.CreateIcon("Raffica.Icon.X", 16) };
        _close.Classes.Add("document-tab-close");
        AutomationProperties.SetName(_close, ShellStrings.CloseDocument);
        ToolTip.SetTip(_close, ShellStrings.CloseDocument);
        _close.Click += (_, e) =>
        {
            close(model);
            e.Handled = true;
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(10, 0, 2, 0) };
        row.Children.Add(_title);
        Grid.SetColumn(_dirtyMark, 1);
        _dirtyMark.Margin = new Thickness(4, 0, 4, 0);
        row.Children.Add(_dirtyMark);
        Grid.SetColumn(_close, 2);
        row.Children.Add(_close);

        _topEdge.Classes.Add("document-tab-edge");
        var stack = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        stack.Children.Add(_topEdge);
        Grid.SetRow(row, 1);
        stack.Children.Add(row);
        Child = stack;

        PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsMiddleButtonPressed)
            {
                close(model);
                e.Handled = true;
            }
            else if (point.Properties.IsLeftButtonPressed)
            {
                select(model);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space && ReferenceEquals(e.Source, this))
            {
                select(model);
                e.Handled = true;
            }
        };
    }

    public DocumentTab Model { get; }

    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>Whether this is the selected tab (the pseudo class <c>:selected</c>).</summary>
    public bool IsSelected
    {
        get => PseudoClasses.Contains(":selected");
        set
        {
            if (IsSelected == value)
            {
                return;
            }

            PseudoClasses.Set(":selected", value);
            (ControlAutomationPeer.FromElement(this) as DocumentTabItemAutomationPeer)?.SelectionChanged(!value, value);
        }
    }

    // The model must not hold the view once the tab is closed: listen only while the view is in the tree.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Model.PropertyChanged += OnModelPropertyChanged;
        UpdateTitle();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Model.PropertyChanged -= OnModelPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTab.IsDirty))
        {
            UpdateDirty();
        }
        else if (e.PropertyName is nameof(DocumentTab.Title) or nameof(DocumentTab.Path))
        {
            UpdateTitle();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DocumentTabItemAutomationPeer(this);

    private void UpdateTitle()
    {
        _title.Text = Model.Title;
        ToolTip.SetTip(this, string.IsNullOrEmpty(Model.Path) ? Model.Title : Model.Path);
        UpdateDirty();
    }

    private void UpdateDirty()
    {
        // The mark is always laid out, only its opacity changes, so the tab keeps its width.
        _dirtyMark.Opacity = Model.IsDirty ? 1 : 0;
        AutomationProperties.SetName(this, Model.IsDirty ? $"{Model.Title}, {ShellStrings.TabUnsavedChanges}" : Model.Title);
    }

    /// <summary>The tab as an automation tab item with the selection item pattern, so assistive technology hears which tab is selected.</summary>
    private sealed class DocumentTabItemAutomationPeer(DocumentTabItem owner) : ControlAutomationPeer(owner), ISelectionItemProvider
    {
        public bool IsSelected => owner.IsSelected;

        public ISelectionProvider? SelectionContainer => null;

        public void Select() => owner._select(owner.Model);

        public void AddToSelection() => Select();

        public void RemoveFromSelection()
        {
            // A tab strip always has one selected tab: it cannot be deselected.
        }

        public void SelectionChanged(bool oldValue, bool newValue) =>
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, oldValue, newValue);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

        // A tab that lies outside the strip's window (scrolled away) is offscreen; one that is partly in view is on screen, because the user sees it and can click it.
        protected override bool IsOffscreenCore()
        {
            if (base.IsOffscreenCore())
            {
                return true;
            }

            return owner.FindAncestorOfType<ScrollViewer>() is { } scroll
                && owner.TranslatePoint(default, scroll) is { } origin
                && !new Rect(origin, owner.Bounds.Size).Intersects(new Rect(scroll.Bounds.Size));
        }
    }
}
