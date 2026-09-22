using GreenSwamp.Alpaca.Client.ViewModels;

namespace GreenSwamp.Alpaca.Telescope.TestHarness;

/// <summary>
/// Synchronous pass-through IUiDispatcher, used by Level 2 ViewModel unit tests so bindable
/// state can be asserted immediately after an event is raised, with no real Avalonia UI
/// thread/dispatcher involved (architecture §11 open item - resolved; see IUiDispatcher remarks).
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
