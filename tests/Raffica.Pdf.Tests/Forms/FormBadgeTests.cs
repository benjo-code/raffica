using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Raffica.Core.Forms;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormBadgeTests
{
    private static BadgeHost HostOf(FormEditFixture f) => f.Window.BadgeHostOf(f.Tab)!;

    private static async Task<FormBadge> BadgeAsync(FormEditFixture f)
    {
        await ViewFixture.WaitAsync(() => HostOf(f).Forms is not null && f.Forms.Info is not null && f.Forms.RequiredKnown);
        ViewFixture.Pump();
        return HostOf(f).Forms!;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Run(FormEditFixture f, string id) =>
        f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).Run!.Invoke();

    [AvaloniaTheory]
    [InlineData("forms-calc.pdf", new[] { "JavaScript" }, new[] { "XFA", "signature", "Buttons" })]
    [InlineData("forms-xfa.pdf", new[] { "XFA" }, new[] { "JavaScript", "signature", "Buttons" })]
    [InlineData("forms-signed.pdf", new[] { "signature", "Buttons" }, new[] { "JavaScript", "XFA" })]
    [InlineData("forms-basic.pdf", new string[0], new[] { "JavaScript", "XFA", "signature", "Buttons" })]
    public async Task OneLineNamesOnlyWhatApplies(string file, string[] named, string[] notNamed)
    {
        using var f = await FormEditFixture.OpenAsync(file);
        var badge = await BadgeAsync(f);
        if (named.Length == 0)
        {
            Assert.False(badge.NoticeVisible);
            Assert.Equal(string.Empty, badge.NoticeText);
            return;
        }

        Assert.True(badge.NoticeVisible);
        Assert.All(named, n => Assert.Contains(n, badge.NoticeText, StringComparison.Ordinal));
        Assert.All(notNamed, n => Assert.DoesNotContain(n, badge.NoticeText, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task NoticeIsShownOnceAndDismissible()
    {
        using var f = await FormEditFixture.OpenAsync("forms-calc.pdf");
        var badge = await BadgeAsync(f);
        Assert.True(badge.NoticeVisible);
        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(badge.DismissButton)));

        Click(badge.DismissButton);
        Assert.False(badge.NoticeVisible);

        // Later changes of the session (a page read, the required list) never bring it back.
        await f.Forms.RefreshRequiredAsync();
        await f.Forms.LoadPageAsync(0);
        ViewFixture.Pump();
        Assert.False(badge.NoticeVisible);
    }

    [AvaloniaFact]
    public async Task CounterFollowsTheEmptyRequiredFields()
    {
        using var f = await FormEditFixture.OpenAsync();
        var badge = await BadgeAsync(f);
        var count = f.Forms.EmptyRequired.Count;
        Assert.True(count > 0);
        Assert.True(badge.CounterVisible);
        Assert.Equal(CounterText(count), badge.CounterText);

        var result = await f.Forms.SetValueAsync(f.Field("Pflicht"), FormFieldState.OfText("ausgefuellt"));
        Assert.IsType<FormEditResult.Applied>(result);
        await ViewFixture.WaitAsync(() => f.Forms.EmptyRequired.Count == count - 1);
        ViewFixture.Pump();
        if (count == 1)
        {
            Assert.False(badge.CounterVisible);
        }
        else
        {
            Assert.Equal(CounterText(count - 1), badge.CounterText);
        }
    }

    private static string CounterText(int count) =>
        count == 1 ? PdfStrings.FormRequiredOne : PdfStrings.FormRequiredManyFormat.Replace("{0}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    [AvaloniaFact]
    public async Task CounterButtonGoesToTheNextRequiredField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var badge = await BadgeAsync(f);
        var required = f.Field("Pflicht");
        Assert.True(badge.CounterVisible);

        Click(badge.CounterButton);
        await ViewFixture.WaitAsync(() => f.Parts.Focus.Current == required.Key);
    }

    [AvaloniaFact]
    public async Task HighlightSwitchTogglesTheSessionAndRendersTheField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var badge = await BadgeAsync(f);
        var toggle = badge.HighlightToggle;
        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(toggle)));
        await ViewFixture.WaitAsync(() => f.Forms.Highlight);
        Assert.True(toggle.IsChecked);
        var on = await FieldPixelsAsync(f);

        // Off by a real click on the switch (it lies over the page: the click is the badge's, not the page's).
        var centre = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), f.Window)!.Value;
        Assert.True(f.View.IsInBadge(toggle));
        f.Window.MouseDown(centre, MouseButton.Left);
        f.Window.MouseUp(centre, MouseButton.Left);
        ViewFixture.Pump();
        Assert.False(f.Forms.Highlight);
        Assert.False(toggle.IsChecked);
        var off = await FieldPixelsAsync(f);
        Assert.NotEqual(on, off);

        // On again by the ribbon command: the switch follows.
        Run(f, PdfCommandIds.FormsHighlight);
        Assert.True(f.Forms.Highlight);
        Assert.True(toggle.IsChecked);

        // And off by the command: the switch follows again.
        Run(f, PdfCommandIds.FormsHighlight);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaFact]
    public async Task BadgeIsHiddenForADocumentWithoutAForm()
    {
        using var windows = WindowFixture.Create();
        using var copy = new TempCopy("pages-marked.pdf");
        var tab = await FormEditFixture.OpenPathAsync(windows, copy.Path);
        await Task.Delay(100);
        ViewFixture.Pump();
        Assert.Null(windows.Window.FormsOf(tab));
        var host = windows.Window.BadgeHostOf(tab)!;
        Assert.Null(host.Forms);
        Assert.False(host.IsVisible);
        Assert.Null(tab.FormsIfCreated);
    }

    [AvaloniaFact]
    public async Task HostShowsTheAnnotateBadgeWhileAToolIsActive()
    {
        using var f = await FormEditFixture.OpenAsync();
        var badge = await BadgeAsync(f);
        var host = HostOf(f);
        Assert.True(host.IsVisible);
        Assert.True(badge.IsVisible);
        Assert.False(host.AnnotateVisible);
        Assert.True(f.View.IsInBadge(badge.CounterButton));

        f.Window.Tools.Choose(AnnotateTool.Highlight);
        ViewFixture.Pump();
        Assert.True(host.AnnotateVisible);
        Assert.False(badge.IsVisible);

        f.Window.Tools.Choose(AnnotateTool.None);
        ViewFixture.Pump();
        Assert.False(host.AnnotateVisible);
        Assert.True(badge.IsVisible);
    }

    private static Point CentreOf(FormEditFixture f, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), f.Window)!.Value;

    [AvaloniaFact]
    public async Task APressOnTheBadgeBackgroundNeverReachesThePage()
    {
        using var f = await FormEditFixture.OpenAsync("forms-calc.pdf");
        var badge = await BadgeAsync(f);
        Assert.True(badge.NoticeVisible);
        var text = f.Window.TextOf(f.Tab)!;

        // Between the notice text and the frame edge, in the notice text itself, and in the padding at the top left corner of the badge.
        var host = HostOf(f);
        await Task.Delay(100);
        ViewFixture.Pump();
        List<Point> spots =
        [
            host.TranslatePoint(new Point(3, 3), f.Window)!.Value,
            host.TranslatePoint(new Point(host.Bounds.Width - 3, host.Bounds.Height - 3), f.Window)!.Value,
            CentreOf(f, badge.NoticeBlock),
        ];
        var away = f.View.TranslatePoint(new Point(f.View.Bounds.Width / 2, f.View.Bounds.Height / 2), f.Window)!.Value;
        foreach (var spot in spots)
        {
            Assert.True(f.View.IsInBadge(f.Window.InputHitTest(spot)), "the spot " + spot + " is not on the badge");

            // A click, and a drag that starts on the badge and leaves it: no selection and no capture by the page.
            f.Window.MouseDown(spot, MouseButton.Left);
            f.Window.MouseMove(away);
            f.Window.MouseMove(away + new Point(30, 20));
            Assert.False(text.Selection.HasSelection, "a press at " + spot + " started a selection");
            f.Window.MouseUp(away, MouseButton.Left);
            ViewFixture.Pump();
            Assert.False(text.Selection.HasSelection, "a press at " + spot + " selected text");
            Assert.False(text.IsPressed, "the page took the press at " + spot);
        }
    }

    [AvaloniaFact]
    public async Task ClickingTheBadgeButtonsLeavesTheKeyboardOnThePage()
    {
        using var f = await FormEditFixture.OpenAsync("forms-calc.pdf");
        var badge = await BadgeAsync(f);
        f.View.Focus();
        ViewFixture.Pump();
        Assert.Same(f.View, f.Window.FocusManager!.GetFocusedElement());

        foreach (var button in new Button[] { badge.HighlightToggle, badge.DismissButton })
        {
            var centre = CentreOf(f, button);
            f.Window.MouseDown(centre, MouseButton.Left);
            f.Window.MouseUp(centre, MouseButton.Left);
            ViewFixture.Pump();
            Assert.Same(f.View, f.Window.FocusManager!.GetFocusedElement());
        }

        // Tab still walks the fields of the page.
        Assert.Null(f.Parts.Focus.Current);
        f.PressKey(PhysicalKey.Tab, RawInputModifiers.None);
        await ViewFixture.WaitAsync(() => f.Parts.Focus.Current is not null);
    }

    [AvaloniaFact]
    public async Task AFailingLoadIsObservedAndTheBadgeStaysUsable()
    {
        using var file = await AnnotatedFile.OpenAsync("forms-basic.pdf");
        using var forms = await FormFocusTests.SessionAsync(file, FormFocusTests.Widget("a", 0));
        using var markers = new FormMarkers(forms, new FormFocus());
        var calls = 0;
        var unobserved = false;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved = true;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var badge = new FormBadge(forms, markers, () => { }, () =>
            {
                calls++;
                throw new InvalidOperationException("an unexpected failure of the document");
            });
            Assert.Equal(1, calls);
            await Task.Delay(50);
            ViewFixture.Pump();

            // Nothing was left faulted, and the next change of the form asks again (the single flight was released).
            file.Fixture.Window.Content = file.Fixture.View;
            markers.ApplyHighlight(true);
            ViewFixture.Pump();
            Assert.Equal(2, calls);
            Assert.False(badge.NoticeVisible);
        }
        finally
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.False(unobserved);
    }

    // The pixels in the left part of the "Name" field as drawn in the view once the rendering has settled.
    private static async Task<byte[]> FieldPixelsAsync(FormEditFixture f)
    {
        var field = f.Field("Name");
        await ViewFixture.WaitAsync(() => f.Tab.Session.Scheduler is { PendingCount: 0 });
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(25);
            ViewFixture.Pump();
        }

        var rect = f.View.ToView(0, field.Bounds)!.Value;
        var size = new PixelSize((int)f.View.Bounds.Width, (int)f.View.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(f.View);
        var stride = size.Width * 4;
        var all = new byte[stride * size.Height];
        var buffer = Marshal.AllocHGlobal(all.Length);
        try
        {
            frame.CopyPixels(new PixelRect(size), buffer, all.Length, stride);
            Marshal.Copy(buffer, all, 0, all.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var x0 = (int)rect.Left + 3;
        var y0 = (int)rect.Top + 3;
        var w = Math.Max(4, (int)(rect.Width / 4));
        var h = Math.Max(4, (int)rect.Height - 6);
        List<byte> result = [];
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                result.AddRange(all.AsSpan((y * stride) + (x * 4), 4).ToArray());
            }
        }

        return [.. result];
    }
}
