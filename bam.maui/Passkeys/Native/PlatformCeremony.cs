namespace Bam.Maui.Passkeys;

/// <summary>
/// The pending result of a platform ceremony that reports through a callback.  The caller's token
/// completes it as well as the callback does, whichever comes first: a platform API is not bound
/// to call back once its operation is cancelled (Android's Credential Manager returns without
/// calling the receiver when the signal is already cancelled), and the caller must never wait on
/// a callback that will not come.
/// </summary>
/// <typeparam name="TOutcome">What the callback reports.</typeparam>
internal sealed class PlatformCeremony<TOutcome> : IDisposable
{
    private readonly TaskCompletionSource<TOutcome> _completion = new TaskCompletionSource<TOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _registration;

    /// <summary>Starts waiting.</summary>
    /// <param name="cancellationToken">The caller's token; when it fires the ceremony completes as cancelled, then <paramref name="cancelPlatform"/> runs.</param>
    /// <param name="cancelPlatform">Asks the platform to close its prompt.</param>
    internal PlatformCeremony(CancellationToken cancellationToken, Action cancelPlatform)
    {
        ArgumentNullException.ThrowIfNull(cancelPlatform);
        _registration = cancellationToken.Register(() =>
        {
            // Completed first, so the caller is released even if the platform's cancel throws.
            _completion.TrySetCanceled(cancellationToken);
            cancelPlatform();
        });
    }

    /// <summary>Completes when the callback reports or the token fires; a fired token makes it throw <see cref="OperationCanceledException"/>.</summary>
    internal Task<TOutcome> Completion => _completion.Task;

    /// <summary>True once the ceremony has completed either way.</summary>
    internal bool IsCompleted => _completion.Task.IsCompleted;

    /// <summary>Records what the callback reported; ignored when the ceremony already completed.</summary>
    /// <param name="outcome">The report.</param>
    /// <returns>True when this report is the ceremony's outcome.</returns>
    internal bool Report(TOutcome outcome)
    {
        return _completion.TrySetResult(outcome);
    }

    /// <summary>Stops listening to the token.</summary>
    public void Dispose()
    {
        _registration.Dispose();
    }
}
