# Telescope Integration Architecture

**Status:** Approved, with post-implementation amendments applied. §6.2's `ITelescopeSession` sketch now includes the `FindHome` member and a single collapsed `StateUpdated` event (both confirmed as-built, per implementation design §6.5 and §2a/§3/§4 respectively — previously flagged here as small additive amendments still owed to this document). §11's Open Items list is annotated with as-built status (✅/🔶/⬜) and a "New items identified during this implementation slice" subsection, reflecting what the first Connect/FindHome/Park/Abort implementation pass against a real reference server actually confirmed, resolved, or newly revealed — see that section for details, most notably: `IsGreenSwampClass` detection does not currently match the real reference server (🔶, §6.8/§11), and there is no continuous state-polling loop yet, so requirements FR37/38/41 are not yet fully met (🔶, §11).

## 1. Purpose
This document defines the software architecture for the telescope integration capability specified in `telescope-requirements.md` (FR1-48, NFR49-65) and designed for testability per `telescope-test-strategy.md`. It refines the MVVM shape into concrete layers, contracts, and project structure, and confirms the design against current .NET 10 / Avalonia 12 architectural guidance (consulted via the Avalonia docs MCP and Microsoft Learn MCP) and against the real ASCOM client libraries and the real GreenSwampAlpacaServer codebase.

Settings/configuration persistence remains out of scope (per the requirements doc, Constraints). Where the architecture must assume *something* about configuration (e.g. "a session is constructed with connection parameters"), this is flagged explicitly as a seam to be filled by the (future) settings design, not designed here.

