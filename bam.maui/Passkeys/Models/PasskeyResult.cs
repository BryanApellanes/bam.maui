namespace Bam.Maui.Passkeys;

/// <summary>
/// The outcome of an <see cref="IPasskeyService"/> operation that returns no value.
/// </summary>
public class PasskeyResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="error">The failure, or null for success.</param>
    protected PasskeyResult(PasskeyError? error)
    {
        Error = error;
    }

    /// <summary>True when the operation completed and, for a ceremony, the backend verified it.</summary>
    public bool Succeeded => Error is null;

    /// <summary>The failure, or null when the operation succeeded.</summary>
    public PasskeyError? Error { get; }

    /// <summary>True when the operation ended because the user dismissed the prompt.</summary>
    public bool WasCancelled => Error is not null && Error.IsCancellation;

    /// <summary>A successful result.</summary>
    /// <returns>The result.</returns>
    public static PasskeyResult Success()
    {
        return new PasskeyResult(null);
    }

    /// <summary>A failed result.</summary>
    /// <param name="error">Why the operation failed.</param>
    /// <returns>The result.</returns>
    public static PasskeyResult Failure(PasskeyError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new PasskeyResult(error);
    }
}

/// <summary>
/// The outcome of an <see cref="IPasskeyService"/> operation that returns a value.
/// </summary>
/// <typeparam name="TValue">The value produced on success.</typeparam>
public sealed class PasskeyResult<TValue> : PasskeyResult
{
    private PasskeyResult(TValue? value, PasskeyError? error) : base(error)
    {
        Value = value;
    }

    /// <summary>The value on success; default when the operation failed.</summary>
    public TValue? Value { get; }

    /// <summary>A successful result carrying a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The result.</returns>
    public static PasskeyResult<TValue> Success(TValue value)
    {
        return new PasskeyResult<TValue>(value, null);
    }

    /// <summary>A failed result.</summary>
    /// <param name="error">Why the operation failed.</param>
    /// <returns>The result.</returns>
    public static new PasskeyResult<TValue> Failure(PasskeyError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new PasskeyResult<TValue>(default, error);
    }
}
