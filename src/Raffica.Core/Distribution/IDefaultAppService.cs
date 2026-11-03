namespace Raffica.Core.Distribution;

/// <summary>Whether Raffica opens PDF files by default.</summary>
public enum DefaultAppStatus
{
    /// <summary>The system could not be asked, or has no such notion.</summary>
    Unknown,

    /// <summary>Raffica is the default app for PDF files.</summary>
    IsDefault,

    /// <summary>Another app is the default.</summary>
    NotDefault,
}

/// <summary>The outcome of <see cref="IDefaultAppService.MakeDefaultAsync"/>.</summary>
public enum DefaultAppResult
{
    /// <summary>Raffica is now the default app.</summary>
    Done,

    /// <summary>The system keeps that choice to the user; its settings page is open and the user decides there.</summary>
    OpenedSystemSettings,

    /// <summary>This system offers no way to set it.</summary>
    NotAvailable,

    /// <summary>Something went wrong or took too long.</summary>
    Failed,
}

/// <summary>Reads and sets the default app for PDF files. Never blocks the caller: all work is asynchronous.</summary>
public interface IDefaultAppService
{
    /// <summary>Gets whether this system is one the service can work on.</summary>
    bool IsSupported { get; }

    /// <summary>Asks the system whether Raffica is the default app for PDF files.</summary>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>The status; <see cref="DefaultAppStatus.Unknown"/> when it cannot be told.</returns>
    Task<DefaultAppStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Makes Raffica the default app, or opens the place where the user does.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened.</returns>
    Task<DefaultAppResult> MakeDefaultAsync(CancellationToken cancellationToken);
}
