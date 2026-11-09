using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Raffica.Shell.Theming;

/// <summary>The token set of Windows high contrast. <see cref="ShellTheme"/> puts it into the application as a theme variant of its own.</summary>
public sealed partial class TokensHighContrast : ResourceDictionary
{
    public TokensHighContrast() => AvaloniaXamlLoader.Load(this);
}
