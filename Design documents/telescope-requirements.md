# Telescope Integration Requirements

**Status:** Approved. A first implementation slice (Connect/FindHome/Park/Abort, Alpaca REST only) has been built and tested against a real GreenSwampAlpacaServer reference simulator — see `telescope-implementation-design.md` §10 for as-built status, confirmed deviations, and open items. Notably, **FR37/38/41 ("ongoing" position/state updates via internal polling) are not yet fully met** — the current implementation publishes state only at connect and after each command, with no continuous background poll loop yet (implementation design §10.2).

**Get Well Guidance.md Stage 3 note:** the FR37/38/41 gap referenced above is now RESOLVED at the cadence-decision level. Per Stage 3 ("Poll loop") of `Get Well Guidance.md` (see `SignalR-transport-implementation-plan.md` Decision C, RESOLVED, and `telescope-implementation-design.md` §10.2/§10.3), a poll loop is required for both GreenSwampClass and AlpacaClass telescopes: a 250ms SignalR ingestion loop for GreenSwampClass telescopes while connected, and a 1 second ASCOM Alpaca `DeviceState` loop for AlpacaClass telescopes (continuously) and GreenSwampClass telescopes (as the Decision D fallback only). This document's requirements text (FR41/42/43 below) is otherwise unaffected by Stage 3 and has not been touched — the loop's internal implementation remains Stage 4 ("Threading and Data Reconciliation") work.

**Get Well Guidance.md Stage 4 note:** FR43's "avoid presenting stale, duplicate, or out-of-order position/state values... when both REST polling and SignalR updates are active for the same instance" is now RESOLVED. Per Stage 4 ("Threading and Data Reconciliation") of `Get Well Guidance.md` (see `SignalR-transport-implementation-plan.md` Decision C/D, Stage 4, RESOLVED, and Finding A4, RESOLVED): the "both...active for the same instance" scenario FR43 anticipates does not actually arise for a GreenSwampClass telescope — SignalR and the fallback REST/AP loop are never concurrent active sources (Decision D) — so there is no dual-source merge to get wrong. For AlpacaClass telescopes, the only concurrency is multiple fixed-cadence REST calls (FR41/Decision B/C), resolved by time-ordering/sequencing those calls, with standard OnChange semantics applying per field. This document's requirements text (FR41/42/43 below) is otherwise unaffected by Stage 4 and has not been touched.

## 1. Purpose
This document defines the requirements for adding telescope control support to the GreenSwamp Alpaca Client solution. The requirements cover a telescope control capability that can operate against any standard ASCOM Alpaca telescope, and that can additionally exploit a supplementary low-latency SignalR channel exposed by GreenSwamp-class telescope servers, while preserving a clean, instance-based MVVM architecture suitable for a future multi-telescope tabbed UI.

## 2. Goals
- Provide telescope control capabilities to the application through dependency injection.
- Support a universal ASCOM Alpaca REST control/status baseline for any compliant telescope device.
- Additionally exploit a supplementary low-latency SignalR channel where the connected device is GreenSwamp-class and exposes one, for both telemetry and (where supported) command dispatch.
- Keep ViewModels free from Alpaca-specific and SignalR-specific protocol details, regardless of which transport(s) are active.
- Minimize control and feedback latency by selecting the best available transport per operation, transparently to the MVVM layer.
- Support both user-initiated telescope actions and ongoing telescope state/position updates.
- Design the telescope abstraction as instance-based so that multiple concurrent telescope sessions (one per future UI tab) are supported from the outset, even though the initial prototype connects to a single telescope at a time.
- Establish a requirements baseline for later architecture and implementation design documents.

## 3. Scope
### In Scope
- Application-facing telescope abstraction for MVVM consumption.
- Support for ASCOM Alpaca REST as the universal telescope control/status transport.
- Support for a supplementary GreenSwamp SignalR real-time channel, used opportunistically alongside Alpaca REST for GreenSwamp-class devices.
- Transparent transport selection and fallback so the MVVM layer is unaware of which transport(s) are active for a given telescope.
- Dependency injection registration and resolution of telescope services.
- Instance-based session design supporting multiple concurrent telescope connections (one per future UI tab).
- Telescope movement, connection, and state monitoring requirements.
- Position and state update delivery from backend services to the MVVM layer.
- Per-instance session lifecycle management (create, connect, disconnect, dispose).

### Out of Scope
- Detailed software architecture and class design.
- Exact package selection beyond known backend technologies.
- UI layout and visual design, including the tabbed multi-telescope UI itself (only the underlying instance-based service design is in scope here).
- Persisted settings design.
- Security design beyond high-level integration expectations.
- Test strategy details.
- Any changes to the GreenSwamp Alpaca Server's SignalR hub (`ChartHub`) itself; this document only defines client-side requirements. Any server-side hub changes needed to support command dispatch (see Risks) are a separate, cross-repository concern.

