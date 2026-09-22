# GreenSwampAlpacaServer — TelescopeState SignalR Broadcast: Capability Specification

**Status:** Draft specification for offline review. This is a **server-side** (`GreenSwampAlpacaServer`) capability specification, written to unblock the client-side (`GreenSwampAlpacaClient`) `GreenSwampSignalRProvider` design (architecture §6.4), per the stakeholder-agreed sequencing: **specify/design → client design + server implementation in parallel**, not full implementation first.

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

## 9. Open Items Requiring Your Confirmation

1. **New hub vs. extending `ChartHub`** (§5) — recommendation given; please confirm or redirect.
2. **DTO strategy** (§6) — shared explicit DTO record vs. loosely-typed JSON matching `TelescopeStateModel` shape; recommendation given (shared DTO), please confirm.
3. **View-registry keep-alive interval** (§7.2) — 5 seconds recommended (comfortably inside the existing 10-second staleness window); confirm or adjust.
4. **No historical replay** (§7.5) — confirm this is acceptable, or whether a "send current cached snapshot immediately on join" behavior (a single immediate send, not a time series) is wanted so subscribers aren't left waiting up to 250ms for their first value.
5. **Authentication/trust model** (§8) — confirm no auth is required, consistent with the existing `ChartHub`/Alpaca REST trust model.
6. **Full-model vs. narrower DTO** (§6) — confirm broadcasting the complete `TelescopeStateModel` shape is desired, rather than a curated subset limited to properties the client's implementation-design property table currently marks as "expose now."

## 10. Process Note

This is a specification for **server-side** work in `GreenSwampAlpacaServer`. Once you confirm §9, the recommended next steps run in parallel: (a) implement this hub/service in `GreenSwampAlpacaServer` against this agreed contract, and (b) continue the client's implementation design (`telescope-implementation-design.md`) referencing this same contract for the GreenSwamp-class SignalR-sourced properties in its property table, replacing "not currently exposed" markers with "available via `TelescopeStateHub`" for every field this capability covers.
