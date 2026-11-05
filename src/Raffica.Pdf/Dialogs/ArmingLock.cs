using Avalonia.Controls;
using Avalonia.Threading;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The 400 ms lock of the dangerous choices of a card (Don't save, Save anyway, Overwrite, Close and discard changes): the buttons start
/// disabled and are enabled by a timer once <see cref="DelayMilliseconds"/> have passed on the clock. <see cref="IsArmed"/> asks the clock
/// itself, so a click that reaches a button some other way (a script, assistive technology) is no more able to confirm early than the pointer.
/// </summary>
internal sealed class ArmingLock
{
    /// <summary>How long the dangerous choices stay disabled after the card was made.</summary>
    public const int DelayMilliseconds = 400;

    private readonly TimeProvider _time;
    private readonly long _started;

    public ArmingLock(TimeProvider? time, Dialog dialog, params Button[] dangerous)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        _time = time ?? TimeProvider.System;
        _started = _time.GetTimestamp();
        foreach (var button in dangerous)
        {
            button.IsEnabled = false;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DelayMilliseconds) };
        timer.Tick += (_, _) =>
        {
            if (IsArmed)
            {
                timer.Stop();
                foreach (var button in dangerous)
                {
                    button.IsEnabled = true;
                }
            }
            else
            {
                timer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, DelayMilliseconds - _time.GetElapsedTime(_started).TotalMilliseconds));
            }
        };
        dialog.AttachedToVisualTree += (_, _) => timer.Start();
        dialog.DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    public bool IsArmed => _time.GetElapsedTime(_started) >= TimeSpan.FromMilliseconds(DelayMilliseconds);
}
