using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Text;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The confirmation before an address outside the document is opened. It shows the address in full and exactly as it will be opened, and,
/// when Core flags the address as deceptive (user information before the host, an internationalised host name), a warning that says so.
/// Cancel has the focus, Esc and the close button cancel. Open only works after <see cref="ArmingDelayMilliseconds"/>: it starts disabled,
/// and a click or a key that reaches it earlier (a double click on the link that landed on the card) does nothing, so one click cannot
/// both show the card and confirm it. The card opens nothing itself; it calls the actions it is given.
/// </summary>
internal static class LinkCard
{
    /// <summary>How long Open stays disabled after the card was made.</summary>
    public const int ArmingDelayMilliseconds = 400;

    private const double AddressMaxHeight = 120;

    /// <param name="link">The validated address.</param>
    /// <param name="open">Called once when the user confirms, after the arming delay.</param>
    /// <param name="cancel">Called when the user cancels (Cancel, Esc, the close button).</param>
    /// <param name="time">The clock of the delay; null takes the system clock.</param>
    public static Dialog Create(ExternalLink link, Action open, Action cancel, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(cancel);
        time ??= TimeProvider.System;
        var started = time.GetTimestamp();
        var delay = TimeSpan.FromMilliseconds(ArmingDelayMilliseconds);
        bool IsArmed() => time.GetElapsedTime(started) >= delay;

        var message = new TextBlock { Text = PdfStrings.LinkMessage, TextWrapping = TextWrapping.Wrap };
        var address = new TextBlock { Name = "PART_Address", Text = link.Url, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(address, PdfStrings.LinkAddressName + ": " + link.Url);
        var addressBox = new Border
        {
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new ScrollViewer { MaxHeight = AddressMaxHeight, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = address },
        };
        addressBox.Bind(Border.BackgroundProperty, addressBox.GetResourceObservable("Raffica.Brush.Surface.Band"));
        addressBox.Bind(Border.BorderBrushProperty, addressBox.GetResourceObservable("Raffica.Brush.Border.Control"));

        var body = new StackPanel { Spacing = 12, Children = { message, addressBox } };
        if (WarningText(link) is { } warning)
        {
            body.Children.Add(CreateWarning(warning));
        }

        var cancelButton = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancelButton.Classes.Add("boxed");
        var openButton = new Button { Name = "PART_Open", Content = PdfStrings.LinkOpen, IsEnabled = false };
        openButton.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = PdfStrings.LinkTitle,
            Content = body,
            Footer = CardFooter.Build(openButton, null, cancelButton),
        };

        var finished = false;
        void Finish(Action action)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            action();
        }

        cancelButton.Click += (_, _) => Finish(cancel);
        dialog.CancelRequested += (_, _) => Finish(cancel);

        // The click is checked against the clock as well as the button being disabled: a click that is sent to the button some other way
        // (a script, an assistive technology) is no more able to confirm before the delay than the pointer is.
        openButton.Click += (_, _) =>
        {
            if (IsArmed())
            {
                Finish(open);
            }
        };

        // Enables the button once the delay is over; a timer that fires early asks again for the rest.
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            if (IsArmed())
            {
                timer.Stop();
                openButton.IsEnabled = true;
            }
            else
            {
                timer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, ArmingDelayMilliseconds - time.GetElapsedTime(started).TotalMilliseconds));
            }
        };
        dialog.AttachedToVisualTree += (_, _) => timer.Start();
        dialog.DetachedFromVisualTree += (_, _) => timer.Stop();
        return dialog;
    }

    private static string? WarningText(ExternalLink link) => link.Warning switch
    {
        ExternalLinkWarning.UserInfo => PdfStrings.LinkWarningUserInfo,
        ExternalLinkWarning.Punycode => PdfStrings.Fill(PdfStrings.LinkWarningPunycodeFormat, link.UnicodeHost),
        _ => null,
    };

    private static StackPanel CreateWarning(string text)
    {
        var icon = new StrokeIcon { Size = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable("Raffica.Icon.TriangleAlert", value => value as Geometry));
        icon.Bind(StrokeIcon.StrokeProperty, icon.GetResourceObservable("Raffica.Brush.Warning.Icon"));
        var label = new TextBlock { Name = "PART_Warning", Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, MaxWidth = 440 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, label } };
        return row;
    }
}
