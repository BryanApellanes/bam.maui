using System.Net;
using System.Text.Json;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

/// <summary>The lost-device key-set recovery (bam.maui#20, against bam.useraccounts#27): the HTTP contract and the client ceremony.</summary>
[UnitTestMenu("Key Set Recovery Should", Selector = "pkkr")]
public class KeySetRecoveryShould : UnitTestMenuContainer
{
    private const string Credential = "{\"id\":\"BAUG\",\"rawId\":\"BAUG\",\"type\":\"public-key\",\"response\":{\"clientDataJSON\":\"e30\"}}";
    private const string SessionCookie = ".Bam.Account=s3ss10n; Path=/; Secure; HttpOnly; SameSite=Lax";
    private static readonly DateTime Effective = new DateTime(2026, 11, 29, 12, 0, 0, DateTimeKind.Utc);

    public KeySetRecoveryShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void SpeakTheRecoveryContract()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("lists with a waiting recovery and with none, begins naming the lost passkey and without, completes one that ends the session, then cancels", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.OK, "{\"handle\":\"ada\"}", SessionCookie)
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[{\"id\":\"BAUG\",\"name\":\"Laptop\"}],\"pendingRecovery\":{\"id\":\"cmVj\",\"removes\":\"AQID\",\"heir\":\"BAUG\",\"effectiveUtc\":\"2026-11-29T12:00:00Z\"},\"keySetRecoverable\":false}")
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[],\"pendingRecovery\":null,\"keySetRecoverable\":true}")
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[],\"keySetUnrecoverable\":true}")
                .Respond(HttpStatusCode.OK, "{\"recoveryId\":\"cmVj\",\"options\":{\"challenge\":\"Y2g\",\"allowCredentials\":[{\"id\":\"BAUG\",\"type\":\"public-key\"}],\"userVerification\":\"required\"}}")
                .Respond(HttpStatusCode.OK, "{\"recoveryId\":\"cmVk\",\"options\":{\"challenge\":\"Y2g\"}}")
                .Respond(HttpStatusCode.Accepted, "{\"signedOut\":true,\"nextUrl\":\"/account/signedout\",\"effectiveUtc\":\"2026-11-29T12:00:00Z\"}")
                .Respond(HttpStatusCode.NoContent, null);
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
            backend.CompleteAuthenticationAsync(new PasskeyAuthenticationChallenge { Options = new WebAuthnRequestOptions("{}"), CeremonyId = "c-1" }, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();

            PasskeyList waiting = backend.GetPasskeyListAsync().GetAwaiter().GetResult();
            PasskeyList recoverable = backend.GetPasskeyListAsync().GetAwaiter().GetResult();
            PasskeyList older = backend.GetPasskeyListAsync().GetAwaiter().GetResult();
            PasskeyRecoveryChallenge named = backend.BeginKeySetRecoveryAsync("AQID", "BAUG").GetAwaiter().GetResult();
            PasskeyRecoveryChallenge heirOnly = backend.BeginKeySetRecoveryAsync(null, "BAUG").GetAwaiter().GetResult();
            PasskeyRecoveryStarted started = backend.CompleteKeySetRecoveryAsync(named, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();
            backend.CancelKeySetRecoveryAsync("cmVj").GetAwaiter().GetResult();

            List<RecordedRequest> requests = fake.Requests;
            using JsonDocument complete = JsonDocument.Parse(requests[6].Body!);
            PasskeyPendingRecovery pending = waiting.PendingRecovery!;
            return new ContractOutcome(
                pending.RecoveryId + "|" + pending.RemovedCredentialId + "|" + pending.HeirCredentialId + "|" + (pending.EffectiveUtc == Effective && pending.EffectiveUtc.Kind == DateTimeKind.Utc) + "|" + waiting.KeySetRecoverable + "|" + waiting.Passkeys.Count,
                (recoverable.PendingRecovery is null) + "|" + recoverable.KeySetRecoverable + "|" + recoverable.KeySetUnrecoverable,
                (older.PendingRecovery is null) + "|" + older.KeySetRecoverable + "|" + older.KeySetUnrecoverable,
                requests[4].Method + " " + requests[4].Uri + " " + requests[4].Body,
                requests[5].Body ?? "no body",
                named.RecoveryId + "|" + named.Options.Json.Contains("allowCredentials", StringComparison.Ordinal) + "|" + heirOnly.RecoveryId,
                requests[6].Method + " " + requests[6].Uri + " " + string.Join(",", complete.RootElement.EnumerateObject().Select(property => property.Name)) + "|" + complete.RootElement.GetProperty("recoveryId").GetString(),
                started.SignOut.SignedOut + "|" + started.SignOut.NextUrl + "|" + (started.EffectiveUtc == Effective),
                requests[7].Method + " " + requests[7].Uri + " " + requests[7].Body,
                requests[7].Cookie,
                string.Join(",", requests.Where(request => request.Method != "GET").Select(request => request.AccountHeader ?? "missing")),
                requests.Any(request => request.HasOrigin),
                named + "|" + pending);
        })
        .TheTest
        .ShouldPass<ContractOutcome>((because, outcome) =>
        {
            because.ItsTrue("a waiting recovery is read from the list: " + outcome.Waiting, outcome.Waiting == "cmVj|AQID|BAUG|True|False|1");
            because.ItsTrue("a null one is nothing waiting, with recovery possible and keySetUnrecoverable omitted: " + outcome.Recoverable, outcome.Recoverable == "True|True|False");
            because.ItsTrue("an omitted pendingRecovery and keySetRecoverable read as nothing, and keySetUnrecoverable is read: " + outcome.Older, outcome.Older == "True|False|True");
            because.ItsTrue("begin posts the lost passkey and the heir: " + outcome.NamedBegin, outcome.NamedBegin == "POST https://example.com/account/keyset/recovery/begin {\"removes\":\"AQID\",\"heir\":\"BAUG\"}");
            because.ItsTrue("and only the heir when no passkey holds the key: " + outcome.HeirOnlyBody, outcome.HeirOnlyBody == "{\"heir\":\"BAUG\"}");
            because.ItsTrue("begin returns the recovery id and the options as sent: " + outcome.Challenges, outcome.Challenges == "cmVj|True|cmVk");
            because.ItsTrue("complete posts the recovery id and the credential: " + outcome.Complete, outcome.Complete == "POST https://example.com/account/keyset/recovery/complete recoveryId,credential|cmVj");
            because.ItsTrue("and reads the 202's sign-out and date: " + outcome.Started, outcome.Started == "True|/account/signedout|True");
            because.ItsTrue("cancel posts an empty body to the recovery's path: " + outcome.Cancel, outcome.Cancel == "POST https://example.com/account/keyset/recovery/cmVj/cancel {}");
            because.ItsTrue("after a recovery that ended the session the cookie is forgotten", outcome.CookieAfter is null);
            because.ItsTrue("every state-changing call carries the header: " + outcome.Headers, !outcome.Headers.Contains("missing"));
            because.ItsTrue("and none an Origin", !outcome.AnyOrigin);
            because.ItsTrue("neither the challenge nor the waiting recovery prints its id: " + outcome.Printed, outcome.Printed == nameof(PasskeyRecoveryChallenge) + "|" + nameof(PasskeyPendingRecovery));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void MapEveryRecoveryRefusal()
    {
        After.Setup(reg => reg.Set(new List<string>()))
        .When<List<string>>("is refused at begin, complete and cancel with each code the endpoints send, and told when a too-new heir becomes eligible", (wrong, reg) =>
        {
            foreach ((HttpStatusCode status, string code) in new[]
            {
                (HttpStatusCode.NotFound, "NotFound"), (HttpStatusCode.BadRequest, "NotKeySet"), (HttpStatusCode.BadRequest, "InvalidHeir"),
                (HttpStatusCode.BadRequest, "RemovesRequired"), (HttpStatusCode.Conflict, "HeirTooNew"), (HttpStatusCode.Conflict, "RecoveryPending"),
                (HttpStatusCode.Conflict, "NoKeySet"), (HttpStatusCode.Conflict, "RotationUnavailable")
            })
            {
                Refusal(wrong, status, code, PasskeyErrorCategory.InvalidOptions, Stage.Begin);
            }
            Refusal(wrong, HttpStatusCode.BadRequest, "HandoverExpired", PasskeyErrorCategory.ExpiredChallenge, Stage.Complete);
            Refusal(wrong, HttpStatusCode.BadRequest, "ProofRefused", PasskeyErrorCategory.ServerVerificationFailed, Stage.Complete);
            Refusal(wrong, HttpStatusCode.Conflict, "KeySetChanged", PasskeyErrorCategory.InvalidOptions, Stage.Complete);
            Refusal(wrong, HttpStatusCode.Conflict, "HeirGone", PasskeyErrorCategory.InvalidOptions, Stage.Complete);
            Refusal(wrong, HttpStatusCode.Conflict, "RecoveryPending", PasskeyErrorCategory.InvalidOptions, Stage.Complete);
            Refusal(wrong, HttpStatusCode.NotFound, "NotFound", PasskeyErrorCategory.InvalidOptions, Stage.Cancel);
            Refusal(wrong, HttpStatusCode.Unauthorized, "NotSignedIn", PasskeyErrorCategory.NotSignedIn, Stage.Cancel);

            DateTime? eligible = EligibleFrom("{\"error\":\"HeirTooNew\",\"message\":\"x\",\"eligibleUtc\":\"2026-12-03T08:00:00Z\"}");
            DateTime? notADate = EligibleFrom("{\"error\":\"HeirTooNew\",\"message\":\"x\",\"eligibleUtc\":\"soon\"}");
            DateTime? otherCode = EligibleFrom("{\"error\":\"InvalidHeir\",\"message\":\"x\",\"eligibleUtc\":\"2026-12-03T08:00:00Z\"}");
            return new RefusalOutcome(string.Join("; ", wrong), eligible, notADate, otherCode);
        })
        .TheTest
        .ShouldPass<RefusalOutcome>((because, outcome) =>
        {
            because.ItsTrue("each refusal has its category and keeps its code, never the server's text: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("a too-new heir carries when it becomes eligible, as UTC", outcome.Eligible == new DateTime(2026, 12, 3, 8, 0, 0, DateTimeKind.Utc) && outcome.Eligible.Value.Kind == DateTimeKind.Utc);
            because.ItsTrue("a date that isn't one is dropped, not an error", outcome.NotADate is null);
            because.ItsTrue("and only HeirTooNew carries one", outcome.OtherCode is null);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseBadRecoveryAnswersAndUnsafeIds()
    {
        After.Setup(reg => reg.Set(new List<string>()))
        .When<List<string>>("gets begin, complete and list answers missing what the client needs, and ids that can't go in a path or body", (wrong, reg) =>
        {
            PasskeyRecoveryChallenge challenge = new PasskeyRecoveryChallenge { Options = new WebAuthnRequestOptions("{}"), RecoveryId = "cmVj" };
            Func<HttpPasskeyBackend, Task> begin = backend => backend.BeginKeySetRecoveryAsync(null, "BAUG");
            Func<HttpPasskeyBackend, Task> complete = backend => backend.CompleteKeySetRecoveryAsync(challenge, new WebAuthnAssertionResponse(Credential));
            Func<HttpPasskeyBackend, Task> list = backend => backend.GetPasskeyListAsync();
            Bad(wrong, "begin without recoveryId", "{\"options\":{}}", begin, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "begin with a blank recoveryId", "{\"recoveryId\":\" \",\"options\":{}}", begin, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "begin without options", "{\"recoveryId\":\"cmVj\"}", begin, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "complete without signedOut", "{\"effectiveUtc\":\"2026-11-29T12:00:00Z\"}", complete, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "complete without effectiveUtc", "{\"signedOut\":false}", complete, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "complete with an effectiveUtc that isn't a date", "{\"signedOut\":false,\"effectiveUtc\":\"later\"}", complete, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "a pendingRecovery that isn't an object", "{\"passkeys\":[],\"pendingRecovery\":\"cmVj\"}", list, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "a pendingRecovery id that can't go in a path", "{\"passkeys\":[],\"pendingRecovery\":{\"id\":\"a/b\",\"heir\":\"BAUG\",\"effectiveUtc\":\"2026-11-29T12:00:00Z\"}}", list, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "a pendingRecovery without an heir", "{\"passkeys\":[],\"pendingRecovery\":{\"id\":\"cmVj\",\"effectiveUtc\":\"2026-11-29T12:00:00Z\"}}", list, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "a pendingRecovery without a date", "{\"passkeys\":[],\"pendingRecovery\":{\"id\":\"cmVj\",\"heir\":\"BAUG\"}}", list, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "a keySetRecoverable that isn't true or false", "{\"passkeys\":[],\"keySetRecoverable\":\"yes\"}", list, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "a keySetUnrecoverable that isn't true or false", "{\"passkeys\":[],\"keySetUnrecoverable\":1}", list, PasskeyErrorCategory.InvalidServerResponse, 1);
            Bad(wrong, "an unsafe lost passkey id", "{}", backend => backend.BeginKeySetRecoveryAsync("../signout", "BAUG"), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an unsafe heir id", "{}", backend => backend.BeginKeySetRecoveryAsync(null, "a\"b"), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an unsafe recovery id to cancel", "{}", backend => backend.CancelKeySetRecoveryAsync("../../signout"), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "a blank recovery id to cancel", "{}", backend => backend.CancelKeySetRecoveryAsync(""), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "a challenge without an id", "{}", backend => backend.CompleteKeySetRecoveryAsync(challenge with { RecoveryId = "" }, new WebAuthnAssertionResponse(Credential)), PasskeyErrorCategory.InvalidOptions, 0);
            Bad(wrong, "an assertion that isn't JSON", "{}", backend => backend.CompleteKeySetRecoveryAsync(challenge, new WebAuthnAssertionResponse("not json")), PasskeyErrorCategory.PlatformFailure, 0);
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
    public void RunTheRecoveryCeremony()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("recovers to the session's passkey naming the lost one, recovers with no lost passkey, lists, then cancels", (fixture, reg) =>
        {
            PasskeyClient client = fixture.Client();
            fixture.Backend.RecoveryResult = new PasskeyRecoveryStarted { SignOut = new PasskeySignOut { SignedOut = false }, EffectiveUtc = Effective };
            PasskeyResult<PasskeyRecoveryStarted> named = Wait(client.RecoverKeySetAsync("phone", "laptop"));
            string sent = fixture.Backend.RecoveryRemoves + "|" + fixture.Backend.RecoveryHeir + "|" + fixture.Authenticator.AssertedWithJson + "|" + fixture.Backend.CompletedRecovery?.RecoveryId + "|" + fixture.Backend.CompletedAssertionJson;
            string log = fixture.Log.Joined;
            PasskeyResult<PasskeyRecoveryStarted> unnamed = Wait(client.RecoverKeySetAsync("phone", "  "));
            string? unnamedRemoves = fixture.Backend.RecoveryRemoves;

            fixture.Backend.Credentials = new List<PasskeyCredential> { new PasskeyCredential { CredentialId = "phone" } };
            fixture.Backend.PendingRecovery = new PasskeyPendingRecovery { RecoveryId = "recovery-1", HeirCredentialId = "phone", EffectiveUtc = Effective };
            fixture.Backend.KeySetRecoverable = true;
            PasskeyResult<PasskeyList> listed = Wait(client.GetPasskeyListAsync());
            PasskeyResult cancelled = Wait(client.CancelKeySetRecoveryAsync("recovery-1"));
            return new CeremonyOutcome(named.Value?.SignOut.SignedOut, named.Value?.EffectiveUtc, sent, log, unnamed.Succeeded, unnamedRemoves,
                listed.Value?.Passkeys.Count + "|" + listed.Value?.PendingRecovery?.RecoveryId + "|" + listed.Value?.KeySetRecoverable,
                cancelled.Succeeded + "|" + fixture.Backend.CancelledRecoveryId);
        })
        .TheTest
        .ShouldPass<CeremonyOutcome>((because, outcome) =>
        {
            because.ItsTrue("a recovery reports what the backend said", outcome.SignedOut == false && outcome.EffectiveUtc == Effective);
            because.ItsTrue("the lost passkey and heir go to begin, the heir's options to the prompt, its assertion back with the recovery: " + outcome.Sent, outcome.Sent == "laptop|phone|{\"challenge\":\"recovery\"}|recovery-1|{\"id\":\"asserted\"}");
            because.ItsTrue("begin, the prompt, then complete: " + outcome.Log, outcome.Log == "backend.beginRecovery,authenticator.assert,backend.completeRecovery");
            because.ItsTrue("a blank lost passkey is none", outcome.UnnamedSucceeded && outcome.UnnamedRemoves is null);
            because.ItsTrue("the list carries the key set's state: " + outcome.Listed, outcome.Listed == "1|recovery-1|True");
            because.ItsTrue("and cancelling names the recovery: " + outcome.Cancelled, outcome.Cancelled == "True|recovery-1");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseARecoveryThatCantWork()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("recovers with no heir, to the lost passkey, with management off, on a device without passkeys, with no challenge, with no answer, dismissed, refused as too new, and cancels with no id or with management off", (fixture, reg) =>
        {
            PasskeyErrorCategory? noHeir = Wait(fixture.Client().RecoverKeySetAsync(" ", "laptop")).Error?.Category;
            PasskeyErrorCategory? toItself = Wait(fixture.Client().RecoverKeySetAsync("laptop", "laptop")).Error?.Category;
            string earlyLog = fixture.Log.Joined;

            fixture.Configuration.Features.EnableManagement = false;
            PasskeyErrorCategory? off = Wait(fixture.Client().RecoverKeySetAsync("phone", "laptop")).Error?.Category;
            PasskeyErrorCategory? cancelOff = Wait(fixture.Client().CancelKeySetRecoveryAsync("recovery-1")).Error?.Category;
            fixture.Configuration.Features.EnableManagement = true;
            string offLog = fixture.Log.Joined;

            fixture.Authenticator.Capabilities = PasskeyCapabilities.Unsupported();
            PasskeyErrorCategory? unsupported = Wait(fixture.Client().RecoverKeySetAsync("phone", "laptop")).Error?.Category;
            string unsupportedLog = fixture.Log.Joined;
            fixture.Authenticator.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.Available, SupportsDiscoverableCredentials = true, Platform = "Test" };

            fixture.Backend.ReturnNullOptions = true;
            PasskeyErrorCategory? noChallenge = Wait(fixture.Client().RecoverKeySetAsync("phone", "laptop")).Error?.Category;
            fixture.Backend.ReturnNullOptions = false;
            string noChallengeLog = fixture.Log.Joined;

            fixture.Backend.ReturnNullRecovery = true;
            PasskeyErrorCategory? noAnswer = Wait(fixture.Client().RecoverKeySetAsync("phone", "laptop")).Error?.Category;
            fixture.Backend.ReturnNullRecovery = false;

            fixture.Authenticator.AssertFailure = new OperationCanceledException();
            PasskeyErrorCategory? dismissed = Wait(fixture.Client().RecoverKeySetAsync("phone", "laptop")).Error?.Category;
            fixture.Authenticator.AssertFailure = null;
            string dismissedTail = fixture.Log.Calls[fixture.Log.Calls.Count - 1];

            DateTime eligibleUtc = new DateTime(2026, 12, 3, 8, 0, 0, DateTimeKind.Utc);
            fixture.Backend.Failures["backend.beginRecovery"] = new PasskeyException(PasskeyErrorCategory.InvalidOptions, "too new", null, "HeirTooNew", eligibleUtc);
            PasskeyError? tooNew = Wait(fixture.Client().RecoverKeySetAsync("phone", "laptop")).Error;
            fixture.Backend.Failures.Clear();

            PasskeyErrorCategory? cancelNoId = Wait(fixture.Client().CancelKeySetRecoveryAsync(" ")).Error?.Category;
            return new RefusedOutcome(noHeir, toItself, earlyLog, off, cancelOff, offLog, unsupported, unsupportedLog, noChallenge, noChallengeLog, noAnswer, dismissed, dismissedTail,
                tooNew?.Category, tooNew?.BackendCode, tooNew?.EligibleUtc == eligibleUtc, tooNew?.Message == fixture.Configuration.Messages.For(PasskeyErrorCategory.InvalidOptions), cancelNoId,
                fixture.Log.Calls.Contains("backend.cancelRecovery"));
        })
        .TheTest
        .ShouldPass<RefusedOutcome>((because, outcome) =>
        {
            because.ItsTrue("a recovery needs an heir", outcome.NoHeir == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("and the key can't go to the lost passkey", outcome.ToItself == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("neither asks the backend or the prompt: " + outcome.EarlyLog, outcome.EarlyLog.Length == 0);
            because.ItsTrue("management off refuses recovering and cancelling: " + outcome.OffLog, outcome.Off == PasskeyErrorCategory.FeatureDisabled && outcome.CancelOff == PasskeyErrorCategory.FeatureDisabled && outcome.OffLog.Length == 0);
            because.ItsTrue("a device that can't run the prompt refuses it before the backend is asked: " + outcome.UnsupportedLog, outcome.Unsupported == PasskeyErrorCategory.UnsupportedOperatingSystem && outcome.UnsupportedLog.Length == 0);
            because.ItsTrue("a begin without a challenge is an invalid response, with no prompt: " + outcome.NoChallengeLog, outcome.NoChallenge == PasskeyErrorCategory.InvalidServerResponse && outcome.NoChallengeLog == "backend.beginRecovery");
            because.ItsTrue("so is a complete that says nothing", outcome.NoAnswer == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a dismissed prompt is a cancellation and nothing is completed: " + outcome.DismissedTail, outcome.Dismissed == PasskeyErrorCategory.UserCancelled && outcome.DismissedTail == "authenticator.assert");
            because.ItsTrue("a too-new heir keeps its code and when it becomes eligible, with the app's message", outcome.TooNew == PasskeyErrorCategory.InvalidOptions && outcome.TooNewCode == "HeirTooNew" && outcome.TooNewEligible && outcome.TooNewMessage);
            because.ItsTrue("cancelling needs an id, and the backend isn't asked without one", outcome.CancelNoId == PasskeyErrorCategory.InvalidOptions && !outcome.CancelAsked);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private enum Stage
    {
        Begin,
        Complete,
        Cancel
    }

    private static void Refusal(List<string> wrong, HttpStatusCode status, string code, PasskeyErrorCategory expected, Stage stage)
    {
        FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(status, "{\"error\":\"" + code + "\",\"message\":\"server text\"}");
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
        PasskeyRecoveryChallenge challenge = new PasskeyRecoveryChallenge { Options = new WebAuthnRequestOptions("{}"), RecoveryId = "cmVj" };
        PasskeyException? refused = stage switch
        {
            Stage.Begin => Caught(() => backend.BeginKeySetRecoveryAsync("AQID", "BAUG")),
            Stage.Complete => Caught(() => backend.CompleteKeySetRecoveryAsync(challenge, new WebAuthnAssertionResponse(Credential))),
            _ => Caught(() => backend.CancelKeySetRecoveryAsync("cmVj"))
        };
        if (refused?.Category != expected || refused.BackendCode != code || refused.Message.Contains("server text", StringComparison.Ordinal))
        {
            wrong.Add(stage + " " + code + ": " + (refused?.Category.ToString() ?? "no failure") + "/" + (refused?.BackendCode ?? "no code"));
        }
    }

    private static DateTime? EligibleFrom(string body)
    {
        FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(HttpStatusCode.Conflict, body);
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
        return Caught(() => backend.BeginKeySetRecoveryAsync(null, "BAUG"))?.EligibleUtc;
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

    private sealed record ContractOutcome(string Waiting, string Recoverable, string Older, string NamedBegin, string HeirOnlyBody, string Challenges, string Complete, string Started, string Cancel, string? CookieAfter, string Headers, bool AnyOrigin, string Printed);

    private sealed record RefusalOutcome(string Wrong, DateTime? Eligible, DateTime? NotADate, DateTime? OtherCode);

    private sealed record CeremonyOutcome(bool? SignedOut, DateTime? EffectiveUtc, string Sent, string Log, bool UnnamedSucceeded, string? UnnamedRemoves, string Listed, string Cancelled);

    private sealed record RefusedOutcome(PasskeyErrorCategory? NoHeir, PasskeyErrorCategory? ToItself, string EarlyLog, PasskeyErrorCategory? Off, PasskeyErrorCategory? CancelOff, string OffLog,
        PasskeyErrorCategory? Unsupported, string UnsupportedLog, PasskeyErrorCategory? NoChallenge, string NoChallengeLog, PasskeyErrorCategory? NoAnswer, PasskeyErrorCategory? Dismissed, string DismissedTail,
        PasskeyErrorCategory? TooNew, string? TooNewCode, bool TooNewEligible, bool TooNewMessage, PasskeyErrorCategory? CancelNoId, bool CancelAsked);
}
