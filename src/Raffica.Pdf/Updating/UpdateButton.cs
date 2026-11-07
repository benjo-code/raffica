using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Raffica.Core.Updates;

namespace Raffica.Pdf.Updating;

/// <summary>
/// The button of the title bar that says a newer version is on offer ("Update available"), in the trailing slot of the shell's title bar. It is made by the window together
/// with the update flow (nothing of it exists before the first check) and follows the state of the flow: visible exactly while the stage is not
/// <see cref="UpdateStage.None"/>, hidden otherwise. Its text, its tooltip and its accessible name carry the news in words ("Update available: Raffica PDF 0.2.0"),
/// never colour alone. A click opens the offer card (<see cref="UpdateCard"/>); the window decides what that does.
/// </summary>
internal sealed class UpdateButton : Button
{
    private readonly Action _open;

    /// <param name="open">Called on a click (the window shows the offer).</param>
    public UpdateButton(Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
        Name = "PART_UpdateButton";
        Classes.Add("boxed");
        Content = PdfStrings.UpdateButton;
        Margin = new Thickness(0, 0, 8, 0);
        IsVisible = false;

        // A user element of the window decorations: the title bar is the drag area, and a click on the button must reach it.
        WindowDecorationProperties.SetElementRole(this, WindowDecorationsElementRole.User);
        Click += (_, _) => _open();
    }

    /// <summary>The button is a <see cref="Button"/> in every style rule.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>Follows a state of the update flow: shown with the version while something newer is known, hidden when nothing is.</summary>
    public void Apply(UpdateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stage == UpdateStage.None || state.Version is not { } version)
        {
            IsVisible = false;
            return;
        }

        var name = PdfStrings.Fill(PdfStrings.UpdateButtonNameFormat, version.ToString());
        AutomationProperties.SetName(this, name);
        ToolTip.SetTip(this, name);
        IsVisible = true;
    }
}