## 2. Sources Consulted
- `telescope-requirements.md` and `telescope-test-strategy.md` (this session's approved baseline).
- Avalonia official docs (via MCP): "Implementing dependency injection" (`Microsoft.Extensions.DependencyInjection` + `App.axaml.cs`), "How to: Implement common MVVM patterns" (`CommunityToolkit.Mvvm`), Avalonia expert rules (compiled bindings, `ObservableObject`, `[RelayCommand]`, no ReactiveUI, `Dispatcher.UIThread`).
- Microsoft Learn (via MCP): `TimeProvider`/`FakeTimeProvider`, ASP.NET Core SignalR .NET client (`HubConnectionBuilder`, `WithAutomaticReconnect`), .NET Generic Host (`Microsoft.Extensions.Hosting`, `IHostedService`/`BackgroundService` lifecycle).
- The actual client project (`GreenSwamp.Alpaca.Client.csproj`): `net10.0`, Avalonia 12.1.2, already references `ASCOM.AstrometryTools`, `ASCOM.Common.Components`, `ASCOM.Exception.Library`, `ASCOM.Tools` — but **not yet** `ASCOM.Alpaca.Components`, which was found to contain the official ASCOM-Initiative Alpaca REST client (`ASCOM.Alpaca.Clients.AlpacaTelescope`) and discovery components (`ASCOM.Alpaca.Discovery.Finder`/`AlpacaDiscovery`). This was confirmed by extracting and inspecting the package directly.
- Reflection over `ASCOM.Alpaca.Components 4.0.0`: `AlpacaTelescope` implements `IAlpacaClientV2`, `IAscomDeviceV2`, `IAscomDevice`, `ITelescopeV4`, `ITelescopeV3`, `IDisposable`. `ASCOM.Common.ClientExtensions.ConnectAsync`/`DisconnectAsync` extension methods already implement the "call `Connect()`, poll `Connecting` until false" pattern for Platform 7 (V2+) devices.
- The real `GreenSwampAlpacaServer` codebase: `ChartHub.cs`, `ChartDataService.cs`, `TelescopeStateService.cs`, `TelescopeStateModel.cs`, confirming exact SignalR payload shapes (see §6.4).
- Further real-codebase investigation (this pass), prompted by stakeholder feedback on Open Items: `HandControllerPanel.razor.cs` and `Mount.HandController.cs` (hand-controller jog business logic — mode/backlash/flip/tracking-rate composition, see §6.7); `ConfigController.cs` (the server's broader Configuration File Management REST surface, see §6.8); `Program.cs` (`ServerName`/`Manufacturer` constants used in the Alpaca management API description, see §6.8); `docs/API-REFERENCE.md` (management API response shapes). Reflection over `ASCOM.Alpaca.Discovery.AlpacaDiscovery`/`AlpacaDevice`/`AscomDevice` in `ASCOM.Alpaca.Components 4.0.0` (see §6.9).

## 3. Key Architectural Decisions (confirmed with stakeholder)
These were raised as explicit decision points during this design pass and resolved as follows:

1. **DI composition root: .NET Generic Host**, not a bare `ServiceCollection` built inline in `App.axaml.cs`. Rationale: each telescope session owns one or more long-running background loops (REST polling, SignalR connection) that benefit from `IHostedService`-style coordinated start/stop, plus centralized configuration/logging composition. The Avalonia UI thread and lifetime remain governed by `AppBuilder`/`IClassicDesktopStyleApplicationLifetime`; the Generic Host runs alongside it, not instead of it (see §7).
2. **Alpaca REST provider is a thin wrapper around `ASCOM.Alpaca.Clients.AlpacaTelescope` and `ASCOM.Common.ClientExtensions`**, not a hand-rolled HTTP/JSON client. The project already references `ASCOM.Common.Components`; it must add `ASCOM.Alpaca.Components` (same publisher/version family, `net10.0`-targeted, MIT licensed) to obtain `AlpacaTelescope`, `AlpacaConfiguration`, and `ASCOM.Alpaca.Discovery.Finder`/`AlpacaDiscovery`. This directly satisfies FR10, FR12, and removes an entire hand-written protocol layer plus its testing burden (see §6.3).
3. **Target interface is `ITelescopeV4`** (ASCOM Platform 7, per `https://ascom-standards.org/newdocs/telescope.html`), not `ITelescopeV3`. `AlpacaTelescope` already implements both; `ITelescopeV4` (via `IAscomDeviceV2`) adds `Connect()`/`Disconnect()`/`Connecting`/`DeviceState` alongside the V3 property/method surface. The internal Alpaca provider must code against `ITelescopeV4` + `IAscomDeviceV2`, not just V3, so connect/disconnect is asynchronous-native rather than emulated via the `Connected` property setter (see §6.3, §6.5).
4. **GreenSwamp SignalR telemetry carries genuine RA/Dec/Alt-Az position data, not just raw axis steps.** Initial exploration of `ChartHub` found only axis-step chart data; the stakeholder clarified that `TelescopeStateService`'s underlying `TelescopeStateModel` (the source data `ChartDataService` taps) additionally carries `RightAscension`, `Declination`, `Altitude`, `Azimuth`, `ActualAxisX/Y`, `AppAxisX/Y` in addition to `AxisSteps`. **However, the existing `ChartHub`/`ChartDataService` SignalR surface is scoped specifically to charting and only ever broadcasts step-based `ChartPointDto`/`PulsePointDto` payloads (`ReceiveAxisPoint`, `ReceivePulsePoint`) — it does not currently broadcast RA/Dec/Alt-Az telemetry.** Using SignalR for genuine low-latency sky-coordinate telemetry (not just step/pulse charting) therefore requires a **new or extended GreenSwamp-specific SignalR surface** (a new hub, or new hub methods/groups on a broadcast of `TelescopeStateModel`-shaped position data) that does not exist today. This is documented as an assumption/dependency, not assumed away (see §6.4, §11).
5. **Update delivery mechanism: plain C# events (`EventHandler<T>`)**, not `IObservable<T>`/Rx.NET, and not `INotifyPropertyChanged` on the session itself. Chosen for consistency with `CommunityToolkit.Mvvm` idioms already mandated by the Avalonia guidance, and to avoid introducing Rx.NET as a second reactive paradigm. §6.6 documents this choice's limitations explicitly (ordering/back-pressure/thread-marshaling responsibilities that events do not solve for free) and how the architecture compensates.
6. **Jog/manual-control business logic is deliberately staged, not delivered in full for v1.** Direct inspection of the real hand-controller implementation (`HandControllerPanel.razor.cs`, `Mount.HandController.cs`) found it is materially more complex than a single rate parameter: three HC modes (Axes / Guiding / Pulse), axis-flip settings (`HcFlipEw`/`HcFlipNs`), anti-backlash logic (`HcAntiRa`/`HcAntiDec`), hemisphere/alt-az-aware sign inversion, and composition with the currently active tracking rate (`RateMovePrimaryAxis`/`RateMoveSecondaryAxis` cancel other motion, apply the new rate, then reassert tracking). Per stakeholder direction, **v1 does not replicate this logic**: `JogAsync`/`StopJogAsync` map directly onto Alpaca `ITelescopeV4.MoveAxis(axis, rate)` with a user-selected rate. Full analysis of hand-controller-parity jog logic, in the context of the current GreenSwampAlpacaServer solution, is explicitly deferred to a dedicated future design pass (see §6.7).
7. **GreenSwamp-class detection (FR14) is resolved using the standard Alpaca Management API description, not a custom protocol extension.** GreenSwampAlpacaServer's `/management/v1/description` endpoint returns `ServerName`/`Manufacturer` values that identify the server as GreenSwamp (confirmed in `Program.cs`); the per-device `Description`/`DriverInfo` properties provide a secondary, device-level signal. No GreenSwamp-specific Alpaca `Action` or custom endpoint is required (see §6.8).

## 4. Architectural Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│ View (Avalonia .axaml)                                                   │
│   - Visual composition only. Binds to ViewModel via compiled bindings.   │
└───────────────────────────────┬───────────────────────────────────────┘
								 │ DataContext (compiled bindings, x:DataType)
┌───────────────────────────────▼───────────────────────────────────────┐
│ ViewModel (CommunityToolkit.Mvvm, per tab)                                │
│   TelescopeTabViewModel : ObservableObject                               │
│   - [ObservableProperty] bindable position/state/capability fields       │
│   - [RelayCommand] Connect/Disconnect/SlewTo/Jog/Stop/Park/Unpark/Track   │
│   - Depends ONLY on ITelescopeSession (app-facing abstraction)            │
│   - No Alpaca/SignalR/HTTP types, no polling logic, no dispatcher logic  │
└───────────────────────────────┬───────────────────────────────────────┘
								 │ ITelescopeSession (per-instance, DI-resolved via factory)
┌───────────────────────────────▼───────────────────────────────────────┐
│ Model — Application-Facing Abstraction Layer                             │
│   ITelescopeSessionFactory  (DI singleton)                               │
│   ITelescopeSession         (one per tab/instance; owns providers)        │
│     - Connect/Disconnect/MoveAxis/Jog/SlewToCoordinatesAsync/...          │
│     - PositionUpdated / StateUpdated / ConnectionStatusChanged events     │
│     - Capabilities snapshot (transport + device capability union)        │
└───────┬───────────────────────────────────────────────────┬───────────┘
		│                                                     │
┌───────▼───────────────────┐                     ┌───────────▼─────────────┐
│ AlpacaTelescopeProvider     │                     │ GreenSwampSignalRProvider │
│ (mandatory, all devices)    │                     │ (supplementary, opt-in)   │
│  - Wraps ASCOM.Alpaca.       │                     │  - Wraps HubConnection    │
│    Clients.AlpacaTelescope   │                     │    to GreenSwamp server   │
│  - ITelescopeV4 surface       │                     │  - Position telemetry     │
│  - Poll loop via TimeProvider │                     │    (future extended hub) │
│  - ConnectAsync/DisconnectAsync│                    │  - Command dispatch      │
│    (ASCOM.Common.ClientExtensions)│                 │    (future, not yet real)│
└───────┬───────────────────┘                     └───────────┬─────────────┘
		│ HTTP (Alpaca REST)                                    │ SignalR (WebSocket)
┌───────▼───────────────────┐                     ┌───────────▼─────────────┐
│ Any ASCOM Alpaca telescope   │                     │ GreenSwampAlpacaServer    │
│ (3rd-party driver, ASCOM      │                     │ (ChartHub + future        │
│  reference simulator, or       │                     │  position/command hub)   │
│  GreenSwampAlpacaServer)        │                     └───────────────────────────┘
└───────────────────────────┘
```

Layering rules (enforced by project references, see §10):
- **View → ViewModel**: compiled bindings only, no code-behind business logic.
- **ViewModel → Model abstraction**: constructor-injected `ITelescopeSessionFactory` / `ITelescopeSession` only. No `using ASCOM.*` or `using Microsoft.AspNetCore.SignalR.Client` in any ViewModel project.
- **Model abstraction → Providers**: internal to the Model assembly; providers are not exposed to ViewModels.
- **Providers → transport SDKs**: `AlpacaTelescopeProvider` depends on `ASCOM.Alpaca.Components`; `GreenSwampSignalRProvider` depends on `Microsoft.AspNetCore.SignalR.Client`. Neither dependency leaks upward.

## 5. Project / Assembly Structure

```
GreenSwamp.Alpaca.Client.sln
  GreenSwamp.Alpaca.Client                     (existing Avalonia app project)
	- Generic Host bootstrap (Program.cs, App.axaml.cs)
	- Views (.axaml) only

  GreenSwamp.Alpaca.Client.ViewModels          (new)
	- TelescopeTabViewModel, MainViewModel, etc.
	- References: GreenSwamp.Alpaca.Telescope.Abstractions, CommunityToolkit.Mvvm
	- Does NOT reference ASCOM.* or SignalR.Client

  GreenSwamp.Alpaca.Telescope.Abstractions     (new)
	- ITelescopeSession, ITelescopeSessionFactory, ITelescopeDiscoveryService, TelescopePosition,
	  TelescopeState, TelescopeCapabilities, DiscoveredTelescopeDevice, event-arg types
	- No transport dependencies at all — pure contracts + DTOs
	- Referenced by both ViewModels and the Model implementation project

  GreenSwamp.Alpaca.Telescope.Model            (new)
	- TelescopeSessionFactory, TelescopeSession (orchestrator), TelescopeDiscoveryService
	- Providers/AlpacaTelescopeProvider (uses ASCOM.Alpaca.Components)
	- Providers/GreenSwampSignalRProvider (uses Microsoft.AspNetCore.SignalR.Client)
	- Reconciliation logic, capability detection (shared GreenSwamp-class recognition helper, §6.8/§6.10), TimeProvider-based polling
	- Implements GreenSwamp.Alpaca.Telescope.Abstractions

  GreenSwamp.Alpaca.Themes                     (existing, unchanged)
```

This mirrors the test strategy's proposed test project layout (`...Tests`, `...IntegrationTests`, `...TestHarness` referencing these same assemblies) and satisfies NFR49-51 (maintainability: transport/protocol concerns fully separated from MVVM) and the test strategy's Level 2 requirement that ViewModel tests need only a fake `ITelescopeSession`, never a real provider.

**Open question — resolved by implementation.** `Abstractions` and `Model` were built as two separate assemblies, exactly as sketched above (`GreenSwamp.Alpaca.Telescope.Abstractions`, `GreenSwamp.Alpaca.Telescope.Model`), alongside `GreenSwamp.Alpaca.Client.ViewModels` as its own third assembly. All internal types (`TelescopeSession`, `TelescopeSessionFactory`, `AlpacaTelescopeProvider`, `GreenSwampClassDetector`) are `internal sealed`, confirming the two-assembly split adds real encapsulation value, not just documentation-only separation.

## 6. The Application-Facing Abstraction

### 6.1 `ITelescopeSessionFactory` (DI singleton)
```csharp
public interface ITelescopeSessionFactory
{
	ITelescopeSession Create(TelescopeConnectionDescriptor descriptor);
}
```
- Registered as a DI singleton (FR6-7). Does not itself hold telescope state — purely a factory, satisfying the "no ambient/static state" constraint (Requirements §9).
- `TelescopeConnectionDescriptor` is a minimal, immutable record carrying whatever a session needs to connect (host/port/device-number/transport hints). Its exact shape is intentionally left light here because **connection descriptor sourcing is a settings-design concern** (out of scope); the factory signature only commits to "some descriptor comes in, a session comes out."
- ViewModels never call transport-specific constructors; they call `ITelescopeSessionFactory.Create(...)` once per tab (FR8, FR16-22).

### 6.2 `ITelescopeSession` (one instance per tab, per connected telescope)
```csharp
public interface ITelescopeSession : IAsyncDisposable
{
	Guid InstanceId { get; }
	TelescopeCapabilities Capabilities { get; }
	TelescopeConnectionStatus Status { get; }

	Task ConnectAsync(CancellationToken ct = default);
	Task DisconnectAsync(CancellationToken ct = default);

	Task SlewToCoordinatesAsync(double rightAscensionHours, double declinationDegrees, CancellationToken ct = default);
	Task SlewToAltAzAsync(double azimuthDegrees, double altitudeDegrees, CancellationToken ct = default);
	Task JogAsync(TelescopeAxis axis, double rateDegreesPerSecond, CancellationToken ct = default);
	Task StopJogAsync(TelescopeAxis axis, CancellationToken ct = default);
	Task AbortSlewAsync(CancellationToken ct = default);
	Task SetTrackingAsync(bool enabled, CancellationToken ct = default);
	Task ParkAsync(CancellationToken ct = default);
	Task UnparkAsync(CancellationToken ct = default);
	Task FindHome(CancellationToken ct = default);   // added per implementation design §6.5 — deliberately no "Async" suffix, matching the ASCOM FindHome() method name itself

	event EventHandler<TelescopeStateUpdatedEventArgs>? StateUpdated;
	event EventHandler<TelescopeConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
}
```
**Amendment applied (was flagged in implementation design §6.5 as a small additive change to batch in here):** `FindHome` is added as a member. The originally-sketched separate `PositionUpdated` event is also removed here — see §6.6 amendment below; as built, a single `StateUpdated` event carries the full position+state snapshot, since both the standard Alpaca `DeviceState` call and the future GreenSwamp `TelescopeStateHub` each deliver one full refresh per tick rather than separately-timed position/state updates.
Design notes:
- **Concepts, not protocol calls** (FR1-5): `JogAsync`/`StopJogAsync` are new application-level concepts with no 1:1 Alpaca REST method; internally they resolve to repeated/held `MoveAxis` calls (over whichever transport is fastest — FR30) rather than exposing `MoveAxis` raw semantics to the ViewModel.
- **`TelescopeAxis`** is a small app-level enum (`RightAscension`/`Primary`, `Declination`/`Secondary`, and potentially `Tertiary`) — a thin re-export/mirror of `ASCOM.Common.DeviceInterfaces.TelescopeAxis`, defined in `Abstractions` so the ViewModel layer never references `ASCOM.Common` directly, even though the underlying enum values are identical. This is intentional protocol insulation, not accidental duplication.
- **`IAsyncDisposable`**: disposal must await teardown of the REST polling loop and the SignalR connection cleanly (FR21, NFR63-65) — synchronous `Dispose` would either block or leave the loop running past disposal.
- Every method takes a `CancellationToken` (test strategy §4, point 6).

### 6.3 Alpaca REST Provider — internal, wraps the real ASCOM client
```csharp
internal sealed class AlpacaTelescopeProvider : IAsyncDisposable
{
	// Wraps ASCOM.Alpaca.Clients.AlpacaTelescope (which implements ITelescopeV4)
	private readonly AlpacaTelescope _client;
	...
}
```
- **Do not hand-roll HTTP/JSON.** `ASCOM.Alpaca.Components` (confirmed present on nuget.org, MIT-licensed, ASCOM-Initiative-owned, `net10.0`-targeted, same publisher as the already-referenced `ASCOM.Common.Components`/`ASCOM.Tools`/`ASCOM.Exception.Library`) supplies `ASCOM.Alpaca.Clients.AlpacaTelescope`, which already implements `ITelescopeV4` end-to-end over HTTP, including retries, timeouts, and JSON casing tolerance. This must be added as a new `PackageReference` in `GreenSwamp.Alpaca.Telescope.Model.csproj`.
- Connect/disconnect uses `ASCOM.Common.ClientExtensions.ConnectAsync`/`DisconnectAsync`, which already implement the ASCOM-recommended "call `Connect()`, poll `Connecting` until it returns false, with cancellation and timeout" pattern for Platform-7 (V2+) devices — this satisfies FR16-17 without reimplementing connect-polling logic.
- **Position/state polling loop**: uses an injected `TimeProvider` (NFR55-57, test strategy §8) rather than `Task.Delay` directly, so Level 1 tests can advance a `FakeTimeProvider` deterministically. Poll cadence is a to-be-confirmed constant/setting (Open Item), default informed by the reference server's own 250ms internal refresh (`TelescopeStateService`) as a reasonable starting point, not a hard requirement.
- **Discovery is a required, ViewModel-facing capability, not deferred** (revised from "optional, forward-looking" per stakeholder direction — see §6.10 for the dedicated `ITelescopeDiscoveryService` design). `ASCOM.Alpaca.Discovery.AlpacaDiscovery` (same package) is the underlying implementation.
- **Capability detection** (FR14): reads standard `Can*` properties from `ITelescopeV4`/`ITelescopeV3` plus an Alpaca `management`/API-version or a GreenSwamp-specific marker (e.g. a custom `Action` string, or presence of a `/api/v1/telescope/{n}/greenswampinfo`-style extension, **mechanism TBD at implementation-design stage** — flagged as an explicit Open Item) to decide whether to also attempt a SignalR connection.

### 6.4 GreenSwamp SignalR Provider — supplementary, opportunistic
- Wraps `Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder`, using `WithAutomaticReconnect()` (confirmed idiomatic via Microsoft Learn) for FR46-47 (reflect degraded/lost SignalR without losing the whole session).
- **Critical, confirmed assumption (documented, not assumed away):** the *existing* `ChartHub` in `GreenSwampAlpacaServer` is scoped to charting and only broadcasts step-based `ChartPointDto`/`PulsePointDto` payloads (via `ReceiveAxisPoint`/`ReceivePulsePoint`) for `RaDecChart-{n}`/`PulseChart-{n}` groups — confirmed by direct inspection of `ChartHub.cs` and `ChartDataService.cs`. It intentionally continues to serve this charting-only role and is **not** the transport this architecture uses for low-latency sky-position telemetry.
  - The underlying `TelescopeStateModel`/`TelescopeStateService` *does* already hold genuine `RightAscension`/`Declination`/`Altitude`/`Azimuth` (refreshed ~every 250ms) — so the *data* needed for low-latency position telemetry already exists server-side; it is just not yet broadcast over SignalR in that form.
  - This architecture therefore depends on a **future GreenSwamp-specific SignalR surface** (a new hub, or new hub method(s)/group(s) added to a server-side service, broadcasting `TelescopeStateModel`-shaped position/state data, not just chart points) — a cross-repository dependency on `GreenSwampAlpacaServer`, tracked identically to the pre-existing "SignalR command dispatch doesn't exist yet" risk in the requirements doc (§11).
  - Until that surface exists, `GreenSwampSignalRProvider` for position/state has **no real server counterpart to connect to**; the architecture defines the provider's shape and integration seam now (so the Model layer and tests are ready) but its live use is gated behind that future server capability. The existing `ChartHub` remains usable, unmodified, purely for optional chart-style visualizations of raw axis/pulse data if the UI wants that later — a distinct, secondary use case from telemetry-driven position display.
- **Axis-step-to-sky-coordinate conversion is explicitly out of this architecture's responsibility.** Should a future need arise to derive RA/Dec from raw `ChartHub` step data directly (bypassing the future position hub), that requires mount-specific gearing/steps-per-degree data that a GreenSwamp-class driver does not yet expose over Alpaca. Per the stakeholder, a **future phase** of GreenSwamp-class drivers is expected to expose this settings data, at which point step-based conversion could become a second, independent data path. This architecture does not build speculative conversion logic against undefined gearing data; it documents the assumption and leaves an extension seam (see §11, Open Items) rather than a design gap.
- Command dispatch over SignalR (FR13, FR30) remains gated on the pre-existing "no command hub yet" risk from the requirements doc; `GreenSwampSignalRProvider` defines the seam (an internal `ISignalRCommandChannel`-shaped capability, only activated when the server advertises command support) but has no real implementation to call until the server adds it.

### 6.5 `TelescopeSession` (orchestrator, internal)
- Owns one `AlpacaTelescopeProvider` (always) and, when the device is GreenSwamp-class and the future position hub is available, one `GreenSwampSignalRProvider` (opportunistically).
- Implements FR41-43 reconciliation: prefers SignalR-pushed updates when fresh; falls back to REST-polled updates when SignalR is stale/absent; de-duplicates/orders by a monotonic sequence or `TimeProvider`-derived timestamp attached at ingestion (not the transport's own wall-clock, to avoid clock-skew issues between client and two different transports/servers). Exact reconciliation algorithm remains an Open Item (per requirements §12, test strategy §12) but the seam (a single ingestion point per instance, both providers feed into it, not the ViewModel) is fixed here.
- Routes commands per FR30 (lowest-latency active transport per command) — initially this reduces to "always REST" until real SignalR command support exists (§6.4), but the routing decision point is isolated in `TelescopeSession` so adding a second real transport later does not touch ViewModels or `ITelescopeSession`'s public shape.

### 6.6 Update Delivery: Plain C# Events — Rationale and Documented Limitations
Per stakeholder decision, `ITelescopeSession` exposes plain `EventHandler<T>` events rather than `IObservable<T>` or an `ObservableObject`-based session. This is recorded here **with its trade-offs made explicit**, as requested:

**Why events, here:**
- Matches the mandated `CommunityToolkit.Mvvm` idiom already used for ViewModel properties/commands; no second reactive paradigm (Rx.NET) needs to be learned, tested, or version-pinned.
- Lowest-ceremony option for a ViewModel to subscribe/unsubscribe (`session.PositionUpdated += OnPositionUpdated;` in a `partial void OnActivated()`-style hook, unsubscribed on deactivation/dispose).
- Directly testable: Level 1/2 tests can subscribe and assert on raised events without needing an Rx test scheduler.

**Documented limitations and how the architecture compensates (do not treat these as solved by "just using events"):**
1. **No built-in back-pressure or coalescing.** If `AlpacaTelescopeProvider`'s poll loop and a future SignalR provider both raise `PositionUpdated` in quick succession, subscribers (ViewModels) receive every raised event, at whatever rate providers produce them. Mitigation: `TelescopeSession`'s reconciliation layer (§6.5) is responsible for **not raising** redundant/stale events in the first place (FR43) — the event mechanism itself does no filtering, so correctness here rests entirely on the orchestrator, not the transport.
2. **No thread affinity guarantee.** Events can be raised from whatever thread the originating provider uses (a polling `Task`, or SignalR's own connection thread) — never assume the UI thread. Per the Avalonia expert rules, marshaling to the UI thread is the **View's/ViewModel's** responsibility (e.g. `Dispatcher.UIThread.Post(...)` at the point the ViewModel updates its `[ObservableProperty]` state), not the Model's. `ITelescopeSession` implementations must document (and this architecture mandates) that events are raised on arbitrary background threads, never the UI thread, so this responsibility is unambiguous.
3. **Multicast delegate leaks are a real risk with per-tab instances.** Because `ITelescopeSession` is instance-based and potentially created/disposed repeatedly (tabs opening/closing, FR20), a ViewModel that forgets to unsubscribe on disposal leaks both the ViewModel and the session's providers. Mitigation: `ITelescopeSession` is `IAsyncDisposable`; its `DisposeAsync()` must itself clear all its own event subscriber lists (defensive) and the pattern of "ViewModel subscribes in constructor/activation, unsubscribes in its own dispose/deactivation" must be enforced by code review/analyzer convention, not by the language. This is called out explicitly as a manual discipline the events approach does not automate away (unlike `IObservable<T>.Subscribe` returning an `IDisposable` that composes more naturally with `CompositeDisposable`-style patterns).
4. **Harder to compose/merge than `IObservable<T>`.** Combining "whichever of REST-poll-event or SignalR-push-event is newest" is exactly the kind of problem Rx's `Merge`/`CombineLatest` solve declaratively. Using plain events means this merge logic must be **hand-written** inside `TelescopeSession` (§6.5) rather than composed from operators. This is an accepted, explicit cost of the decision, not an oversight; if reconciliation logic proves awkward as hand-written event-handling code during implementation, revisiting this decision (e.g. wrapping only the *internal* provider-to-session boundary in `IObservable<T>`, while keeping the external `ITelescopeSession` surface as plain events) is a legitimate fallback, noted here as an Open Item rather than foreclosed.
5. **Testability is good but not automatic.** Plain events are easy to assert against in unit tests (subscribe, trigger, assert), but ordering/timing assertions (test strategy FR43 coverage) require the test to control the event-raising order explicitly (e.g. via `FakeTimeProvider` ticks or manually invoking provider-internal hooks) — the test harness's "Scenario/event recorder" component (test strategy §6, item 7) is still necessary and does not become simpler just because events were chosen over `IObservable<T>`.

**Amendment, confirmed at implementation-design stage (§2a/§3/§4 of that document) and now built:** the originally-sketched separate `PositionUpdated`/`StateUpdated` events are collapsed into a single `StateUpdated` event carrying one full `TelescopeState` snapshot. This reflects how both real transports actually deliver data — one bundled `DeviceState` REST call, or (once built) one full-model `TelescopeStateHub` push — rather than independently-timed position-only/state-only updates. `ITelescopeSession`'s public shape (§6.2 above) has been updated to match.

### 6.7 Jog / Manual-Control: Staged Scope (v1 vs. Future)

Stakeholder review of this document identified that the real GreenSwampAlpacaServer hand-controller implementation is considerably more complex than the initial `JogAsync(axis, rateDegreesPerSecond)` sketch in §6.2 implied. This was confirmed by direct inspection of `HandControllerPanel.razor.cs` and `Mount.HandController.cs`:

- **The Blazor hand-controller UI only records key-down/key-up events** (`OnButtonDown(direction)`/`OnButtonUp(direction)`), plus a separately-set `_speed` (HC speed 1-8) and mode. The direction UI itself carries no rate information — rate/mode/flip/anti-backlash are all server-side state, not part of the "gesture."
- **`Mount.HcMoves(speed, direction)` is a substantial business-logic method**, not a thin pass-through to `MoveAxis`. It:
  - Resolves an effective rate (`delta`) from the selected HC speed (`SlewSpeed.One`..`Eight`, mapped to per-mount `_slewSpeedOne`.._slewSpeedEight` values) — the caller never supplies a raw ASCOM rate directly.
  - Applies **axis-flip settings** (`Settings.HcFlipEw`/`HcFlipNs`) via `ApplyFlip(direction)`, remapping compass directions before any rate is computed.
  - Branches on **HC mode** (`HcMode.Axes` / `HcMode.Guiding` / `HcMode.Pulse`), each with materially different sign/composition rules (e.g. `ApplyGuidingChange` additionally depends on `SideOfPier`/pier-side and whether the mount is a simulator vs. real SkyWatcher hardware; `HcMode.Pulse` diverts to an entirely separate async pulse-guide loop, `HcPulseMoveAsync`, not `MoveAxis` at all).
  - Applies **hemisphere-aware and Alt-Az-aware sign inversion** (`southernHemisphere`, `altAzMode` change the sign of `change[0]`/`change[1]` independently per direction).
  - Applies **anti-backlash compensation** (`HcAntiRa`/`HcAntiDec`, `RaBacklash`/`DecBacklash`) by tracking the previous move's step delta and injecting a compensating step count in the opposite direction on direction reversal.
  - Ultimately writes to `Mount.RateMovePrimaryAxis`/`RateMoveSecondaryAxis`, whose setters **cancel all other queued motion, dispatch a rate command to the hardware/simulator queue, and — critically — if `Tracking` is true, reassert tracking afterward** (`if (Tracking) this.SetTracking();`). This means a jog rate is not simply "instead of" tracking; it is layered on top of and must be recombined with the currently active tracking rate.
- **This logic is currently GreenSwampAlpacaServer/`Mount`-internal** — it is not exposed through the Alpaca REST `MoveAxis(axis, rate)` surface at all (Alpaca `MoveAxis` on `Telescope.cs` is a straight `_mount.RateMovePrimaryAxis = Rate`/`RateMoveSecondaryAxis = Rate`, i.e. remote Alpaca REST clients calling `MoveAxis` **do not get** HC mode/flip/anti-backlash treatment; only the in-process Blazor hand-controller panel calling `Mount.HcMoves` directly gets it).

**Decision for this architecture (v1 scope):** `ITelescopeSession.JogAsync(TelescopeAxis axis, double rateDegreesPerSecond)`/`StopJogAsync` map directly onto `ITelescopeV4.MoveAxis(axis, rate)` with a rate the user selects in the UI (e.g. a rate picker analogous to HC speed, but resolved client-side into a `deg/sec` value, not a GreenSwamp HC-speed enum). No flip/anti-backlash/mode/tracking-recomposition logic is replicated client-side for v1. This is a deliberate, stakeholder-directed scope reduction, not an oversight:

- It matches what any generic (non-GreenSwamp) Alpaca `ITelescopeV4` device already exposes — so v1 jog works identically and correctly for third-party Alpaca-only devices, which have no equivalent HC business logic to replicate anyway.
- For GreenSwamp-class devices, v1 jog is a strict subset of the Blazor hand-controller's capability (no anti-backlash, no flip compensation, no HC-mode-aware guiding/pulse behavior, no automatic re-assertion of a pre-existing tracking rate beyond whatever `ITelescopeV4.MoveAxis`/`Tracking` themselves already provide over Alpaca). This is an accepted, explicit limitation of v1, not a hidden gap.
- **Full hand-controller-parity jog control is deferred to a dedicated future design pass**, scoped specifically to the GreenSwampAlpacaServer solution's HC settings and business rules (flip/anti-backlash/mode/hemisphere/pier-side composition), and is tracked as an Open Item (§11) rather than designed here. Any such future design must also decide whether/how this GreenSwamp-specific jog richness is exposed over Alpaca REST at all (today it is not — only the in-process Blazor UI gets it) or whether it requires the future GreenSwamp SignalR command channel (§6.4) as its delivery mechanism, since REST `MoveAxis` alone cannot currently carry HC-mode/flip/anti-backlash parameters.

### 6.8 GreenSwamp-Class Device Identification and Capability Detection (FR14)

Direct inspection of the real server resolves what was previously an open question:

- **`GET /management/v1/description`** (standard ASCOM Alpaca Management API, confirmed via `AlpacaConfiguration.cs`/`Program.cs`) returns `ServerName = "Green Swamp Alpaca Server"` and `Manufacturer = "Green Swamp Software"` (constants in `Program.cs`), plus `ManufacturerVersion`/`Location`. This is a **standard, mandatory Alpaca endpoint** every Alpaca server exposes — no GreenSwamp-specific extension or custom `Action` is required to identify a GreenSwamp-class server.
- **Decision:** `AlpacaTelescopeProvider`'s capability probe (FR14) calls the standard Alpaca management description endpoint once at connect time and treats a recognized `ServerName`/`Manufacturer` combination (exact match strategy TBD at implementation-design stage — e.g. a configurable/known-value allow-list rather than a hard-coded string, so third-party servers are never misidentified) as the signal to also attempt the GreenSwamp SignalR provider (§6.4). Per-device `Description` (`_mount.Settings.DeviceDescription`, e.g. `"GreenSwamp Alpaca Server"`) and `DriverInfo` provide a secondary, device-level corroborating signal but are not required, since the server-level management description is authoritative and cheaper to obtain (one call, not per-device).
- This resolves Open Item "exact capability/transport-detection mechanism (FR14)" **for the detection trigger**; the exact allow-list/match strategy and how a false negative (GreenSwamp server misidentified as generic Alpaca) degrades gracefully (it should simply mean "SignalR provider not attempted, Alpaca REST-only operation continues correctly" — never a hard failure) remain implementation-design-stage decisions, now narrowed rather than open-ended.
- **As-built deviation, confirmed by live-server testing (recorded here, not yet fixed — see §11):** the implemented `GreenSwampClassDetector` matches only against **per-device** `Description`/`DriverInfo` strings (avoiding an extra server-level round-trip, since Connect already retrieves `Description` for free), not the server-level `management/v1/description` endpoint described above. Its three hard-coded markers (`"Green Swamp Alpaca Server"`, `"GreenSwamp Alpaca Server"`, `"GreenSwampServer"`) do **not** match the real running reference server's actual values (`Description = "GreenSwamp ASCOM Alpaca Telescope Simulator"`, `DriverInfo = "GreenSwamp.Alpaca.Server, Version=0.0.0.0, ..."`) — only the server-level `ServerName` (`"Green Swamp Alpaca Server"`) matches. **`IsGreenSwampClass` is therefore currently always `false` against the real reference server.** Stakeholder decision (implementation phase): not fixed now, since nothing in the current Connect/FindHome/Park/Abort command slice consumes `IsGreenSwampClass` — revisit when GreenSwamp SignalR support is actually built (§11).

### 6.9 Awareness of the Wider GreenSwampAlpacaServer REST Surface

Inspection of `ConfigController.cs` confirms GreenSwampAlpacaServer exposes a substantially larger REST API than the Alpaca `ITelescopeV4` device surface alone: a **Configuration File Management API** (`/api/config/monitor`, `/api/config/server`, `/api/config/observatory`, `/api/config/alpaca-devices`, `/api/config/devices/{n}`, each with paired `download`/`upload` file-transfer endpoints) for monitor settings, server configuration, observatory settings, Alpaca device registration, and per-device operational settings.

- **This architecture deliberately does not integrate with the Configuration File Management API.** It is out of scope per both approved documents' "settings/configuration persistence remains out of scope" constraint, and per this document's Purpose (§1). It is recorded here only as a confirmed fact about the target server, not as a requirement.
- **Why this matters architecturally, even while out of scope:** it confirms `ITelescopeSession`'s deliberately narrow, telescope-concept-only surface (§6.2) is correctly scoped — this client is a *telescope control and monitoring* client, not a GreenSwampAlpacaServer administration client. Should a future phase want to surface configuration management (e.g. an "observatory settings" tab), it should be modeled as an **entirely separate application-facing abstraction** (e.g. `IGreenSwampConfigurationClient`), parallel to `ITelescopeSession`, not folded into it — preserving the same separation-of-concerns principle already applied to telescope control vs. transport. This is noted as a natural extension seam, not designed further here.

### 6.10 Device Discovery — `ITelescopeDiscoveryService` (required, not deferred)

Per stakeholder direction, discovery is promoted from an "available but optional" note (as originally drafted) to a **required application-facing capability**, because the ViewModel/UI layer needs to *surface* discovered devices (e.g. a "Discover devices" action populating a picker before a tab connects), not just have discovery available at the library level.

```csharp
public interface ITelescopeDiscoveryService
{
	Task<IReadOnlyList<DiscoveredTelescopeDevice>> DiscoverAsync(TimeSpan? timeout = null, CancellationToken ct = default);
}

public sealed record DiscoveredTelescopeDevice(
	string ServerName,
	string Manufacturer,
	string HostName,
	int Port,
	int AlpacaDeviceNumber,
	bool IsGreenSwampClass);
```

Design notes:

- **New assembly member of `Abstractions`/`Model`** (§5): `ITelescopeDiscoveryService` lives in `Abstractions` (pure contract, no ASCOM types leak to ViewModels — same layering rule as §4), implemented in `Model` by wrapping `ASCOM.Alpaca.Discovery.AlpacaDiscovery` (confirmed present via reflection: `StartDiscovery(...)`, `AlpacaDevicesUpdated`/`DiscoveryCompleted` events, `GetAlpacaDevices()`/`GetAscomDevices(DeviceTypes?)` returning `AlpacaDevice`/`AscomDevice` DTOs with `HostName`/`IpAddress`/`Port`/`ServerName`/`Manufacturer`/`ManufacturerVersion`/`AlpacaDeviceNumber`/`InterfaceVersion`). This is a UDP-broadcast-based LAN discovery mechanism per the ASCOM Alpaca discovery protocol, not a REST call.
- **Registered as a DI singleton alongside `ITelescopeSessionFactory`** (§7.3) — it has no per-instance state and is not tied to any particular `ITelescopeSession`.
- **`DiscoveredTelescopeDevice.IsGreenSwampClass`** is populated using the same `ServerName`/`Manufacturer` recognition rule as §6.8's connect-time capability probe, so the UI can visually distinguish "GreenSwamp-class (SignalR available)" from "generic Alpaca" devices *before* the user connects — this reuses, rather than duplicates, the FR14 detection logic (the `Model` layer should factor the recognition rule into one shared internal helper used by both `ITelescopeDiscoveryService` and `AlpacaTelescopeProvider`'s connect-time probe).
- **ViewModel usage**: a discovery command (e.g. `[RelayCommand] DiscoverDevicesAsync`) populates an `ObservableCollection<DiscoveredTelescopeDevice>` the user picks from before constructing a `TelescopeConnectionDescriptor` and calling `ITelescopeSessionFactory.Create(...)` (§6.1) — discovery and connection remain two distinct steps; discovery never itself creates a session.
- **Testability**: `ITelescopeDiscoveryService` is trivially fakeable for Level 2 ViewModel tests (test strategy), exactly like `ITelescopeSession`. Level 1/3+ tests exercising the real `AlpacaDiscovery` wrapper need a LAN-broadcast-capable test environment or are scoped out to manual/exploratory verification — flagged as an Open Item (§11) since UDP broadcast discovery is inherently harder to virtualize than HTTP/SignalR transports.
- **Interaction with settings (deferred)**: discovery *populates* candidate connection descriptors; whether/how a discovered device is persisted as a saved connection remains a settings-design concern (out of scope, per both approved documents), consistent with `TelescopeConnectionDescriptor`'s deliberately light shape (§6.1).

