using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>Start-cost guard of milestone 4.5b: a document that opens and shows its first sharp page creates nothing of the field refresh (no runner, no host, no bar, no cards).</summary>
public sealed class StartupCreatesNoFieldWorkTests
{
    private static async Task<(WindowFixture Fixture, WriteWindow Window, WriteTab Tab, ManualFieldClock Clock)> ShowAsync(byte[] bytes)
    {
        var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Write("doc.docx", bytes);
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        var clock = new ManualFieldClock();
        tab.FieldClock = clock;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        return (fixture, window, tab, clock);
    }

    [AvaloniaFact]
    public async Task NothingExistsAtTheFirstSharpPage()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var drawn = false;
        var madeAtTheFirstSharpPage = false;
        window.FirstSharpPageDrawn += shown =>
        {
            drawn = true;
            madeAtTheFirstSharpPage = shown.Fields is not null;
        };
        var path = fixture.Write("doc.docx", FieldTestDocument.Bytes());
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        var clock = new ManualFieldClock();
        tab.FieldClock = clock;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        await WindowFixture.WaitAsync(() => drawn);

        // The runner of a document is made after its first sharp page (a small document is complete by then), never before it; nothing else exists either.
        Assert.False(madeAtTheFirstSharpPage);
        Assert.Null(window.ShownFieldsBar);
        Assert.False(window.FieldCardsExist);
        Assert.Null(tab.Editor);
        Assert.False(tab.IsDirty);
        Assert.Equal(0, tab.Docx.Model.Edits.DerivedVersion);
        Assert.Equal("Figure 77", FieldTestDocument.TextOf(tab, 1));

        // When the layout is complete the runner is made, and it has still done nothing.
        await WindowFixture.WaitAsync(() => tab.Fields is not null);
        Assert.Equal(0, tab.Fields!.Passes);
        Assert.Equal(1, clock.Created);
    }

    [AvaloniaFact]
    public async Task ADocumentWithoutFieldsNeverMakesARunnerNotEvenWhenItsLayoutIsComplete()
    {
        var (fixture, window, tab, clock) = await ShowAsync(FieldTestDocument.PlainBytes());
        using (fixture)
        {
            tab.Runner.Start();
            await WindowFixture.WaitAsync(() => tab.Runner.PageLayout.IsComplete);
            WindowFixture.Pump();

            Assert.Null(tab.Fields);
            Assert.Equal(0, clock.Created);
            Assert.Null(window.ShownFieldsBar);
        }
    }

    [AvaloniaFact]
    public async Task ADocumentWithFieldsMakesItsRunnerWhenTheLayoutIsCompleteAndRefreshesOnlyWhenTheTimerFires()
    {
        var (fixture, window, tab, clock) = await ShowAsync(FieldTestDocument.Bytes());
        using (fixture)
        {
            Assert.Null(tab.Fields);
            tab.Runner.Start();
            await WindowFixture.WaitAsync(() => tab.Fields is not null);

            Assert.True(tab.Runner.PageLayout.IsComplete);
            Assert.Equal(0, tab.Fields!.Passes);
            Assert.True(clock.IsArmed);
            Assert.Equal("Figure 77", FieldTestDocument.TextOf(tab, 1));
            Assert.Null(tab.Editor);

            clock.Fire();
            WindowFixture.Pump();

            Assert.Equal(1, tab.Fields.Passes);
            Assert.Equal("Figure 1", FieldTestDocument.TextOf(tab, 1));
            Assert.Null(tab.Editor);
            Assert.False(tab.IsDirty);
            Assert.NotNull(window.ShownFieldsBar);
        }
    }

    [AvaloniaFact]
    public async Task AThrowingScanInTheWatchNeverEscapes()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();

        // The scan is set before the layout can report (the first sharp page is drawn before the layout is started).
        window.FirstSharpPageDrawn += shown => shown.FieldScan = _ => throw new InvalidOperationException("test");
        var path = fixture.Write("doc.docx", FieldTestDocument.Bytes());
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.FieldClock = new ManualFieldClock();
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => tab.FieldWiringFailures > 0);
        WindowFixture.Pump();

        Assert.Null(tab.Fields);
    }

    [AvaloniaFact]
    public async Task TheWatchIsArmedOnlyAfterTheFirstSharpPageAndNothingIsScannedBefore()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var scans = 0;
        var armedAtTheFirstSharpPage = true;
        var scansAtTheFirstSharpPage = -1;
        window.FirstSharpPageDrawn += shown =>
        {
            armedAtTheFirstSharpPage = window.IsWatchingFields(shown);
            scansAtTheFirstSharpPage = scans;
        };
        var path = fixture.Write("doc.docx", FieldTestDocument.Bytes());
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.FieldClock = new ManualFieldClock();
        tab.FieldScan = m =>
        {
            scans++;
            return FieldView.AnyIn(m);
        };
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => scansAtTheFirstSharpPage >= 0);

        Assert.False(armedAtTheFirstSharpPage);
        Assert.Equal(0, scansAtTheFirstSharpPage);

        await WindowFixture.WaitAsync(() => tab.Fields is not null);
        Assert.True(window.IsWatchingFields(tab));
        Assert.True(scans > 0);
    }
}
