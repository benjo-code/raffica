using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Picture properties: the width and height of the picture (kept in proportion while "keep the proportions" is on), a switch that puts it back to its original size, its description for screen readers and how the text wraps.
/// The values shown are read from the picture when the card opens; only the parts the user changed come back, as one <see cref="PictureProperties"/> that the window writes as one history step. Lengths are typed in
/// centimetres or inches by the region rule of the page presets. A turned picture offers no size and a picture in a table cell no floating wrap (the editor refuses both). A wrong entry shows a hint and keeps the card open;
/// nothing changed closes the card with no answer; Enter confirms, Esc and Cancel change nothing.
/// </summary>
internal static class PicturePropertiesCard
{
    /// <summary>The smallest side the card takes, in twips (one point).</summary>
    internal const int MinTwips = 20;

    /// <summary>The largest side the card takes, in twips (Word's 1584 points).</summary>
    internal const int MaxTwips = 31_680;

    private const long EmuPerTwip = 635;

    /// <param name="context">The facts of the picture and whether lengths are in inches.</param>
    /// <param name="done">What was changed, or null when the user cancelled or changed nothing.</param>
    public static Dialog Create(PicturePropertiesContext context, Action<PictureProperties?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);
        var finish = CardParts.Once(done);
        var unit = context.Inches ? WriteStrings.PsUnitInch : WriteStrings.PsUnitCm;

        var width = new PageSetupCard.Length("PART_Width", WriteStrings.PpWidthFormat, unit, context.Inches);
        var height = new PageSetupCard.Length("PART_Height", WriteStrings.PpHeightFormat, unit, context.Inches);
        width.Set(context.WidthTwips);
        height.Set(context.HeightTwips);
        var ratio = new CheckBox { Name = "PART_LockRatio", Content = WriteStrings.PpLockRatio, IsChecked = true };
        var reset = new CheckBox { Name = "PART_Reset", Content = WriteStrings.PpReset };
        var alt = CardParts.Text("PART_Alt", WriteStrings.PpAlt, context.AltText, 320);
        var wraps = PicturePayloads.Wraps.Select(PicturePayloads.WrapLabel).ToArray();
        var wrap = CardParts.Select("PART_Wrap", WriteStrings.PpWrap, wraps);
        wrap.SelectedIndex = Math.Max(0, PicturePayloads.Wraps.ToList().IndexOf(context.WrapId));
        wrap.IsEnabled = context.CanFloat;
        var hint = CardParts.Hint();

        // The size follows the other side while the proportions are kept; the sides of a turned picture are not offered.
        var sizeOn = !context.Rotated;
        width.Box.IsEnabled = height.Box.IsEnabled = ratio.IsEnabled = reset.IsEnabled = sizeOn;
        reset.IsCheckedChanged += (_, _) => width.Box.IsEnabled = height.Box.IsEnabled = ratio.IsEnabled = sizeOn && reset.IsChecked != true;
        // The proportions are the picture's own (width over height as the card opened). The side the user edited LAST decides: the other follows it when the box is left, when the lock is switched on and, as a last
        // word, when OK is pressed (Enter confirms without leaving the box, so a blur alone would let a distorted picture through). A side that was never edited never drives the other.
        var lastEdited = (PageSetupCard.Length?)null;
        var applying = false;
        // The property changes at once (the TextChanged event of a box is raised later), so a value the card sets itself is told from a typed one by the flag.
        width.Box.PropertyChanged += (_, e) => EditedBy(e, width);
        height.Box.PropertyChanged += (_, e) => EditedBy(e, height);
        width.Box.LostFocus += (_, _) => Follow();
        height.Box.LostFocus += (_, _) => Follow();
        ratio.IsCheckedChanged += (_, _) => Follow();

        void EditedBy(Avalonia.AvaloniaPropertyChangedEventArgs e, PageSetupCard.Length field)
        {
            if (e.Property == TextBox.TextProperty && !applying)
            {
                lastEdited = field;
            }
        }

