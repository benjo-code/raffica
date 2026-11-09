using Avalonia;
using Avalonia.Threading;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// Shows the cards of the open flow one at a time in a <see cref="DialogHost"/>: a card that arrives while a dialog is open (one of this
/// queue or any other, such as the unsaved-changes prompt) waits, and appears in order as soon as the host is free again. The queue watches
/// the host, so it does not matter who closed the previous dialog.
/// </summary>
internal sealed class DialogQueue
{
    private readonly DialogHost _host;
    private readonly Queue<Func<bool>> _waiting = new();
    private bool _drainPosted;

    public DialogQueue(DialogHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _host.GetObservable(DialogHost.ActiveDialogProperty).Subscribe(new HostObserver(this));
    }

    /// <summary>Whether a dialog is open in the host (whoever opened it).</summary>
    public bool IsOpen => _host.IsOpen;

    /// <summary>Shows the card made by <paramref name="create"/> (it gets the callback that closes the card with a result) and returns the result.</summary>
    /// <remarks>When the card cannot be created or shown, the returned task fails with that exception and the queue goes on.</remarks>
    public Task<T> ShowAsync<T>(Func<Action<T>, Dialog> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        bool TryShow()
        {
            if (_host.IsOpen)
            {
                return false;
            }

            try
            {
                _host.Show(create(result =>
                {
                    if (answer.Task.IsCompleted)
                    {
                        return;
                    }

                    _host.Close();
                    answer.TrySetResult(result);
                }));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                answer.TrySetException(ex);
            }

            return true;
        }

        // A card never jumps the queue: it shows at once only when nothing is open and nothing waits.
        if (_waiting.Count > 0 || !TryShow())
        {
            _waiting.Enqueue(TryShow);
        }

        return answer.Task;
    }

    private void OnActiveDialogChanged(Dialog? active)
    {
        if (active is not null || _waiting.Count == 0 || _drainPosted)
        {
            return;
        }

        // Posted: the host is still in the middle of Close, and a card shown now would be undone by the rest of it.
        _drainPosted = true;
        Dispatcher.UIThread.Post(Drain);
    }

    private void Drain()
    {
        _drainPosted = false;
        while (_waiting.Count > 0 && _waiting.Peek()())
        {
            _waiting.Dequeue();

            // A card that failed to show did not open the host: the next one may.
            if (_host.IsOpen)
            {
                break;
            }
        }
    }

    private sealed class HostObserver(DialogQueue queue) : IObserver<Dialog?>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(Dialog? value) => queue.OnActiveDialogChanged(value);
    }
}
