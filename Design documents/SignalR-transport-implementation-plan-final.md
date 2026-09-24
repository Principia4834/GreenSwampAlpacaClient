﻿# GreenSwampAlpacaClient – SignalR Transport Implementation Plan (Final)

This document is the implementation-ready distillation of `SignalR-transport-implementation-plan.md`. It states only the final, resolved design — it does not record how each item was decided, what was superseded, or the review history. See `SignalR-transport-implementation-plan.md` if that history is ever needed; it is not required to implement this work.

All items below are settled. None are open for further review.

====================================================================
## 1. Scope and design intent
====================================================================
- Two telescope classes, one contract:
  1. **GreenSwamp-class telescopes**: Alpaca REST for connection/commands (already built, retained regardless of SignalR state). Telescope **state** is sourced from the GreenSwamp SignalR channel (`TelescopeStateHub`) as the **single source of truth for all state fields** while the SignalR connection is healthy. REST/AP state polling is used only as a fallback after irrecoverable SignalR loss.
  2. **Standard Alpaca-class telescopes**: Alpaca REST/`DeviceState`/other `ITelescopeV3`/`ITelescopeV4` properties only — never attempts a SignalR connection. Fields absent from `DeviceState` (`TargetRightAscension`, `TargetDeclination`, `SiteLatitude`, `SiteLongitude`, `SiteElevation`, `AlignmentMode`, `TrackingRate`) are populated via individual Alpaca property (AP) requests on fixed cadences (§3).
- `ITelescopeSession` is the stable seam (already built): `ConnectAsync`, `DisconnectAsync`, `FindHome`, `ParkAsync`, `AbortSlewAsync`, `StateUpdated`, `ConnectionStatusChanged`, `Capabilities`, `Status`. This plan adds no new members to that public interface — the SignalR path is purely an additional internal data source feeding the same `StateUpdated` event.
- ViewModel/UI must remain fully transport-agnostic — no `Microsoft.AspNetCore.SignalR.Client` or ASCOM type ever crosses into `GreenSwamp.Alpaca.Client.ViewModels` or the Avalonia app project.
- Command routing (`FindHome`/`ParkAsync`/`AbortSlewAsync`) is unaffected by any of this — commands remain REST-only. The SignalR channel is telemetry-only (command dispatch over SignalR is out of scope).

====================================================================
## 2. GreenSwampClass detection
====================================================================
Detection uses the ASCOM Alpaca `DriverInfo` string only — not `Description`, and not a server-level `management/v1/description` probe.

- Parse `DriverInfo` as comma-separated fields.
- Trim the first field and compare against the known server assembly name `"GreenSwamp.Alpaca.Server"`.
- Locate the `Version=` field among the remaining fields and validate it parses as a four-part version (`a.b.c.d`); `0.0.0.0` is a valid value.
- Both the name check and the version check must pass together; return `true` only then.

This directly matches the real reference server's actual `DriverInfo` value (`"GreenSwamp.Alpaca.Server, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"`), which the previous three-hard-coded-marker/`Description`-based approach did not.

