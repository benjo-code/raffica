using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Raffica.Shell;

/// <summary>The style bundle of the shell: the one entry point an app adds to its application styles.</summary>
public sealed partial class RafficaStyles : Styles
{
    public RafficaStyles() => AvaloniaXamlLoader.Load(this);
}
