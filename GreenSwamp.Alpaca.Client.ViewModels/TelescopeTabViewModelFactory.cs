using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Client.ViewModels;

/// <summary>
/// Creates a <see cref="TelescopeTabViewModel"/> for a specific, runtime-supplied
/// <see cref="TelescopeConnectionDescriptor"/> (architecture §7.3) - not itself resolvable
/// per-tab from DI since the descriptor is a runtime value, not a DI-injectable dependency.
/// Registered as a DI singleton; <see cref="MainViewModel"/> (or equivalent) calls
/// <see cref="Create"/> once per opened tab.
/// </summary>
public sealed class TelescopeTabViewModelFactory(ITelescopeSessionFactory sessionFactory, IUiDispatcher dispatcher)
{
    public TelescopeTabViewModel Create(TelescopeConnectionDescriptor descriptor)
    {
        var session = sessionFactory.Create(descriptor);
        return new TelescopeTabViewModel(session, dispatcher);
    }
}