## 4. Stakeholders
- Application users controlling and monitoring one or more telescopes concurrently.
- Developers implementing MVVM views and view models.
- Developers implementing telescope transport providers.
- Future maintainers adding new telescope transport/provider types.

## 5. Definitions
- **MVVM**: Model-View-ViewModel pattern used by the application.
- **Transport**: A specific communication mechanism used to reach a telescope device or server (e.g. Alpaca REST, GreenSwamp SignalR). A single telescope instance may use one or more transports concurrently.
- **Provider**: An implementation that communicates with a telescope system over a specific transport.
- **GreenSwamp-class device**: A telescope server (such as GreenSwamp Alpaca Server) that implements the standard ASCOM Alpaca REST interface and additionally exposes the GreenSwamp SignalR real-time channel.
- **Generic Alpaca device**: A telescope device or server that implements only the standard ASCOM Alpaca REST interface, with no supplementary real-time channel.
- **Telescope instance / Telescope session**: A single, independently addressable connection to one telescope device, encapsulating whichever transport(s) are active for it. The application shall support multiple concurrent telescope instances, each independent of the others.
- **Telescope-neutral abstraction**: An application-facing contract that hides Alpaca REST and SignalR protocol details, and hides which transport(s) are in use for a given telescope instance.
- **Command dispatch**: Sending a control instruction to a telescope (e.g. slew, jog/move-axis, park, set tracking).
- **Position update**: An update containing telescope pointing information (e.g. RA/Dec, Alt/Az), regardless of whether it originated from REST polling or a pushed SignalR message.
- **State update**: An update containing telescope status information (e.g. connected, slewing, tracking, parked), regardless of originating transport.
- **Transport capability set**: The set of operations and update mechanisms available for a given telescope instance, determined by which transport(s) it supports.

## 6. Functional Requirements

### 6.1 Telescope-Neutral Abstraction
1. The system shall provide an application-facing telescope control abstraction that is independent of Alpaca REST and SignalR implementation details.
2. The MVVM layer shall depend only on this application-facing abstraction.
3. The abstraction shall represent telescope concepts (connect, move, track, park, status) rather than transport-specific protocol calls.
4. The abstraction shall support the introduction of additional transports or providers in the future without requiring redesign of MVVM consumers.
5. The abstraction shall be instance-based: each telescope connection shall be represented by its own independent object/handle, so that the MVVM layer can hold one instance per tab without shared or ambient state between instances.

### 6.2 Dependency Injection
6. The telescope control capability shall be registered in the application's dependency injection container.
7. The DI-registered surface shall be a factory/manager capable of producing and tracking multiple independent telescope instances, rather than a single ambient telescope service.
8. ViewModels shall be able to obtain a telescope instance appropriate to their tab/context through constructor-injected services.
9. Transport-specific implementations shall be hidden behind dependency injection and application-facing service contracts.

### 6.3 Transport Support
10. The solution shall support ASCOM Alpaca REST as the baseline transport for any compliant telescope device.
11. The solution shall support the GreenSwamp SignalR real-time channel as a supplementary transport, used in addition to Alpaca REST when connecting to a GreenSwamp-class device.
12. For a generic Alpaca-only device, the system shall operate correctly using Alpaca REST alone.
13. For a GreenSwamp-class device, the system shall be able to use SignalR opportunistically for lower-latency position/state updates and, where the server supports it, lower-latency command dispatch, while Alpaca REST remains available as the authoritative/fallback transport.
14. The determination of which transport(s) are active for a given telescope instance shall be made automatically (e.g. via capability/version detection or configuration) and shall not be exposed as a decision the MVVM layer needs to make.
15. The system should support future addition of further transports/providers without changing ViewModel-facing contracts.

### 6.4 Connection and Session Management
16. The system shall support connecting a telescope instance to its configured device.
17. The system shall support disconnecting a telescope instance from its device.
18. The system shall support multiple telescope instances being connected concurrently, each independent of the others (e.g. one per UI tab).
19. The system shall ensure ViewModels interact only with their own telescope instance and shall not be able to observe or affect another instance's state.
20. The system shall support creating a new telescope instance and disposing of an existing one during application execution (e.g. opening/closing tabs) without affecting other active instances.
21. The system shall correctly tear down all transport-level subscriptions and activity (REST polling loops, SignalR group memberships/connections) when a telescope instance is disconnected or disposed.
22. The initial prototype may create and use a single telescope instance at a time; the underlying design shall not preclude multiple concurrent instances.

