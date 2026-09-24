# Telescope Integration Test Strategy

**Status:** Approved, with as-built annotations. The first implementation slice (Connect/FindHome/Park/Abort) is built and passing at all three levels actually used — Level 1 (`GreenSwamp.Alpaca.Telescope.Tests\Model\Providers`: `TelescopeStateMapperTests`, `GreenSwampClassDetectorTests` — pure logic only, no fake-HTTP provider test), Level 2 (`GreenSwamp.Alpaca.Client.Tests\ViewModels`: `TelescopeTabViewModel` against a fake `ITelescopeSession`), and Level 3 (`GreenSwamp.Alpaca.Telescope.IntegrationTests\TelescopeSessionIntegrationTests`: one consolidated scenario against the real, running GreenSwampAlpacaServer reference simulator). §10's proposed project tree is realized only for these three projects/folders so far — the `TestHarness`, Resilience/Concurrency/Latency suites, and SignalR-related test doubles in §10/§12 remain unbuilt, deferred until GreenSwamp SignalR work begins. See `telescope-implementation-design.md` §10 for full as-built detail, including a narrower-than-planned Level 1 scope (item added to §12 below) and the currently-unmet FR37/38/41 polling requirement.

**Get Well Guidance.md Stage 3 note:** the "currently-unmet FR37/38/41 polling requirement" referenced above is now RESOLVED at the cadence-decision level — see §12's updated time-abstraction item below and `SignalR-transport-implementation-plan.md` Decision C (RESOLVED) for the confirmed poll-loop cadences (250ms SignalR / 1 second `DeviceState`). This document's other sections are unaffected by Stage 3 and have not been touched.

**Get Well Guidance.md Stage 4 note:** §12's "reconciliation algorithm for combined update streams" item (FR43) is now RESOLVED — see §12's updated item below and `SignalR-transport-implementation-plan.md` Decision C/D (Stage 4, RESOLVED) confirming that resolving Telescope State (Stage 2) and Poll Loop (Stage 3) removes all remaining back-pressure/reconciliation concerns. This document's other sections are unaffected by Stage 4 and have not been touched.

## 1. Purpose
This document defines the test strategy for the telescope integration capability described in `telescope-requirements.md`. It is intended to be used **during architecture and implementation design**, so that testability is designed into the Model and ViewModel layers from the outset rather than retrofitted. It also defines the shape of the test harness and test project(s) needed to validate the design without a working UI.

This strategy assumes the prototype UI is deferred. Until the UI exists, correctness must be demonstrated through automated tests against the Model (transport providers, telescope instances) and the ViewModel layer directly (property/command-level, without a rendered view).

## 2. Objectives
- Enable co-design of application functionality and its test project side by side, so every non-trivial design decision has a corresponding test approach agreed at design time.
- Make the telescope abstraction, transport providers, and ViewModels independently testable without live Alpaca or SignalR endpoints for fast, deterministic unit-level feedback, **and** validate real protocol correctness against genuine, proven reference implementations rather than relying solely on hand-rolled fakes.
- Provide confidence in the hardest parts of this design up front: dual-transport update reconciliation, transport degradation/fallback, and multi-instance isolation.
- Exploit the two proven reference implementations available to this project (see Section 4a) as the primary source of protocol-level test confidence, reserving fakes for scenarios real servers cannot easily produce (fault injection, deterministic latency, edge-case error responses).
- Establish the testing seams (abstractions) that the architecture must expose so that unit-level tests do not depend on wall-clock time, real sockets, or shared static state, while integration-level tests can still substitute real servers behind those same seams.