## 7. Composition Root: Generic Host + Avalonia

### 7.1 Why Generic Host over a bare `ServiceCollection`
Avalonia's own official DI guidance (fetched via MCP) uses a bare `ServiceCollection`/`BuildServiceProvider()` inline in `App.axaml.cs`, which is sufficient for simple, stateless service graphs. This application's Model layer runs genuinely long-lived background work per telescope instance (REST polling loops, SignalR connections) that must start and stop deterministically and be coordinated with app shutdown — exactly the problem `Microsoft.Extensions.Hosting`'s `IHost`/`IHostedService` lifecycle exists to solve, and it additionally gives centralized, conventional configuration/logging composition for free. Per stakeholder decision (§3.1), the Generic Host is used.

### 7.2 Structural shape
```csharp
// Program.cs
public static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		var host = Host.CreateApplicationBuilder(args) is var builder
			? ConfigureHost(builder).Build()
			: throw new InvalidOperationException();

		host.Start();   // starts any registered IHostedService (none required yet at Model layer;
						// per-instance session lifetimes are owned by ITelescopeSessionFactory,
						// NOT by a single app-wide IHostedService — see note below)

		BuildAvaloniaApp(host.Services)
			.StartWithClassicDesktopLifetime(args);

		host.StopAsync().GetAwaiter().GetResult();
	}

	private static IHostBuilder ConfigureHost(HostApplicationBuilder builder)
	{
		builder.Services.AddTelescopeIntegration();   // ServiceCollection extension, Model layer
		// future: builder.Services.AddSettings(), etc.
		return builder;
	}

	public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
		=> AppBuilder.Configure(() => new App(services))
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace();
}
```
```csharp
// App.axaml.cs
public partial class App : Application
{
	private readonly IServiceProvider _services;
	public App(IServiceProvider services) => _services = services;

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var vm = _services.GetRequiredService<MainViewModel>();
			desktop.MainWindow = new MainWindow { DataContext = vm };
		}
		base.OnFrameworkInitializationCompleted();
	}
}
```
**Important nuance, deliberately not glossed over:** individual telescope sessions are **not** registered as `IHostedService`s. `IHostedService` instances are singletons resolved once at host start — that model does not fit N dynamically created/disposed per-tab sessions (FR16-22). Instead:
- `ITelescopeSessionFactory` is the only long-lived, host-registered singleton.
- Each `ITelescopeSession` manages its own internal background work (e.g. a polling `Task` started in `ConnectAsync`, stopped in `DisconnectAsync`/`DisposeAsync`) using a `CancellationTokenSource` owned by the session itself, **not** the host's `IHostedService` mechanism.
- The Generic Host's value here is DI composition, configuration, and logging — not lifetime management of per-instance sessions, which remains the Model abstraction's own responsibility (consistent with the instance-based constraint in Requirements §9).
- On host/app shutdown, `MainViewModel` (or a top-level session registry) should proactively `DisposeAsync()` any still-open sessions before `host.StopAsync()` returns, so no orphaned polling loop survives process exit (NFR63-65).

