using Avalonia.Automation;
using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Borders of the selected cells: a preset (all borders, outside borders, box, no borders) or single sides (top, bottom, left, right and the lines inside, set or turned off), with the style (single, double, dashed,
/// dotted), the width (a quarter of a point to six points) and the colour (automatic or one of the fixed colours of the Home tab). The presets mean what the Table tab's picker says: outside borders draw the outline of
/// the range and leave the lines inside alone, box draws the outline and turns the lines inside off. Nothing is read from the document: the card hands one <see cref="BorderSpec"/> back and the window writes it as one
/// history step. Enter confirms, Esc and Cancel change nothing; a choice of single sides with no side shows a hint.
/// </summary>
internal static class TableBordersCard
{
    private static readonly string[] s_presets = ["all", "outside", "box", "none", "sides"];
    private static readonly TableBorderStyle[] s_styles = [TableBorderStyle.Solid, TableBorderStyle.DoubleLine, TableBorderStyle.Dashed, TableBorderStyle.Dotted];

    private static readonly (BorderSides Side, string Key, string Name)[] s_sides =
    [
        (BorderSides.Top, "TableBordersTop", "PART_Top"),
        (BorderSides.Bottom, "TableBordersBottom", "PART_Bottom"),
        (BorderSides.Left, "TableBordersLeft", "PART_Left"),
        (BorderSides.Right, "TableBordersRight", "PART_Right"),
        (BorderSides.InsideHorizontal, "TableBordersInsideHorizontal", "PART_InsideH"),
        (BorderSides.InsideVertical, "TableBordersInsideVertical", "PART_InsideV"),
    ];

    /// <param name="done">The borders chosen, or null when the user cancelled.</param>
    public static Dialog Create(Action<BorderSpec?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var finish = CardParts.Once(done);

        var preset = CardParts.Select("PART_Preset", WriteStrings.TbPreset, [WriteStrings.TableBordersAll, WriteStrings.TableBordersOutside, WriteStrings.TableBordersBox, WriteStrings.TableBordersNone, WriteStrings.TbPresetSides]);
        preset.SelectedIndex = 0;
        var sides = s_sides.Select(s =>
        {
            var box = new CheckBox { Name = s.Name, Content = WriteStrings.Get(s.Key), IsEnabled = false };
            AutomationProperties.SetName(box, WriteStrings.Get(s.Key));
            return (s.Side, Box: box);
        }).ToList();
        var turnOff = new CheckBox { Name = "PART_TurnOff", Content = WriteStrings.TbTurnOff, IsEnabled = false };
        var style = CardParts.Select("PART_Style", WriteStrings.TbStyle, [WriteStrings.TbStyleSolid, WriteStrings.TbStyleDouble, WriteStrings.TbStyleDashed, WriteStrings.TbStyleDotted]);
        style.SelectedIndex = 0;
        var width = CardParts.Select("PART_Width", WriteStrings.TbWidth, TablePayloads.BorderWidths.Select(w => WriteStrings.Fill(WriteStrings.TbWidthFormat, (w / 8.0).ToString("0.##", WriteStrings.Culture))));
        width.SelectedIndex = TablePayloads.BorderWidths.ToList().IndexOf(4);
        var color = CardParts.Select("PART_Color", WriteStrings.TbColor, [WriteStrings.TbColorAuto, .. ColorChoices.TextColors.Select(c => WriteStrings.Get(c.NameKey))]);
        color.SelectedIndex = 0;
        var hint = CardParts.Hint();

        void Sync()
        {
            var custom = preset.SelectedIndex == s_presets.Length - 1;
            foreach (var (_, box) in sides)
            {
                box.IsEnabled = custom;
            }

            turnOff.IsEnabled = custom;
            var line = preset.SelectedIndex != 3 && !(custom && turnOff.IsChecked == true);
            style.IsEnabled = line;
            width.IsEnabled = line;
            color.IsEnabled = line;
        }

        preset.SelectionChanged += (_, _) => Sync();
        turnOff.IsCheckedChanged += (_, _) => Sync();
        Sync();

        var body = new StackPanel { Spacing = 10, MinWidth = 340 };
        body.Children.Add(new TextBlock { Text = WriteStrings.TbNote, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "secondary" } });
        body.Children.Add(CardParts.Field(WriteStrings.TbPreset, preset));
        var sideList = new StackPanel { Name = "PART_Sides", Spacing = 4 };
        sideList.Children.Add(new TextBlock { Text = WriteStrings.TbSides });
        foreach (var (_, box) in sides)
        {
            sideList.Children.Add(box);
        }

        sideList.Children.Add(turnOff);
        body.Children.Add(sideList);
        body.Children.Add(CardParts.Field(WriteStrings.TbStyle, style));
        body.Children.Add(CardParts.Field(WriteStrings.TbWidth, width));
        body.Children.Add(CardParts.Field(WriteStrings.TbColor, color));
        body.Children.Add(hint);

        var (ok, cancel) = CardParts.Buttons(WriteStrings.TbOk, ShellStrings.Cancel);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MaxHeight = 560, Content = body };
        var dialog = new Dialog { Title = WriteStrings.TbTitle, Content = scroll, Footer = CardFooter.Build(ok, null, cancel) };

        void Confirm()
        {
            var chosen = sides.Where(s => s.Box.IsChecked == true).Aggregate(BorderSides.None, (all, s) => all | s.Side);
            var spec = Read(preset.SelectedIndex, chosen, turnOff.IsChecked == true, style.SelectedIndex, width.SelectedIndex, color.SelectedIndex);
            if (spec is null)
            {
                hint.Text = WriteStrings.TbNoSide;
                hint.IsVisible = true;
                return;
            }

            finish(spec);
        }

        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        return dialog;
    }

    /// <summary>
    /// The spec for the choices of the card; null for single sides with no side chosen. <paramref name="preset"/> indexes the presets (all, outside, box, none, single sides), <paramref name="style"/>, <paramref name="width"/>
    /// and <paramref name="color"/> the lists of the card (the colour 0 is automatic).
    /// </summary>
    internal static BorderSpec? Read(int preset, BorderSides sides, bool turnOff, int style, int width, int color)
    {
        var line = s_styles[Math.Clamp(style, 0, s_styles.Length - 1)];
        var eighths = TablePayloads.BorderWidths[Math.Clamp(width, 0, TablePayloads.BorderWidths.Count - 1)];
        uint? argb = color <= 0 ? null : ColorChoices.TextColors[Math.Min(color, ColorChoices.TextColors.Count) - 1].Argb;
        return preset switch
        {
            0 => BorderSpec.All(line, eighths, argb),
            1 => BorderSpec.Outside(line, eighths, argb),
            2 => BorderSpec.Box(line, eighths, argb),
            3 => BorderSpec.None(),
            _ when sides == BorderSides.None => null,
            _ => turnOff ? BorderSpec.Off(sides) : BorderSpec.Sides(sides, line, eighths, argb),
        };
    }
}
