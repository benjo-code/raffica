using Raffica.Write.Viewing;

namespace Raffica.Write.Editing;

/// <summary>
/// The right to change the model: while it is held no background layout step runs and none starts (see <see cref="LayoutRunner.PauseForEdit"/>). Disposing it
/// lets the layout go on from where it stands; disposing it twice, or after the runner ended, does nothing.
/// </summary>
internal sealed class EditLease : IDisposable
{
    private LayoutRunner? _runner;

    internal EditLease(LayoutRunner runner) => _runner = runner;

    public void Dispose() => Interlocked.Exchange(ref _runner, null)?.ReleaseLease();
}
