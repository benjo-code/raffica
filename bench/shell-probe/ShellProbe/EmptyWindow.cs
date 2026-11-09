using Avalonia.Controls;

namespace ShellProbe;

/// <summary>The baseline of the measurement: one window of 1440 × 900 with nothing in it.</summary>
internal sealed class EmptyWindow : Window
{
    public EmptyWindow()
    {
        Width = 1440;
        Height = 900;
        Title = Strings.Get("AppName");
    }
}
