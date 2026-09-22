using GreenSwamp.Alpaca.Telescope.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GreenSwamp.Alpaca.Telescope.Model;

/// <summary>
/// Composition-root registration for the telescope client's Model layer (architecture §7.2-§7.3).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ITelescopeSessionFactory and its dependencies as DI singletons. Individual
    /// ITelescopeSession instances are created at runtime via the factory - they are not
    /// themselves DI-registered (architecture §6.1).
    /// </summary>
    public static IServiceCollection AddTelescopeIntegration(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITelescopeSessionFactory, TelescopeSessionFactory>();
        return services;
    }
}
