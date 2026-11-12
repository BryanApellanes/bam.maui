using System.Net;
using System.Net.Http;
using System.Text.Json;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Http Passkey Backend Should", Selector = "pkhb")]
public class HttpPasskeyBackendShould : UnitTestMenuContainer
{
    private const string Secret = "SECRET-MARKER";
    private const string Credential = "{\"id\":\"AQID\", \"rawId\":\"AQID\",\"type\":\"public-key\",\"response\":{\"clientDataJSON\":\"e30\"}}";

    public HttpPasskeyBackendShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void SignUpAndKeepTheSession()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("signs up, then lists passkeys with the session cookie", (fake, reg) =>
        {
            // Odd spacing: the options must reach the authenticator byte for byte.
            string options = "{ \"rp\" : {\"id\":\"example.com\"},  \"challenge\":\"AAEC\" }";
            fake.Respond(HttpStatusCode.OK, "{\"options\":" + options + "}")
                .Respond(HttpStatusCode.OK, "{\"handle\":\"ada\",\"displayName\":\"Ada\",\"nextUrl\":\"/welcome\",\"profileCreated\":true}", ".Bam.Account=s3ss10n; Path=/; Secure; HttpOnly; SameSite=Lax")
                .Respond(HttpStatusCode.OK, "{\"passkeys\":[{\"id\":\"AQID\",\"name\":\"Phone\",\"createdUtc\":\"2026-11-01T10:00:00Z\",\"lastUsedUtc\":null,\"current\":true,\"registeredKeySet\":true}]}");
            using HttpPasskeyBackend backend = Backend(fake);
            PasskeyRegistrationRequest form = new PasskeyRegistrationRequest { Handle = "ada", DisplayName = "Ada", Email = "ada@example.com" };

            PasskeyRegistrationChallenge challenge = backend.BeginRegistrationAsync(form).GetAwaiter().GetResult();
            PasskeySignIn signIn = backend.CompleteRegistrationAsync(form, new WebAuthnRegistrationResponse(Credential)).GetAwaiter().GetResult();
            IReadOnlyList<PasskeyCredential> passkeys = backend.GetPasskeysAsync().GetAwaiter().GetResult();

            RecordedRequest begin = fake.Requests[0];
            RecordedRequest complete = fake.Requests[1];
            RecordedRequest list = fake.Requests[2];
            using JsonDocument beginBody = JsonDocument.Parse(begin.Body!);
            using JsonDocument completeBody = JsonDocument.Parse(complete.Body!);
            PasskeyCredential first = passkeys[0];
            return new SignUpOutcome(
                begin.Method + " " + begin.Uri,
                string.Join(",", fake.Requests.Select(request => request.AccountHeader ?? "missing")),
                fake.Requests.Any(request => request.HasOrigin),
                begin.ContentType ?? string.Empty,
                Members(beginBody.RootElement),
                challenge.Options.Json == options,
                challenge.Handle,
                complete.Method + " " + complete.Uri,
                Members(completeBody.RootElement),
                completeBody.RootElement.GetProperty("credential").GetRawText() == Credential,
                signIn.Handle + "|" + signIn.DisplayName + "|" + signIn.NextUrl,
                begin.Cookie,
                complete.Cookie,
                list.Method + " " + list.Uri + " " + (list.Body ?? "no body"),
                list.Cookie,
                passkeys.Count + "|" + first.CredentialId + "|" + first.Name + "|" + first.CreatedUtc?.ToString("O") + "|" + (first.LastUsedUtc is null ? "never" : "used"));
        })
        .TheTest
        .ShouldPass<SignUpOutcome>((because, outcome) =>
        {
            because.ItsTrue("begin posts to the account endpoint: " + outcome.Begin, outcome.Begin == "POST https://example.com/account/passkey/registration/begin");
            because.ItsTrue("every request carries the account header: " + outcome.Headers, outcome.Headers == "bam.maui,bam.maui,bam.maui");
            because.ItsTrue("no request carries an Origin", !outcome.AnyOrigin);
            because.ItsTrue("the body is JSON: " + outcome.ContentType, outcome.ContentType == "application/json; charset=utf-8");
            because.ItsTrue("begin sends the form, without a blank invite code: " + outcome.BeginMembers, outcome.BeginMembers == "handle,displayName,email");
            because.ItsTrue("the options reach the authenticator byte for byte", outcome.OptionsExact);
            because.ItsTrue("begin reports no normalized handle", outcome.ChallengeHandle is null);
            because.ItsTrue("complete posts to its endpoint: " + outcome.Complete, outcome.Complete == "POST https://example.com/account/passkey/registration/complete");
            because.ItsTrue("complete sends the form again and the credential: " + outcome.CompleteMembers, outcome.CompleteMembers == "handle,displayName,email,credential");
            because.ItsTrue("the credential is the authenticator's JSON unchanged", outcome.CredentialExact);
            because.ItsTrue("the sign-in is the server's: " + outcome.SignIn, outcome.SignIn == "ada|Ada|/welcome");
            because.ItsTrue("no cookie before there is a session", outcome.BeginCookie is null && outcome.CompleteCookie is null);
            because.ItsTrue("the listing is a GET with no body: " + outcome.List, outcome.List == "GET https://example.com/account/passkeys no body");
            because.ItsTrue("the session cookie rides on the next request: " + outcome.ListCookie, outcome.ListCookie == ".Bam.Account=s3ss10n");
            because.ItsTrue("the passkey is read: " + outcome.Passkeys, outcome.Passkeys == "1|AQID|Phone|2026-11-01T10:00:00.0000000Z|never");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SignInAndKeepOnlySitePaths()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("signs in with and without a handle", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.OK, "{\"options\":{\"rpId\":\"example.com\"},\"ceremonyId\":\"c-1\"}")
                .Respond(HttpStatusCode.OK, "{\"handle\":\"ada\",\"displayName\":\"Ada\",\"nextUrl\":\"//evil.example/x\"}")
                .Respond(HttpStatusCode.OK, "{\"options\":{\"rpId\":\"example.com\"},\"ceremonyId\":\"c-2\"}");
            using HttpPasskeyBackend backend = Backend(fake);

            PasskeyAuthenticationChallenge usernameLess = backend.BeginAuthenticationAsync(null).GetAwaiter().GetResult();
            PasskeySignIn signIn = backend.CompleteAuthenticationAsync(usernameLess, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();
            PasskeyAuthenticationChallenge named = backend.BeginAuthenticationAsync("ada").GetAwaiter().GetResult();
            int sent = fake.Requests.Count;
            PasskeyException? noCeremony = Caught(() => backend.CompleteAuthenticationAsync(new PasskeyAuthenticationChallenge { Options = named.Options }, new WebAuthnAssertionResponse(Credential)));

            using JsonDocument completeBody = JsonDocument.Parse(fake.Requests[1].Body!);
            string[] paths = { "/home", "/a/b?c=d", "//evil.example", "/\\evil.example", "https://evil.example/", "home", "/a\nb", "/a\\b", string.Empty };
            string kept = string.Join(",", paths.Select(path => HttpPasskeyBackend.SitePathOrNull(path) ?? "null"));
            return new SignInOutcome(
                fake.Requests[0].Uri + " " + fake.Requests[0].Body,
                usernameLess.CeremonyId,
                Members(completeBody.RootElement) + "|" + completeBody.RootElement.GetProperty("ceremonyId").GetString(),
                completeBody.RootElement.GetProperty("credential").GetRawText() == Credential,
                signIn.Handle + "|" + (signIn.NextUrl ?? "null"),
                fake.Requests[2].Body ?? string.Empty,
                named.CeremonyId,
                noCeremony?.Category,
                fake.Requests.Count == sent,
                kept);
        })
        .TheTest
        .ShouldPass<SignInOutcome>((because, outcome) =>
        {
            because.ItsTrue("username-less begin sends an empty object: " + outcome.UsernameLessBegin, outcome.UsernameLessBegin == "https://example.com/account/passkey/assertion/begin {}");
            because.ItsTrue("the ceremony id is kept", outcome.FirstCeremony == "c-1");
            because.ItsTrue("complete sends the ceremony id and the credential: " + outcome.CompleteMembers, outcome.CompleteMembers == "ceremonyId,credential|c-1");
            because.ItsTrue("the assertion is the authenticator's JSON unchanged", outcome.CredentialExact);
            because.ItsTrue("a next URL off the site is dropped: " + outcome.SignIn, outcome.SignIn == "ada|null");
            because.ItsTrue("a named begin sends the handle: " + outcome.NamedBegin, outcome.NamedBegin == "{\"handle\":\"ada\"}");
            because.ItsTrue("its ceremony id is read", outcome.SecondCeremony == "c-2");
            because.ItsTrue("completing without a ceremony id is invalid options", outcome.NoCeremony == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("and sends nothing", outcome.NothingSent);
            because.ItsTrue("only site paths are kept: " + outcome.Kept, outcome.Kept == "/home,/a/b?c=d,null,null,null,null,null,null,null");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void AddAndDeletePasskeys()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("adds a passkey, deletes one, and refuses unsafe ids", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.OK, "{\"options\":{\"rp\":{\"id\":\"example.com\"}}}")
                .Respond(HttpStatusCode.OK, "{\"credentialId\":\"BAUG\"}")
                .Respond(HttpStatusCode.OK, "{\"signedOut\":false,\"nextUrl\":null}");
            using HttpPasskeyBackend backend = Backend(fake);

            WebAuthnCreationOptions options = backend.BeginAdditionalRegistrationAsync().GetAwaiter().GetResult();
            backend.CompleteAdditionalRegistrationAsync(new WebAuthnRegistrationResponse(Credential)).GetAwaiter().GetResult();
            backend.DeletePasskeyAsync("AQID-_z9").GetAwaiter().GetResult();
            int sent = fake.Requests.Count;

            string[] unsafeIds = { "../signout", "a/b", "a b", "%2e%2e", "AQID==", "AQ+D", string.Empty, new string('A', 1401) };
            List<string> notRefused = new List<string>();
            foreach (string id in unsafeIds)
            {
                if (Caught(() => backend.DeletePasskeyAsync(id))?.Category != PasskeyErrorCategory.InvalidOptions)
                {
                    notRefused.Add(id);
                }
            }
            using JsonDocument completeBody = JsonDocument.Parse(fake.Requests[1].Body!);
            return new ManageOutcome(
                fake.Requests[0].Method + " " + fake.Requests[0].Uri + " " + fake.Requests[0].Body,
                options.Json,
                fake.Requests[1].Uri + " " + Members(completeBody.RootElement),
                fake.Requests[2].Method + " " + fake.Requests[2].Uri + " " + (fake.Requests[2].AccountHeader ?? "no header"),
                string.Join(" ", notRefused),
                fake.Requests.Count == sent);
        })
        .TheTest
        .ShouldPass<ManageOutcome>((because, outcome) =>
        {
            because.ItsTrue("additional begin posts an empty object: " + outcome.Begin, outcome.Begin == "POST https://example.com/account/passkey/additional/begin {}");
            because.ItsTrue("its options are returned", outcome.Options == "{\"rp\":{\"id\":\"example.com\"}}");
            because.ItsTrue("complete sends the credential only: " + outcome.Complete, outcome.Complete == "https://example.com/account/passkey/additional/complete credential");
            because.ItsTrue("delete targets the passkey, with the header: " + outcome.Delete, outcome.Delete == "DELETE https://example.com/account/passkeys/AQID-_z9 bam.maui");
            because.ItsTrue("unsafe ids are refused as invalid options; not refused: " + outcome.NotRefused, outcome.NotRefused.Length == 0);
            because.ItsTrue("and none was sent", outcome.NothingSent);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void MapRefusalsByCodeThenStatus()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("gets each kind of refusal", (unused, reg) =>
        {
            List<RefusalCase> cases = new List<RefusalCase>
            {
                new RefusalCase("401 NotSignedIn", new FakeResponse { Status = HttpStatusCode.Unauthorized, Body = "{\"error\":\"NotSignedIn\",\"message\":\"" + Secret + "\"}" }, PasskeyErrorCategory.NotSignedIn, "NotSignedIn"),
                new RefusalCase("401 SignInFailed", new FakeResponse { Status = HttpStatusCode.Unauthorized, Body = "{\"error\":\"SignInFailed\",\"message\":\"" + Secret + "\"}" }, PasskeyErrorCategory.ServerVerificationFailed, "SignInFailed"),
                new RefusalCase("409 HandleTaken", new FakeResponse { Status = HttpStatusCode.Conflict, Body = "{\"error\":\"HandleTaken\",\"message\":\"" + Secret + "\"}" }, PasskeyErrorCategory.InvalidOptions, "HandleTaken"),
                new RefusalCase("403 RequestRefused", new FakeResponse { Status = HttpStatusCode.Forbidden, Body = "{\"error\":\"RequestRefused\"}" }, PasskeyErrorCategory.ServerVerificationFailed, "RequestRefused"),
                new RefusalCase("429 bare", new FakeResponse { Status = HttpStatusCode.TooManyRequests }, PasskeyErrorCategory.RateLimited, null),
                new RefusalCase("503 html", new FakeResponse { Status = HttpStatusCode.ServiceUnavailable, Body = "<html>" + Secret + "</html>", ContentType = "text/html" }, PasskeyErrorCategory.NetworkFailure, null),
                new RefusalCase("500 bare", new FakeResponse { Status = HttpStatusCode.InternalServerError }, PasskeyErrorCategory.InvalidServerResponse, null),
                new RefusalCase("302 redirect", new FakeResponse { Status = HttpStatusCode.Found, Location = "https://evil.example/login" }, PasskeyErrorCategory.InvalidServerResponse, null),
                new RefusalCase("404 bare", new FakeResponse { Status = HttpStatusCode.NotFound }, PasskeyErrorCategory.InvalidServerResponse, null),
                new RefusalCase("400 junk code", new FakeResponse { Status = HttpStatusCode.BadRequest, Body = "{\"error\":\"Bad " + Secret + "\"}" }, PasskeyErrorCategory.InvalidOptions, null),
                new RefusalCase("418 unknown code", new FakeResponse { Status = (HttpStatusCode)418, Body = "{\"error\":\"Teapot\"}" }, PasskeyErrorCategory.ServerVerificationFailed, "Teapot"),
            };
            List<string> wrong = new List<string>();
            List<string> leaked = new List<string>();
            foreach (RefusalCase refusal in cases)
            {
                FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(refusal.Response);
                using HttpPasskeyBackend backend = Backend(fake);
                PasskeyException? caught = Caught(() => backend.BeginAuthenticationAsync("ada"));
                if (caught is null || caught.Category != refusal.Category || caught.BackendCode != refusal.Code)
                {
                    wrong.Add(refusal.Name + "=" + (caught?.Category.ToString() ?? "none") + "/" + (caught?.BackendCode ?? "null"));
                }
                if (caught is not null && caught.Message.Contains(Secret, StringComparison.Ordinal))
                {
                    leaked.Add(refusal.Name);
                }
            }

            Dictionary<string, PasskeyErrorCategory> codes = new Dictionary<string, PasskeyErrorCategory>
            {
                ["BadRequest"] = PasskeyErrorCategory.InvalidOptions,
                ["RequestRefused"] = PasskeyErrorCategory.ServerVerificationFailed,
                ["NotSignedIn"] = PasskeyErrorCategory.NotSignedIn,
                ["SignUpClosed"] = PasskeyErrorCategory.ServerVerificationFailed,
                ["InviteRequired"] = PasskeyErrorCategory.InvalidOptions,
                ["InvalidHandle"] = PasskeyErrorCategory.InvalidOptions,
                ["HandleTaken"] = PasskeyErrorCategory.InvalidOptions,
                ["EmailRequired"] = PasskeyErrorCategory.InvalidOptions,
                ["SignUpFailed"] = PasskeyErrorCategory.ServerVerificationFailed,
                ["SignInFailed"] = PasskeyErrorCategory.ServerVerificationFailed,
                ["PasskeyNotAdded"] = PasskeyErrorCategory.ServerVerificationFailed,
                ["NotFound"] = PasskeyErrorCategory.InvalidOptions,
                ["LastPasskey"] = PasskeyErrorCategory.InvalidOptions,
                ["RateLimited"] = PasskeyErrorCategory.RateLimited,
            };
            foreach (KeyValuePair<string, PasskeyErrorCategory> code in codes)
            {
                if (HttpPasskeyBackend.CategoryFor(HttpStatusCode.BadRequest, code.Key) != code.Value)
                {
                    wrong.Add("code " + code.Key);
                }
            }

            // A transport failure is thrown as it is; the normalizer makes it a network failure.
            FakeAccountEndpoints offline = new FakeAccountEndpoints().Respond(new FakeResponse { Throw = new HttpRequestException("offline") });
            using HttpPasskeyBackend offlineBackend = Backend(offline);
            Exception? transport = null;
            try
            {
                offlineBackend.GetPasskeysAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                transport = exception;
            }
            PasskeyErrorCategory? transportCategory = transport is null ? null : PasskeyErrorNormalizer.Categorize(transport);
            return new RefusalOutcome(cases.Count, string.Join(", ", wrong), string.Join(", ", leaked), transportCategory);
        })
        .TheTest
        .ShouldPass<RefusalOutcome>((because, outcome) =>
        {
            because.ItsTrue("the cases ran", outcome.Cases > 10);
            because.ItsTrue("each refusal gets its category and code; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("no refusal quotes the body; leaked: " + outcome.Leaked, outcome.Leaked.Length == 0);
            because.ItsTrue("a transport failure is a network failure", outcome.Transport == PasskeyErrorCategory.NetworkFailure);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseResponsesThatAreNotTheContract()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("gets each malformed success", (unused, reg) =>
        {
            string big = "{\"options\":{\"pad\":\"" + new string('x', 200) + "\"}}";
            List<MalformedCase> cases = new List<MalformedCase>
            {
                new MalformedCase("not json", new FakeResponse { Body = "not json " + Secret }, Call.RegistrationBegin),
                new MalformedCase("an array", new FakeResponse { Body = "[1]" }, Call.RegistrationBegin),
                new MalformedCase("no options", new FakeResponse { Body = "{}" }, Call.RegistrationBegin),
                new MalformedCase("options a string", new FakeResponse { Body = "{\"options\":\"" + Secret + "\"}" }, Call.RegistrationBegin),
                new MalformedCase("html content type", new FakeResponse { Body = "{\"options\":{}}", ContentType = "text/html" }, Call.RegistrationBegin),
                new MalformedCase("no content", new FakeResponse { Status = HttpStatusCode.NoContent }, Call.RegistrationBegin),
                new MalformedCase("too large, declared", new FakeResponse { Body = big }, Call.RegistrationBegin),
                new MalformedCase("too large, undeclared", new FakeResponse { Body = big, OmitLength = true }, Call.RegistrationBegin),
                new MalformedCase("no ceremony id", new FakeResponse { Body = "{\"options\":{}}" }, Call.AssertionBegin),
                new MalformedCase("blank ceremony id", new FakeResponse { Body = "{\"options\":{},\"ceremonyId\":\" \"}" }, Call.AssertionBegin),
                new MalformedCase("no handle", new FakeResponse { Body = "{\"displayName\":\"Ada\"}" }, Call.AssertionComplete),
                new MalformedCase("numeric display name", new FakeResponse { Body = "{\"handle\":\"ada\",\"displayName\":7}" }, Call.AssertionComplete),
                new MalformedCase("no passkeys array", new FakeResponse { Body = "{\"passkeys\":{}}" }, Call.Passkeys),
                new MalformedCase("no content for passkeys", new FakeResponse { Status = HttpStatusCode.NoContent }, Call.Passkeys),
                new MalformedCase("passkey without id", new FakeResponse { Body = "{\"passkeys\":[{\"name\":\"x\"}]}" }, Call.Passkeys),
                new MalformedCase("passkey bad date", new FakeResponse { Body = "{\"passkeys\":[{\"id\":\"AQID\",\"createdUtc\":\"" + Secret + "\"}]}" }, Call.Passkeys),
                new MalformedCase("no credential id", new FakeResponse { Body = "{}" }, Call.AdditionalComplete),
            };
            List<string> wrong = new List<string>();
            List<string> leaked = new List<string>();
            PasskeyHttpBackendOptions small = new PasskeyHttpBackendOptions { MaxResponseBytes = 128 };
            foreach (MalformedCase malformed in cases)
            {
                FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(malformed.Response);
                using HttpPasskeyBackend backend = Backend(fake, small);
                PasskeyException? caught = Caught(() => Run(backend, malformed.Call));
                if (caught?.Category != PasskeyErrorCategory.InvalidServerResponse)
                {
                    wrong.Add(malformed.Name + "=" + (caught?.Category.ToString() ?? "accepted"));
                }
                if (caught is not null && caught.Message.Contains(Secret, StringComparison.Ordinal))
                {
                    leaked.Add(malformed.Name);
                }
            }

            // The authenticator's response must be a JSON object before it is sent.
            FakeAccountEndpoints untouched = new FakeAccountEndpoints();
            using HttpPasskeyBackend guarded = Backend(untouched);
            PasskeyException? notObject = Caught(() => guarded.CompleteAdditionalRegistrationAsync(new WebAuthnRegistrationResponse("[1]")));
            PasskeyException? notJson = Caught(() => guarded.CompleteAdditionalRegistrationAsync(new WebAuthnRegistrationResponse("{" + Secret)));
            return new MalformedOutcome(cases.Count, string.Join(", ", wrong), string.Join(", ", leaked), notObject?.Category, notJson?.Category, untouched.Requests.Count);
        })
        .TheTest
        .ShouldPass<MalformedOutcome>((because, outcome) =>
        {
            because.ItsTrue("the cases ran", outcome.Cases > 15);
            because.ItsTrue("every malformed success is an invalid server response; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("no refusal quotes the body; leaked: " + outcome.Leaked, outcome.Leaked.Length == 0);
            because.ItsTrue("a response that is not an object is a platform failure", outcome.CredentialNotObject == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("a response that is not JSON is a platform failure", outcome.CredentialNotJson == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("and neither is sent", outcome.SentForBadCredentials == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void OnlyTalkToTheRelyingParty()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("builds backends over each endpoint", (fake, reg) =>
        {
            string[] accepted = { "https://example.com/account", "https://EXAMPLE.com/account/", "https://api.example.com/v1/account", "https://example.com:8443/account/" };
            string[] refused = { "http://example.com/account/", "https://evil.example/account/", "https://example.com.evil.net/account/", "https://notexample.com/account/", "https://example.com/account/?x=1", "https://example.com/account/#x", "https://ada:pw@example.com/account/" };
            List<string> resolved = new List<string>();
            foreach (string endpoint in accepted)
            {
                using HttpPasskeyBackend backend = Backend(new FakeAccountEndpoints(), new PasskeyHttpBackendOptions { AccountEndpoint = new Uri(endpoint) });
                resolved.Add(backend.AccountEndpoint.AbsoluteUri);
            }
            List<string> notRefused = new List<string>();
            foreach (string endpoint in refused)
            {
                if (!Refuses(new PasskeyHttpBackendOptions { AccountEndpoint = new Uri(endpoint) }))
                {
                    notRefused.Add(endpoint);
                }
            }
            if (!Refuses(new PasskeyHttpBackendOptions { AccountEndpoint = new Uri("/account/", UriKind.Relative) }))
            {
                notRefused.Add("relative");
            }
            if (!Refuses(new PasskeyHttpBackendOptions { Timeout = TimeSpan.Zero }))
            {
                notRefused.Add("zero timeout");
            }
            if (!Refuses(new PasskeyHttpBackendOptions { MaxResponseBytes = 0 }))
            {
                notRefused.Add("zero size");
            }
            using HttpPasskeyBackend defaults = Backend(new FakeAccountEndpoints());
            return new EndpointOutcome(defaults.AccountEndpoint.AbsoluteUri, string.Join(" ", resolved), string.Join(" ", notRefused));
        })
        .TheTest
        .ShouldPass<EndpointOutcome>((because, outcome) =>
        {
            because.ItsTrue("the default is the relying party's /account/: " + outcome.Default, outcome.Default == "https://example.com/account/");
            because.ItsTrue("the RP and its subdomains are accepted, ending in a slash: " + outcome.Resolved,
                outcome.Resolved == "https://example.com/account/ https://example.com/account/ https://api.example.com/v1/account/ https://example.com:8443/account/");
            because.ItsTrue("everything else is refused: " + outcome.NotRefused, outcome.NotRefused.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void CarryTheBackendCodeToTheApp()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("signs up through PasskeyClient against a taken handle", (fake, reg) =>
        {
            fake.Respond(HttpStatusCode.Conflict, "{\"error\":\"HandleTaken\",\"message\":\"That handle is taken.\"}");
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(configuration, null, fake);
            PasskeyClient client = new PasskeyClient(backend, new FakePasskeyAuthenticator(new CallLog()), configuration, new PasskeyErrorNormalizer(configuration.Messages));
            PasskeyResult<PasskeySignIn> result = client.RegisterPasskeyAsync(new PasskeyRegistrationRequest { Handle = "ada", DisplayName = "Ada" }).GetAwaiter().GetResult();

            PasskeyMessages messages = new PasskeyMessages();
            ServiceRegistry bound = new ServiceRegistry().AddHttpPasskeyBackend(configuration);
            FakePasskeyBackend existing = new FakePasskeyBackend(new CallLog());
            ServiceRegistry kept = new ServiceRegistry();
            kept.For<IPasskeyBackend>().UseSingleton<IPasskeyBackend>(existing);
            kept.AddHttpPasskeyBackend(configuration);
            return new CodeOutcome(
                result.Error?.Category,
                result.Error?.BackendCode,
                result.Error?.Message == configuration.Messages.InvalidOptions,
                messages.For(PasskeyErrorCategory.NotSignedIn) + "|" + messages.For(PasskeyErrorCategory.RateLimited),
                bound.Get<IPasskeyBackend>() is HttpPasskeyBackend,
                ReferenceEquals(kept.Get<IPasskeyBackend>(), existing));
        })
        .TheTest
        .ShouldPass<CodeOutcome>((because, outcome) =>
        {
            because.ItsTrue("a taken handle is invalid options", outcome.Category == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("with the backend's code: " + outcome.Code, outcome.Code == "HandleTaken");
            because.ItsTrue("and the app's message, not the server's", outcome.AppMessage);
            because.ItsTrue("the new categories have messages: " + outcome.NewMessages, outcome.NewMessages == "Sign in again to continue.|Too many attempts. Wait a moment and try again.");
            because.ItsTrue("the registration binds the HTTP backend", outcome.Bound);
            because.ItsTrue("but keeps a backend the host bound", outcome.KeptExisting);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void CutOffAStalledBody()
    {
        After.Setup(reg => reg.Set(new FakeAccountEndpoints()))
        .When<FakeAccountEndpoints>("gets headers and then no body, through its own client and a host's client, and once with the caller cancelling", (unused, reg) =>
        {
            PasskeyHttpBackendOptions quick = new PasskeyHttpBackendOptions { Timeout = TimeSpan.FromMilliseconds(300) };
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            FakeAccountEndpoints own = new FakeAccountEndpoints().Respond(new FakeResponse { StallBody = true });
            using HttpPasskeyBackend created = Backend(own, quick);
            PasskeyException? ownClient = Caught(() => created.GetPasskeysAsync());

            // A host's client with no timeout of its own: the backend's timeout still applies.
            FakeAccountEndpoints hosted = new FakeAccountEndpoints().Respond(new FakeResponse { StallBody = true });
            using HttpClient hostClient = new HttpClient(hosted) { Timeout = Timeout.InfiniteTimeSpan };
            using HttpPasskeyBackend host = new HttpPasskeyBackend(hostClient, PasskeyClientFixture.ValidConfiguration(), quick);
            PasskeyException? hostClientCaught = Caught(() => host.GetPasskeysAsync());
            long elapsed = watch.ElapsedMilliseconds;

            FakeAccountEndpoints cancelled = new FakeAccountEndpoints().Respond(new FakeResponse { StallBody = true });
            using HttpPasskeyBackend patient = Backend(cancelled, new PasskeyHttpBackendOptions { Timeout = TimeSpan.FromMinutes(5) });
            using CancellationTokenSource caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            string callerCancel = "nothing";
            try
            {
                patient.GetPasskeysAsync(caller.Token).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                callerCancel = exception is OperationCanceledException ? "cancelled" : exception.GetType().Name;
            }

            HttpClientHandler platform = HttpPasskeyBackend.CreatePlatformHandler();
            return new StallOutcome(ownClient?.Category, hostClientCaught?.Category, elapsed, callerCancel, platform.AllowAutoRedirect, platform.UseCookies);
        })
        .TheTest
        .ShouldPass<StallOutcome>((because, outcome) =>
        {
            because.ItsTrue("a stalled body through the backend's own client is a network failure, got " + outcome.OwnClient, outcome.OwnClient == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("through a host's client with no timeout too, got " + outcome.HostClient, outcome.HostClient == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("both are cut off by the 300 ms timeout, took " + outcome.ElapsedMilliseconds + " ms", outcome.ElapsedMilliseconds < 10000);
            because.ItsTrue("a caller's cancel stays a cancellation, got " + outcome.CallerCancel, outcome.CallerCancel == "cancelled");
            because.ItsTrue("the platform handler does not follow redirects", !outcome.FollowsRedirects);
            because.ItsTrue("leaves cookies to the jar", !outcome.UsesCookies);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static HttpPasskeyBackend Backend(FakeAccountEndpoints fake, PasskeyHttpBackendOptions? options = null)
    {
        return HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), options, fake);
    }

    private static bool Refuses(PasskeyHttpBackendOptions options)
    {
        try
        {
            using HttpPasskeyBackend backend = Backend(new FakeAccountEndpoints(), options);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
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
            // Anything but a PasskeyException is a defect: report it as Unknown so the test names it instead of crashing.
            return new PasskeyException(PasskeyErrorCategory.Unknown, exception.GetType().Name);
        }
    }

    private static Task Run(HttpPasskeyBackend backend, Call call)
    {
        PasskeyRegistrationRequest form = new PasskeyRegistrationRequest { Handle = "ada", DisplayName = "Ada" };
        return call switch
        {
            Call.RegistrationBegin => backend.BeginRegistrationAsync(form),
            Call.AssertionBegin => backend.BeginAuthenticationAsync(null),
            Call.AssertionComplete => backend.CompleteAuthenticationAsync(new PasskeyAuthenticationChallenge { Options = new WebAuthnRequestOptions("{}"), CeremonyId = "c-1" }, new WebAuthnAssertionResponse(Credential)),
            Call.Passkeys => backend.GetPasskeysAsync(),
            _ => backend.CompleteAdditionalRegistrationAsync(new WebAuthnRegistrationResponse(Credential))
        };
    }

    private enum Call
    {
        RegistrationBegin,
        AssertionBegin,
        AssertionComplete,
        Passkeys,
        AdditionalComplete
    }

    private sealed record RefusalCase(string Name, FakeResponse Response, PasskeyErrorCategory Category, string? Code);

    private sealed record MalformedCase(string Name, FakeResponse Response, Call Call);

    private sealed record SignUpOutcome(string Begin, string Headers, bool AnyOrigin, string ContentType, string BeginMembers, bool OptionsExact, string? ChallengeHandle, string Complete, string CompleteMembers, bool CredentialExact, string SignIn, string? BeginCookie, string? CompleteCookie, string List, string? ListCookie, string Passkeys);

    private sealed record SignInOutcome(string UsernameLessBegin, string? FirstCeremony, string CompleteMembers, bool CredentialExact, string SignIn, string NamedBegin, string? SecondCeremony, PasskeyErrorCategory? NoCeremony, bool NothingSent, string Kept);

    private sealed record ManageOutcome(string Begin, string Options, string Complete, string Delete, string NotRefused, bool NothingSent);

    private sealed record RefusalOutcome(int Cases, string Wrong, string Leaked, PasskeyErrorCategory? Transport);

    private sealed record MalformedOutcome(int Cases, string Wrong, string Leaked, PasskeyErrorCategory? CredentialNotObject, PasskeyErrorCategory? CredentialNotJson, int SentForBadCredentials);

    private sealed record EndpointOutcome(string Default, string Resolved, string NotRefused);

    private sealed record CodeOutcome(PasskeyErrorCategory? Category, string? Code, bool AppMessage, string NewMessages, bool Bound, bool KeptExisting);

    private sealed record StallOutcome(PasskeyErrorCategory? OwnClient, PasskeyErrorCategory? HostClient, long ElapsedMilliseconds, string CallerCancel, bool FollowsRedirects, bool UsesCookies);
}
