using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Platform Ceremony Should", Selector = "pkpc")]
public class PlatformCeremonyShould : UnitTestMenuContainer
{
    public PlatformCeremonyShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void CompleteFromTheTokenWithoutACallback()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("cancels before and during a ceremony whose platform never calls back", (unused, reg) =>
        {
            // Cancelled before the ceremony starts: the platform is told and the wait ends at once.
            using CancellationTokenSource early = new CancellationTokenSource();
            early.Cancel();
            int earlyPlatformCancels = 0;
            using PlatformCeremony<string> before = new PlatformCeremony<string>(early.Token, () => earlyPlatformCancels++);
            bool beforeCancelled = EndsCancelled(before.Completion);

            // Cancelled while the prompt is up, and the platform drops the callback.
            using CancellationTokenSource late = new CancellationTokenSource();
            int latePlatformCancels = 0;
            using PlatformCeremony<string> during = new PlatformCeremony<string>(late.Token, () => latePlatformCancels++);
            bool pendingBeforeCancel = !during.IsCompleted;
            late.Cancel();
            bool duringCancelled = EndsCancelled(during.Completion);
            bool lateReportIgnored = !during.Report("late");

            // The platform's cancel throwing still releases the caller.
            using CancellationTokenSource throwing = new CancellationTokenSource();
            using PlatformCeremony<string> broken = new PlatformCeremony<string>(throwing.Token, () => throw new InvalidOperationException("platform"));
            bool cancelThrew = false;
            try
            {
                throwing.Cancel();
            }
            catch (AggregateException)
            {
                cancelThrew = true;
            }
            bool brokenCancelled = EndsCancelled(broken.Completion);

            return new CancelOutcome(beforeCancelled, earlyPlatformCancels, pendingBeforeCancel, duringCancelled, latePlatformCancels, lateReportIgnored, cancelThrew, brokenCancelled);
        })
        .TheTest
        .ShouldPass<CancelOutcome>((because, outcome) =>
        {
            because.ItsTrue("a token cancelled before the ceremony ends it cancelled", outcome.BeforeCancelled);
            because.ItsTrue("and tells the platform once", outcome.EarlyPlatformCancels == 1);
            because.ItsTrue("the ceremony waits while nothing has happened", outcome.PendingBeforeCancel);
            because.ItsTrue("cancelling while the prompt is up ends it cancelled with no callback", outcome.DuringCancelled);
            because.ItsTrue("and tells the platform once", outcome.LatePlatformCancels == 1);
            because.ItsTrue("a callback after the cancel is ignored", outcome.LateReportIgnored);
            because.ItsTrue("a throwing platform cancel surfaces to the canceller", outcome.CancelThrew);
            because.ItsTrue("but the ceremony still ends cancelled", outcome.BrokenCancelled);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void KeepTheFirstOutcome()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("reports, then cancels, then disposes", (unused, reg) =>
        {
            using CancellationTokenSource source = new CancellationTokenSource();
            int platformCancels = 0;
            string result;
            bool secondReportIgnored;
            using (PlatformCeremony<string> ceremony = new PlatformCeremony<string>(source.Token, () => platformCancels++))
            {
                bool firstTaken = ceremony.Report("first");
                secondReportIgnored = !ceremony.Report("second");
                source.Cancel();
                result = firstTaken ? ceremony.Completion.GetAwaiter().GetResult() : string.Empty;
            }

            // After disposal the token no longer reaches the platform.
            using CancellationTokenSource afterwards = new CancellationTokenSource();
            int afterDisposeCancels = 0;
            PlatformCeremony<string> disposed = new PlatformCeremony<string>(afterwards.Token, () => afterDisposeCancels++);
            disposed.Dispose();
            afterwards.Cancel();

            return new FirstOutcome(result, secondReportIgnored, afterDisposeCancels, disposed.IsCompleted);
        })
        .TheTest
        .ShouldPass<FirstOutcome>((because, outcome) =>
        {
            because.ItsTrue("the callback's report is the result: " + outcome.Result, outcome.Result == "first");
            because.ItsTrue("a second report is ignored", outcome.SecondReportIgnored);
            because.ItsTrue("a disposed ceremony no longer cancels the platform", outcome.AfterDisposeCancels == 0);
            because.ItsTrue("nor completes", !outcome.DisposedCompleted);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static bool EndsCancelled(Task<string> completion)
    {
        try
        {
            completion.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // A cancelled task throws here; its state is read below.
        }
        return completion.IsCanceled;
    }

    private sealed record CancelOutcome(bool BeforeCancelled, int EarlyPlatformCancels, bool PendingBeforeCancel, bool DuringCancelled, int LatePlatformCancels, bool LateReportIgnored, bool CancelThrew, bool BrokenCancelled);

    private sealed record FirstOutcome(string Result, bool SecondReportIgnored, int AfterDisposeCancels, bool DisposedCompleted);
}
