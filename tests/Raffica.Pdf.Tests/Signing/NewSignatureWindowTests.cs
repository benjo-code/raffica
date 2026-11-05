using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Imaging;
using Raffica.Core.Signatures;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class NewSignatureWindowTests
{
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
            ViewFixture.Pump();
        }

        Assert.True(condition(), "The condition was not reached in time.");
    }

    [AvaloniaFact]
    public async Task TheWindowShowsTheCardWithItsPickerAndSavesIntoItsOwnFolder()
    {
        using var windows = WindowFixture.Create();
        var bgra = new byte[40 * 20 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = bgra[i + 1] = bgra[i + 2] = (byte)(i / 4 % 40 is >= 10 and < 30 ? 20 : 240);
            bgra[i + 3] = 255;
        }

        var png = PngEncoder.EncodeWithAlpha(new SignatureImage(40, 20, bgra));
        var picks = 0;

        var shown = windows.Window.ShowNewSignatureAsync(() =>
        {
            picks++;
            return Task.FromResult<Stream?>(new MemoryStream(png));
        });
        await UntilAsync(() => windows.Window.Host.IsOpen);
        var card = windows.Window.Host.ActiveDialog!;
        card.GetVisualDescendants().OfType<Raffica.Shell.Controls.SegmentedControl>().Single().SelectedIndex = 1;
        ViewFixture.Pump();
        Button Find(string name) => card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
        Find("PART_Choose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => Find("PART_Save").IsEnabled);
        Find("PART_Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var saved = await shown;

        Assert.Equal(1, picks);
        Assert.NotNull(saved);
        Assert.False(windows.Window.Host.IsOpen);
        Assert.Equal(saved!.Id, Assert.Single(await windows.Window.Signatures.LoadAsync()).Id);
        Assert.True(File.Exists(Path.Combine(windows.Folder, "signatures", "signatures.json")));
    }

    [AvaloniaFact]
    public async Task CancellingGivesNull()
    {
        using var windows = WindowFixture.Create();

        var shown = windows.Window.ShowNewSignatureAsync(() => Task.FromResult<Stream?>(null));
        await UntilAsync(() => windows.Window.Host.IsOpen);
        windows.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(await shown);
        Assert.Empty(await windows.Window.Signatures.LoadAsync());
    }
}