====================================================================
## 3. Field placement and sourcing model
====================================================================
`TargetRightAscension`, `TargetDeclination`, `SiteLatitude`, `SiteLongitude`, `SiteElevation`, `AlignmentMode`, and `TrackingRate` are **not** `DeviceState` fields for either class. All seven live on the neutral `TelescopeState` (both classes' ViewModels bind to the same properties there) — not on `GreenSwampTelescopeState` — but the *sourcing mechanism and update cadence* differ per class:

| Field group | AlpacaClass sourcing | GreenSwampClass sourcing |
|---|---|---|
| `AlignmentMode`, `SiteLatitude`, `SiteLongitude`, `SiteElevation`, `TrackingRate` | AP request at session start, then every **30 seconds** | SignalR (sole source while connected); AP on the same 30s cadence only during the Decision-D fallback window (§5) |
| `TargetRightAscension`, `TargetDeclination` | AP request on every `Slewing` false→true transition, then every **1 second** | SignalR (sole source while connected); AP on the same 1s+transition cadence only during the fallback window (§5) |

GreenSwampClass sources all of the above (and every other field) from the SignalR payload as the sole source of truth while the SignalR connection is healthy — not via the AlpacaClass AP-polling cadences, and not as a continuous freshness-merge against REST/AP.

**Known server-side gap (implementation work required, not a design ambiguity):**
- `TelescopeStateService.BuildSnapshot` currently hardcodes `TrackingRate = DriveRate.Sidereal` instead of reading the mount's live value. Fix required, analogous to the existing `SiteLatitude = mount.Settings.Latitude` pattern (read `mount.Settings.TrackingRate`).
- `SiteLongitude`/`SiteElevation` do not exist in `TelescopeStateModel` at all today. Server-side field addition required (`TelescopeStateModel` + `TelescopeStateService.BuildSnapshot`, reading `mount.Settings.Longitude`/`mount.Settings.Elevation`), following the exact `SiteLatitude` pattern.
- Both items are cross-repository (`GreenSwampAlpacaServer`) changes. `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5 is the interface control specification for this work — it fully defines the target field contract; only the code changes remain.

====================================================================
## 4. Poll loops
====================================================================
Two independently-cadenced poll loops are required, in addition to the two AP-property cadences in §3 (which cover fields absent from `DeviceState`):

- **SignalR loop, 250ms cadence** — GreenSwampClass telescopes only, while the SignalR connection is healthy. Ingests each `ReceiveTelescopeState` push and republishes `StateUpdated`. 250ms matches the reference server's own internal `TelescopeStateService` tick.
- **ASCOM Alpaca `DeviceState` loop, 1 second cadence** — the primary, continuous state-polling loop for AlpacaClass telescopes. For GreenSwampClass telescopes, this same loop is the Decision-D fallback loop only (§5) — it does not run continuously alongside the SignalR loop; it engages only after irrecoverable SignalR loss and stops again once SignalR resumes as sole source of truth.

The only case of multiple concurrent cadences is for AlpacaClass telescopes (and GreenSwampClass telescopes during the fallback window): the `DeviceState` loop (1s), the site/alignment/tracking-rate AP cadence (30s), and the target-coordinate AP cadence (1s + slewing-transition) all run for the same session. These blocking REST calls are time-ordered/sequenced so each field is saved into the model as its individual call completes; the standard OnChange/property-update pattern then applies per field. No shared-timer/lock-based composition scheme is required — sequencing the calls is sufficient. GreenSwampClass telescopes while SignalR-connected have only the one 250ms SignalR loop active (the 1s `DeviceState` loop is dormant).

====================================================================
## 5. Reconciliation and fallback (Decision D)
====================================================================
- For a **GreenSwampClass** session: while the SignalR connection is healthy, every published `StateUpdated` snapshot is SignalR-sourced in full — no field-by-field freshness comparison against REST/AP is performed, because REST/AP is not a concurrent source for this class.
- `TelescopeSession` switches to REST/AP-sourced state for a GreenSwampClass device only after an **irrecoverable** loss of the SignalR connection, defined as: failure to re-establish the connection after 5 retries, at intervals of 2, 5, 10, 10, and 10 seconds. Once SignalR reconnects successfully (at any point, including after the fallback has engaged), it resumes being the sole source of truth again.
- This reconciliation model does not apply to AlpacaClass telescopes at all — their state is always AP/`DeviceState`-sourced per §3's cadences; there is no "prefer SignalR" step for that class.
- No de-duplication/ordering algorithm across two concurrent transports is needed: for a GreenSwampClass session, SignalR and the fallback loop are never concurrent active sources — either SignalR is healthy (sole source), or it has been marked failed after the 5-retry schedule (fallback is the sole source instead). There is no parallel-source window to reconcile.
- No bespoke cross-thread signaling mechanism is needed for "irrecoverable" detection: `TelescopeSession` owns all transport concerns end-to-end and is the single place the source/validity decision is made, regardless of which thread the retry-exhaustion determination occurs on. Counting the 5 retries may use `WithAutomaticReconnect()`'s own built-in behavior or independent tracking — an implementation-time choice, not a design gap.
- `TelescopeSession` still raises `StateUpdated` at most once per genuinely-new snapshot — never for a stale/duplicate/out-of-order value — regardless of which class/source produced it.

====================================================================
## 6. Wire contract (enums, JSON)
====================================================================
- `GreenSwampAlignmentMode { AltAz = 0, Polar = 1, GermanPolar = 2 }`
- `GreenSwampDriveRate { Sidereal = 0, Lunar = 1, Solar = 2, King = 3 }`
- Both (and `SideOfPier`/`PointingState`, `SlewState`/`SlewType`, `MountType` — all five enum-bearing properties) are wire-matched **by string member name**, not by ordinal — the integer values above are documentation-only.
- The client must configure its `HubConnectionBuilder` with the inverse converter: `.AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))`, covering all five enum-bearing properties.
- PascalCase property names, unchanged.
- `Vector`-shaped `TrackingOffsetRate` (`{X,Y}`) maps to the existing `(double X, double Y)` tuple.
- `ParkSelectedName` nullability preserved.

====================================================================
## 7. Implementation phases
====================================================================

### Phase 0 — Pre-work (blocking prerequisites)
- 0.1 Fix `GreenSwampClassDetector` per §2.
- 0.2 Implement the per-class field placement/sourcing model (§3) before writing any mapper code, since it affects the shape of `TelescopeState`.
- 0.3 Confirm the exact JSON contract per §6.

### Phase 1 — Add the GreenSwamp SignalR provider (model layer)
- New type: `GreenSwampSignalRProvider`, internal to `GreenSwamp.Alpaca.Telescope.Model`, alongside `AlpacaTelescopeProvider` (same assembly, same visibility pattern).
- Package: add `Microsoft.AspNetCore.SignalR.Client` to `GreenSwamp.Alpaca.Telescope.Model.csproj` (not the Client app project).
- Connection: `HubConnectionBuilder().WithUrl(".../telescopestatehub").WithAutomaticReconnect()`, plus `.AddJsonProtocol(...)` registering `JsonStringEnumConverter` (§6).
- Lifecycle:
  - `JoinTelescopeStateGroupAsync(deviceNumber)` on successful (re)connect — must be re-issued after every automatic reconnect, since `WithAutomaticReconnect()` does not restore server-side group membership by itself. (The periodic keepalive `Touch` itself is a server-side responsibility triggered by group membership — the client's only obligation is to keep `HubConnection` alive and rejoin the group after reconnects.)
  - `LeaveTelescopeStateGroupAsync(deviceNumber)` + `StopAsync()` on disconnect/dispose.
- Payload handling: register a handler for `ReceiveTelescopeState`, deserialize into the client-side neutral mirror type (§3's resolved shape), and hand the resulting snapshot to `TelescopeSession`'s single ingestion point — the provider itself does not decide precedence over REST; that is `TelescopeSession`'s job (§5).
- No historical replay: a newly-joined subscriber gets nothing until the next ~250ms server tick. The provider (and `TelescopeSession`) must tolerate a brief "no SignalR data yet" window after join without treating it as degraded/faulted.

### Phase 2 — Integrate into `TelescopeSession` orchestration
- Attempt `GreenSwampSignalRProvider` **only when** `Capabilities.IsGreenSwampClass == true` — standard Alpaca-class sessions never construct or connect this provider at all.
- Add the AlpacaClass AP-polling loop (§3) alongside the `DeviceState` poll loop (1s cadence, §4), so both `DeviceState`-sourced fields and AP-sourced fields are present on every published `TelescopeState` snapshot for a standard Alpaca-class session.
- Implement the single ingestion point per §5's per-class model.

### Phase 3 — Connection-status and degraded-mode reporting
`TelescopeConnectionStatus.IsSignalRActive` / `IsSignalRDegraded` become meaningful:
- Non-GreenSwamp-class device: both always `false` (no attempt made).
- GreenSwamp-class device, hub connected and joined: `IsSignalRActive = true`, `IsSignalRDegraded = false`.
- GreenSwamp-class device, hub disconnected/reconnecting, within the 5-retry/2-5-10-10-10s schedule: `IsSignalRActive = false`, `IsSignalRDegraded = true`, while `IsAlpacaRestActive` remains `true` if REST is still reachable — state continues to be reported from the last-known SignalR snapshot until the fallback engages.
- GreenSwamp-class device, irrecoverable SignalR loss (all 5 retries exhausted): `IsSignalRActive = false`, `IsSignalRDegraded = true` persists, and `TelescopeSession` begins publishing AlpacaClass-style AP/`DeviceState`-sourced `StateUpdated` snapshots instead of waiting indefinitely for SignalR data.
- Raise `ConnectionStatusChanged` on every transition, not just on terminal states.

====================================================================
## 8. Test plan (mapped onto telescope-test-strategy.md's six levels)
====================================================================

**Level 1 (Model unit, fakes only):**
- `GreenSwampClassDetector` matcher tests: name match with no/invalid version field, version-only match with the wrong name, malformed version strings, and the real server's exact string.
- `GreenSwampSignalRProvider` fake-hub suite — three tests, run in this order:
  1. **Deserialization test** — the full `TelescopeStateModel`-shaped JSON payload, including all five string-enum properties (`SideOfPier`/`PointingState`, `TrackingRate`/`DriveRate`, `SlewState`/`SlewType`, `MountType`, `AlignmentMode`) and the `Vector`-shaped `TrackingOffsetRate`, deserializes correctly. No fake hub required — a direct unit test against the payload type/converter registration.
  2. **Population/field-mapping confidence test** — a full representative payload sent through the fake hub, asserting every `TelescopeState`/`GreenSwampTelescopeState` field currently defined in the SignalR spec maps correctly into the telescope model.
  3. **Provider-level fake-hub connection lifecycle test** — connect, disconnect/automatic-reconnect, re-join (`JoinTelescopeStateGroupAsync` re-issued after reconnect), and dispose (`LeaveTelescopeStateGroupAsync`/`StopAsync`) behavior against the fake hub.
- `TelescopeSession` reconciliation/ordering tests: `TelescopeSession`'s 5-retry/2-5-10-10-10s fallback-counting logic, using a lighter fake provider rather than the fake hub itself.
- `AlpacaTelescopeProvider` AP-polling cadence tests: `AlignmentMode`/`SiteLatitude`/`SiteLongitude`/`SiteElevation`/`TrackingRate` polled at connect and every 30s; `TargetRightAscension`/`TargetDeclination` polled on `Slewing` false→true transition and every 1s — all driven by `FakeTimeProvider`.
- Poll-loop cadence tests: the SignalR ingestion loop ticks at exactly 250ms for a GreenSwampClass session while connected; the `DeviceState` loop ticks at exactly 1 second for an AlpacaClass session continuously, and for a GreenSwampClass session only during the fallback window (dormant otherwise) — all driven by `FakeTimeProvider`.
- Multi-cadence sequencing tests: for an AlpacaClass session, confirm the `DeviceState` (1s), site/alignment/tracking-rate AP (30s), and target-coordinate AP (1s + slewing-transition) calls are time-ordered/sequenced correctly, with each field saved into the model as its own call completes and the standard OnChange pattern firing per field.

**Level 2 (ViewModel unit, fake `ITelescopeSession`):**
- Confirm `TelescopeTabViewModel` requires no changes to consume SignalR-sourced snapshots (regression check).
- New bindable fields (the seven fields on core `TelescopeState`, §3) get basic property-mapping tests.

**Level 3 (real reference server):**
- `IsGreenSwampClass` returns `true` against the real server.
- Connect → join hub → receive at least one real `ReceiveTelescopeState` push → confirm it reaches `TelescopeSession.StateUpdated` with correctly-typed enums.
- **Field-completeness test — hard-fail by design:** connect → join hub → receive a real `ReceiveTelescopeState` push → assert no expected SignalR-sourced field is missing/null/default. No skip/xfail escape hatch — this test exists to verify the real server implementation has been brought fully into step with client expectations. This test is gated on the server-side (`TrackingRate` live-value fix, `SiteLongitude`/`SiteElevation` addition — §3) and client-side (`TelescopeState` field additions) code changes landing.
- Carry forward the existing manual precondition process (design §10.3): manually started server, manually-confirmed device precondition.

**Level 4 (resilience/fault-injection):** deferred for this pass — no fault/latency proxy exists yet. If a manual "kill the server process mid-connection" smoke check is feasible without the proxy, do it as an exploratory check only.

**Level 5 (concurrency/multi-instance):** at minimum, confirm two concurrently open GreenSwamp-class sessions against two different device numbers each receive only their own group's broadcasts (group isolation).

**Level 6 (latency-sensitive):** out of scope for this pass — no command dispatch exists over SignalR yet.

**TestHarness:** add a fake hub double sized to support all three Level 1 `GreenSwampSignalRProvider` tests (deserialization, population/field-mapping, connection lifecycle with reconnect/retry/backoff/dispose), not merely a minimal join/leave stub. Do not build `ProcessLifecycleManager`/`FaultLatencyProxy` this pass — continue the manual-server-start precedent.

====================================================================
## 9. Required changes by area
====================================================================
- **Abstractions layer** (`GreenSwamp.Alpaca.Telescope.Abstractions`):
  - Add `GreenSwampAlignmentMode`, `GreenSwampDriveRate` enums (exact members per §6).
  - Add `TargetRightAscension`, `TargetDeclination`, `SiteLatitude`, `SiteLongitude`, `SiteElevation`, `AlignmentMode`, and `TrackingRate` to the neutral `TelescopeState` — not to `GreenSwampTelescopeState`.
  - No new dependency on `Microsoft.AspNetCore.SignalR.Client` or ASCOM types here — this project remains pure contracts/DTOs.
- **Model layer** (`GreenSwamp.Alpaca.Telescope.Model`):
  - Add `GreenSwampSignalRProvider` (internal) and the `Microsoft.AspNetCore.SignalR.Client` package reference.
  - Extend `AlpacaTelescopeProvider` with the two AP-polling cadences (§3).
  - Add the SignalR ingestion loop (250ms, GreenSwampClass) and the `DeviceState` poll loop (1s, AlpacaClass always; GreenSwampClass as fallback only) (§4).
  - Extend `TelescopeSession` with the per-class ingestion model (§5).
  - Fix `GreenSwampClassDetector` (§2).
- **ViewModel/UI layers:** no structural change expected — verified via Level 2 regression tests only.
- **Test projects:**
  - `GreenSwamp.Alpaca.Telescope.Tests`: new Level 1 coverage per §8.
  - `GreenSwamp.Alpaca.Telescope.IntegrationTests`: new Level 3 SignalR scenario, alongside the existing Connect→FindHome→Abort→Park→Disconnect test.
  - `GreenSwamp.Alpaca.Telescope.TestHarness`: add the fake hub double per §8.

====================================================================
## 10. Acceptance criteria
====================================================================
- `IsGreenSwampClass` correctly returns `true` against the real GreenSwampAlpacaServer reference simulator, via the `DriverInfo` name+version check (§2) — not `Description` matching and not a `management/v1/description` probe.
- Standard Alpaca-class devices never attempt a SignalR connection and are functionally unchanged.
- GreenSwamp-class devices receive live telemetry via `TelescopeStateHub` without any SignalR/ASCOM type crossing into the ViewModel/UI layer.
- All five enum-bearing properties deserialize correctly via `JsonStringEnumConverter` against real server payloads, not just hand-written fixtures.
- GreenSwampClass devices publish state sourced exclusively from SignalR while connected — no continuous REST/AP-vs-SignalR merge occurs for this class; REST/AP is used only after the 5-retry/2-5-10-10-10s irrecoverable-loss schedule is exhausted, and SignalR resumes as sole source once reconnected.
- Standard Alpaca-class devices publish `TargetRightAscension`/`TargetDeclination`/`SiteLatitude`/`SiteLongitude`/`SiteElevation`/`AlignmentMode`/`TrackingRate` via AP requests on the fixed cadences (§3).
- A poll loop runs for both classes at the fixed cadences confirmed by §4 — demonstrated by a deterministic, `FakeTimeProvider`-driven Level 1 test, not solely observed informally against the real server.
- `TelescopeSession` never raises `StateUpdated` for a stale/duplicate/out-of-order snapshot, demonstrated by a deterministic, `FakeTimeProvider`-driven Level 1 test.
- `TelescopeConnectionStatus.IsSignalRActive`/`IsSignalRDegraded` correctly reflect hub connectivity, including the "degraded responsiveness, not availability" transition when the hub drops, and the irrecoverable-loss fallback transition specifically.
- The only multi-cadence concurrency case (AlpacaClass AP/`DeviceState` polling) is resolved by time-ordering/sequencing the fixed-cadence blocking REST calls, with standard OnChange semantics applying per field thereafter.
- `GreenSwampSignalRProvider`'s Level 1 test suite demonstrates: correct deserialization of all five enum-bearing properties and the `Vector` shape; correct field-mapping of a full representative payload into the telescope model; and correct connect/reconnect/retry-backoff/dispose behavior against a fake hub — all via `GreenSwamp.Alpaca.Telescope.TestHarness`'s fake hub double, no real server required.
- A dedicated Level 3 field-completeness test confirms the real GreenSwampClass reference server populates every SignalR-sourced field the client expects, with no silent gaps. This test is gated on the server-side and client-side code changes in §3 landing, and is not considered part of this plan's acceptance criteria until those changes are complete and the test can actually exercise a fully-populated payload.

====================================================================
## 11. Sequencing
====================================================================
1. Phase 0: fix `GreenSwampClassDetector`; confirm it against the real server (Level 3).
2. Phase 1: implement `GreenSwampSignalRProvider` against a fake hub (Level 1) first, then the real hub (Level 3) once the server-side `TelescopeStateHub` is available.
3. Phase 2: integrate into `TelescopeSession`, including the per-class ingestion model, the AlpacaClass AP-polling cadences, and the two poll-loop cadences.
4. Phase 3: wire connection-status/degraded-mode reporting.
5. Phase 4: complete the Level 1/2/3/5 test coverage named in §8; explicitly record Level 4/6 as deferred, not silently skipped. The Level 3 field-completeness test cannot be completed until the server-side and client-side field-coverage work (§3) lands in code on both server and client — sequence it after that lands, not before.
6. Regression pass: re-run the existing Connect/FindHome/Park/Abort Level 1–3 suite to confirm no behavior change for the already-shipped REST-only slice.

====================================================================
## 12. Known outstanding implementation work (carried forward, not design gaps)
====================================================================
- **Server-side (`GreenSwampAlpacaServer`):** fix `TelescopeStateService.BuildSnapshot`'s `TrackingRate` hardcode; add `SiteLongitude`/`SiteElevation` to `TelescopeStateModel`/`BuildSnapshot`. See `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5 for the exact target contract.
- **Client-side (`GreenSwampAlpacaClient`):** add the seven fields to `TelescopeState` (Abstractions layer); implement `GreenSwampSignalRProvider`, the two poll loops, and the `TelescopeSession` per-class ingestion model (Phases 1–3, §7).
- **Test infrastructure:** build the `TestHarness` fake hub double (§8); the Level 3 field-completeness test is blocked until the server/client field-coverage work above lands.
- **Explicitly deferred, not in scope for this pass:** `ProcessLifecycleManager`/`FaultLatencyProxy` (Level 4 fault-injection tooling) — continue the manual-server-start precedent instead.

====================================================================
NOTES
====================================================================
This is an implementation-ready plan derived from the reviewed and fully-resolved `SignalR-transport-implementation-plan.md`. No production code has been changed as part of producing this document.
