using System.Windows.Input;

namespace Bam.Maui.Passkeys;

/// <summary>
/// An asynchronous <see cref="ICommand"/> that runs one execution at a time.  While it runs,
/// <see cref="CanExecute"/> is false, so a double tap can't start a second ceremony.
/// </summary>
public sealed class PasskeyCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _running;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">The work.  It runs on the calling thread up to its first await, and its exceptions are not swallowed.</param>
    /// <param name="canExecute">An extra condition, or null for none.</param>
    public PasskeyCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>Whether an execution is in progress.</summary>
    public bool IsRunning => _running;

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        return !_running && (_canExecute is null || _canExecute());
    }

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        await ExecuteAsync().ConfigureAwait(true);
    }

    /// <summary>Runs the command unless it is already running or can't run.</summary>
    /// <returns>A task that completes when the work is done; it completes at once when nothing ran.</returns>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(true);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Tells bound controls to read <see cref="CanExecute"/> again.</summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
