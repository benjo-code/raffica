using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class FieldCodeBadgeTests
{
    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, AppInk.Write)]
    [InlineData(ThemeMode.Light, AppInk.Sheets)]
    [InlineData(ThemeMode.Light, AppInk.Slides)]
    [InlineData(ThemeMode.Light, AppInk.Pdf)]
    [InlineData(ThemeMode.Dark, AppInk.Write)]
    [InlineData(ThemeMode.Dark, AppInk.Sheets)]
    [InlineData(ThemeMode.Dark, AppInk.Slides)]
    [InlineData(ThemeMode.Dark, AppInk.Pdf)]
    public void BadgeIs18HighWithInkFillAndOnInkText(ThemeMode mode, AppInk ink)
    {
        var f = FieldCodeFixture.Create(mode, ink);
        f.Alt();

        var badge = FieldCodeFixture.BadgeFor(f.TabButton("H"));
        var fill = ControlHarness.Part<Border>(badge, "PART_Fill");
        var text = ControlHarness.Part<TextBlock>(badge, "PART_Text");

        Assert.Equal(18, badge.Bounds.Height, 0.01);
        Assert.True(badge.Bounds.Width >= 18);
        Assert.Equal(new CornerRadius(3), fill.CornerRadius);
        Assert.Equal(new Thickness(4, 0), fill.Padding);
        ControlHarness.AssertToken("Raffica.Brush.Ink", fill.Background, "badge fill");
        ControlHarness.AssertToken("Raffica.Brush.Ink." + ink, fill.Background, "badge fill follows the app ink");
        ControlHarness.AssertToken("Raffica.Brush.Text.OnInk", text.Foreground, "badge text");
        Assert.Equal(12, text.FontSize);
        Assert.Equal(FontWeight.SemiBold, text.FontWeight);
        Assert.Equal("Barlow Semi Condensed", text.FontFamily.FamilyNames[0]);
        Assert.False(badge.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void ALongCodeWidensTheBadgeBeyondTheMinimum()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        f.TypeCode("H");

        var narrow = FieldCodeFixture.BadgeFor(f.TabButton("H"));
        var two = Assert.Single(f.Badges, b => b.Text == "AL");
        Assert.Equal(18, narrow.Bounds.Width, 0.01);
        Assert.True(two.Bounds.Width > 18);
    }

    private static void AssertBottomRight(FieldCodeFixture f, Control target)
    {
        var badge = FieldCodeFixture.BadgeFor(target);
        var targetOrigin = target.TranslatePoint(default, f.Window)!.Value;
        var badgeOrigin = badge.TranslatePoint(default, f.Window)!.Value;
        Assert.Equal(targetOrigin.X + target.Bounds.Width + 4, badgeOrigin.X + badge.Bounds.Width, 0.51);
        Assert.Equal(targetOrigin.Y + target.Bounds.Height + 6, badgeOrigin.Y + badge.Bounds.Height, 0.51);
    }

    [AvaloniaFact]
    public void BadgesAreAtTheBottomRightOfTheirControl()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();

        AssertBottomRight(f, f.TabButton("H"));
        AssertBottomRight(f, f.TabButton("W"));
        AssertBottomRight(f, f.Search);
        AssertBottomRight(f, f.Left);
        AssertBottomRight(f, f.Right);

        f.TypeCode("H");
        foreach (var button in RibbonViewTests.Buttons(f.Ribbon))
        {
            AssertBottomRight(f, button);
        }

        var group = (TextBlock)Assert.Single(ControlHarness.Descendants(f.Ribbon.Band).OfType<TextBlock>(), t => t.Text == "Clipboard");
        AssertBottomRight(f, group);
    }

    [AvaloniaFact]
    public void BadgesFollowTheirControlWhenTheLayoutChanges()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        f.Window.Width = 1000;
        ControlHarness.Settle(f.Window);

        AssertBottomRight(f, f.Right);
    }

    [AvaloniaFact]
    public void PaneAndSearchBadgesShowOnlyWhileTheModeIsOn()
    {
        var f = FieldCodeFixture.Create();
        Assert.Empty(f.Badges);

        f.Alt();
        Assert.Equal("Q", FieldCodeFixture.BadgeFor(f.Search).Text);
        Assert.Equal("6", FieldCodeFixture.BadgeFor(f.Left).Text);
        Assert.Equal("7", FieldCodeFixture.BadgeFor(f.Right).Text);

        f.Right.IsOpen = false;
        f.Press(Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
        f.Alt();
        Assert.DoesNotContain("7", f.BadgeTexts());
        Assert.Contains("6", f.BadgeTexts());
    }

    [AvaloniaFact]
    public void NoBadgesAtRest()
    {
        var f = FieldCodeFixture.Create();
        Assert.Empty(f.Badges);
        Assert.Empty(AdornerLayer.GetAdornerLayer(f.Search)!.Children.OfType<FieldCodeBadge>());

        f.Alt();
        Assert.NotEmpty(f.Badges);
        f.Alt();
        Assert.Empty(f.Badges);
        Assert.Empty(AdornerLayer.GetAdornerLayer(f.Search)!.Children.OfType<FieldCodeBadge>());

        // Also with a ribbon that was never wired to a session.
        var plain = FieldCodeFixture.Create();
        plain.Attachment.Dispose();
        Assert.Empty(plain.Badges);
    }

    private static RibbonDefinition Translated(RibbonDefinition source)
    {
        // Names that share nothing with the English ones: the codes must not depend on them.
        static string Mangle(string s) => new(s.Reverse().ToArray());
        CommandDefinition Command(CommandDefinition c) => c with { Name = Mangle(c.Name), Items = c.Items?.Select(Command).ToList() };
        var definition = new RibbonDefinition(source.Tabs.Select(t => t with
        {
            Name = Mangle(t.Name),
            Groups = t.Groups.Select(g => g with { Label = Mangle(g.Label), Commands = g.Commands.Select(Command).ToList() }).ToList(),
        }).ToList());
        definition.Validate();
        return definition;
    }

    private static List<string> CodesIn(CultureInfo culture, RibbonDefinition definition)
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = culture;
            var f = FieldCodeFixture.Create(definition: definition);
            f.Alt();
            var texts = f.BadgeTexts();
            f.TypeCode(definition.Tabs[0].Letter);
            texts.AddRange(f.BadgeTexts());
            return texts;
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void SameCodesInGermanAndEnglish()
    {
        var english = CodesIn(CultureInfo.GetCultureInfo("en"), SampleRibbon.Create());
        var german = CodesIn(CultureInfo.GetCultureInfo("de"), Translated(SampleRibbon.Create()));

        Assert.NotEmpty(english);
        Assert.Contains("AL", english);
        Assert.Equal(english.Order().ToList(), german.Order().ToList());
    }

    [AvaloniaTheory]
    [InlineData("en", "Type a field code · Esc goes back")]
    [InlineData("de", "Feldcode eingeben · Esc geht zurück")]
    public void HintIsLocalized(string culture, string expected)
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo(culture);
            var f = FieldCodeFixture.Create();
            f.Alt();
            Assert.Equal(expected, f.Status.Hint);
            Assert.Equal(expected, ShellStrings.FieldCodeHint);
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }
}