### 7.3 DI Registration Sketch
```csharp
public static class TelescopeIntegrationServiceCollectionExtensions
{
	public static IServiceCollection AddTelescopeIntegration(this IServiceCollection services)
	{
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<ITelescopeSessionFactory, TelescopeSessionFactory>();
		services.AddSingleton<ITelescopeDiscoveryService, TelescopeDiscoveryService>();
		// ViewModels are transient/scoped-per-tab, resolved when a tab is opened,
		// NOT singletons -- each tab gets its own MainViewModel-created child ViewModel
		// and calls the factory itself rather than resolving a pre-built session from DI.
		services.AddTransient<TelescopeTabViewModelFactory>();
		return services;
	}
}
```
Note: `TelescopeTabViewModel` instances are **not** resolved directly from the root DI container per tab in the usual singleton/transient sense, because each needs a *specific* `TelescopeConnectionDescriptor` at creation time (a runtime value, not something DI naturally injects). The conventional pattern is a small `TelescopeTabViewModelFactory` (itself DI-registered, holding only the `ITelescopeSessionFactory` and other stable dependencies) with a `Create(TelescopeConnectionDescriptor)` method that `MainViewModel` calls when the user opens a new tab — this keeps DI usage idiomatic while still supporting N runtime-parameterized instances (FR7-8, FR18-22).

