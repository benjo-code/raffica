using Avalonia.Headless.XUnit;
using Raffica.Core.Forms;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormMarkersTests
{
    private const string Basic = "forms-basic.pdf";

    private static async Task<(AnnotatedFile File, FormSession Forms, FormFocus Focus, FormMarkers Markers)> SetUpAsync(params FormField[] fields)
    {
        var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = await FormFocusTests.SessionAsync(file, fields);
        var focus = new FormFocus();
        var markers = new FormMarkers(forms, focus);
        file.Fixture.View.FormOverlay = markers;
        return (file, forms, focus, markers);
    }

    private static async Task ShownAsync(AnnotatedFile file)
    {
        // The fixture view is off screen: put it into the window and wait for the first sharp page.
        file.Fixture.Window.Content = file.Fixture.View;
        file.Fixture.Window.Show();
        await ViewFixture.WaitAsync(() => file.Fixture.View.FirstSharpPageShown);
    }

    [AvaloniaFact]
    public async Task StarOnEmptyRequiredAndNoStarOnceFilled()
    {
        var empty = FormFocusTests.Widget("req", 0, traits: FormFieldTraits.Required);
        var (file, forms, _, markers) = await SetUpAsync(empty, FormFocusTests.Widget("opt", 1));
        using (file)
        using (forms)
        {
            await ShownAsync(file);
            var plan = markers.Plan(file.Fixture.View);
            Assert.Single(plan, m => m.Kind == FormMarkerKind.Star);
            Assert.Equal("req", plan.Single(m => m.Kind == FormMarkerKind.Star).Key.FieldName);
            Assert.DoesNotContain(plan, m => m.Kind == FormMarkerKind.Lock);

            using var filled = new FormSession(file.Tab, new QueueContext())
            {
                PageReader = (_, _, _) => Task.FromResult<IReadOnlyList<FormField>>([empty with { State = FormFieldState.OfText("x") }]),
            };
            await filled.LoadPageAsync(0);
            using var other = new FormMarkers(filled, new FormFocus());
            Assert.DoesNotContain(other.Plan(file.Fixture.View), m => m.Kind == FormMarkerKind.Star);
        }
    }

    [AvaloniaFact]
    public async Task LockOnReadOnlySignatureAndButtonFields()
    {
        var (file, forms, _, markers) = await SetUpAsync(
            FormFocusTests.Widget("ro", 0, locked: FormLock.ReadOnly),
            FormFocusTests.Widget("sig", 1, FormFieldKind.Signature, locked: FormLock.Unsupported),
            FormFocusTests.Widget("btn", 2, FormFieldKind.PushButton, locked: FormLock.Unsupported),
            FormFocusTests.Widget("free", 3));
        using (file)
        using (forms)
        {
            await ShownAsync(file);
            var locks = markers.Plan(file.Fixture.View).Where(m => m.Kind == FormMarkerKind.Lock).Select(m => m.Key.FieldName).Order().ToArray();
            Assert.Equal(["btn", "ro", "sig"], locks);
        }
    }

    [AvaloniaFact]
    public async Task FocusRingFollowsTheFocusedWidget()
    {
        var (file, forms, focus, markers) = await SetUpAsync(FormFocusTests.Widget("a", 0), FormFocusTests.Widget("b", 1));
        using (file)
        using (forms)
        {
            await ShownAsync(file);
            var changes = 0;
            markers.Changed += () => changes++;
            Assert.DoesNotContain(markers.Plan(file.Fixture.View), m => m.Kind == FormMarkerKind.Focus);

            focus.Set(new FormWidgetKey("b", 0, 1));
            Assert.Equal(1, changes);
            var ring = markers.Plan(file.Fixture.View).Single(m => m.Kind == FormMarkerKind.Focus);
            Assert.Equal("b", ring.Key.FieldName);
            Assert.Equal(file.Fixture.View.ToView(0, forms.FieldsOn(0)[1].Bounds), ring.Bounds);

            focus.Set(new FormWidgetKey("a", 0, 0));
            Assert.Equal("a", markers.Plan(file.Fixture.View).Single(m => m.Kind == FormMarkerKind.Focus).Key.FieldName);
        }
    }

    [AvaloniaFact]
    public async Task NothingIsDrawnWhileAnAnnotateToolIsActive()
    {
        var (file, forms, focus, markers) = await SetUpAsync(FormFocusTests.Widget("req", 0, traits: FormFieldTraits.Required));
        using (file)
        using (forms)
        {
            await ShownAsync(file);
            focus.Set(new FormWidgetKey("req", 0, 0));
            Assert.NotEmpty(markers.Plan(file.Fixture.View));

            var active = true;
            markers.Suppressed = () => active;
            Assert.Empty(markers.Plan(file.Fixture.View));
            active = false;
            Assert.NotEmpty(markers.Plan(file.Fixture.View));
        }
    }

    [AvaloniaFact]
    public async Task MarkersFollowZoomAndScroll()
    {
        var (file, forms, _, markers) = await SetUpAsync(FormFocusTests.Widget("req", 0, traits: FormFieldTraits.Required));
        using (file)
        using (forms)
        {
            await ShownAsync(file);
            var view = file.Fixture.View;
            var bounds = forms.FieldsOn(0)[0].Bounds;
            var before = markers.Plan(view).Single();
            Assert.Equal(view.ToView(0, bounds)!.Value.Right, before.Bounds.Right, 0.01);

            view.SetZoom(view.Zoom * 2);
            ViewFixture.Pump();
            var zoomed = markers.Plan(view).Single();
            Assert.Equal(view.ToView(0, bounds)!.Value.Right, zoomed.Bounds.Right, 0.01);
            Assert.NotEqual(before.Bounds.Right, zoomed.Bounds.Right, 0.5);

            var top = zoomed.Bounds.Top;
            view.Offset = new Avalonia.Vector(view.Offset.X, view.Offset.Y + 10);
            ViewFixture.Pump();
            var scrolled = markers.Plan(view).Single();
            Assert.Equal(top - 10, scrolled.Bounds.Top, 0.01);
        }
    }

    [AvaloniaFact]
    public async Task TheFirstHighlightRendersTheVisiblePagesAgain()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        using var forms = await FormFocusTests.SessionAsync(file, FormFocusTests.Widget("a", 0));
        using var markers = new FormMarkers(forms, new FormFocus());
        var view = file.Fixture.View;
        markers.Attach(view);
        view.FormOverlay = markers;

        await ShownAsync(file);
        await ViewFixture.WaitAsync(() => forms.Highlight);

        // The flip emptied the session's cache; the page comes back without any scroll or zoom.
        var cache = file.Fixture.Session.Cache!;
        await ViewFixture.WaitAsync(() => cache.Count > 0 && file.Fixture.Session.Scheduler is { PendingCount: 0 });
        await file.Fixture.SettleAsync();
        Assert.True(view.Bitmaps.Count > 0);
        Assert.True(cache.Count > 0);
    }

    [AvaloniaFact]
    public async Task NothingIsLoadedBeforeTheFirstSharpPage()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var reads = 0;
        using var forms = new FormSession(file.Tab, new QueueContext())
        {
            PageReader = (_, _, _) =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult<IReadOnlyList<FormField>>([FormFocusTests.Widget("req", 0, traits: FormFieldTraits.Required)]);
            },
        };
        using var markers = new FormMarkers(forms, new FormFocus());
        var view = file.Fixture.View;
        markers.Attach(view);
        view.FormOverlay = markers;

        // The view is arranged but nothing was painted: no page was asked for and nothing is planned.
        Assert.False(view.FirstSharpPageShown);
        Assert.Equal(0, Volatile.Read(ref reads));
        Assert.Empty(markers.Plan(view));

        await ShownAsync(file);
        await ViewFixture.WaitAsync(() => forms.IsLoaded(0));
        Assert.Equal(1, Volatile.Read(ref reads));

        view.SetZoom(view.Zoom * 2);
        ViewFixture.Pump();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.Equal(1, Volatile.Read(ref reads));
    }
}
