using Bam.Logging;

namespace Bam.Maui.Tests.TestClasses;

/// <summary>A logger that keeps every committed event so a test can read what was logged.</summary>
public sealed class CapturingLogger : Logger
{
    private readonly List<LogEvent> _events = new List<LogEvent>();

    public CapturingLogger()
    {
        CommitCycleDelay = 0;
        Verbosity = VerbosityLevel.Information;
        StartLoggingThread();
    }

    /// <summary>The events committed so far, after the queue has drained.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            BlockUntilEventQueueIsEmpty();
            lock (_events)
            {
                return _events.ToList();
            }
        }
    }

    /// <summary>Everything any event could print, joined, for "this text appears nowhere" assertions.</summary>
    public string AllText
    {
        get
        {
            return string.Join("\n", Events.Select(e => string.Join("|", e.Message, e.MessageSignature, string.Join(",", e.MessageVariableValues ?? Array.Empty<string?>()), e.StackTrace)));
        }
    }

    public override void CommitLogEvent(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }
    }
}

/// <summary>A logger whose every write throws, to prove logging cannot break a ceremony result.</summary>
public sealed class ThrowingLogger : Logger
{
    public override void AddEntry(string messageSignature, LogEventType type, params string?[] variableMessageValues)
    {
        throw new InvalidOperationException("logger is broken");
    }

    public override void CommitLogEvent(LogEvent logEvent)
    {
        throw new InvalidOperationException("logger is broken");
    }
}
