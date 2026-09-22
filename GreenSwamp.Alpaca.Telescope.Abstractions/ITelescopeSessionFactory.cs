namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// Creates ITelescopeSession instances. Registered as a DI singleton (architecture §6.1, §7.3);
/// holds no telescope state itself, satisfying the "no ambient/static state" constraint
/// (requirements §9).
/// </summary>
public interface ITelescopeSessionFactory
{
    ITelescopeSession Create(TelescopeConnectionDescriptor descriptor);
}
