using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyClient Management Should", Selector = "pkcm")]
public class PasskeyClientManagementShould : UnitTestMenuContainer
{
    public PasskeyClientManagementShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void AddRenameRemoveAndSignOut()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("adds a named passkey, renames and clears a name, removes the session's passkey, signs out and saves a profile", (fixture, reg) =>
        {
            PasskeyClient client = fixture.Client();
            fixture.Backend.DeleteResult = new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" };
            PasskeyResult<string> added = Wait(client.AddPasskeyAsync("  My phone  "));
            string? addedName = fixture.Backend.AddedName;
            PasskeyResult<string> unnamed = Wait(client.AddPasskeyAsync());
            string? unnamedName = fixture.Backend.AddedName;
            PasskeyResult renamed = Wait(client.RenamePasskeyAsync("laptop", " Work laptop "));
            string renamedTo = fixture.Backend.RenamedCredentialId + "|" + fixture.Backend.RenamedTo;
            PasskeyResult cleared = Wait(client.RenamePasskeyAsync("laptop", "   "));
            string? clearedTo = fixture.Backend.RenamedTo;
            PasskeyResult<PasskeySignOut> removed = Wait(client.DeletePasskeyAsync("phone"));
            PasskeyResult<PasskeySignOut> signedOut = Wait(client.SignOutAsync());
            PasskeyResult saved = Wait(client.SaveProfileAsync(new PasskeyProfile { DisplayName = " Ada ", Email = "  " }));
            return new ManagedOutcome(added.Value, addedName, unnamed.Succeeded, unnamedName, renamed.Succeeded, renamedTo, cleared.Succeeded, clearedTo,
                removed.Value?.SignedOut, removed.Value?.NextUrl, signedOut.Value?.SignedOut, saved.Succeeded,
                fixture.Backend.SavedProfile?.DisplayName + "|" + (fixture.Backend.SavedProfile?.Email ?? "null"), fixture.Log.Joined);
        })
        .TheTest
        .ShouldPass<ManagedOutcome>((because, outcome) =>
        {
            because.ItsTrue("adding returns the new id", outcome.AddedId == "added-1");
            because.ItsTrue("with the name trimmed: " + outcome.AddedName, outcome.AddedName == "My phone");
            because.ItsTrue("a passkey can be added without a name", outcome.UnnamedAdded && outcome.UnnamedName is null);
            because.ItsTrue("a rename sends the trimmed name: " + outcome.RenamedTo, outcome.Renamed && outcome.RenamedTo == "laptop|Work laptop");
            because.ItsTrue("a blank name clears it", outcome.Cleared && outcome.ClearedTo is null);
            because.ItsTrue("removing the session's passkey says the session ended", outcome.RemovedSignedOut == true && outcome.RemovedNextUrl == "/account/signedout");
            because.ItsTrue("sign-out reports the sign-out", outcome.SignedOut == true);
            because.ItsTrue("the profile is trimmed and a blank email dropped: " + outcome.SavedProfile, outcome.Saved && outcome.SavedProfile == "Ada|null");
            because.ItsTrue("the calls ran in order: " + outcome.Log, outcome.Log == "backend.beginAdditional,authenticator.create,backend.completeAdditional,backend.beginAdditional,authenticator.create,backend.completeAdditional,backend.renamePasskey,backend.renamePasskey,backend.deletePasskey,backend.signOut,backend.saveProfile");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void GateManagementButNotLeavingOrTheProfile()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("renames and removes with management off, signs out and saves a profile with it off, and sends blanks", (fixture, reg) =>
        {
            fixture.Configuration.Features.EnableManagement = false;
            PasskeyClient client = fixture.Client();
            PasskeyResult rename = Wait(client.RenamePasskeyAsync("laptop", "x"));
            PasskeyResult<PasskeySignOut> remove = Wait(client.DeletePasskeyAsync("laptop"));
            PasskeyResult<string> add = Wait(client.AddPasskeyAsync("x"));
            string gatedLog = fixture.Log.Joined;
            PasskeyResult<PasskeySignOut> signOut = Wait(client.SignOutAsync());
            PasskeyResult profile = Wait(client.SaveProfileAsync(new PasskeyProfile { DisplayName = "Ada" }));
            PasskeyResult blankProfile = Wait(client.SaveProfileAsync(new PasskeyProfile { DisplayName = "  " }));
            PasskeyResult nullProfile = Wait(client.SaveProfileAsync(null!));
            fixture.Configuration.Features.EnableManagement = true;
            PasskeyClient enabled = fixture.Client();
            PasskeyResult blankRename = Wait(enabled.RenamePasskeyAsync(" ", "x"));
            return new GateOutcome(rename.Error?.Category, remove.Error?.Category, add.Error?.Category, gatedLog, signOut.Succeeded, profile.Succeeded,
                blankProfile.Error?.Category, nullProfile.Error?.Category, blankRename.Error?.Category, fixture.Log.Joined);
        })
        .TheTest
        .ShouldPass<GateOutcome>((because, outcome) =>
        {
            because.ItsTrue("rename is off with management", outcome.Rename == PasskeyErrorCategory.FeatureDisabled);
            because.ItsTrue("so is removal", outcome.Remove == PasskeyErrorCategory.FeatureDisabled);
            because.ItsTrue("and adding", outcome.Add == PasskeyErrorCategory.FeatureDisabled);
            because.ItsTrue("none reached the backend: " + outcome.GatedLog, outcome.GatedLog.Length == 0);
            because.ItsTrue("a signed-in person can always sign out", outcome.SignOut);
            because.ItsTrue("and finish their profile", outcome.Profile);
            because.ItsTrue("a profile needs a name", outcome.BlankProfile == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("a missing profile is refused, not thrown", outcome.NullProfile == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("a rename needs an id", outcome.BlankRename == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("only the allowed calls were sent: " + outcome.Log, outcome.Log == "backend.signOut,backend.saveProfile");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ReportBackendFailuresAsResults()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("renames, removes, signs out and saves a profile against a failing backend, and gets an empty added id", (fixture, reg) =>
        {
            fixture.Backend.Failures["backend.renamePasskey"] = new PasskeyException(PasskeyErrorCategory.InvalidOptions, "no such passkey", null, "NotFound");
            fixture.Backend.Failures["backend.deletePasskey"] = new PasskeyException(PasskeyErrorCategory.InvalidOptions, "last one", null, "LastPasskey");
            fixture.Backend.Failures["backend.signOut"] = new HttpRequestException("offline");
            fixture.Backend.Failures["backend.saveProfile"] = new PasskeyException(PasskeyErrorCategory.NotSignedIn, "401", null, "NotSignedIn");
            fixture.Backend.AddedCredentialId = " ";
            PasskeyClient client = fixture.Client();
            PasskeyResult rename = Wait(client.RenamePasskeyAsync("laptop", "x"));
            PasskeyResult<PasskeySignOut> remove = Wait(client.DeletePasskeyAsync("phone"));
            PasskeyResult<PasskeySignOut> signOut = Wait(client.SignOutAsync());
            PasskeyResult profile = Wait(client.SaveProfileAsync(new PasskeyProfile { DisplayName = "Ada" }));
            PasskeyResult<string> add = Wait(client.AddPasskeyAsync());
            return new FailureOutcome(rename.Error?.BackendCode, remove.Error?.BackendCode, remove.Error?.Category, signOut.Error?.Category, profile.Error?.Category, add.Error?.Category);
        })
        .TheTest
        .ShouldPass<FailureOutcome>((because, outcome) =>
        {
            because.ItsTrue("a refused rename carries the server's code", outcome.RenameCode == "NotFound");
            because.ItsTrue("so does a refused removal", outcome.RemoveCode == "LastPasskey" && outcome.RemoveCategory == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("an unreachable sign-out is a network failure", outcome.SignOut == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("a lost session while saving the profile says so", outcome.Profile == PasskeyErrorCategory.NotSignedIn);
            because.ItsTrue("an added passkey the backend doesn't name is an invalid response", outcome.Add == PasskeyErrorCategory.InvalidServerResponse);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static TResult Wait<TResult>(Task<TResult> task)
    {
        return task.GetAwaiter().GetResult();
    }

    private sealed record ManagedOutcome(string? AddedId, string? AddedName, bool UnnamedAdded, string? UnnamedName, bool Renamed, string RenamedTo, bool Cleared, string? ClearedTo,
        bool? RemovedSignedOut, string? RemovedNextUrl, bool? SignedOut, bool Saved, string SavedProfile, string Log);

    private sealed record GateOutcome(PasskeyErrorCategory? Rename, PasskeyErrorCategory? Remove, PasskeyErrorCategory? Add, string GatedLog, bool SignOut, bool Profile,
        PasskeyErrorCategory? BlankProfile, PasskeyErrorCategory? NullProfile, PasskeyErrorCategory? BlankRename, string Log);

    private sealed record FailureOutcome(string? RenameCode, string? RemoveCode, PasskeyErrorCategory? RemoveCategory, PasskeyErrorCategory? SignOut, PasskeyErrorCategory? Profile, PasskeyErrorCategory? Add);
}