### 6.5 Telescope Control Operations
23. The system shall support telescope movement to target coordinates.
24. The system shall support continuous directional movement (jog/arrow-style axis movement) suitable for interactive, low-latency manual control.
25. The system shall support stopping or aborting telescope motion.
26. The system shall support tracking control.
27. The system shall support park and unpark operations where the device supports them.
28. The system shall support additional movement-related operations according to device capabilities.
29. Unsupported operations shall be represented through capabilities rather than by exposing transport-specific behavior directly to the ViewModel.
30. Command dispatch shall be routed over whichever active transport for that telescope instance offers the lowest practical latency for that command, transparently to the caller.

### 6.6 State, Capability, and Position Reporting
31. The system shall expose telescope position information through the application-facing abstraction.
32. The system shall expose telescope state information through the application-facing abstraction.
33. The system shall expose telescope capabilities through the application-facing abstraction, including which transport(s) are active for that instance.
34. Position reporting shall include sufficient information for the MVVM layer to present current pointing values.
35. State reporting shall include sufficient information for the MVVM layer to present connection and motion state.
36. Capability reporting shall enable the MVVM layer to enable, disable, or adapt user interactions based on device and transport support (e.g. enabling low-latency jog controls only when a suitable transport is active).

### 6.7 Position and State Updates
37. The system shall provide ongoing telescope position updates to the MVVM layer, per telescope instance.
38. The system shall provide ongoing telescope state updates to the MVVM layer, per telescope instance.
39. The MVVM layer shall consume application-level updates and shall not implement REST polling or SignalR subscription logic itself.
40. Transport-specific update acquisition mechanisms may differ, but the MVVM-facing update model shall be transport-neutral.
41. For a telescope instance using Alpaca REST only, the provider shall obtain updates via an internal polling mechanism.
42. For a telescope instance with an active SignalR channel, the provider shall prefer pushed SignalR updates for position/state where available, and shall use Alpaca REST polling to fill gaps or as a fallback if the SignalR channel is unavailable or drops.
43. The system shall avoid presenting stale, duplicate, or out-of-order position/state values to the MVVM layer when both REST polling and SignalR updates are active for the same instance.

### 6.8 Error and Availability Handling
44. The system shall surface connection and operational failures through explicit application-facing status or error information, per telescope instance.
45. The system shall not require ViewModels to interpret transport-specific exception or protocol semantics.
46. Loss of connectivity on any active transport shall be reflected in the application-facing telescope state for that instance.
47. If the SignalR channel for a GreenSwamp-class device becomes unavailable, the system shall continue operating via Alpaca REST alone, degrading responsiveness but not availability, and shall reflect this degraded condition in the application-facing state.
48. The system shall handle device/transport unavailability in a way that allows the MVVM layer to reflect the condition to the user, per affected telescope instance.

## 7. Non-Functional Requirements

### 7.1 Maintainability
49. The design shall separate transport/protocol concerns from MVVM concerns.
50. The design shall support independent evolution of the telescope abstraction and its transport providers.
51. The design shall minimize coupling between UI code and communication technologies.

### 7.2 Extensibility
52. The system shall support adding new transports/providers without requiring changes to existing ViewModel-facing contracts.
53. Transport-specific logic should be isolated so that new providers can be added with localized changes.
54. The design shall support adding further concurrent telescope instances without structural rework.

### 7.3 Testability
55. The telescope abstraction shall be suitable for substitution with test doubles or simulated providers.
56. ViewModels shall be testable without live Alpaca or SignalR endpoints.
57. Multi-instance behavior (e.g. instance isolation, independent lifecycle) shall be verifiable without requiring multiple physical devices.

### 7.4 Responsiveness and Latency
58. Telescope operations shall support asynchronous execution suitable for UI applications.
59. Update delivery shall support timely UI refresh of position and state, including for scenarios where client and server are separated by a slower wide-area network connection.
60. Long-running operations shall not block the UI thread.
61. Interactive manual-control operations (e.g. jog/arrow movement) shall be designed to minimize round-trip latency, favoring the lowest-latency available transport for the connected device.
62. The system shall avoid introducing additional buffering or batching delay for latency-sensitive commands and updates beyond what the active transport itself requires.

### 7.5 Reliability
63. Per-instance session lifecycle management shall avoid duplicate active subscriptions and orphaned update loops.
64. Creating or disposing one telescope instance shall leave all other active instances in a consistent, unaffected state.
65. The system shall provide deterministic handling of start, stop, connect, disconnect, transport-degradation, and instance-disposal scenarios.

