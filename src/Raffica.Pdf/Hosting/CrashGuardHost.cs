using Avalonia.Threading;

namespace Raffica.Pdf.Hosting;

/// <summary>Subscribes a <see cref="CrashGuard"/> to the places an uncaught exception comes from. Subscribing does no work and no file access.</summary>
internal static class CrashGuardHost
{
    /// <summary>The UI thread: an exception of a job of <paramref name="ui"/> goes to the guard and is marked handled when the guard says so.</summary>
    public static IDisposable AttachUi(CrashGuard guard, Dispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(ui);
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            guard.OnUiException(e.Exception, out var handled);
            if (handled)
            {
                e.Handled = true;
            }
        }

        ui.UnhandledException += OnUnhandled;
        return new Subscription(() => ui.UnhandledException -= OnUnhandled);
    }

    /// <summary>Every other thread: an uncaught exception of the process and an unobserved task exception are logged; the task one is then observed.</summary>
    public static IDisposable AttachProcess(CrashGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        void OnDomain(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                guard.OnBackgroundException(ex);
            }
        }

        void OnTask(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            guard.OnBackgroundException(e.Exception);
            e.SetObserved();
        }

        AppDomain.CurrentDomain.UnhandledException += OnDomain;
        TaskScheduler.UnobservedTaskException += OnTask;
        return new Subscription(() =>
        {
            AppDomain.CurrentDomain.UnhandledException -= OnDomain;
            TaskScheduler.UnobservedTaskException -= OnTask;
        });
    }

    private sealed class Subscription(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
