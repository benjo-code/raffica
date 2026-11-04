using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// Shows the cards of the open flow one at a time in a <see cref="DialogHost"/>: a card that arrives while another dialog is open waits
/// until the host is free again (several dropped files can each need a card).
/// </summary>
internal sealed class DialogQueue(DialogHost host)
{
    private readonly Queue<Func<bool>> _waiting = new();

    /// <summary>Shows the card made by <paramref name="create"/> (it gets the callback that closes the card with a result) and returns the result.</summary>
    public Task<T> ShowAsync<T>(Func<Action<T>, Dialog> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        bool TryShow()
        {
            if (host.IsOpen)
            {
                return false;
            }

            host.Show(create(result =>
            {
                if (answer.Task.IsCompleted)
                {
                    return;
                }

                host.Close();
                answer.TrySetResult(result);
                ShowNext();
            }));
            return true;
        }

        if (!TryShow())
        {
            _waiting.Enqueue(TryShow);
        }

        return answer.Task;
    }

    private void ShowNext()
    {
        if (_waiting.Count > 0 && _waiting.Peek()())
        {
            _waiting.Dequeue();
        }
    }
}