## 8. MVVM Layer Detail

### 8.1 ViewModel base shape (CommunityToolkit.Mvvm)
```csharp
public partial class TelescopeTabViewModel : ObservableObject, IAsyncDisposable
{
	private readonly ITelescopeSession _session;

	[ObservableProperty] private double _rightAscensionHours;
	[ObservableProperty] private double _declinationDegrees;
	[ObservableProperty] private bool _isConnected;
	[ObservableProperty] private bool _isSlewing;
	[ObservableProperty] private bool _isTracking;
	[ObservableProperty] private string? _statusMessage;
	[ObservableProperty] private bool _isJogSupported;      // driven by Capabilities (FR36)

	public TelescopeTabViewModel(ITelescopeSession session)
	{
		_session = session;
		_session.PositionUpdated += OnPositionUpdated;
		_session.StateUpdated += OnStateUpdated;
		_session.ConnectionStatusChanged += OnConnectionStatusChanged;
	}

	[RelayCommand]
	private async Task ConnectAsync(CancellationToken ct) => await _session.ConnectAsync(ct);

	[RelayCommand(CanExecute = nameof(IsJogSupported))]
	private async Task JogNorthAsync(CancellationToken ct) =>
		await _session.JogAsync(TelescopeAxis.Declination, JogRate, ct);

	private void OnPositionUpdated(object? sender, TelescopePositionUpdatedEventArgs e)
		=> Dispatcher.UIThread.Post(() =>
		{
			RightAscensionHours = e.Position.RightAscensionHours;
			DeclinationDegrees = e.Position.DeclinationDegrees;
		});

	public async ValueTask DisposeAsync()
	{
		_session.PositionUpdated -= OnPositionUpdated;
		_session.StateUpdated -= OnStateUpdated;
		_session.ConnectionStatusChanged -= OnConnectionStatusChanged;
		await _session.DisposeAsync();
	}
}
```
This satisfies:
- FR1-2 (depends only on `ITelescopeSession`).
- FR23-30 (jog/slew/stop/park/tracking as commands, capability-gated per FR36 via `CanExecute`).
- NFR60 (async commands, no UI-thread blocking).
- The Avalonia expert rule that UI-thread marshaling belongs at the View/ViewModel boundary (`Dispatcher.UIThread.Post`), not inside the Model.
- Test strategy Level 2 (a fake `ITelescopeSession` can be substituted trivially; no Avalonia dispatcher needed if the test asserts on `RightAscensionHours` post-event without going through the real dispatcher — tests should construct the ViewModel with a test-provided synchronization shim or assert against the property setter contract directly, an Open Item to formalize at implementation-design stage, see §11).

