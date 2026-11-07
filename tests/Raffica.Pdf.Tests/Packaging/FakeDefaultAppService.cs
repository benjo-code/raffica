using Raffica.Core.Distribution;

namespace Raffica.Pdf.Tests.Packaging;

/// <summary>A default app service that answers what the test says and counts what it was asked; it starts no process and reads no registry.</summary>
internal sealed class FakeDefaultAppService : IDefaultAppService
{
    public bool IsSupported { get; init; } = true;

    public DefaultAppStatus Status { get; set; } = DefaultAppStatus.NotDefault;

    public DefaultAppResult Result { get; set; } = DefaultAppResult.Done;

    /// <summary>When set, the next <see cref="MakeDefaultAsync"/> throws it.</summary>
    public Exception? MakeThrows { get; set; }

    /// <summary>When set, <see cref="MakeDefaultAsync"/> does not end until the token is canceled.</summary>
    public bool MakeHangs { get; set; }

    public int StatusCalls { get; private set; }

    public int MakeCalls { get; private set; }

    public Task<DefaultAppStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        StatusCalls++;
        return Task.FromResult(Status);
    }

    public async Task<DefaultAppResult> MakeDefaultAsync(CancellationToken cancellationToken)
    {
        MakeCalls++;
        if (MakeThrows is { } exception)
        {
            throw exception;
        }

        if (MakeHangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return Result;
    }
}
