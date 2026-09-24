using FluentAssertions;
using GreenSwamp.Alpaca.Client.ViewModels;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.TestHarness;

namespace GreenSwamp.Alpaca.Client.Tests.ViewModels;

public class TelescopeTabViewModelStatusTests
{
    [Fact]
    public void ConnectionStatusChanged_UpdatesConnectionState_AndErrorMessage()
    {
        var session = new FakeTelescopeSession();
        var vm = new TelescopeTabViewModel(session, new ImmediateUiDispatcher());

        session.RaiseConnectionStatusChanged(new TelescopeConnectionStatus(
            TelescopeConnectionState.Connected,
            IsAlpacaRestActive: true,
            IsSignalRActive: true,
            IsSignalRDegraded: false,
            ErrorMessage: null));

        vm.IsConnected.Should().BeTrue();
        vm.ConnectionState.Should().Be(TelescopeConnectionState.Connected);
        vm.StatusMessage.Should().BeNullOrEmpty();

        session.RaiseConnectionStatusChanged(new TelescopeConnectionStatus(
            TelescopeConnectionState.Connected,
            IsAlpacaRestActive: true,
            IsSignalRActive: false,
            IsSignalRDegraded: true,
            ErrorMessage: null));

        vm.IsConnected.Should().BeTrue();
        vm.ConnectionState.Should().Be(TelescopeConnectionState.Connected);
    }
}
