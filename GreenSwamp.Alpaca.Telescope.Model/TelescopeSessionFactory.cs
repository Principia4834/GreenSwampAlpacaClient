using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Telescope.Model;

/// <summary>
/// DI-singleton factory implementing ITelescopeSessionFactory (architecture §6.1). Holds no
/// telescope state itself - purely constructs new TelescopeSession instances.
/// </summary>
internal sealed class TelescopeSessionFactory(TimeProvider timeProvider) : ITelescopeSessionFactory
{
    public ITelescopeSession Create(TelescopeConnectionDescriptor descriptor) =>
        new TelescopeSession(descriptor, timeProvider);
}