## 8. Assumptions
- The application will use an MVVM architecture.
- The main application will use dependency injection.
- ASCOM Alpaca REST is the universal, mandatory transport for all telescope devices.
- The GreenSwamp SignalR channel is an optional, supplementary transport available only for GreenSwamp-class devices, used alongside (not instead of) Alpaca REST.
- Third-party telescope drivers will only ever support the Alpaca REST transport.
- The GreenSwamp Alpaca Server's SignalR hub (`ChartHub`) currently supports telemetry streaming only (RA/Dec and pulse-guide points); using SignalR for command dispatch (e.g. jog/arrow controls) will require corresponding server-side hub capability, which may not exist yet and is tracked as a risk (see Section 12).
- The initial prototype targets a single active telescope instance; the production application will present a tabbed UI with one telescope instance per tab.
- The same MVVM UI flows should operate against a telescope instance regardless of which transport(s) back it.

## 9. Constraints
- The MVVM layer must not be coupled to Alpaca-REST-specific or SignalR-specific APIs.
- Transport-specific communication details must be contained within provider implementations or lower-level service layers.
- The telescope abstraction must be instance-based; no ambient/static single-telescope state is permitted, to allow multiple concurrent instances.
- Requirements defined here must remain implementation-agnostic and must not prematurely constrain the later architecture/design phase.
- Settings/configuration design (e.g. how a telescope instance's connection details are supplied or persisted) is explicitly deferred and not addressed by this document.

## 10. Acceptance Criteria Summary
The requirements shall be considered satisfied when:
- A transport-neutral telescope service contract exists for MVVM consumption (satisfies FR 1-5).
- Telescope-related ViewModels depend only on that contract and obtain telescope instances through DI (satisfies FR 6-9).
- Both Alpaca REST and the GreenSwamp SignalR channel can be represented behind the same per-instance contract, with Alpaca REST always available and SignalR used opportunistically (satisfies FR 10-15).
- The application can create, connect, control, and monitor multiple independent telescope instances concurrently, tearing down cleanly on disposal (satisfies FR 16-22).
- Control operations, including low-latency jog/arrow movement, are dispatched via the most suitable active transport without ViewModel awareness of transport choice (satisfies FR 23-30).
- Position, state, and capability information — including active transport and degraded-transport conditions — are exposed per instance in a transport-neutral manner (satisfies FR 31-36, 44-48).
- Position and state updates are delivered to the MVVM layer per instance without transport-specific polling/subscription logic in ViewModels, and without stale/duplicate/out-of-order values when multiple update sources are active (satisfies FR 37-43).

## 11. Risks
- **SignalR command-dispatch capability may not yet exist server-side.** The current GreenSwamp Alpaca Server `ChartHub` only streams telemetry (RA/Dec and pulse points) and has no command methods (e.g. no jog/slew/stop methods). Achieving low-latency jog control over SignalR depends on adding command support to the server hub (or an equivalent new hub), which is outside this client-side requirements document and must be coordinated separately.
- **Update reconciliation complexity.** Combining polled Alpaca REST updates with pushed SignalR updates for the same telescope instance introduces ordering, staleness, and de-duplication concerns that must be resolved in architecture/design, not left implicit. **Get Well Guidance.md Stage 4 note — RESOLVED:** this risk has been resolved rather than realized. Per Stage 4 (see `SignalR-transport-implementation-plan.md` Decision C/D, Stage 4, RESOLVED), REST and SignalR are never concurrent active sources for the same GreenSwampClass instance, so the anticipated dual-source ordering/staleness/de-duplication problem does not arise; the only remaining concurrency (AlpacaClass multi-cadence REST polling) is resolved by simple call sequencing.
- **Capability divergence between transports.** Alpaca's capability model (`Can*` properties) and any future SignalR command surface may not map 1:1; the design must tolerate a reduced common capability set rather than assuming full parity.
- **Coordinate frame/epoch consistency.** Alpaca telescopes report `EquatorialSystem` (e.g. JNow vs J2000); if GreenSwamp SignalR telemetry and Alpaca REST values are not in the same frame, position readings could disagree depending on active transport. This must be resolved in architecture.
- **Multi-instance resource usage.** Each telescope instance may run its own REST polling loop and/or SignalR connection; with several concurrent tabs this increases network/connection overhead, which should be considered in design (e.g. shared HTTP client, connection pooling).

## 12. Open Items for Later Design Documents
The following topics are intentionally deferred to architecture and implementation design:
- Exact interface names and method signatures.
- Choice of events versus observable streams for update delivery.
- Exact DI lifetimes and registration structure for the instance factory/manager and per-instance providers.
- Project layout and assembly boundaries.
- Polling intervals, retry behavior, and reconnection policy for both transports.
- Mechanism for detecting whether a connected device is GreenSwamp-class (and therefore SignalR-capable).
- Concrete error models and logging strategy.
- Configuration/settings persistence approach (explicitly out of scope for this pass).
- Detailed unit/integration test strategy.
- Server-side SignalR hub changes needed to support command dispatch, and coordination with the GreenSwampAlpacaServer repository.
