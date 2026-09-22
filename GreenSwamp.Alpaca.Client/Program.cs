using Avalonia;
using System;
using GreenSwamp.Alpaca.Client.ViewModels;
using GreenSwamp.Alpaca.Telescope.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GreenSwamp.Alpaca.Client;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Generic Host composition root (architecture §7.1-7.2): DI/configuration/logging
        // composition only - per-instance telescope session lifetimes are owned by
        // ITelescopeSessionFactory/ITelescopeSession themselves, not by IHostedService.
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddTelescopeIntegration();
        builder.Services.AddTelescopeViewModels();
        builder.Services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        using var host = builder.Build();
        host.Start();

        BuildAvaloniaApp(host.Services)
            .StartWithClassicDesktopLifetime(args);

        host.StopAsync().GetAwaiter().GetResult();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
