using Avalonia.Controls;
using GreenSwamp.Alpaca.Client.ViewModels;
using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Client;

public partial class MainWindow : Window
{
    private readonly TelescopeTabViewModelFactory? _viewModelFactory;
    private TelescopeTabViewModel? _activeViewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(TelescopeTabViewModelFactory viewModelFactory, TelescopeTabViewModel viewModel) : this()
    {
        _viewModelFactory = viewModelFactory;
        SetActiveViewModel(viewModel);
    }

    protected override async void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        if (_activeViewModel is not null)
        {
            await _activeViewModel.DisposeAsync();
        }
    }

    private async void OnCreateSessionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModelFactory is null)
        {
            ConnectionInputStatusTextBlock.Text = "Session factory is not available.";
            return;
        }

        var hostName = HostNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(hostName))
        {
            ConnectionInputStatusTextBlock.Text = "Host/IP is required.";
            return;
        }

        if (!int.TryParse(PortTextBox.Text, out var port) || port <= 0)
        {
            ConnectionInputStatusTextBlock.Text = "Port must be a positive number.";
            return;
        }

        if (!int.TryParse(DeviceNumberTextBox.Text, out var deviceNumber) || deviceNumber < 0)
        {
            ConnectionInputStatusTextBlock.Text = "Device number must be zero or greater.";
            return;
        }

        var oldViewModel = _activeViewModel;
        var newViewModel = _viewModelFactory.Create(new TelescopeConnectionDescriptor(hostName, port, deviceNumber));
        SetActiveViewModel(newViewModel);

        if (oldViewModel is not null)
        {
            await oldViewModel.DisposeAsync();
        }

        ConnectionInputStatusTextBlock.Text = $"Created session for {hostName}:{port} (device {deviceNumber}).";
    }

    private void SetActiveViewModel(TelescopeTabViewModel viewModel)
    {
        _activeViewModel = viewModel;
        DataContext = viewModel;
    }
}