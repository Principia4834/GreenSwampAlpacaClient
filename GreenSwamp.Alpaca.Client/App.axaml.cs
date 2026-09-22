using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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
            // Prototype UI (MainViewModel/tab binding) is explicitly deferred to a later stage;
            // _services is retained for that future work. For now the window has no DataContext.
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
