# GreenSwampAlpacaServer — TelescopeState SignalR Broadcast: Capability Specification

**Status:** Draft specification for offline review. This is a **server-side** (`GreenSwampAlpacaServer`) capability specification, written to unblock the client-side (`GreenSwampAlpacaClient`) `GreenSwampSignalRProvider` design (architecture §6.4), per the stakeholder-agreed sequencing: **specify/design → client design + server implementation in parallel**, not full implementation first.

**Last updated:** 2026-09-23 13:52 — all §9 items confirmed; §11 enum-namespace ambiguity and field gaps resolved (see §11.4/§11.4.1/§11.7). Specification is ready to drive parallel client-side and server-side implementation.

## 1. Purpose and Why This Is Needed

`telescope-architecture.md` (§6.4, client repo) already documents that the *existing* `ChartHub` is scoped purely to charting (raw axis-step points and pulse-guide points) and is not a suitable transport for low-latency sky-position/state telemetry. The data itself (`RightAscension`/`Declination`/`Altitude`/`Azimuth`/etc., refreshed ~every 250ms) already exists server-side in `TelescopeStateModel`/`TelescopeStateService` — it is simply not yet broadcast over SignalR in a form a remote client can consume.

This document specifies a **new SignalR capability** in `GreenSwampAlpacaServer` to broadcast telescope state to subscribed clients, closing that gap. It does not implement the capability — implementation is a follow-on step once this specification is reviewed and approved (see the client's implementation-design document, §8, for the parallel client-side dependency this unblocks).

## 2. Scope

**In scope:**
- A new SignalR hub (or new methods/groups on a viable existing hub — recommendation below) that broadcasts `TelescopeStateModel`-shaped telescope state to subscribed clients, per device.
- The DTO/payload shape, broadcast cadence, and subscription (join/leave) semantics.
- Resolving the "no active Blazor view = no data" gap identified below (§4).

**Out of scope (deferred):**
- Command dispatch over SignalR (jog, slew, park, etc.) — a separate, already-flagged cross-repository risk (requirements §11, architecture §6.4); this document is telemetry-only, one-directional (server → client).
- Any change to the Alpaca REST `Telescope` driver or `DeviceState` implementation.
- The Configuration File Management REST API (`ConfigController`) — unrelated, already out of scope per architecture §6.9.
- Client-side consumption details (that is the client's `GreenSwampSignalRProvider` design, which depends on this spec but is authored separately).
- Any change to the existing `ChartHub`'s charting behavior — it continues unmodified for its existing purpose.

## 3. Evidence Base (source inspection performed for this specification)

- `T:\...\GreenSwamp.Alpaca.Server\Services\TelescopeStateService.cs` — background loop (`RunLoopAsync`) ticks every 250ms via `Task.Delay`, but **only for devices returned by `_activeViews.GetActiveDeviceNumbers(TimeSpan.FromSeconds(10))`**; if no device has an active view, the loop does nothing that tick (`if (active.Count == 0) continue;`). Exposes `DeviceStateChanged` (per-device, fires every tick with a fixed `PropertyName` of `nameof(TelescopeStateModel.AxisSteps)` regardless of what actually changed — see §6 note) and a coarser `StateChanged` (no per-device args).
- `T:\...\GreenSwamp.Alpaca.Server\Services\ActiveDeviceViewRegistry.cs` — a `ConcurrentDictionary<viewSessionId, (DeviceNumber, LastSeenUtc)>`; `Touch(viewSessionId, deviceNumber)` records/refreshes a view; `GetActiveDeviceNumbers(staleAfter)` prunes and returns distinct device numbers still within the staleness window. Called today only from Blazor page `OnAfterRenderAsync`/timer-tick code-behind (`TelescopeView.razor.cs`, `MountStatus.razor.cs`, `MountControl.razor.cs`, `RaDecChartHandler.cs`), each with its own `_viewSessionId` per browser tab.
- `T:\...\GreenSwamp.Alpaca.Server\Hubs\ChartHub.cs` and `Services\ChartDataService.cs` — the established pattern this new capability should follow: hub exposes `Join*GroupAsync(deviceNumber)`/`Leave*GroupAsync(deviceNumber)`, tracks per-connection group membership in a static `ConcurrentDictionary` (survives transient hub instantiation), calls into a **singleton** service (`ChartDataService`) for subscriber-count gating (`OnRaDecClientJoined`/`OnRaDecClientLeft`) and for historical-data replay (`GetRaDecHistory`), and `OnDisconnectedAsync` cleans up abrupt disconnects. `ChartDataService` itself subscribes to `TelescopeStateService.DeviceStateChanged` and pushes to hub groups via `IHubContext<ChartHub>.Clients.Group(...).SendAsync(...)`.
- `T:\...\GreenSwamp.Alpaca.Server\Program.cs` — `AddSignalR()` already registered; `MapHub<ChartHub>("/charthub")` is the existing endpoint pattern to extend or mirror.
- `T:\...\GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs` (`DeviceState` getter) and `ASCOM.Common.DeviceStateClasses.TelescopeState` (NuGet XML docs) — confirms the standard 11-property `DeviceState` snapshot shape already used elsewhere in this project's documents, useful as a baseline/minimum payload if a narrower DTO is preferred over full `TelescopeStateModel` (see §6 open item).
- Client-side `telescope-architecture.md` §6.3/§6.4 and `telescope-implementation-design.md` §3 (property table, this session) — the consumer-side context and the specific property list this capability needs to be able to satisfy.

## 4. Key Design Issue: "No Active View" Gap (resolved direction, confirmed with stakeholder)

**Finding:** `TelescopeStateService`'s loop is currently driven entirely by Blazor browser tabs calling `ActiveViews.Touch(...)`. A remote SignalR-only client (no Blazor tab open for that device) would join a hypothetical new hub group but receive **no data**, because `TelescopeStateService` would never populate/refresh that device's cache — `GetActiveDeviceNumbers` would not return it.

**Confirmed direction:** the new hub's subscription (group-join) itself must count as an active view. Concretely: joining the new hub's group for a device calls `ActiveDeviceViewRegistry.Touch(viewSessionId, deviceNumber)` (using the SignalR `ConnectionId` as the view-session key, or a similar per-connection identifier), and this call must be **repeated periodically** (not just once at join time) so the view does not go stale after the registry's existing 10-second window while a remote client remains connected with no Blazor tab open. This makes `ActiveDeviceViewRegistry` a shared "who needs live data" mechanism across both Blazor pages and remote SignalR subscribers, rather than a Blazor-only concept — a natural generalization of its existing purpose, not a redesign.

This is called out as a **specific implementation requirement** in §7 below, not left as an afterthought.

## 5. Recommended Approach: New Hub vs. Extend `ChartHub`

**Recommendation: a new, separate hub** (e.g. `TelescopeStateHub` at `/telescopestatehub`), not new methods bolted onto `ChartHub`.

Rationale:
- `ChartHub`'s existing purpose (raw step/pulse chart data for chart *windows*) is a different concern and a different consumer (Blazor chart pages) from full telescope state for a remote MVVM client. Mixing them risks the same kind of scope creep the architecture document already flagged for the Configuration API (§6.9's "keep parallel, don't fold in" principle applies equally here).
- A dedicated hub keeps subscription semantics (group-per-device) simple and lets this capability evolve (e.g. future command dispatch, per architecture §6.4) without perturbing chart functionality.
- Precedent: `ChartHub` itself is already a separate hub from any future command hub — this project already uses one-hub-per-concern.

This recommendation is presented for confirmation, not assumed final — flagged in §9.

## 6. Payload Shape

**Recommendation:** broadcast the **full `TelescopeStateModel`** (or a DTO mirroring it 1:1) rather than a narrower subset, for these reasons:
- The client-side property table (`telescope-implementation-design.md` §3) was built specifically to identify every `TelescopeStateModel` field the ViewModel might eventually want, including several marked "not currently exposed" precisely because no transport carries them today. Broadcasting the full model closes that gap in one step rather than requiring repeated round-trips to add fields piecemeal.
- `TelescopeStateService.BuildSnapshot` already constructs a complete `TelescopeStateModel` every tick — there is no extra server-side computation cost to broadcasting the whole object versus a hand-picked subset; the cost is already paid.
- A narrower, hand-picked DTO would need to be revisited every time the client's needs evolve, whereas the full model is self-describing and future-proof.

**Caveat / open item:** `TelescopeStateModel` is currently defined in `GreenSwamp.Alpaca.Server.Models`, a project the client cannot (and should not) reference directly (it's an ASP.NET Core Blazor server project). This specification therefore requires either:
(a) a plain-data DTO record defined in a shared/neutral location (e.g. a new small shared contracts project, or inline in the hub, mirroring `TelescopeStateModel`'s shape field-for-field) that both sides can agree on without a project reference, or
(b) the client treats the SignalR payload purely as loosely-typed JSON (matching System.Text.Json's default camelCase serialization) and defines its own DTO independently, accepting the duplication-of-shape risk if the two ever drift.

Recommendation is (a) — a small, explicitly-versioned shared DTO shape (not a shared assembly reference, just an agreed-upon JSON contract, documented here) — but this is flagged as a decision point for you to confirm (§9).

**Note on `DeviceStateChanged`'s existing `PropertyName` argument:** `TelescopeStateService.RunLoopAsync` currently raises `DeviceStateChanged` every tick with a **hard-coded** `PropertyName` of `nameof(TelescopeStateModel.AxisSteps)`, regardless of what actually changed in the snapshot (confirmed by reading the loop — it is not per-field change detection; the whole model is rebuilt and the event just carries a fixed label consumed today only by `ChartDataService`'s `if (e.PropertyName != nameof(TelescopeStateModel.AxisSteps)) return;` filter). This new capability should **not** attempt to reuse `PropertyName` for change-detection purposes; it should treat every tick as "full snapshot available" and broadcast unconditionally to subscribed groups, exactly as `ChartDataService` already does for `AxisSteps`.

## 7. Subscription, Broadcast Cadence, and Lifecycle — Detailed Requirements

1. **Hub:** `TelescopeStateHub` (new), mapped at a new endpoint (e.g. `/telescopestatehub`), following `ChartHub`'s method-naming and group-naming conventions:
   - `JoinTelescopeStateGroupAsync(int deviceNumber)` → adds caller to group `"TelescopeState-{deviceNumber}"`.
   - `LeaveTelescopeStateGroupAsync(int deviceNumber)` → removes caller from the group.
   - `OnDisconnectedAsync` → cleans up group tracking and the corresponding `ActiveDeviceViewRegistry` entry on abrupt disconnects, mirroring `ChartHub`'s existing pattern exactly.
2. **View-registry keep-alive (resolves §4):** on join, the hub (or a backing singleton service, mirroring `ChartDataService`'s role) calls `ActiveDeviceViewRegistry.Touch(Context.ConnectionId, deviceNumber)`. This call must be **repeated on a timer** (recommended: every 5 seconds, well inside the registry's existing 10-second staleness window) for as long as the connection remains subscribed — not just once at join — so a long-lived remote client without a Blazor tab keeps the corresponding device "active" in `TelescopeStateService`'s eyes. On leave/disconnect, the corresponding registry entry is removed via `ActiveDeviceViewRegistry.Remove(Context.ConnectionId)`.
3. **Broadcast trigger:** a new singleton service (e.g. `TelescopeStateBroadcastService`, parallel to `ChartDataService`) subscribes to `TelescopeStateService.DeviceStateChanged` and, for every event where the device has at least one subscriber in its group (tracked the same way `ChartDataService` tracks `_raDecSubscribersByDevice`), sends the full snapshot to `Clients.Group($"TelescopeState-{deviceNumber}")` via a named method (e.g. `"ReceiveTelescopeState"`).
4. **Cadence:** inherits `TelescopeStateService`'s existing ~250ms tick — this specification does not introduce a second polling loop or a different cadence. No throttling/coalescing is proposed at this layer (consistent with the client architecture's own position that filtering/reconciliation is the *client's* `TelescopeSession` responsibility, not the transport's — architecture §6.6, limitation 1).
5. **No historical replay required** for this capability (unlike `ChartHub`'s `RequestHistoricalDataAsync`) — telescope state is a live "current value," not a time series; a newly-joined subscriber simply receives the next tick's broadcast. Confirm this assumption in §9.
6. **Device-number scoping:** identical multi-device model to `ChartHub` — one group per device number, supporting the existing multi-instance GreenSwampAlpacaServer deployment pattern already assumed throughout the client's requirements/architecture documents.

## 8. Non-Functional Considerations

- **Backward compatibility:** purely additive — no changes to `ChartHub`, `ConfigController`, or the Alpaca REST `Telescope` driver. Existing Blazor pages and existing Alpaca REST clients are unaffected.
- **Server load:** broadcasting the full `TelescopeStateModel` (rather than a narrow DTO) to N subscribed groups every 250ms is a modest, bounded cost proportional to active subscriber count — consistent with the existing `ChartHub` broadcast pattern already proven in production for this project.
- **Security/versioning:** no authentication/authorization scheme currently exists on `ChartHub`; this specification assumes the same trust model (LAN-local server, no auth) unless you direct otherwise — flagged as an open item.
- **Testability (forward reference to client test strategy):** this hub's payload shape and cadence directly determine what the client's Level 3-6 test infrastructure (real reference servers / fault-latency proxy, per `telescope-test-strategy.md`) needs to simulate. Once implemented, the client's test harness will need either a real `GreenSwampAlpacaServer` instance or a lightweight fake hub emitting the same contract — noted for the test strategy document's eventual update, not actioned here.

## 9. Confirmed Decisions (all items resolved)

1. **New hub vs. extending `ChartHub`** (§5) — **CONFIRMED.** A new, separate hub (`TelescopeStateHub`) will be created; `ChartHub` is not extended.
2. **DTO strategy** (§6) — **CONFIRMED, see §11.** Decision: loosely-typed JSON (System.Text.Json default serialization) matching `TelescopeStateModel`'s shape 1:1, paired with client-authored neutral mirror types and a maintained field-contract table — not a shared compiled assembly or Git submodule. This supersedes the "shared DTO record" recommendation originally given in §6.
3. **View-registry keep-alive interval** (§7.2) — **CONFIRMED: 5 seconds** (comfortably inside the existing 10-second staleness window).
4. **No historical replay** (§7.5) — **CONFIRMED.** No "send current snapshot immediately on join" behavior; a newly-joined subscriber receives the next tick's broadcast, as originally drafted in §7.5.
5. **Authentication/trust model** (§8) — **CONFIRMED: no authentication**, consistent with the existing `ChartHub`/Alpaca REST trust model.
6. **Full-model vs. narrower DTO** (§6) — **CONFIRMED.** The complete `TelescopeStateModel` shape is broadcast, not a curated subset.

The remaining items raised in §11.7 (enum-namespace ambiguity, enum wire format, five-field gap) are also now resolved — see §11.3, §11.4, §11.4.1, and §11.7.

## 10. Process Note

This is a specification for **server-side** work in `GreenSwampAlpacaServer`. Once you confirm §9, the recommended next steps run in parallel: (a) implement this hub/service in `GreenSwampAlpacaServer` against this agreed contract, and (b) continue the client's implementation design (`telescope-implementation-design.md`) referencing this same contract for the GreenSwamp-class SignalR-sourced properties in its property table, replacing "not currently exposed" markers with "available via `TelescopeStateHub`" for every field this capability covers.

## 11. DTO Contract Strategy (resolves open item §9.2)

### 11.1 Decision — CONFIRMED

The payload is **loosely-typed JSON** (System.Text.Json default serialization of the full `TelescopeStateModel`, per §6's recommendation), **not** a shared compiled DTO assembly and **not** a Git submodule/subtree. The client continues to define its own neutral mirror types independently, as it already does today.

This supersedes the "(a) shared DTO record" recommendation originally given in §6 and listed as open item §9.2. That recommendation is withdrawn in favor of (b) (loosely-typed JSON, client-defined DTOs), for the reasons in §11.2.

### 11.2 Rationale

- **The client has already adopted this exact pattern, consistently, for every prior cross-repository type.** `GreenSwamp.Alpaca.Telescope.Abstractions\TelescopePierSide.cs` and `TelescopeAxis.cs` are each explicitly documented as "neutral, protocol-agnostic mirror[s]... defined here (not referenced from ASCOM.Common) so the ViewModel/Abstractions layer never takes a compile-time dependency on the ASCOM client library." `GreenSwampTelescopeState.cs` (same project) goes further and is already a hand-authored, evidence-based mirror of most of `TelescopeStateModel`'s GreenSwamp-specific fields, including its own neutral `GreenSwampSlewType`/`GreenSwampMountType` enums — this work substantially pre-dates this specification. A shared-assembly or submodule approach would be the one inconsistency in an otherwise deliberate pattern.
- **Neither repository has the infrastructure a shared-package approach requires.** `GreenSwampAlpacaClient` and `GreenSwampAlpacaServer` are separate Git repositories with separate GitHub remotes (`Principia4834/GreenSwampAlpacaClient` and `Principia4834/GreenSwampAlpaca`), no shared solution, and no submodule linkage today. `GreenSwamp.Alpaca.Server`'s `NuGet.config` lists only `nuget.org` and the public ASCOM MyGet feed — there is no private feed to publish a contracts package to. Standing up that infrastructure to serve one DTO would be disproportionate.
- **A checked-in, maintained contract table (§11.4) closes the main risk of the JSON approach** — silent drift between server and client shapes — without new tooling, by making the field-by-field mapping an explicit, reviewable artifact rather than tribal knowledge.
- A schema/codegen pipeline (auto-generating client types from a server-emitted schema) was considered and rejected as premature: the field set is being defined once, deliberately, in this document, with both sides collaborating on the same spec. Codegen solves a maintenance-phase drift problem that does not yet exist; it can be revisited later if manual-sync drift actually occurs in practice.

### 11.3 Serialization Conventions (must be agreed before implementation)

- **Property naming — CONFIRMED:** properties serialize **as declared, PascalCase** (e.g. `RightAscension`), matching `TelescopeStateModel` exactly. The server adds no `PropertyNamingPolicy` override (`Program.cs`'s `AddSignalR()` call remains as-is on this point); the client deserializes against PascalCase property names.
- **Enum representation — CONFIRMED:** the server registers `JsonStringEnumConverter` for the hub's JSON protocol (`AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))`), so all five enums in the payload (`PointingState`, `DriveRate`, `SlewType`, `MountType`, `AlignmentMode`) serialize as their **string member names**, not integer ordinals. **The client must register the inverse `JsonStringEnumConverter` (or equivalent) in its own deserialization options**, so string values are parsed back into its neutral mirror enums (`TelescopePierSide`, `GreenSwampDriveRate`, `GreenSwampSlewType`, `GreenSwampMountType`, `GreenSwampAlignmentMode` — see §11.4.1). Without the client-side converter, deserialization of these five properties will fail against a `TelescopeStateModel`-shaped payload.
- **Arrays/collections:** `double[]`, `long[]`, `string[]`, `List<string>` all serialize as standard JSON arrays — no special handling needed.
- **`Vector` (`TrackingOffsetRate`):** a struct with `X`/`Y` `double` properties — serializes as a JSON object `{ "X": ..., "Y": ... }`. The client's `GreenSwampTelescopeState.TrackingOffsetRate` already models this as a `(double X, double Y)` tuple, which is a compatible shape for manual deserialization.
- **Nullability:** `ParkSelectedName` (`string?`) serializes as JSON `null` when unset — the client's mirror (`string? ParkSelectedName`) already matches.

### 11.4 Field Contract Table (evidence-based, `TelescopeStateModel.cs` ↔ client `Abstractions` types)

This table is the authoritative field-by-field mapping. It must be updated whenever either side's shape changes — see §11.6.

| `TelescopeStateModel` field (server) | CLR type (server) | Client mirror | Status |
|---|---|---|---|
| `Altitude` | `double` | `TelescopeState.Altitude` | Mirrored (core) |
| `Azimuth` | `double` | `TelescopeState.Azimuth` | Mirrored (core) |
| `Declination` | `double` | `TelescopeState.Declination` | Mirrored (core) |
| `RightAscension` | `double` | `TelescopeState.RightAscension` | Mirrored (core) |
| `SideOfPier` | `ASCOM.Common.DeviceInterfaces.PointingState` | `TelescopeState.SideOfPier` (`TelescopePierSide`) | Mirrored (core, string-enum mapping) |
| `LocalHourAngle` | `double` | `GreenSwampTelescopeState.LocalHourAngle` | Mirrored |
| `UTCDate` | `DateTime` | `TelescopeState.UtcDate` | Mirrored (core) |
| `LocalDate` | `DateTime` | `GreenSwampTelescopeState.LocalDate` | Mirrored |
| `Slewing` | `bool` | `TelescopeState.Slewing` | Mirrored (core) |
| `Tracking` | `bool` | `TelescopeState.Tracking` | Mirrored (core) |
| `LimitsOn` | `bool` | `GreenSwampTelescopeState.LimitsOn` | Mirrored |
| `LimitWarningActive` | `bool` | `GreenSwampTelescopeState.LimitWarningActive` | Mirrored |
| `LimitWarningMessage` | `string` | `GreenSwampTelescopeState.LimitWarningMessage` | Mirrored |
| `LimitWarningSequence` | `long` | `GreenSwampTelescopeState.LimitWarningSequence` | Mirrored |
| `AtPark` | `bool` | `TelescopeState.AtPark` | Mirrored (core) |
| `AtHome` | `bool` | `TelescopeState.AtHome` | Mirrored (core) |
| `LimitTriggered` | `bool` | `GreenSwampTelescopeState.LimitTriggered` | Mirrored |
| `IsMountRunning` | `bool` | `GreenSwampTelescopeState.IsMountRunning` | Mirrored |
| `ComPort` | `string` | `GreenSwampTelescopeState.ComPort` | Mirrored |
| `ConnectedClientCount` | `int` | `GreenSwampTelescopeState.ConnectedClientCount` | Mirrored |
| `HasEverBeenConnected` | `bool` | `GreenSwampTelescopeState.HasEverBeenConnected` | Mirrored |
| `ParkSelectedName` | `string?` | `GreenSwampTelescopeState.ParkSelectedName` | Mirrored |
| `ParkPositionNames` | `List<string>` | `GreenSwampTelescopeState.ParkPositionNames` | Mirrored |
| `TargetRightAscension` | `double` | `GreenSwampTelescopeState.TargetRightAscension` (new) | **Confirmed for addition** |
| `TargetDeclination` | `double` | `GreenSwampTelescopeState.TargetDeclination` (new) | **Confirmed for addition** |
| `ActualAxisX` / `ActualAxisY` | `double` | `GreenSwampTelescopeState.ActualAxisX/Y` | Mirrored |
| `AppAxisX` / `AppAxisY` | `double` | `GreenSwampTelescopeState.AppAxisX/Y` | Mirrored |
| `AxisSteps` | `double[2]` | `GreenSwampTelescopeState.AxisSteps` (`IReadOnlyList<double>`) | Mirrored |
| `TrackingRate` | `ASCOM.Common.DeviceInterfaces.DriveRate` | `GreenSwampTelescopeState.TrackingRate` (`GreenSwampDriveRate`, new — see §11.4.1) | **Confirmed for addition** (string-enum mapping) |
| `IsPulseGuidingRa` / `IsPulseGuidingDec` | `bool` | `GreenSwampTelescopeState.IsPulseGuidingRa/Dec` | Mirrored |
| `SlewState` | `GreenSwamp.Alpaca.MountControl.SlewType` | `GreenSwampTelescopeState.SlewState` (`GreenSwampSlewType`) | Mirrored (string-enum mapping) |
| `LoopCounter` | `ulong` | `GreenSwampTelescopeState.LoopCounter` | Mirrored |
| `TimerOverruns` | `int` | `GreenSwampTelescopeState.TimerOverruns` | Mirrored |
| `LastUpdate` | `DateTime` | `GreenSwampTelescopeState.LastUpdate` | Mirrored |
| `FlipOnNextGoto` | `bool` | `GreenSwampTelescopeState.FlipOnNextGoto` | Mirrored |
| `ControllerVoltage` | `double` | `GreenSwampTelescopeState.ControllerVoltage` | Mirrored |
| `LowVoltageEvent` | `bool` | `GreenSwampTelescopeState.LowVoltageEvent` | Mirrored |
| `VoiceActive` | `bool` | `GreenSwampTelescopeState.VoiceActive` | Mirrored |
| `VoiceName` | `string` | `GreenSwampTelescopeState.VoiceName` | Mirrored |
| `VoiceVolume` | `int` | `GreenSwampTelescopeState.VoiceVolume` | Mirrored |
| `IsAutoHomeRunning` | `bool` | `GreenSwampTelescopeState.IsAutoHomeRunning` | Mirrored |
| `AutoHomeProgressBar` | `int` | `GreenSwampTelescopeState.AutoHomeProgressBar` | Mirrored |
| `IsGermanPolarMode` | `bool` | `GreenSwampTelescopeState.IsGermanPolarMode` | Mirrored |
| `AutoHomeAxisX` / `AutoHomeAxisY` | `double` | `GreenSwampTelescopeState.AutoHomeAxisX/Y` | Mirrored |
| `StepsPerRevolution` | `long[2]` | `GreenSwampTelescopeState.StepsPerRevolution` (`IReadOnlyList<long>`) | Mirrored |
| `StepsWormPerRevolution` | `double[2]` | `GreenSwampTelescopeState.StepsWormPerRevolution` (`IReadOnlyList<double>`) | Mirrored |
| `StepsTimeFreq` | `long[2]` | `GreenSwampTelescopeState.StepsTimeFreq` (`IReadOnlyList<long>`) | Mirrored |
| `TrackingOffsetRate` | `GreenSwamp.Alpaca.Shared.Vector` (`{X,Y}`) | `GreenSwampTelescopeState.TrackingOffsetRate` (`(double X, double Y)`) | Mirrored |
| `CanPPec` | `bool` | `GreenSwampTelescopeState.CanPPec` | Mirrored |
| `CanHomeSensor` | `bool` | `GreenSwampTelescopeState.CanHomeSensor` | Mirrored |
| `CanPolarLed` | `bool` | `GreenSwampTelescopeState.CanPolarLed` | Mirrored |
| `CanAdvancedCmdSupport` | `bool` | `GreenSwampTelescopeState.CanAdvancedCmdSupport` | Mirrored |
| `MountName` | `string` | `GreenSwampTelescopeState.MountName` | Mirrored |
| `MountVersion` | `string[2]` | `GreenSwampTelescopeState.MountVersion` (`IReadOnlyList<string>`) | Mirrored |
| `Capabilities` | `string` | `GreenSwampTelescopeState.Capabilities` | Mirrored |
| `SiteLatitude` | `double` | `GreenSwampTelescopeState.SiteLatitude` (new) | **Confirmed for addition** |
| `AlignmentMode` | `ASCOM.Common.DeviceInterfaces.AlignmentMode` | `GreenSwampTelescopeState.AlignmentMode` (`GreenSwampAlignmentMode`, new — see §11.4.1) | **Confirmed for addition** (string-enum mapping) |
| `MountType` | `GreenSwamp.Alpaca.MountControl.MountType` (namespace confirmed — see §11.7) | `GreenSwampTelescopeState.MountType` (`GreenSwampMountType`) | Mirrored (string-enum mapping) |

### 11.4.1 New Client-Side Neutral Enums (required for `TrackingRate` and `AlignmentMode`)

Two new neutral mirror enums are confirmed for addition to `GreenSwamp.Alpaca.Telescope.Abstractions` (same file/pattern as the existing `GreenSwampSlewType`/`GreenSwampMountType` in `GreenSwampTelescopeState.cs`). Each is a direct 1:1 mirror of the corresponding ASCOM enum (member names and values as supplied), `GreenSwamp`-prefixed to avoid any future name clash with a plain `AlignmentMode`/`DriveRate` type elsewhere in the client codebase:

```csharp
/// <summary>Neutral mirror of ASCOM.Common.DeviceInterfaces.AlignmentMode.</summary>
public enum GreenSwampAlignmentMode
{
    AltAz = 0,
    Polar = 1,
    GermanPolar = 2
}

/// <summary>Neutral mirror of ASCOM.Common.DeviceInterfaces.DriveRate.</summary>
public enum GreenSwampDriveRate
{
    Sidereal = 0,
    Lunar = 1,
    Solar = 2,
    King = 3
}
```

Both are 1:1 mirrors — no value remapping, consistent with the `TelescopePierSide` precedent (§11.4). Because the wire format is string-based (§11.3), the integer values above are for documentation/parity only; matching is by **member name**.

### 11.5 Fields Confirmed for Addition (formerly "Gaps Identified by This Table")

Five `TelescopeStateModel` fields had no client-side mirror when this table was first drafted. All five are now **confirmed for addition** to `GreenSwampTelescopeState` (server-side, `TelescopeStateService.BuildSnapshot` already populates all five today — confirmed by inspection, so there is no server-side data gap, only a client-side mirror gap):

- `TargetRightAscension` (`double`)
- `TargetDeclination` (`double`)
- `TrackingRate` (`ASCOM.Common.DeviceInterfaces.DriveRate` → client `GreenSwampDriveRate`, §11.4.1)
- `SiteLatitude` (`double`)
- `AlignmentMode` (`ASCOM.Common.DeviceInterfaces.AlignmentMode` → client `GreenSwampAlignmentMode`, §11.4.1)

The §11.4 table is the authoritative source for these five rows; this subsection is a summary record, not an additional task list.

### 11.6 Change-Control Process

To keep the two independently-versioned repositories from drifting silently:

1. Any change to a field, type, or enum member covered by §11.4 (add, remove, rename, retype) **must** update this table in the same change, on the server side.
2. The client-side `GreenSwampTelescopeState`/`TelescopeState`/enum mirrors are updated as a follow-on change, referencing the updated table — the same discipline `GreenSwampTelescopeState.cs`'s existing comments already describe informally ("a direct, evidence-based mirror... no fields are invented").
3. Enum member changes are the highest-risk category (§11.3) — any insertion, removal, or reordering of an enum member in `PointingState`, `DriveRate`, `SlewType`, `MountType`, or `AlignmentMode` must be called out explicitly in the commit/PR description referencing this table, even though `JsonStringEnumConverter` (§11.3) removes the ordinal-drift risk for *values already agreed*; a genuinely new member still requires the client's mirror enum to be extended to match.

### 11.7 Resolution Log (formerly "Open Items Introduced by This Section")

7. **`MountType` source namespace — RESOLVED.** `TelescopeStateModel.MountType` (and the value it is assigned from, `mount.Settings.Mount` in `TelescopeStateService.BuildSnapshot`) binds to `GreenSwamp.Alpaca.MountControl.MountType` (`Enums.cs`: `Simulator`, `SkyWatcher`) — **not** `GreenSwamp.Alpaca.Settings.Models.MountType` (`AlignmentMode.cs`). The two duplicate enum definitions remain in the server codebase; consolidating them is a separate, pre-existing concern outside this specification's scope, noted here only because §11.4's table needed to cite the correct one.
8. **Enum wire format — RESOLVED.** `JsonStringEnumConverter` is confirmed for the server's SignalR JSON protocol (§11.3); the client registers the inverse converter.
9. **Five-field client-side gap — RESOLVED.** All five fields are confirmed for addition to `GreenSwampTelescopeState` (§11.5), including two new neutral enums, `GreenSwampAlignmentMode` and `GreenSwampDriveRate` (§11.4.1).

With §9 and all items in this section resolved, this specification is ready to drive parallel client-side (`GreenSwampAlpacaClient`) and server-side (`GreenSwampAlpacaServer`) implementation.