### 8.2 Multi-instance / tabbed shape
- `MainViewModel` holds an `ObservableCollection<TelescopeTabViewModel>`.
- "Add tab" command asks `TelescopeTabViewModelFactory.Create(descriptor)` for a new `TelescopeTabViewModel` (which internally calls `ITelescopeSessionFactory.Create(descriptor)`).
- "Close tab" command removes it from the collection and calls its `DisposeAsync()` — directly exercising FR20-22, NFR63-64.
- No static/ambient telescope registry anywhere in this layer (Requirements §9 constraint) — the `MainViewModel`'s collection is the *only* place multiple instances are tracked, and it is itself an ordinary DI-resolved singleton-scoped ViewModel, not a static class.

## 9. Traceability to Requirements
| Requirement(s) | Architectural Element |
|---|---|
| FR1-5 (neutral abstraction) | `ITelescopeSession`/`Abstractions` assembly; no ASCOM/SignalR types cross into ViewModels |
| FR6-9 (DI) | Generic Host + `AddTelescopeIntegration()`; `ITelescopeSessionFactory` singleton; `TelescopeTabViewModelFactory` |
| FR10, FR12 (Alpaca baseline) | `AlpacaTelescopeProvider` wrapping `ASCOM.Alpaca.Clients.AlpacaTelescope` (`ITelescopeV4`) |
| FR11, FR13 (GreenSwamp SignalR) | `GreenSwampSignalRProvider`; gated on future server-side position/command hub (§6.4, §11) |
| FR14 (capability/transport detection) | GreenSwamp-class recognition via standard Alpaca management description (`ServerName`/`Manufacturer`), shared by `AlpacaTelescopeProvider` and `ITelescopeDiscoveryService` (§6.8, §6.10) |
| FR15 (future transports) | Provider seam under `TelescopeSession`; adding a provider does not change `ITelescopeSession` |
| FR16-22 (instance/session lifecycle) | `ITelescopeSessionFactory.Create`, `IAsyncDisposable` sessions, per-tab ViewModel lifecycle (§8.2) |
| FR23-29 (control operations) | `ITelescopeSession` command methods (§6.2) |
| FR24 (jog/arrow) | `JogAsync`/`StopJogAsync` → `ITelescopeV4.MoveAxis(axis, rate)`, v1 scope deliberately excludes HC-parity flip/anti-backlash/mode logic (§6.7); capability-gated `IsJogSupported` (§8.1) |
| FR30 (latency-based routing) | `TelescopeSession` command routing logic (§6.5) |
| FR31-36 (state/capability reporting) | `TelescopeCapabilities`, `TelescopeConnectionStatus`, event args DTOs (§6.2, §6.6) |
| FR37-43 (position/state updates, reconciliation) | `TelescopeSession` ingestion/reconciliation point (§6.5); events (§6.6) |
| FR44-48 (error/availability) | `ConnectionStatusChanged`, degraded-state modeling in `TelescopeConnectionStatus` |
| NFR49-51 (maintainability) | Assembly separation (§5), layering rules (§4) |
| NFR52-54 (extensibility) | Provider seam (§6.3-6.4), factory-based instance creation |
| NFR55-57 (testability) | `TimeProvider` injection, fake `ITelescopeSession` for Level 2, per test strategy |
| NFR58-62 (latency) | Transport routing (§6.5), no added buffering in event delivery (§6.6) |
| NFR63-65 (reliability) | `IAsyncDisposable` teardown discipline, per-instance `CancellationTokenSource` (§6.2, §7.2) |
| Discovery (stakeholder-directed addition) | `ITelescopeDiscoveryService` (§6.10), DI singleton, wraps `ASCOM.Alpaca.Discovery.AlpacaDiscovery` |

