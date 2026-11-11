using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyErrorNormalizer Should", Selector = "pken")]
public class PasskeyErrorNormalizerShould : UnitTestMenuContainer
{
    public PasskeyErrorNormalizerShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void KeepThePasskeyExceptionCategory()
    {
        After.Setup(reg => reg.Set(new PasskeyErrorNormalizer(new PasskeyMessages())))
        .When<PasskeyErrorNormalizer>("normalizes a PasskeyException for every category", (normalizer, reg) =>
        {
            List<PasskeyErrorCategory> mismatched = new List<PasskeyErrorCategory>();
            foreach (PasskeyErrorCategory category in Enum.GetValues<PasskeyErrorCategory>())
            {
                PasskeyErrorCategory expected = category == PasskeyErrorCategory.None ? PasskeyErrorCategory.Unknown : category;
                PasskeyError error = normalizer.Normalize(new PasskeyException(category));
                if (error.Category != expected)
                {
                    mismatched.Add(category);
                }
            }
            return new CategoryOutcome(mismatched.Count, normalizer.Normalize(new PasskeyException(PasskeyErrorCategory.UserCancelled)).IsCancellation);
        })
        .TheTest
        .ShouldPass<CategoryOutcome>((because, outcome) =>
        {
            because.ItsTrue("every category survives normalization (None becomes Unknown)", outcome.Mismatched == 0);
            because.ItsTrue("a cancelled error says it is a cancellation", outcome.CancellationFlag);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void CategorizeFrameworkExceptions()
    {
        After.Setup(reg => reg.Set(new PasskeyErrorNormalizer(new PasskeyMessages())))
        .When<PasskeyErrorNormalizer>("normalizes transport, cancellation, format and unknown exceptions", (normalizer, reg) =>
        {
            return new FrameworkOutcome(
                normalizer.Normalize(new OperationCanceledException()).Category,
                normalizer.Normalize(new TaskCanceledException()).Category,
                normalizer.Normalize(new TaskCanceledException("timeout", new TimeoutException())).Category,
                normalizer.Normalize(new HttpRequestException("no route")).Category,
                normalizer.Normalize(new TimeoutException()).Category,
                normalizer.Normalize(new System.Text.Json.JsonException("bad")).Category,
                normalizer.Normalize(new Newtonsoft.Json.JsonSerializationException("bad")).Category,
                normalizer.Normalize(new FormatException("bad")).Category,
                normalizer.Normalize(new InvalidOperationException("boom")).Category);
        })
        .TheTest
        .ShouldPass<FrameworkOutcome>((because, outcome) =>
        {
            because.ItsTrue("cancellation is user cancelled", outcome.Cancelled == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("task cancellation is user cancelled", outcome.TaskCancelled == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("an http timeout surfaced as cancellation is a network failure", outcome.HttpTimeout == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("an http request failure is a network failure", outcome.Http == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("a timeout is a network failure", outcome.Timeout == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("a System.Text.Json failure is an invalid server response", outcome.SystemJson == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a Newtonsoft failure is an invalid server response", outcome.NewtonsoftJson == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a format failure is an invalid server response", outcome.Format == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("anything else is unknown", outcome.Other == PasskeyErrorCategory.Unknown);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void UseConfiguredMessagesAndNeverExceptionText()
    {
        After.Setup(reg => reg.Set(new PasskeyMessages { NetworkFailure = "Sin conexión.", Unknown = "Algo salió mal." }))
        .When<PasskeyMessages>("normalizes exceptions whose messages carry sensitive text", (messages, reg) =>
        {
            PasskeyErrorNormalizer normalizer = new PasskeyErrorNormalizer(messages);
            PasskeyError network = normalizer.Normalize(new HttpRequestException("POST https://internal.example/secret failed"));
            PasskeyError unknown = normalizer.Normalize(new InvalidOperationException("clientDataJSON=eyJjaGFsbGVuZ2Ui"));
            PasskeyError passkey = normalizer.Normalize(new PasskeyException(PasskeyErrorCategory.ServerVerificationFailed, "signature mismatch for credential abc"));
            return new MessageOutcome(network.Message, unknown.Message, passkey.Message, normalizer.FromCategory(PasskeyErrorCategory.ExpiredChallenge).Message, messages.For((PasskeyErrorCategory)999));
        })
        .TheTest
        .ShouldPass<MessageOutcome>((because, outcome) =>
        {
            because.ItsTrue("the network message is the configured one", outcome.Network == "Sin conexión.");
            because.ItsTrue("the unknown message is the configured one", outcome.Unknown == "Algo salió mal.");
            because.ItsTrue("the exception's diagnostic text is not shown", !outcome.Passkey.Contains("abc") && !outcome.Unknown.Contains("clientDataJSON"));
            because.ItsTrue("a category resolves to its default message", outcome.Expired == new PasskeyMessages().ExpiredChallenge);
            because.ItsTrue("an undefined category falls back to the unknown message", outcome.Undefined == "Algo salió mal.");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private sealed record CategoryOutcome(int Mismatched, bool CancellationFlag);

    private sealed record FrameworkOutcome(PasskeyErrorCategory Cancelled, PasskeyErrorCategory TaskCancelled, PasskeyErrorCategory HttpTimeout, PasskeyErrorCategory Http, PasskeyErrorCategory Timeout, PasskeyErrorCategory SystemJson, PasskeyErrorCategory NewtonsoftJson, PasskeyErrorCategory Format, PasskeyErrorCategory Other);

    private sealed record MessageOutcome(string Network, string Unknown, string Passkey, string Expired, string Undefined);
}
