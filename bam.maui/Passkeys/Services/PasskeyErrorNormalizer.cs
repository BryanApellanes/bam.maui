using System.Net.Http;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Turns whatever an authenticator or backend threw into a <see cref="PasskeyError"/>: a category
/// the app can branch on and the configured user-facing message for it.  The exception's own
/// text is never copied into the result.
/// </summary>
public sealed class PasskeyErrorNormalizer
{
    private readonly PasskeyMessages _messages;

    /// <summary>Creates a normalizer over the app's messages.</summary>
    /// <param name="messages">The user-facing text per category.</param>
    public PasskeyErrorNormalizer(PasskeyMessages messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
    }

    /// <summary>The error for a known category.</summary>
    /// <param name="category">The failure category.</param>
    /// <returns>The error carrying the configured message.</returns>
    public PasskeyError FromCategory(PasskeyErrorCategory category)
    {
        return new PasskeyError(category, _messages.For(category));
    }

    /// <summary>
    /// The error for an exception.  <see cref="PasskeyException"/> keeps its category; cancellation
    /// becomes <see cref="PasskeyErrorCategory.UserCancelled"/> (<see cref="PasskeyClient"/> turns a
    /// backend cancellation the caller did not ask for into a network failure before it gets here); transport faults and timeouts
    /// become <see cref="PasskeyErrorCategory.NetworkFailure"/>; malformed data becomes
    /// <see cref="PasskeyErrorCategory.InvalidServerResponse"/>; anything else is
    /// <see cref="PasskeyErrorCategory.Unknown"/>.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    /// <returns>The normalized error.</returns>
    public PasskeyError Normalize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return FromCategory(Categorize(exception));
    }

    /// <summary>The category for an exception, by the rules <see cref="Normalize"/> documents.</summary>
    /// <param name="exception">What was thrown.</param>
    /// <returns>The category.</returns>
    public static PasskeyErrorCategory Categorize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            PasskeyException passkeyException => passkeyException.Category == PasskeyErrorCategory.None ? PasskeyErrorCategory.Unknown : passkeyException.Category,
            // HttpClient reports its own timeout as a cancellation wrapping a TimeoutException; that is the network, not the user.
            OperationCanceledException { InnerException: TimeoutException } => PasskeyErrorCategory.NetworkFailure,
            OperationCanceledException => PasskeyErrorCategory.UserCancelled,
            HttpRequestException => PasskeyErrorCategory.NetworkFailure,
            TimeoutException => PasskeyErrorCategory.NetworkFailure,
            FormatException => PasskeyErrorCategory.InvalidServerResponse,
            System.Text.Json.JsonException => PasskeyErrorCategory.InvalidServerResponse,
            Newtonsoft.Json.JsonException => PasskeyErrorCategory.InvalidServerResponse,
            _ => PasskeyErrorCategory.Unknown
        };
    }
}