## 10. Testability Confirmation (cross-check against `telescope-test-strategy.md`)
- **Level 1 (Model unit)**: `AlpacaTelescopeProvider`/`GreenSwampSignalRProvider` internal logic is testable by substituting `TimeProvider` and, where the real ASCOM client allows, an injectable `HttpMessageHandler`/`HubConnection` factory seam — confirmed compatible with `ASCOM.Alpaca.Clients.AlpacaTelescope`'s configuration-based construction (`AlpacaConfiguration`), which does not hard-code `HttpClient` creation inaccessibly (implementation-time verification needed — flagged as Open Item since this session did not exhaustively confirm `AlpacaTelescope`'s internal `HttpClient` is substitutable versus merely configurable).
- **Level 2 (ViewModel unit)**: `TelescopeTabViewModel` depends only on `ITelescopeSession` — trivially fake-able, per §8.1.
- **Level 3-6 (real reference servers, fault/latency proxy)**: unaffected by this architecture pass; `AlpacaTelescopeProvider` talks real Alpaca REST wire protocol (via the real ASCOM client library) to whatever real server/simulator sits behind the fault/latency proxy, exactly as the test strategy assumed.
- **Discovery (new, §6.10)**: `ITelescopeDiscoveryService` is fake-able at Level 2 exactly like `ITelescopeSession`. Testing the real `AlpacaDiscovery`-wrapping implementation is not directly covered by the approved test strategy's existing levels, since it is UDP-broadcast-based rather than HTTP/SignalR — tracked as a new Open Item (§11) requiring either a test strategy addendum or a scoped decision to treat it as manually/exploratory-verified only.
- **No new conflicts identified** between this architecture and the approved test strategy; the plain-events decision (§6.6) does add a firm requirement that the test harness's "Scenario/event recorder" component subscribes to `ITelescopeSession` events directly rather than an `IObservable<T>` stream, which the test strategy's harness description already accommodates generically ("capture the sequence of position/state updates emitted by a provider or instance").

