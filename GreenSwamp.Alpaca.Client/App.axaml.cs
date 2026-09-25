using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GreenSwamp.Alpaca.Client.ViewModels;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GreenSwamp.Alpaca.Client;

public partial class App : Application
{
    private readonly IServiceProvider? _services;

    public App() { }

    /// <summary>Used by Program.cs's Generic Host composition root (architecture §7.2).</summary>
    public App(IServiceProvider services) => _services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_services is null)
            {
                desktop.MainWindow = new MainWindow();
            }
            else
            {
                var factory = _services.GetRequiredService<TelescopeTabViewModelFactory>();
                var viewModel = factory.Create(new TelescopeConnectionDescriptor("127.0.0.1", 11111, 0));
                desktop.MainWindow = new MainWindow(factory, viewModel);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
