using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Gallery.Sections;

/// <summary>
/// The dialog and the notification bars: the three bars (the crash recovery example for Info with its two actions, a warning and an
/// error with one action and the close button) and a button that opens a sample dialog in the <see cref="DialogHost"/> of the window.
/// <see cref="OpenDialog"/> opens the dialog for the <c>--open-dialog</c> screenshot.
/// </summary>
internal sealed partial class DialogsSection : UserControl
{
    private readonly Dialog _dialog;

    public DialogsSection()
    {
        InitializeComponent();
        Title.Text = GalleryStrings.SectionDialogs;

        Bars.Children.Add(InfoBar());
        Bars.Children.Add(WarningBar());
        Bars.Children.Add(ErrorBar());

        OpenButton.Content = GalleryStrings.DialogOpen;
        AutomationProperties.SetName(OpenButton, GalleryStrings.DialogOpen);
        OpenButton.Click += (_, _) => OpenDialog();

        _dialog = SampleDialog();
    }

    /// <summary>True while the sample dialog is open.</summary>
    public bool IsDialogOpen => Host?.ActiveDialog == _dialog;

    private DialogHost? Host => this.FindAncestorOfType<DialogHost>();

    /// <summary>Opens the sample dialog in the host of the window (nothing if there is no host or it is busy).</summary>
    public void OpenDialog()
    {
        if (Host is { IsOpen: false } host)
        {
            host.Show(_dialog);
        }
    }

    private static NotificationBar InfoBar()
    {
        // Crash recovery (section 5 of the design spec): "Compare" and "Close" are the two actions, so no extra close button.
        var bar = new NotificationBar { Severity = NotificationSeverity.Info, Message = GalleryStrings.NoteCrash, IsClosable = false };
        AutomationProperties.SetName(bar, ShellStrings.NotificationInfo);
        var close = Action(ShellStrings.Close, boxed: false);
        close.Click += (_, _) => bar.Close();
        bar.Actions.Add(Action(GalleryStrings.NoteCompare, boxed: true));
        bar.Actions.Add(close);
        return bar;
    }

    private static NotificationBar WarningBar()
    {
        var bar = new NotificationBar
        {
            Severity = NotificationSeverity.Warning,
            LeadIn = GalleryStrings.NoteFontLead,
            Message = GalleryStrings.NoteFontText,
        };
        AutomationProperties.SetName(bar, ShellStrings.NotificationWarning);
        bar.Actions.Add(Action(GalleryStrings.NoteShowFonts, boxed: true));
        return bar;
    }

    private static NotificationBar ErrorBar()
    {
        var bar = new NotificationBar
        {
            Severity = NotificationSeverity.Error,
            LeadIn = GalleryStrings.NoteSaveLead,
            Message = GalleryStrings.NoteSaveText,
        };
        AutomationProperties.SetName(bar, ShellStrings.NotificationError);
        bar.Actions.Add(Action(GalleryStrings.NoteSaveAs, boxed: true));
        return bar;
    }

    private static Button Action(string text, bool boxed)
    {
        var button = new Button { Content = text };
        if (boxed)
        {
            button.Classes.Add("boxed");
        }

        AutomationProperties.SetName(button, text);
        return button;
    }

    private Dialog SampleDialog()
    {
        var cancel = new Button { Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        AutomationProperties.SetName(cancel, ShellStrings.Cancel);
        cancel.Click += (_, _) => Host?.Close();
        var ok = new Button { Content = ShellStrings.Ok };
        ok.Classes.Add("primary");
        AutomationProperties.SetName(ok, ShellStrings.Ok);
        ok.Click += (_, _) => Host?.Close();

        var dialog = new Dialog
        {
            Title = GalleryStrings.SampleDialogTitle,
            Content = new TextBlock { Text = GalleryStrings.SampleDialogText, TextWrapping = TextWrapping.Wrap },
            Footer = CardFooter.Build(ok, null, cancel),
        };
        dialog.CancelRequested += (_, _) => Host?.Close();
        return dialog;
    }
}