## 3. Scope
### In Scope
- Unit testing of the telescope abstraction, transport providers, and instance/session management (Model layer), using fakes/mocks for fast, deterministic feedback.
- Unit testing of ViewModels against the telescope abstraction, independent of any view/UI framework.
- Integration testing of the Alpaca REST provider against the **real ASCOM reference telescope simulator** (an independent, proven, ASCOM-initiative implementation), run as an external process.
- Integration testing of the Alpaca REST + SignalR providers against the **real GreenSwampAlpacaServer** (using its own mount simulator), run as an external process, to validate the dual-transport (GreenSwamp-class) path against a genuine, proven implementation.
- Fault-injection and latency-injection testing via a lightweight network-level proxy placed in front of either real reference server, so resilience/degradation/latency scenarios remain protocol-authentic rather than relying on a hand-rolled fake protocol implementation.
- Contract-style checks that the Alpaca REST provider's request/response handling matches the ASCOM Alpaca telescope API shape actually used, verified against the real reference simulator.
- Resilience and fault-injection scenarios (transport loss, reconnect, degraded operation).
- Multi-instance isolation and lifecycle testing.
- Latency-sensitive behavior testing (e.g. jog/arrow command path) using the fault/latency-injecting proxy for deterministic, repeatable delay.
- Design-for-testability requirements the architecture must satisfy.

### Out of Scope (for this document)
- UI/view-level automated testing (no UI exists yet; deferred until the prototype UI stage).
- Testing against real third-party Alpaca **hardware** (as opposed to the ASCOM reference **simulator**, which is in scope and does not require hardware).
- Testing against a live GreenSwamp Alpaca Server instance that is also serving real hardware or production settings; tests use a dedicated, disposable instance/configuration of the real server (see Section 4a).
- Load/soak testing at production scale.
- CI/CD pipeline configuration (test execution environment setup is assumed, not designed here, though process-lifecycle needs for the real servers are noted in Section 6).

## 4. Guiding Principle: Design-for-Testability
Because the UI is deferred, the Model and ViewModel layers must be fully verifiable in isolation. This has direct consequences for the architecture, which should be treated as **binding input to the design phase**, not an afterthought:

1. **No ambient/static state.** Per requirement (Constraints, `telescope-requirements.md` §9), the telescope abstraction must be instance-based. This is also a testability requirement: tests must be able to create N independent instances in the same test process without interference (e.g. static dictionaries keyed by device number, as seen in the reference server's `MountRegistry`, must not be replicated in the client's design).
2. **Abstract time.** Any polling loop, retry/backoff, or reconnect timer must go through an injectable time/scheduling abstraction (e.g. an `ITimeProvider`/virtual clock or an injectable delay function), so tests can advance virtual time deterministically instead of sleeping in wall-clock time.
3. **Abstract transport boundaries.** The HTTP client used for Alpaca REST and the SignalR `HubConnection` used for the GreenSwamp channel must be reachable through seams (interfaces or factory abstractions) that a test harness can point at either an in-process fake (Level 1/2) or a real reference server's actual network endpoint (Level 3+), without client code caring which.
4. **Deterministic identity.** Telescope instances must be identifiable/comparable in tests (e.g. an instance id or handle) so isolation and lifecycle tests can assert independence directly.
5. **Observable internal transitions.** State/position update emission (however it is ultimately modeled — events or observables) must be something a test can subscribe to and assert against synchronously/deterministically, without needing a real UI dispatcher.
6. **Cancellation-first design.** All async operations must accept and honor `CancellationToken`s so tests can assert prompt cancellation behavior without arbitrary timeouts.
7. **ViewModel purity.** ViewModels must depend only on the application-facing abstraction (per requirement FR 1-2) and expose bindable state/commands in a way that is assertable without a live UI dispatcher (e.g. avoid hard dependencies on `SynchronizationContext`/Avalonia dispatcher inside the ViewModel itself; marshaling to the UI thread should be the View's responsibility, not baked into ViewModel logic under test).

Architecture and design documents should explicitly confirm how each of these seams is implemented before implementation begins.

## 4a. Use of Real Reference Implementations
Two genuine, independently-proven telescope server implementations are available and should be used as the **primary source of protocol-level confidence**, in preference to hand-rolled fakes, wherever practical:

1. **ASCOM reference telescope simulator** — the ASCOM initiative's own reference driver/simulator, independent of this codebase. This is the best available stand-in for "generic Alpaca-only" third-party devices (FR 12, `Generic Alpaca device` in Definitions), because it is a genuine, spec-compliant Alpaca REST implementation maintained outside this project. Any test validating the Alpaca REST provider against this simulator inherently exercises the real ASCOM wire protocol, not this team's interpretation of it.
2. **GreenSwampAlpacaServer** (with its own bundled mount simulator, e.g. `GreenSwamp.Alpaca.Mount.Simulator`) — a proven, existing implementation of both the Alpaca REST surface and the GreenSwamp SignalR channel (`ChartHub`). This is the best available stand-in for "GreenSwamp-class device" (FR 13, `GreenSwamp-class device` in Definitions), for the same reason: it is the genuine article, not a reimplementation of its own protocol.

### Should they be used? Yes — with a defined role
Using real reference implementations should **replace**, not merely supplement, most of the originally-proposed hand-rolled "fake Alpaca endpoint" and "fake SignalR hub" integration layer (Section 6 v1). Rationale:
- They eliminate the risk that a hand-rolled fake is subtly wrong in a way that masks real client bugs or bakes in incorrect assumptions about the protocol.
- They are already proven/trusted, reducing test-harness maintenance burden compared to hand-maintaining a second, parallel protocol implementation.
- They give genuine confidence that the client works against what it will actually be deployed against.

However, real servers are **run as external processes** (the GreenSwamp server is a full ASP.NET Core host with OS-service/tray/browser-launch behavior; the ASCOM reference simulator is also a standalone process/service). This has consequences the fake-only approach did not have:
- **Process lifecycle overhead.** Starting/stopping a real server per test or per test run is slower than an in-process fake and needs explicit setup/teardown (see Section 6).
- **Limited fault-injection surface.** Neither real server is designed to simulate protocol errors, mid-operation disconnects, or artificial latency on demand. A real server will behave correctly, which is exactly what is needed for contract-correctness tests, but is the wrong tool for deliberately testing how the client behaves when things go wrong.
- **No SignalR command surface yet.** `ChartHub` only streams telemetry today (see Requirements §11 Risks); the real GreenSwamp server cannot be used to test a SignalR command-dispatch path that does not yet exist server-side.

### Resulting hybrid strategy
- **Use the real reference implementations** for provider/contract integration tests (Level 3) and, via a fault/latency-injecting proxy placed in front of them, for resilience and latency tests (Levels 4 and 6) — this keeps fault/latency scenarios protocol-authentic instead of reintroducing a hand-rolled fake protocol.
- **Keep lightweight fakes/mocks** only where a real server is structurally unable to help:
  - Level 1 Model unit tests and Level 2 ViewModel unit tests still use fakes/mocks, for speed, isolation, and to avoid requiring a running process for every test.
  - The future SignalR command-dispatch path (not yet implemented server-side) still needs a fake/placeholder hub until the real server supports it.
- **Retire** the originally-proposed hand-rolled fake Alpaca REST endpoint and fake telemetry-only SignalR hub as the integration-tier backbone; they are no longer the primary integration mechanism now that real implementations are available.

## 5. Test Levels

### 5.1 Level 1 — Model Unit Tests (transport providers, instance/session management)
**Goal:** Verify provider logic in isolation, with all I/O replaced by test doubles.

Covers:
- Alpaca REST provider: request construction, response parsing, error/timeout handling, polling loop behavior (start/stop/cadence), cancellation.
- SignalR provider: connection lifecycle, message handling, reconnect behavior, group join/leave semantics (mirroring `ChartHub`'s join/leave pattern), cancellation.
- Instance/session manager: creation, disposal, concurrent instance isolation, teardown of transport-level subscriptions on dispose (FR 20-22).
- Capability detection logic (how a telescope instance determines it is GreenSwamp-class vs generic Alpaca-only, FR 14).
- Update reconciliation logic: merging polled REST values with pushed SignalR values for the same instance without stale/duplicate/out-of-order results (FR 43).

Test doubles used: in-memory fake HTTP handler (e.g. `HttpMessageHandler` substitute) and a fake SignalR client abstraction — no real network, no `TestServer` required at this level.

### 5.2 Level 2 — ViewModel Unit Tests
**Goal:** Verify ViewModel behavior against the application-facing telescope abstraction, with the Model layer fully mocked/faked — no real provider, no real transport.

Covers:
- Command execution (connect, disconnect, move/jog, stop, park/unpark, tracking) correctly calling the abstraction and reflecting success/failure in bindable state.
- Bindable properties (position, state, capabilities) updating correctly when the abstraction raises position/state updates.
- Command enable/disable state driven by reported capabilities (FR 36).
- Error/degraded-transport conditions surfaced from the Model correctly reflected in ViewModel-level status/error properties (FR 44-48).
- Behavior with multiple ViewModel instances bound to different telescope instances concurrently, confirming no cross-talk (supports the future tabbed UI).

Test doubles used: an in-memory fake/mock implementation of the telescope abstraction contract itself (not the transport providers) — this is the primary reason the abstraction's shape must be finalized and stable early in design.

### 5.3 Level 3 — Provider Contract/Integration Tests (real reference servers)
**Goal:** Verify each provider against the genuine reference implementations described in Section 4a, run as external processes, for authentic protocol-level confidence.

Covers:
- Alpaca REST provider against the **real ASCOM reference telescope simulator**, exercising connect/disconnect, property reads, movement/park/track commands, and standard Alpaca error responses (FR 10, 12, 23-29).
- Alpaca REST + SignalR providers together against the **real GreenSwampAlpacaServer** (with its bundled mount simulator), validating the dual-transport (GreenSwamp-class) path end-to-end, including telemetry-driven position/state updates via `ChartHub` reconciled with Alpaca REST polling (FR 11, 13, 37-43).
- Capability/version detection logic (FR 14) correctly classifying the ASCOM reference simulator as generic-Alpaca-only and the GreenSwamp server as GreenSwamp-class, against the real servers rather than assumptions about their behavior.
- Both real servers should be started/stopped per test run (or per test class) using isolated ports and isolated settings/data directories, so tests do not interfere with any developer's real installation or with each other when run in parallel (see Section 6, Process Lifecycle Manager).

### 5.4 Level 4 — Resilience & Fault-Injection Tests
**Goal:** Specifically target the reliability/error-handling requirements (FR 44-48, NFR 63-65) as first-class scenarios, using a fault-injecting proxy in front of a real reference server so fault scenarios remain protocol-authentic rather than reintroducing a hand-rolled fake protocol.

Covers:
- SignalR channel drops (proxy severs the SignalR connection) while Alpaca REST (via the real server, proxied normally) remains available → instance continues operating in degraded mode, reflected in state (FR 47).
- Alpaca REST becomes unreachable (proxy blocks/resets REST traffic) while SignalR remains connected → instance reflects unavailability correctly rather than silently trusting stale SignalR-only data.
- Reconnect after a proxy-induced transient network failure (once reconnect policy is defined at design stage).
- Rapid connect/disconnect/dispose cycles on one instance, proxied to a real server, do not affect other concurrently active instances (FR 20, 64).
- Where a fault scenario cannot be produced via the proxy alone (e.g. a malformed/non-standard Alpaca error body), a narrowly-scoped fake HTTP handler may still be used at Level 1 instead of forcing it through the real server.

### 5.5 Level 5 — Concurrency / Multi-Instance Tests
**Goal:** Directly validate the instance-based design goal (FR 5, 7, 18-22, NFR 54, 57) ahead of the tabbed UI existing.

Covers:
- Creating N telescope instances against N real reference server device endpoints concurrently (the GreenSwamp server already supports multiple configured mount devices; multiple ASCOM reference simulator instances can be run on different ports); asserting independent state, independent update streams, and independent disposal.
- Disposing one instance mid-operation does not cancel or corrupt sibling instances.
- Shared infrastructure (e.g. a shared `HttpClient` or connection pool, if used per Risk in §11 of the requirements doc) does not leak state or serialize unrelated instances' operations unnecessarily.

### 5.6 Level 6 — Latency-Sensitive Behavior Tests
**Goal:** Validate the "minimize latency" NFRs (NFR 61-62) in a way that is deterministic and repeatable, using the same fault/latency-injecting proxy from Level 4 placed in front of a real reference server, rather than a synthetic in-memory harness.

Covers:
- With the proxy configured to add deterministic, asymmetric delay per transport (e.g. REST calls to the real server delayed by 150ms, SignalR messages delayed by 10ms), assert that jog/movement commands are dispatched over the lower-latency transport when both are available (FR 30).
- Assert no artificial batching/buffering delay is introduced by the provider or instance layer beyond the proxy-induced transport latency (NFR 62).
- These are correctness/behavioral tests of transport selection logic against a real protocol implementation with controlled delay, not a real-network benchmark — true network latency measurement over an actual WAN is a separate, later concern (see §9).

### 5.7 Deferred — View / End-to-End Tests
Not designed here. Once the prototype UI exists, this strategy should be extended with view-binding tests and possibly UI automation, layered on top of the already-tested ViewModel layer.

## 6. Test Harness Components
The following components should be built (or selected) as shared test infrastructure. These should be designed and reviewed alongside the architecture, not written ad hoc per test.

1. **Process Lifecycle Manager** *(new, central to the revised strategy)* — a shared test-fixture utility that starts and stops the real ASCOM reference simulator and/or the real GreenSwampAlpacaServer as external processes for Level 3, 4, 5, and 6 tests. Responsibilities:
   - Allocate an isolated port (or port range) per test run to avoid collisions with a developer's real installation or with parallel test runs.
   - Point each server at an isolated, disposable settings/data directory (the GreenSwamp server reads/writes versioned settings under a resolved settings path; tests must not touch a real user's settings).
   - Suppress interactive behaviors not wanted in test runs (e.g. the GreenSwamp server's tray icon/browser-launch/console-display logic, most of which is already gated behind `Environment.UserInteractive`/`--service`-style flags; confirm at design stage which startup flags achieve a clean headless test run).
   - Wait for the server to be ready (e.g. poll a management/status endpoint) before tests proceed, and reliably stop/dispose the process afterward even if a test fails.
2. **Fault/Latency-Injecting Proxy** *(new, replaces most of the original fake-server role)* — a lightweight TCP/HTTP proxy placed between the provider under test and a real reference server, used only in Levels 4 and 6, capable of:
   - Introducing configurable, deterministic delay per connection/request (for Level 6 latency-selection tests).
   - Dropping or resetting connections on demand (for Level 4 transport-loss tests).
   - Passing traffic through unmodified otherwise, so the client is still talking to a real, correct protocol implementation for everything except the specific fault being injected.
3. **Synthetic telescope scenario setup** — rather than a hand-rolled state machine, this now means: a small helper layer that drives the *real* reference simulators into known states before a test (e.g. commanding the ASCOM reference simulator or GreenSwamp mount simulator to a specific position/tracking/park state) so tests have deterministic starting conditions without needing a fake protocol implementation.
4. **Virtual clock / time abstraction** — still required for Level 1 and Level 2 unit tests (polling cadence, reconnect backoff, timeout behavior tested by advancing virtual time), and for any client-side timing logic exercised in Levels 3-6 that should not depend on wall-clock waits.
5. **Fake telescope abstraction (for ViewModel tests only)** — a simple in-memory implementation of the application-facing contract, used purely for Level 2 ViewModel tests; unaffected by the move to real servers since Level 2 never talks to a transport at all.
6. **Fake SignalR command hub (temporary)** — a minimal, explicitly-labeled-as-temporary in-process hub implementing only a placeholder command surface, used solely to test the client's SignalR command-dispatch logic ahead of the real GreenSwampAlpacaServer hub supporting commands (tracked as a risk in the requirements doc, §11). This should be removed/replaced once the real server gains this capability.
7. **Scenario/event recorder** — a shared test utility to capture the sequence of position/state updates emitted by a provider or instance during a scenario (whether sourced from fakes or real servers), so ordering/staleness/duplication assertions (FR 43) can be written declaratively.

## 7. Traceability
| Test Level | Primary Requirements Covered | Backed By |
|---|---|---|
| 1 — Model Unit | FR 1-22, 29, 33, 40-43 | Fakes/mocks |
| 2 — ViewModel Unit | FR 1-9, 23-36, NFR 55-56 | Fakes/mocks |
| 3 — Provider Contract/Integration | FR 10-15, 30, 37-43 | Real ASCOM reference simulator + real GreenSwampAlpacaServer |
| 4 — Resilience/Fault-Injection | FR 44-48, NFR 63-65 | Real reference servers + fault-injecting proxy |
| 5 — Concurrency/Multi-Instance | FR 5, 7, 18-22, NFR 54, 57 | Real reference servers (multiple instances/devices) |
| 6 — Latency-Sensitive | FR 30, NFR 58-62 | Real reference servers + latency-injecting proxy |

This table should be revisited once concrete interfaces exist, replacing requirement numbers with specific test case names/IDs.

## 8. Tooling Recommendations
These are recommendations to validate at design stage, not final decisions:
- **Test framework:** xUnit (consistent with typical .NET solution conventions; confirm against existing solution conventions if any exist).
- **Assertions:** FluentAssertions or equivalent, for readable scenario-style assertions (e.g. ordered update sequences).
- **Mocking:** NSubstitute or Moq for Level 2 ViewModel tests against the telescope abstraction, and for the narrow Level 1 cases needing a fake HTTP handler.
- **Real server process management:** a small dedicated test-fixture library (the "Process Lifecycle Manager" in Section 6) responsible for starting/stopping the ASCOM reference simulator and GreenSwampAlpacaServer executables/processes, confirmed against whatever startup/CLI options each already supports (the GreenSwamp server already supports `--urls`, `--reset`, `--service`-style flags that should be reused rather than reinvented for test purposes).
- **Fault/latency proxy:** evaluate an existing lightweight TCP/HTTP proxy library capable of scripted delay/drop behavior versus a small purpose-built proxy; avoid building a full protocol-aware fake server as an alternative to this.
- **Time control:** a virtual/fake time provider (e.g. .NET's `TimeProvider` abstraction with a fake implementation) rather than `Thread.Sleep`/`Task.Delay` in Level 1/2 unit tests.
- **Determinism:** avoid real wall-clock waits in Levels 1-2; Levels 3-6 necessarily involve real process startup and real (proxied) network calls, so determinism there comes from controlled scenario setup and the fault/latency proxy rather than avoiding real I/O entirely.

## 9. Non-Functional Test Considerations
- **Performance/latency benchmarking** against real network conditions (e.g. actual WAN links, not just the local latency-injecting proxy) is a separate activity from this strategy and should be planned once the prototype UI exists and a real GreenSwamp server can be deployed remotely; Level 6 here validates *selection logic* using controlled, proxied delay against a real protocol implementation, not real-world WAN latency numbers.
- **Soak/long-running tests** (e.g. leaving a polling loop or SignalR connection running against a real reference server for extended periods) should be considered once the update-reconciliation design (FR 43) is finalized, to catch slow leaks or drift; not required for initial design sign-off.
- **Chaos-style fault injection** (Level 4) should be treated as a first-class, required part of the test suite given the explicit reliability requirements, not an optional extra, and should be done via the proxy against real servers rather than reverting to a fake protocol implementation.
- **Test run cost/duration.** Because Levels 3-6 now start real external processes, expect these to be noticeably slower than pure in-memory fakes; keep Level 1/2 unit tests as the fast, frequently-run inner loop, and treat Levels 3-6 as a slower integration suite run less frequently (e.g. per-PR rather than per-keystroke) — this trade-off should be confirmed at design stage.

## 10. Test Project Structure (proposed, to validate against architecture)
```text
GreenSwamp.Alpaca.Telescope.Tests
  Model/
	Providers/
	  AlpacaRestProviderTests.cs
	  SignalRProviderTests.cs
	  TransportSelectionTests.cs
	  UpdateReconciliationTests.cs
	Instances/
	  TelescopeInstanceLifecycleTests.cs
	  MultiInstanceIsolationTests.cs

GreenSwamp.Alpaca.Telescope.IntegrationTests   (new — real reference server tier)
  AscomReferenceSimulator/
	AlpacaRestContractTests.cs
  GreenSwampServer/
	DualTransportContractTests.cs
	UpdateReconciliationIntegrationTests.cs
  Resilience/
	TransportDegradationTests.cs
	ReconnectTests.cs
  Concurrency/
	MultiInstanceIsolationIntegrationTests.cs
  Latency/
	TransportSelectionLatencyTests.cs

GreenSwamp.Alpaca.Client.Tests   (or similarly named ViewModel test project)
  ViewModels/
	TelescopeViewModelTests.cs
	TelescopeCommandTests.cs
	TelescopeCapabilityBindingTests.cs

GreenSwamp.Alpaca.Telescope.TestHarness   (shared, non-test-runner library referenced by the above)
  ProcessLifecycleManager/        (starts/stops real ASCOM simulator + real GreenSwamp server)
  FaultLatencyProxy/               (fault/latency-injecting proxy used by Resilience + Latency tests)
  VirtualClock/
  FakeTelescopeAbstraction/        (Level 2 only)
  FakeSignalRCommandHub/           (temporary, until real server supports SignalR commands)
```
The harness should be a separate shared library (not duplicated per test project) so Model-level, integration-level, and future tests reuse the same process-lifecycle and proxy infrastructure.

## 11. Definition of Done (Test Strategy Perspective)
The telescope integration design/implementation should not be considered complete until:
- Every functional requirement in `telescope-requirements.md` §6 has at least one corresponding automated test at the appropriate level from §5 above.
- The Alpaca REST provider has passing contract tests against the real ASCOM reference simulator, not just fakes.
- The dual-transport path has passing contract tests against the real GreenSwampAlpacaServer, not just fakes.
- The reconciliation logic for combined REST+SignalR updates (FR 43) has explicit ordering/staleness/duplication test coverage, given it is flagged as a risk, exercised against the real GreenSwamp server.
- Multi-instance isolation is demonstrated with at least 2 concurrent instances against real reference server(s) in a single test run.
- Resilience scenarios (transport loss, degraded operation, recovery) are covered via the fault-injecting proxy against a real reference server, without requiring a hand-rolled fake protocol implementation.
- ViewModel tests pass using only the fake telescope abstraction — no real provider, no real transport — confirming the MVVM/Model boundary is clean.

## 12. Open Items for Design Stage
- ✅ **Resolved.** Finalize the exact shape of the application-facing telescope abstraction so Level 2 fakes can be written against a stable contract. — `ITelescopeSession` is built (implementation design §6), and Level 2 tests use a fake implementation of it successfully.
- ⬜ **Still open.** Decide the concrete time-abstraction mechanism (e.g. `TimeProvider` vs custom interface) to be used across providers. — Not yet needed because no polling loop exists yet (see architecture §11 and implementation design §10.2, item 2); this decision is now a prerequisite for that loop. **Get Well Guidance.md Stage 3 note:** the loop's *cadences* are now RESOLVED (250ms SignalR / 1 second `DeviceState`, per `SignalR-transport-implementation-plan.md` Decision C) — this item's own scope (which time-abstraction *mechanism* implements those cadences) remains open, tracked as Stage 4 ("Threading and Data Reconciliation"). **Get Well Guidance.md Stage 4 note:** Stage 4 confirms no additional threading/locking complexity is needed beyond time-ordering/sequencing the fixed-cadence REST calls (`SignalR-transport-implementation-plan.md` Decision C/D, Stage 4, RESOLVED) — this item's remaining scope is narrowed to picking the concrete abstraction type (`TimeProvider`, already assumed throughout Phase 4's `FakeTimeProvider`-driven tests, is the natural default), a minor implementation-time choice rather than an open design question.
- 🔶 **Resolved, but the resulting mechanism does not work against the real server yet.** Decide how "GreenSwamp-class" capability detection works, so Level 1/3 tests can simulate/verify both positive and negative detection cases. — `GreenSwampClassDetector` is built and has Level 1 tests for its own matching logic in isolation, but the real reference server's actual `Description`/`DriverInfo` values do not match any of its markers, so a *live* Level 3 positive-detection case cannot currently pass. Confirmed by direct query against the running server. See architecture §6.8/§11 and implementation design §10.2, item 1.
- ✅ **Resolved (Get Well Guidance.md Stage 4).** Decide the reconciliation algorithm for combined update streams, so Level 1 tests can be written test-first alongside its implementation, then confirmed at Level 3 against the real GreenSwamp server. — Per Stage 4 (see `SignalR-transport-implementation-plan.md` Decision C/D, Stage 4, RESOLVED, and Finding A4, RESOLVED): there is no "combined update streams" merge algorithm to design, because SignalR and the Decision D fallback loop are never concurrent active sources for a GreenSwamp-class session. The only remaining ordering need — AlpacaClass telescopes' multiple fixed-cadence REST calls — is resolved by time-ordering/sequencing those calls, with standard OnChange semantics applying per field; the corresponding Level 1 test (multi-cadence sequencing) is specified in `SignalR-transport-implementation-plan.md` Phase 4. *(original item, superseded, retained for traceability)*: Not applicable yet; no SignalR provider exists.
- ✅ **Resolved, informally.** Confirm exact startup flags/configuration needed to run the ASCOM reference simulator and GreenSwampAlpacaServer headlessly, on isolated ports, with isolated settings, for automated test runs. — The Level 3 test project (`GreenSwamp.Alpaca.Telescope.IntegrationTests`) runs against a manually-started real server instance rather than a process-lifecycle-managed one; no `ProcessLifecycleManager`/`TestHarness` project was built. This is a real, narrower-than-planned outcome — it works today but places a manual precondition on whoever runs the Level 3 suite (see implementation design §10.3).
- ⬜ **Still open, deferred.** Select or build the fault/latency-injecting proxy component and confirm it can sit in front of both a plain HTTP (Alpaca REST) and a SignalR (WebSocket-based) connection. — Not needed for this slice; no resilience tests exist yet.
- ⬜ **Still open, deferred.** Decide how test doubles for the SignalR command surface (not yet implemented server-side) should be shaped, given that surface does not exist yet in `GreenSwampAlpacaServer`, and how/when the temporary fake hub is retired once real support lands. — Unstarted; GreenSwamp SignalR work has not begun.
- ⬜ **Still open, deferred.** Decide the acceptable test-run duration/frequency trade-off between the fast Level 1/2 unit suite and the slower, real-process-backed Level 3-6 integration suite (e.g. local dev loop vs PR gate). — Not yet decided; only one Level 3 test exists today and CI gating has not been configured.

### New item identified during this implementation slice
- **Level 1 scope was narrower than §5.1/§10 originally described.** Rather than substituting `AlpacaTelescope`'s internal HTTP transport with a fake handler, Level 1 tests instead isolate and test only the genuinely pure, transport-free logic (`TelescopeStateMapper`, `GreenSwampClassDetector`). `AlpacaTelescopeProvider` itself has no dedicated Level 1 test; real-protocol confidence for it comes entirely from the Level 3 real-server test instead. This satisfies the Definition of Done (§11) via Level 3 coverage, so it is not treated as a defect — but it is a real, conscious narrowing that should be revisited (fake-HTTP Level 1 layer for `AlpacaTelescopeProvider`, or accept Level 3-only coverage permanently) at the next design pass, since it changes the fast-feedback-loop story described in §2's second objective.

