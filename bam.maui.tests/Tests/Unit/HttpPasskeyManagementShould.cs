using System.Net;
using System.Text.Json;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Http Passkey Management Should", Selector = "pkhm")]
public class HttpPasskeyManagementShould : UnitTestMenuContainer
{
    private const string Credential = "{\"id\":\"AQID\",\"rawId\":\"AQID\",\"type\":\"public-key\",\"response\":{\"clientDataJSON\":\"e30\"}}";
    private const string SessionCookie = ".Bam.Account=s3ss10n; Path=/; Secure; HttpOnly; SameSite=Lax";

    public HttpPasskeyManagementShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ManageTheSignedInAccount()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("signs up without a profile, lists, adds a named passkey, renames and clears a name, saves the profile, then removes the session's passkey", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.OK, "{\"handle\":\"ada\",\"displayName\":\"ada\",\"nextUrl\":\"/\",\"profileCreated\":false}", SessionCookie)
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[{\"id\":\"AQID\",\"name\":null,\"createdUtc\":\"2026-11-01T10:00:00Z\",\"current\":true,\"registeredKeySet\":true},{\"id\":\"BAUG\",\"name\":\"Laptop\",\"createdUtc\":\"2026-11-02T10:00:00Z\",\"current\":false,\"registeredKeySet\":false},{\"id\":\"BwgJ\"}]}")
                .Respond(HttpStatusCode.OK, "{\"options\":{\"rp\":{\"id\":\"example.com\"}}}")
                .Respond(HttpStatusCode.OK, "{\"credentialId\":\"CgsM\"}")
                .Respond(HttpStatusCode.NoContent, null)
                .Respond(HttpStatusCode.NoContent, null)
                .Respond(HttpStatusCode.NoContent, null)
                .Respond(HttpStatusCode.OK, "{\"signedOut\":true,\"nextUrl\":\"/account/signedout\"}")
                .Respond(HttpStatusCode.Unauthorized, "{\"error\":\"NotSignedIn\",\"message\":\"x\"}");
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
            PasskeyRegistrationRequest form = new PasskeyRegistrationRequest { Handle = "ada", DisplayName = "Ada" };

            PasskeySignIn signIn = backend.CompleteRegistrationAsync(form, new WebAuthnRegistrationResponse(Credential)).GetAwaiter().GetResult();
            IReadOnlyList<PasskeyCredential> passkeys = backend.GetPasskeysAsync().GetAwaiter().GetResult();
            backend.BeginAdditionalRegistrationAsync().GetAwaiter().GetResult();
            string added = backend.CompleteAdditionalRegistrationAsync(new WebAuthnRegistrationResponse(Credential), "My phone").GetAwaiter().GetResult();
            backend.RenamePasskeyAsync("BAUG", "Work laptop").GetAwaiter().GetResult();
            backend.RenamePasskeyAsync("BAUG", "  ").GetAwaiter().GetResult();
            backend.SaveProfileAsync(new PasskeyProfile { DisplayName = "Ada", Email = "ada@example.com" }).GetAwaiter().GetResult();
            PasskeySignOut removed = backend.DeletePasskeyAsync("AQID").GetAwaiter().GetResult();
            PasskeyException? afterRemoval = Caught(() => backend.GetPasskeysAsync());

            List<RecordedRequest> requests = fake.Requests;
            using JsonDocument addBody = JsonDocument.Parse(requests[3].Body!);
            using JsonDocument profileBody = JsonDocument.Parse(requests[6].Body!);
            return new ManageOutcome(
                signIn.ProfileCreated,
                string.Join(";", passkeys.Select(Describe)),
                Members(addBody.RootElement) + "|" + addBody.RootElement.GetProperty("name").GetString(),
                added,
                requests[4].Method + " " + requests[4].Uri + " " + requests[4].Body,
                requests[5].Body ?? "no body",
                requests[6].Method + " " + requests[6].Uri + " " + Members(profileBody.RootElement),
                string.Join(",", requests.Skip(1).Select(request => request.Cookie ?? "none")),
                string.Join(",", requests.Select(request => request.AccountHeader ?? "missing")),
                requests.Any(request => request.HasOrigin),
                requests[7].Method + " " + requests[7].Uri,
                removed.SignedOut + "|" + removed.NextUrl,
                afterRemoval?.Category);
        })
        .TheTest
        .ShouldPass<ManageOutcome>((because, outcome) =>
        {
            because.ItsTrue("a sign-up without its profile says so", !outcome.ProfileCreated);
            because.ItsTrue("the list carries the session and key-set flags, and leaves unreported ones null: " + outcome.Passkeys,
                outcome.Passkeys == "AQID|-|current=True|keyset=True;BAUG|Laptop|current=False|keyset=False;BwgJ|-|current=null|keyset=null");
            because.ItsTrue("adding sends the credential and the name: " + outcome.AddMembers, outcome.AddMembers == "credential,name|My phone");
            because.ItsTrue("and returns the new id", outcome.Added == "CgsM");
            because.ItsTrue("a rename posts the name to the passkey: " + outcome.Rename, outcome.Rename == "POST https://example.com/account/passkeys/BAUG/name {\"name\":\"Work laptop\"}");
            because.ItsTrue("a blank name clears it: " + outcome.ClearedName, outcome.ClearedName == "{\"name\":null}");
            because.ItsTrue("the profile posts the name and email: " + outcome.Profile, outcome.Profile == "POST https://example.com/account/profile displayName,email");
            because.ItsTrue("the session rides on every signed-in call, and not after the session ended: " + outcome.Cookies,
                outcome.Cookies == ".Bam.Account=s3ss10n,.Bam.Account=s3ss10n,.Bam.Account=s3ss10n,.Bam.Account=s3ss10n,.Bam.Account=s3ss10n,.Bam.Account=s3ss10n,.Bam.Account=s3ss10n,none");
            because.ItsTrue("every call carries the header: " + outcome.Headers, !outcome.Headers.Contains("missing"));
            because.ItsTrue("and none an Origin", !outcome.AnyOrigin);
            because.ItsTrue("removal is a DELETE on the passkey: " + outcome.Delete, outcome.Delete == "DELETE https://example.com/account/passkeys/AQID");
            because.ItsTrue("removing the session's passkey reports the sign-out: " + outcome.Removed, outcome.Removed == "True|/account/signedout");
            because.ItsTrue("the next call is not signed in", outcome.AfterRemoval == PasskeyErrorCategory.NotSignedIn);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ForgetTheSessionOnSignOut()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("signs in, removes a passkey that keeps the session, signs out, then calls again", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.OK, "{\"handle\":\"ada\"}", SessionCookie)
                .Respond(HttpStatusCode.OK, "{\"signedOut\":false,\"nextUrl\":null}")
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[]}")
                .Respond(HttpStatusCode.OK, "{\"signedOut\":false,\"nextUrl\":\"//evil.example\"}")
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[]}");
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);

            PasskeySignIn signIn = backend.CompleteAuthenticationAsync(new PasskeyAuthenticationChallenge { Options = new WebAuthnRequestOptions("{}"), CeremonyId = "c-1" }, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();
            PasskeySignOut kept = backend.DeletePasskeyAsync("BAUG").GetAwaiter().GetResult();
            backend.GetPasskeysAsync().GetAwaiter().GetResult();
            PasskeySignOut signedOut = backend.SignOutAsync().GetAwaiter().GetResult();
            backend.GetPasskeysAsync().GetAwaiter().GetResult();

            List<RecordedRequest> requests = fake.Requests;
            return new SignOutOutcome(
                signIn.ProfileCreated,
                kept.SignedOut,
                requests[2].Cookie,
                requests[3].Method + " " + requests[3].Uri + " " + requests[3].Body,
                signedOut.SignedOut + "|" + (signedOut.NextUrl ?? "null"),
                requests[4].Cookie);
        })
        .TheTest
        .ShouldPass<SignOutOutcome>((because, outcome) =>
        {
            because.ItsTrue("a sign-in that doesn't mention the profile has one", outcome.ProfileCreated);
            because.ItsTrue("removing another passkey keeps the session", !outcome.KeptSignedOut);
            because.ItsTrue("so its cookie is still sent: " + outcome.CookieAfterRemoval, outcome.CookieAfterRemoval == ".Bam.Account=s3ss10n");
            because.ItsTrue("sign-out posts an empty object: " + outcome.SignOut, outcome.SignOut == "POST https://example.com/account/signout {}");
            because.ItsTrue("its next URL off the site is dropped: " + outcome.SignedOut, outcome.SignedOut == "False|null");
            because.ItsTrue("and the cookie is forgotten even when the server says no session ended", outcome.CookieAfterSignOut is null);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseMalformedManagementResponses()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("gets bad answers from each management endpoint, and an unsaved profile", (unused, reg) =>
        {
            List<string> wrong = new List<string>();
            Check(wrong, "delete without signedOut", "{\"nextUrl\":null}", backend => backend.DeletePasskeyAsync("AQID"), PasskeyErrorCategory.InvalidServerResponse);
            Check(wrong, "delete with a string signedOut", "{\"signedOut\":\"yes\"}", backend => backend.DeletePasskeyAsync("AQID"), PasskeyErrorCategory.InvalidServerResponse);
            Check(wrong, "sign-out without a body", null, backend => backend.SignOutAsync(), PasskeyErrorCategory.InvalidServerResponse);
            Check(wrong, "a current that isn't true or false", "{\"passkeys\":[{\"id\":\"AQID\",\"current\":1}]}", backend => backend.GetPasskeysAsync(), PasskeyErrorCategory.InvalidServerResponse);
            Check(wrong, "an added id that isn't base64url", "{\"credentialId\":\"../signout\"}", backend => backend.CompleteAdditionalRegistrationAsync(new WebAuthnRegistrationResponse(Credential)), PasskeyErrorCategory.InvalidServerResponse);
            Check(wrong, "a profileCreated that isn't true or false", "{\"handle\":\"ada\",\"profileCreated\":\"no\"}", backend => backend.CompleteRegistrationAsync(new PasskeyRegistrationRequest { Handle = "ada", DisplayName = "Ada" }, new WebAuthnRegistrationResponse(Credential)), PasskeyErrorCategory.InvalidServerResponse);

            FakeAccountEndpoints unsaved = new FakeAccountEndpoints().Respond(HttpStatusCode.Conflict, "{\"error\":\"ProfileNotSaved\",\"message\":\"x\"}");
            using HttpPasskeyBackend profileBackend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, unsaved);
            PasskeyException? notSaved = Caught(() => profileBackend.SaveProfileAsync(new PasskeyProfile { DisplayName = "Ada" }));

            FakeAccountEndpoints refused = new FakeAccountEndpoints();
            using HttpPasskeyBackend renameBackend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, refused);
            PasskeyException? unsafeRename = Caught(() => renameBackend.RenamePasskeyAsync("../signout", "x"));
            return new MalformedOutcome(string.Join("; ", wrong), notSaved?.Category, notSaved?.BackendCode, unsafeRename?.Category, refused.Requests.Count);
        })
        .TheTest
        .ShouldPass<MalformedOutcome>((because, outcome) =>
        {
            because.ItsTrue("every bad answer is an invalid server response: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("an unsaved profile is an unknown failure, not a verification one: " + outcome.NotSaved, outcome.NotSaved == PasskeyErrorCategory.Unknown);
            because.ItsTrue("and keeps the server's code", outcome.NotSavedCode == "ProfileNotSaved");
            because.ItsTrue("an unsafe id is never renamed", outcome.UnsafeRename == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("and nothing is sent", outcome.RenameSent == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static void Check(List<string> wrong, string name, string? body, Func<HttpPasskeyBackend, Task> call, PasskeyErrorCategory expected)
    {
        FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(HttpStatusCode.OK, body);
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
        PasskeyErrorCategory? got = Caught(() => call(backend))?.Category;
        if (got != expected)
        {
            wrong.Add(name + ": " + (got?.ToString() ?? "no failure"));
        }
    }

    private static string Describe(PasskeyCredential credential)
    {
        return credential.CredentialId + "|" + (credential.Name ?? "-") + "|current=" + (credential.IsCurrent?.ToString() ?? "null") + "|keyset=" + (credential.IsRegisteredKeySet?.ToString() ?? "null");
    }

    private static string Members(JsonElement element)
    {
        return string.Join(",", element.EnumerateObject().Select(property => property.Name));
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

    private sealed record ManageOutcome(bool ProfileCreated, string Passkeys, string AddMembers, string Added, string Rename, string ClearedName, string Profile, string Cookies, string Headers, bool AnyOrigin, string Delete, string Removed, PasskeyErrorCategory? AfterRemoval);

    private sealed record SignOutOutcome(bool ProfileCreated, bool KeptSignedOut, string? CookieAfterRemoval, string SignOut, string SignedOut, string? CookieAfterSignOut);

    private sealed record MalformedOutcome(string Wrong, PasskeyErrorCategory? NotSaved, string? NotSavedCode, PasskeyErrorCategory? UnsafeRename, int RenameSent);
}
