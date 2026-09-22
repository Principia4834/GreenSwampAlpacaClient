using GreenSwamp.Alpaca.Telescope.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GreenSwamp.Alpaca.Client.ViewModels;

/// <summary>
/// Composition-root registration for the telescope client's ViewModels layer (architecture §7.3).
/// Does not register <see cref="IUiDispatcher"/> itself - that concrete (Avalonia-backed)
/// implementation lives in the application project (which alone may reference Avalonia.Threading)
/// and must be registered by the caller before/alongside this extension.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelescopeViewModels(this IServiceCollection services)
    {
        services.AddSingleton<TelescopeTabViewModelFactory>();
        return services;
    }
}
