using System.Net;
using System.Text.Json;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

/// <summary>The key-set handover (bam.maui#7, against bam.useraccounts#20): the HTTP contract and the client ceremony.</summary>
[UnitTestMenu("Key Set Handover Should", Selector = "pkks")]
public class KeySetHandoverShould : UnitTestMenuContainer
{
    private const string Credential = "{\"id\":\"AQID\",\"rawId\":\"AQID\",\"type\":\"public-key\",\"response\":{\"clientDataJSON\":\"e30\"}}";
    private const string SessionCookie = ".Bam.Account=s3ss10n; Path=/; Secure; HttpOnly; SameSite=Lax";

    public KeySetHandoverShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void SpeakTheHandoverContract()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("is refused a plain removal of the key-set passkey, removes one keeping the key set, hands over with and without an heir, then lists", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.OK, "{\"handle\":\"ada\"}", SessionCookie)
                .Respond(HttpStatusCode.Conflict, "{\"error\":\"KeySetHandoverRequired\",\"message\":\"x\"}")
                .Respond(HttpStatusCode.OK, "{\"signedOut\":false,\"nextUrl\":null}")
                .Respond(HttpStatusCode.OK, "{\"handoverId\":\"h-1\",\"options\":{\"challenge\":\"Y2g\",\"allowCredentials\":[{\"id\":\"AQID\",\"type\":\"public-key\"}]}}")
                .Respond(HttpStatusCode.OK, "{\"handoverId\":\"h-2\",\"options\":{\"challenge\":\"Y2g\"}}")
                .Respond(HttpStatusCode.OK, "{\"signedOut\":true,\"nextUrl\":\"/account/signedout\"}")
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[]}");
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
            backend.CompleteAuthenticationAsync(new PasskeyAuthenticationChallenge { Options = new WebAuthnRequestOptions("{}"), CeremonyId = "c-1" }, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();

            PasskeyException? refused = Caught(() => backend.DeletePasskeyAsync("AQID"));
            PasskeySignOut kept = backend.DeletePasskeyAsync("AQID", PasskeyKeySetRemoval.KeepKeySet).GetAwaiter().GetResult();
            PasskeyHandoverChallenge named = backend.BeginKeySetHandoverAsync("AQID", "BAUG").GetAwaiter().GetResult();
            PasskeyHandoverChallenge unnamed = backend.BeginKeySetHandoverAsync("AQID", null).GetAwaiter().GetResult();
            PasskeySignOut handedOver = backend.CompleteKeySetHandoverAsync("AQID", named, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();
            backend.GetPasskeysAsync().GetAwaiter().GetResult();

            List<RecordedRequest> requests = fake.Requests;
            using JsonDocument complete = JsonDocument.Parse(requests[5].Body!);
            return new ContractOutcome(
                refused?.Category, refused?.BackendCode,
                requests[1].Method + " " + requests[1].Uri,
                requests[2].Method + " " + requests[2].Uri + "|" + kept.SignedOut,
                requests[3].Method + " " + requests[3].Uri + " " + requests[3].Body,
                requests[4].Body ?? "no body",
                named.HandoverId + "|" + named.Options.Json.Contains("allowCredentials", StringComparison.Ordinal) + "|" + unnamed.HandoverId,
                requests[5].Method + " " + requests[5].Uri + " " + string.Join(",", complete.RootElement.EnumerateObject().Select(property => property.Name)) + "|" + complete.RootElement.GetProperty("handoverId").GetString(),
                handedOver.SignedOut + "|" + handedOver.NextUrl,
                requests[6].Cookie,
                string.Join(",", requests.Select(request => request.AccountHeader ?? "missing")),
                requests.Any(request => request.HasOrigin),
                named.ToString());
        })
        .TheTest
        .ShouldPass<ContractOutcome>((because, outcome) =>
        {
            because.ItsTrue("a plain removal of the key-set passkey is refused with the handover code: " + outcome.RefusedCode, outcome.Refused == PasskeyErrorCategory.InvalidOptions && outcome.RefusedCode == "KeySetHandoverRequired");
            because.ItsTrue("as a DELETE on the passkey: " + outcome.PlainDelete, outcome.PlainDelete == "DELETE https://example.com/account/passkeys/AQID");
            because.ItsTrue("keeping the key set adds keySet=keep: " + outcome.KeptDelete, outcome.KeptDelete == "DELETE https://example.com/account/passkeys/AQID?keySet=keep|False");
            because.ItsTrue("begin posts the heir: " + outcome.NamedBegin, outcome.NamedBegin == "POST https://example.com/account/passkeys/AQID/handover/begin {\"heir\":\"BAUG\"}");
            because.ItsTrue("and an empty object without one: " + outcome.UnnamedBody, outcome.UnnamedBody == "{}");
            because.ItsTrue("begin returns the handover id and the options as sent: " + outcome.Challenges, outcome.Challenges == "h-1|True|h-2");
            because.ItsTrue("complete posts the handover id and the credential: " + outcome.Complete, outcome.Complete == "POST https://example.com/account/passkeys/AQID/handover/complete handoverId,credential|h-1");
            because.ItsTrue("and reports the sign-out: " + outcome.HandedOver, outcome.HandedOver == "True|/account/signedout");
            because.ItsTrue("after which the session cookie is forgotten", outcome.CookieAfter is null);
            because.ItsTrue("every call carries the header: " + outcome.Headers, !outcome.Headers.Contains("missing"));
            because.ItsTrue("and none an Origin", !outcome.AnyOrigin);
            because.ItsTrue("the challenge never prints its id: " + outcome.Printed, outcome.Printed == nameof(PasskeyHandoverChallenge));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void MapEveryHandoverRefusal()
    {
        After.Setup(reg => reg.Set(new List<string>()))
        .When<List<string>>("is refused at begin and complete with each code the endpoints send", (wrong, reg) =>
        {
            Refusal(wrong, HttpStatusCode.NotFound, "NotFound", PasskeyErrorCategory.InvalidOptions, true);
            Refusal(wrong, HttpStatusCode.Conflict, "LastCredential", PasskeyErrorCategory.InvalidOptions, true);
            Refusal(wrong, HttpStatusCode.BadRequest, "InvalidHeir", PasskeyErrorCategory.InvalidOptions, true);
            Refusal(wrong, HttpStatusCode.BadRequest, "NotKeySet", PasskeyErrorCategory.InvalidOptions, true);
            Refusal(wrong, HttpStatusCode.Conflict, "RotationUnavailable", PasskeyErrorCategory.InvalidOptions, true);
            Refusal(wrong, HttpStatusCode.BadRequest, "HandoverExpired", PasskeyErrorCategory.ExpiredChallenge, false);
            Refusal(wrong, HttpStatusCode.BadRequest, "ProofRefused", PasskeyErrorCategory.ServerVerificationFailed, false);
            Refusal(wrong, HttpStatusCode.Conflict, "KeySetChanged", PasskeyErrorCategory.InvalidOptions, false);
            Refusal(wrong, HttpStatusCode.Conflict, "HeirGone", PasskeyErrorCategory.InvalidOptions, false);
            Refusal(wrong, HttpStatusCode.Unauthorized, "NotSignedIn", PasskeyErrorCategory.NotSignedIn, true);
            return string.Join("; ", wrong);
        })
        .TheTest
        .ShouldPass<string>((because, wrong) =>
        {
            because.ItsTrue("each refusal has its category and keeps its code: " + wrong, wrong.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseBadHandoverAnswersAndUnsafeIds()
    {
        After.Setup(reg => reg.Set(new List<string>()))
        .When<List<string>>("gets begin answers without an id or options, a complete without signedOut, and ids that can't go in a path", (wrong, reg) =>
        {
            PasskeyHandoverChallenge challenge = new PasskeyHandoverChallenge { Options = new WebAuthnRequestOptions("{}"), HandoverId = "h-1" };
            Bad(wrong, "begin without handoverId", "{\"options\":{}}", backend => backend.BeginKeySetHandoverAsync("AQID", null), PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "begin with a blank handoverId", "{\"handoverId\":\" \",\"options\":{}}", backend => backend.BeginKeySetHandoverAsync("AQID", null), PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "begin without options", "{\"handoverId\":\"h-1\"}", backend => backend.BeginKeySetHandoverAsync("AQID", null), PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "complete without signedOut", "{\"nextUrl\":null}", backend => backend.CompleteKeySetHandoverAsync("AQID", challenge, new WebAuthnAssertionResponse(Credential)), PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "an unsafe passkey id", "{}", backend => backend.BeginKeySetHandoverAsync("../signout", null), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an unsafe heir id", "{}", backend => backend.BeginKeySetHandoverAsync("AQID", "a/b"), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an unsafe id at complete", "{}", backend => backend.CompleteKeySetHandoverAsync("a?b", challenge, new WebAuthnAssertionResponse(Credential)), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an unsafe id kept", "{}", backend => backend.DeletePasskeyAsync("a b", PasskeyKeySetRemoval.KeepKeySet), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "a challenge without an id", "{}", backend => backend.CompleteKeySetHandoverAsync("AQID", challenge with { HandoverId = "" }, new WebAuthnAssertionResponse(Credential)), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an assertion that isn't JSON", "{}", backend => backend.CompleteKeySetHandoverAsync("AQID", challenge, new WebAuthnAssertionResponse("not json")), PasskeyErrorCategory.PlatformFailure, 0);
            return string.Join("; ", wrong);
        })
        .TheTest
        .ShouldPass<string>((because, wrong) =>
        {
            because.ItsTrue("each is refused with its category, and nothing unsafe is sent: " + wrong, wrong.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RunTheHandoverCeremony()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("hands the key set to a named heir, then to the session's passkey, then removes keeping the key set", (fixture, reg) =>
        {
            PasskeyClient client = fixture.Client();
            fixture.Backend.HandoverResult = new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" };
            PasskeyResult<PasskeySignOut> named = Wait(client.HandOverKeySetAsync("phone", "laptop"));
            string sent = fixture.Backend.HandoverCredentialId + "|" + fixture.Backend.HandoverHeir + "|" + fixture.Authenticator.AssertedWithJson + "|" + fixture.Backend.CompletedHandover?.HandoverId + "|" + fixture.Backend.CompletedAssertionJson;
            string log = fixture.Log.Joined;
            PasskeyResult<PasskeySignOut> unnamed = Wait(client.HandOverKeySetAsync("phone", "  "));
            string? unnamedHeir = fixture.Backend.HandoverHeir;
            Wait(client.DeletePasskeyAsync("phone", PasskeyKeySetRemoval.KeepKeySet));
            PasskeyKeySetRemoval? kept = fixture.Backend.DeletedKeySet;
            Wait(client.DeletePasskeyAsync("phone"));
            return new CeremonyOutcome(named.Value?.SignedOut, named.Value?.NextUrl, sent, log, unnamed.Succeeded, unnamedHeir, kept, fixture.Backend.DeletedKeySet);
        })
        .TheTest
        .ShouldPass<CeremonyOutcome>((because, outcome) =>
        {
            because.ItsTrue("a handover reports what the backend said", outcome.SignedOut == true && outcome.NextUrl == "/account/signedout");
            because.ItsTrue("the removed passkey's options go to the prompt and its assertion back with the handover: " + outcome.Sent, outcome.Sent == "phone|laptop|{\"challenge\":\"handover\"}|handover-1|{\"id\":\"asserted\"}");
            because.ItsTrue("begin, the prompt, then complete: " + outcome.Log, outcome.Log == "backend.beginHandover,authenticator.assert,backend.completeHandover");
            because.ItsTrue("a blank heir leaves the choice to the backend", outcome.UnnamedSucceeded && outcome.UnnamedHeir is null);
            because.ItsTrue("removing while keeping the key set says so", outcome.Kept == PasskeyKeySetRemoval.KeepKeySet);
            because.ItsTrue("and a plain removal leaves it to the backend", outcome.Plain == PasskeyKeySetRemoval.Default);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseAHandoverThatCantWork()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("hands a passkey's key set to itself, with management off, on a device without passkeys, with no challenge, refused, dismissed, and with an unknown removal choice", (fixture, reg) =>
        {
            PasskeyErrorCategory? toItself = Wait(fixture.Client().HandOverKeySetAsync("phone", "phone")).Error?.Category;
            string toItselfLog = fixture.Log.Joined;

            fixture.Configuration.Features.EnableManagement = false;
            PasskeyErrorCategory? off = Wait(fixture.Client().HandOverKeySetAsync("phone", "laptop")).Error?.Category;
            fixture.Configuration.Features.EnableManagement = true;
            string offLog = fixture.Log.Joined;

            fixture.Authenticator.Capabilities = PasskeyCapabilities.Unsupported();
            PasskeyErrorCategory? unsupported = Wait(fixture.Client().HandOverKeySetAsync("phone", "laptop")).Error?.Category;
            string unsupportedLog = fixture.Log.Joined;
            fixture.Authenticator.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.Available, SupportsDiscoverableCredentials = true, Platform = "Test" };

            fixture.Backend.ReturnNullOptions = true;
            PasskeyErrorCategory? noChallenge = Wait(fixture.Client().HandOverKeySetAsync("phone", "laptop")).Error?.Category;
            fixture.Backend.ReturnNullOptions = false;
            string noChallengeLog = fixture.Log.Joined;

            fixture.Backend.Failures["backend.completeHandover"] = new PasskeyException(PasskeyErrorCategory.InvalidOptions, "refused", null, "KeySetChanged");
            PasskeyError? refused = Wait(fixture.Client().HandOverKeySetAsync("phone", "laptop")).Error;
            fixture.Backend.Failures.Clear();

            fixture.Authenticator.AssertFailure = new OperationCanceledException();
            PasskeyResult<PasskeySignOut> dismissed = Wait(fixture.Client().HandOverKeySetAsync("phone", "laptop"));
            fixture.Authenticator.AssertFailure = null;
            string dismissedTail = fixture.Log.Calls[fixture.Log.Calls.Count - 1];

            PasskeyErrorCategory? unknownChoice = Wait(fixture.Client().DeletePasskeyAsync("phone", (PasskeyKeySetRemoval)7)).Error?.Category;
            return new RefusedOutcome(toItself, toItselfLog, off, offLog, unsupported, unsupportedLog, noChallenge, noChallengeLog, refused?.Category, refused?.BackendCode, dismissed.Error?.Category, dismissedTail, unknownChoice, fixture.Log.Calls[fixture.Log.Calls.Count - 1]);
        })
        .TheTest
        .ShouldPass<RefusedOutcome>((because, outcome) =>
        {
            because.ItsTrue("the key set can't go to the passkey being removed", outcome.ToItself == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("and nothing is asked of the backend or the prompt: " + outcome.ToItselfLog, outcome.ToItselfLog.Length == 0);
            because.ItsTrue("management off refuses it", outcome.Off == PasskeyErrorCategory.FeatureDisabled && outcome.OffLog.Length == 0);
            because.ItsTrue("a device that can't run the prompt refuses it before the backend is asked: " + outcome.UnsupportedLog, outcome.Unsupported == PasskeyErrorCategory.UnsupportedOperatingSystem && outcome.UnsupportedLog.Length == 0);
            because.ItsTrue("a begin without a challenge is an invalid response, with no prompt: " + outcome.NoChallengeLog, outcome.NoChallenge == PasskeyErrorCategory.InvalidServerResponse && outcome.NoChallengeLog == "backend.beginHandover");
            because.ItsTrue("a refusal keeps the backend's code", outcome.Refused == PasskeyErrorCategory.InvalidOptions && outcome.RefusedCode == "KeySetChanged");
            because.ItsTrue("a dismissed prompt is a cancellation and nothing is completed: " + outcome.DismissedTail, outcome.Dismissed == PasskeyErrorCategory.UserCancelled && outcome.DismissedTail == "authenticator.assert");
            because.ItsTrue("an unknown removal choice is refused before the backend: " + outcome.UnknownTail, outcome.UnknownChoice == PasskeyErrorCategory.InvalidOptions && outcome.UnknownTail == "authenticator.assert");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static void Refusal(List<string> wrong, HttpStatusCode status, string code, PasskeyErrorCategory expected, bool atBegin)
    {
        FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(status, "{\"error\":\"" + code + "\",\"message\":\"server text\"}");
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
        PasskeyHandoverChallenge challenge = new PasskeyHandoverChallenge { Options = new WebAuthnRequestOptions("{}"), HandoverId = "h-1" };
        PasskeyException? refused = atBegin
            ? Caught(() => backend.BeginKeySetHandoverAsync("AQID", "BAUG"))
            : Caught(() => backend.CompleteKeySetHandoverAsync("AQID", challenge, new WebAuthnAssertionResponse(Credential)));
        if (refused?.Category != expected || refused.BackendCode != code || refused.Message.Contains("server text", StringComparison.Ordinal))
        {
            wrong.Add(code + ": " + (refused?.Category.ToString() ?? "no failure") + "/" + (refused?.BackendCode ?? "no code"));
        }
    }

    private static void Bad(List<string> wrong, string name, string body, Func<HttpPasskeyBackend, Task> call, PasskeyErrorCategory expected, int expectedRequests)
    {
        FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(HttpStatusCode.OK, body);
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
        PasskeyErrorCategory? got = Caught(() => call(backend))?.Category;
        if (got != expected || fake.Requests.Count != expectedRequests)
        {
            wrong.Add(name + ": " + (got?.ToString() ?? "no failure") + ", " + fake.Requests.Count + " sent");
        }
    }

    private static PasskeyException? Caught(Func<Task> call)
    {
        try
        {
            call().GetAwaiter().GetResult();
            return null;
        }
        catch (PasskeyException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            return new PasskeyException(PasskeyErrorCategory.Unknown, exception.GetType().Name);
        }
    }

    private static TResult Wait<TResult>(Task<TResult> task)
    {
        return task.GetAwaiter().GetResult();
    }

    private sealed record ContractOutcome(PasskeyErrorCategory? Refused, string? RefusedCode, string PlainDelete, string KeptDelete, string NamedBegin, string UnnamedBody, string Challenges, string Complete, string HandedOver, string? CookieAfter, string Headers, bool AnyOrigin, string Printed);

    private sealed record CeremonyOutcome(bool? SignedOut, string? NextUrl, string Sent, string Log, bool UnnamedSucceeded, string? UnnamedHeir, PasskeyKeySetRemoval? Kept, PasskeyKeySetRemoval? Plain);

    private sealed record RefusedOutcome(PasskeyErrorCategory? ToItself, string ToItselfLog, PasskeyErrorCategory? Off, string OffLog, PasskeyErrorCategory? Unsupported, string UnsupportedLog,
        PasskeyErrorCategory? NoChallenge, string NoChallengeLog, PasskeyErrorCategory? Refused, string? RefusedCode, PasskeyErrorCategory? Dismissed, string DismissedTail, PasskeyErrorCategory? UnknownChoice, string UnknownTail);
}
