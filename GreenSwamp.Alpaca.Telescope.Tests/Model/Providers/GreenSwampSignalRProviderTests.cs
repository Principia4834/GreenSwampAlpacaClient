using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;
using GreenSwamp.Alpaca.Telescope.TestHarness;

namespace GreenSwamp.Alpaca.Telescope.Tests.Model.Providers;

/// <summary>
/// Level 1 (test strategy §8): GreenSwampSignalRProvider behavior against the fake hub double.
/// </summary>
public class GreenSwampSignalRProviderTests
{
    [Fact]
    public void Payload_Deserializes_AllEnumAndVectorFields()
    {
        var options = CreateJsonOptions();
        var json = """
        {
          "Altitude": 1.25,
          "Azimuth": 2.5,
          "Declination": -3.75,
          "RightAscension": 4.0,
          "SideOfPier": "ThroughThePole",
          "LocalHourAngle": 5.5,
          "UTCDate": "2025-06-01T12:00:00Z",
          "LocalDate": "2025-06-01T13:00:00Z",
          "Slewing": true,
          "Tracking": true,
          "LimitsOn": false,
          "LimitWarningActive": true,
          "LimitWarningMessage": "warning",
          "LimitWarningSequence": 9,
          "AtPark": false,
          "AtHome": true,
          "LimitTriggered": false,
          "IsMountRunning": true,
          "ComPort": "COM1",
          "ConnectedClientCount": 3,
          "HasEverBeenConnected": true,
          "ParkSelectedName": "Park A",
          "ParkPositionNames": ["Park A", "Park B"],
          "TargetRightAscension": 6.5,
          "TargetDeclination": 7.25,
          "ActualAxisX": 8.0,
          "ActualAxisY": 9.0,
          "AppAxisX": 10.0,
          "AppAxisY": 11.0,
          "AxisSteps": [1,2,3],
          "TrackingRate": "Lunar",
          "IsPulseGuidingRa": true,
          "IsPulseGuidingDec": false,
          "SlewState": "SlewHome",
          "LoopCounter": 12,
          "TimerOverruns": 13,
          "LastUpdate": "2025-06-01T12:00:01Z",
          "FlipOnNextGoto": true,
          "ControllerVoltage": 14.5,
          "LowVoltageEvent": false,
          "VoiceActive": true,
          "VoiceName": "Voice",
          "VoiceVolume": 15,
          "IsAutoHomeRunning": false,
          "AutoHomeProgressBar": 16,
          "IsGermanPolarMode": true,
          "AutoHomeAxisX": 17.5,
          "AutoHomeAxisY": 18.5,
          "StepsPerRevolution": [10,20],
          "StepsWormPerRevolution": [30.5,40.5],
          "StepsTimeFreq": [50,60],
          "TrackingOffsetRate": { "X": 1.5, "Y": 2.5 },
          "CanPPec": true,
          "CanHomeSensor": false,
          "CanPolarLed": true,
          "CanAdvancedCmdSupport": false,
          "MountName": "Mount",
          "MountVersion": ["1", "2"],
          "Capabilities": "Cap",
          "SiteLatitude": 51.5,
          "AlignmentMode": "GermanPolar",
          "MountType": "Simulator",
          "SiteLongitude": -0.1,
          "SiteElevation": 123.4
        }
        """;

        var payload = JsonSerializer.Deserialize<GreenSwampTelescopeStatePayload>(json, options);

        payload.Should().NotBeNull();
        payload!.TrackingRate.Should().Be(GreenSwampDriveRate.Lunar);
        payload.AlignmentMode.Should().Be(GreenSwampAlignmentMode.GermanPolar);
        payload.MountType.Should().Be(GreenSwampMountType.Simulator);
        payload.SideOfPier.Should().Be(TelescopePierSide.ThroughThePole);
        payload.SlewState.Should().Be(GreenSwampSlewType.SlewHome);
        payload.TrackingOffsetRate.X.Should().Be(1.5);
        payload.TrackingOffsetRate.Y.Should().Be(2.5);
        payload.ParkPositionNames.Should().ContainInOrder("Park A", "Park B");
        payload.AxisSteps.Should().ContainInOrder(1, 2, 3);
        payload.SiteLongitude.Should().Be(-0.1);
        payload.SiteElevation.Should().Be(123.4);
    }

    [Fact]
    public void FakeHub_IsolatesGroupsByDeviceNumber()
    {
        var hub = new FakeGreenSwampSignalRHub();
        var clientA = hub.Connect(0);
        var clientB = hub.Connect(1);
        clientA.Start();
        clientB.Start();

        var stateA = new TelescopeState { Altitude = 10 };
        var stateB = new TelescopeState { Altitude = 20 };

        hub.Broadcast(0, stateA);
        hub.Broadcast(1, stateB);

        clientA.ReceivedCount.Should().Be(1);
        clientA.LastState.Should().BeSameAs(stateA);
        clientB.ReceivedCount.Should().Be(1);
        clientB.LastState.Should().BeSameAs(stateB);
    }

    [Fact]
    public void ProviderFacingLifecycle_Connect_Rejoin_Leave_Disconnect_AreTracked()
    {
        var hub = new FakeGreenSwampSignalRHub();
        var connection = hub.CreateProviderConnection(0);

        connection.Connect();
        hub.ConnectCallCount.Should().Be(1);
        hub.JoinCallCount.Should().Be(1);

        connection.Rejoin();
        hub.JoinCallCount.Should().Be(2);

        connection.Leave();
        hub.LeaveCallCount.Should().Be(1);

        connection.Disconnect();
        hub.DisconnectCallCount.Should().Be(1);
        hub.LeaveCallCount.Should().Be(2);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
