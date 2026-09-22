namespace GreenSwamp.Alpaca.Client.ViewModels;

/// <summary>
/// Abstraction over UI-thread marshaling (architecture §6.6/§8.1, §11 open item - resolved).
/// Session events (<see cref="Abstractions.ITelescopeSession.StateUpdated"/>,
/// <see cref="Abstractions.ITelescopeSession.ConnectionStatusChanged"/>) are raised on arbitrary
/// background threads; ViewModels must marshal onto the UI thread before mutating bindable state.
///
/// This is injected (not called as a static <c>Dispatcher.UIThread</c> reference) purely so that
/// Level 2 ViewModel unit tests (test strategy §5.2) can substitute a synchronous pass-through
/// implementation, keeping the ViewModels assembly and its test project free of any dependency on
/// a live Avalonia UI thread/dispatcher.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Schedules <paramref name="action"/> to run on the UI thread, without waiting for it to complete.</summary>
    void Post(Action action);
}