        // The other side of the one edited last, or null when nothing was edited, the lock is off or the edited text is no valid size.
        (PageSetupCard.Length Other, int Twips)? Derived()
        {
            if (!sizeOn || ratio.IsChecked != true || lastEdited is null || !lastEdited.TryRead(out var twips) || twips < MinTwips || twips > MaxTwips)
            {
                return null;
            }

            var fromWidth = ReferenceEquals(lastEdited, width);
            var factor = fromWidth ? context.HeightTwips / (double)Math.Max(1, context.WidthTwips) : context.WidthTwips / (double)Math.Max(1, context.HeightTwips);
            return (fromWidth ? height : width, (int)Math.Clamp(Math.Round(twips * factor), 1, int.MaxValue));
        }

        void Follow()
        {
            if (Derived() is { } follow)
            {
                applying = true;
                follow.Other.Set(follow.Twips);
                applying = false;
            }
        }

        var body = new StackPanel { Spacing = 10, MinWidth = 380 };
        body.Children.Add(width.Panel);
        body.Children.Add(height.Panel);
        body.Children.Add(ratio);
        body.Children.Add(reset);
        body.Children.Add(CardParts.Field(WriteStrings.PpAlt, alt));
        body.Children.Add(CardParts.Field(WriteStrings.PpWrap, wrap));
        body.Children.Add(hint);

        var (ok, cancel) = CardParts.Buttons(WriteStrings.PpOk, ShellStrings.Cancel);
        var dialog = new Dialog { Title = WriteStrings.PpTitle, Content = body, Footer = CardFooter.Build(ok, null, cancel) };

        void Confirm()
        {
            var (properties, problem) = Read();
            if (problem is { } wrong)
            {
                hint.Text = wrong.Text;
                hint.IsVisible = true;
                wrong.Focus.Focus();
                wrong.Focus.SelectAll();
                return;
            }

            finish(properties);
        }

        (PictureProperties? Properties, (string Text, TextBox Focus)? Problem) Read()
        {
            Follow();
            long? newWidth = null;
            long? newHeight = null;
            var resetSize = sizeOn && reset.IsChecked == true;
            if (sizeOn && !resetSize)
            {
                if (!width.TryRead(out var w))
                {
                    return (null, (WriteStrings.Fill(WriteStrings.PpInvalidValueFormat, width.Label), width.Box));
                }

                if (!height.TryRead(out var h))
                {
                    return (null, (WriteStrings.Fill(WriteStrings.PpInvalidValueFormat, height.Label), height.Box));
                }

                if (w is < MinTwips or > MaxTwips)
                {
                    return (null, (WriteStrings.Fill(WriteStrings.PpOutOfRangeFormat, width.Label), width.Box));
                }

                if (h is < MinTwips or > MaxTwips)
                {
                    return (null, (WriteStrings.Fill(WriteStrings.PpOutOfRangeFormat, height.Label), height.Box));
                }

                if (w != context.WidthTwips || h != context.HeightTwips)
                {
                    (newWidth, newHeight) = (w * EmuPerTwip, h * EmuPerTwip);
                }
            }

            var text = alt.Text ?? string.Empty;
            if (text.Length > PictureRules.MaxAltTextLength)
            {
                return (null, (WriteStrings.PiAltTooLong, alt));
            }

            var chosen = PicturePayloads.Wraps[Math.Clamp(wrap.SelectedIndex, 0, PicturePayloads.Wraps.Count - 1)];
            var properties = new PictureProperties(
                newWidth,
                newHeight,
                resetSize,
                text != context.AltText ? text : null,
                context.CanFloat && chosen != context.WrapId && PicturePayloads.TryWrap(chosen, out var kind) ? kind : null);
            return (properties.IsEmpty ? null : properties, null);
        }

        CardParts.ConfirmOnEnter(Confirm, [width.Box, height.Box, alt]);
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        return dialog;
    }
}
