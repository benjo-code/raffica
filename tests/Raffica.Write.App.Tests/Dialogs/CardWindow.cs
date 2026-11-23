using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>A card in a window of its own, so that its template is applied and its controls can be found by name, clicked and typed into.</summary>
internal sealed class CardWindow : IDisposable
{
    public CardWindow(Dialog dialog, double width = 640, double height = 900)
    {
        Dialog = dialog;
        Window = new Window { Width = width, Height = height, Content = dialog };
        Window.Show();
        Pump();
    }

    public Dialog Dialog { get; }

    public Window Window { get; }

    public static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    public T Find<T>(string name)
        where T : Control =>
        Dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    public T? FindOrNull<T>(string name)
        where T : Control =>
        Dialog.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Name == name);

    /// <summary>The texts of every visible text block of the card.</summary>
    public List<string> Texts() =>
        [.. Dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty)];

    public void Click(string button)
    {
        Find<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    public void Type(string textBox, string text)
    {
        Find<TextBox>(textBox).Text = text;
        Pump();
    }

    public void Press(Control target, PhysicalKey physical, Key key)
    {
        target.Focus();
        Pump();
        Window.KeyPress(key, RawInputModifiers.None, physical, null);
        Window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Pump();
    }

    public void Dispose() => Window.Close();
}