## 11. Open Items Requiring Implementation-Design-Stage Decisions

**Status key added post-implementation:** ✅ resolved and built · 🔶 partially resolved / narrowed, remainder still open · ⬜ still fully open (unchanged from architecture approval).

- ⬜ **GreenSwamp position/state SignalR surface does not exist yet.** This architecture assumes a *future* extension to `GreenSwampAlpacaServer` (new hub or new hub methods) broadcasting `TelescopeStateModel`-shaped RA/Dec/Alt-Az/state data, distinct from the existing chart-only `ChartHub`. Until that exists, `GreenSwampSignalRProvider`'s position/state path has no real counterpart to connect to and should be developed/tested against a temporary fake hub (per test strategy §6, item 6), with the real integration deferred to when `GreenSwampAlpacaServer` gains this capability. This is a cross-repository dependency, tracked identically to the existing "no SignalR command support" risk. **Not started** — the current implementation slice is Alpaca REST only; `GreenSwampSignalRProvider` does not yet exist as code.
- ⬜ **Axis-step-to-sky-coordinate conversion for `ChartHub`-sourced data remains unsupported** until a future GreenSwamp-class driver phase exposes steps-per-degree/gearing settings data. No conversion logic is designed here; `ChartHub` remains usable only for its existing charting purpose in the interim.
- 🔶 **FR14 detection trigger is now resolved (§6.8); the exact match/allow-list strategy is not.** As built, `GreenSwampClassDetector` uses a simple hard-coded, case-insensitive substring allow-list against per-device `Description`/`DriverInfo` (not the server-level management API endpoint originally decided in §6.8) — see the as-built deviation note in §6.8. **This means `IsGreenSwampClass` is currently always false against the real reference server** (confirmed by live-server testing) and needs a concrete fix — either add the server-level `management/v1/description` check as originally decided, broaden the per-device markers to match the real server's actual strings, or both — **before any GreenSwamp-class-gated feature (SignalR provider, `GreenSwampTelescopeState` population, discovery class-badging) is built**, since none of those can be exercised against a real server until detection actually succeeds.
- ⬜ **Full hand-controller-parity jog/manual-control design is deferred (§6.7).** v1 ships `MoveAxis`-only jog. A dedicated future design pass must analyze, in the context of the current GreenSwampAlpacaServer solution: how (or whether) HC mode/flip/anti-backlash/hemisphere/pier-side logic should be exposed to this client at all; whether that requires new Alpaca REST surface, the future GreenSwamp SignalR command channel (§6.4), or remains a GreenSwampAlpacaServer-internal-only capability (Blazor UI only); and how tracking-rate composition (§6.7's `RateMovePrimaryAxis`/`RateMoveSecondaryAxis` reassert-tracking behavior) should be modeled client-side, if at all. **Not started** — `JogAsync`/`StopJogAsync` are not yet implemented in `ITelescopeSession`.
- ⬜ **Reconciliation algorithm details (FR43)** — the merge/staleness/ordering algorithm inside `TelescopeSession` is specified here only as "must exist as a single ingestion point," not algorithmically. **Not yet relevant** — no second (SignalR) source exists yet to reconcile against.
- ✅ **Whether `Abstractions` and `Model` remain two assemblies or merge into one** (§5) — **resolved and built**: two assemblies, plus a third (`GreenSwamp.Alpaca.Client.ViewModels`), exactly as sketched.
- ✅ **`ITelescopeSession` internal HTTP/SignalR substitutability for Level 1 tests** — **resolved differently than anticipated**: rather than substituting `AlpacaTelescope`'s internal `HttpClient`, Level 1 tests instead isolate the two genuinely pure, transport-free pieces of provider logic (`TelescopeStateMapper`, `GreenSwampClassDetector`) and test those directly with no `AlpacaTelescope`/HTTP involvement at all. Full request/response-level fake-HTTP testing of `AlpacaTelescopeProvider` itself was not attempted this slice; real-protocol confidence instead comes from Level 3 (real reference server, see below) rather than a Level 1 fake-transport harness. This is a narrower Level 1 scope than originally envisioned in the test strategy (§5.1) — flagged for awareness, not treated as a defect, since Level 3 coverage exists.
- ✅ **Whether ViewModel-level UI-thread marshaling (`Dispatcher.UIThread.Post`) needs a test-time shim** — **resolved and built**: `IUiDispatcher` (in `GreenSwamp.Alpaca.Client.ViewModels`) is injected into `TelescopeTabViewModel`; production code registers an `AvaloniaUiDispatcher` (`Dispatcher.UIThread.Post`), Level 2 tests substitute a synchronous `ImmediateUiDispatcher` (`GreenSwamp.Alpaca.Telescope.TestHarness`). Confirms the "inject a marshaling abstraction" option from the two named here.
- 🔶 **Poll cadence and reconnect/backoff policy constants** — **not yet relevant as originally framed**: the current implementation has **no continuous background polling loop at all**. `TelescopeSession` publishes a `StateUpdated` snapshot only (a) once immediately after a successful `ConnectAsync`, and (b) once immediately after each of `FindHome`/`ParkAsync`/`AbortSlewAsync` completes. This satisfies the four in-scope commands' own "resulting state" needs (implementation design §6.1-6.4) but does **not** yet satisfy requirements FR37/38/41 ("shall provide *ongoing* telescope position/state updates" / "shall obtain updates via an internal polling mechanism") for a connected-but-idle session. **This is a real, confirmed scope gap, not a design decision** — a periodic poll loop (using the already-injected `TimeProvider`, consistent with this section's original intent) is required before FR37/38/41 can be considered met, and is the most likely next piece of Model-layer work.
- ⬜ **Discovery test strategy (new, following §6.10's promotion of discovery to required scope)** — `ASCOM.Alpaca.Discovery.AlpacaDiscovery` is UDP-broadcast-based, which is materially harder to virtualize/fake than the HTTP/SignalR transports the test strategy otherwise assumes. Whether Level 1 coverage of `ITelescopeDiscoveryService`'s wrapping logic is achievable by substituting the underlying `AlpacaDiscovery` component, versus requiring a real or simulated LAN broadcast responder, versus being scoped to manual/exploratory verification only, needs a concrete decision and, if needed, an update to the test strategy document. **Not started** — `ITelescopeDiscoveryService` is not yet implemented.
- ⬜ **Shared GreenSwamp-class recognition helper ownership (§6.8/§6.10)** — confirm at implementation-design stage that both `AlpacaTelescopeProvider`'s connect-time probe and `ITelescopeDiscoveryService` call one single internal helper/service (not two independently-maintained copies of the same `ServerName`/`Manufacturer` check). **Partially exists**: `GreenSwampClassDetector` is a single shared internal helper, currently called only from `AlpacaTelescopeProvider` (`ITelescopeDiscoveryService` does not exist yet) — the "shared, not duplicated" goal is met so far by construction; revisit when discovery is built to confirm it continues to call the same helper rather than a second copy.

### New items identified during this implementation slice (not anticipated at architecture-approval time)
- **`TelescopeState.GreenSwamp` is always `null` today.** `AlpacaTelescopeProvider.GetState` maps only the standard `DeviceState`-bundled fields (implementation design §4.1); `GreenSwampTelescopeState` population is correctly deferred (per its own doc comment) but is not yet scheduled against any specific milestone. Tie this to the SignalR-surface item above when that work begins.
- **No `IHostedService`/shutdown-time session disposal exists yet.** §7.2's note that "`MainViewModel` (or a top-level session registry) should proactively `DisposeAsync()` any still-open sessions before `host.StopAsync()` returns" is not yet implemented — there is no `MainViewModel`/tab registry at all yet (prototype UI is deferred). Flagged so it is not forgotten once tabs are built.
