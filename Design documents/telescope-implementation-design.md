# Telescope Client — Implementation Design Document

**Status:** Approved and implemented (first slice). Revision 2 (property tables §3–5, the four in-scope UI action definitions §6, the neutral pier-side enum, and the concrete `GreenSwampTelescopeState` extension type) was approved and has been built end-to-end: `Connect`/`FindHome`/`Park`/`Abort` work against both a fake session (Level 1/2 unit tests) and the real GreenSwampAlpacaServer reference simulator (Level 3 integration test). See new §10 for the as-built implementation status, confirmed deviations from this document, and open items carried into the next phase. §7 (deferred command analysis) remains a placeholder and is the most likely next design-stage piece of work.

**Get Well Guidance.md Stage 2 note:** §5.4's reconciliation-notes row for `TargetRightAscension`/`TargetDeclination`/`TrackingRate`/`SiteLatitude`/`AlignmentMode` has been superseded by Stage 2 of `Get Well Guidance.md` — see the updated §5.4 table below and `SignalR-transport-implementation-plan.md` Decision B/D (RESOLVED) for the confirmed per-class sourcing model. This document's other sections (§3, §4, §6) are unaffected by Stage 2 and have not been touched.

**Get Well Guidance.md Stage 3 note:** §10.2 item 2's "no continuous state-polling loop exists yet" gap and §10.3's corresponding open guidance are now **RESOLVED** by Stage 3 of `Get Well Guidance.md` — see the updated notes inline in §10.2/§10.3 below, and `SignalR-transport-implementation-plan.md` Decision C (RESOLVED), for the confirmed poll-loop cadences (250ms SignalR / 1 second `DeviceState`). This document's other sections are unaffected by Stage 3 and have not been touched.

**Get Well Guidance.md Stage 4 note:** §10.2 item 2's and §10.3's remaining "internal timer composition and thread-safety" open work is now **RESOLVED** by Stage 4 of `Get Well Guidance.md` — see the updated notes inline in §10.2/§10.3 below, and `SignalR-transport-implementation-plan.md` Decision C/D (Stage 4, RESOLVED), which confirm that resolving Telescope State (Stage 2) and Poll Loop (Stage 3) removes all remaining back-pressure/reconciliation concerns. This document's other sections are unaffected by Stage 4 and have not been touched.

**End-of-stages clean-up note:** §5.4's two remaining open rows (`SiteLongitude`/`SiteElevation` field-coverage gap; `TrackingRate` placement discrepancy) are now **RESOLVED** at the specification level — see the updated §3.6/§3.12/§5.4 below and `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5/§11.7 and `SignalR-transport-implementation-plan.md` Finding A2/A11 (RESOLVED) for the confirmed field contract. `TrackingRate` mirrors onto the neutral `TelescopeState` alongside `SiteLatitude`/`SiteLongitude`/`SiteElevation`/`AlignmentMode`, and all four join the same 30-second AP-fallback cadence. The server-side field additions/fixes (`TelescopeStateModel`/`TelescopeStateService.BuildSnapshot`) and the client-side `TelescopeState` property additions have not yet been implemented — this document's tables now serve as part of the interface control specification for that work, alongside the SignalR spec.

## 1. Purpose and Traceability

This document is the fourth artifact in the sequence:
1. `telescope-requirements.md` (approved)
2. `telescope-test-strategy.md` (approved)
3. `telescope-architecture.md` (approved, with revisions)
4. **`telescope-implementation-design.md` (this document)** — takes the approved architecture's `ITelescopeSession`/`ITelescopeSessionFactory` abstraction (architecture §6.1–6.2) and refines it into concrete, class-level functional and interface definitions: exactly which properties the ViewModel exposes, exactly which UI-facing actions it supports, and how each is sourced from (a) a GreenSwamp-class telescope and (b) a standard Alpaca-only telescope.

It is a design **refinement** step, not a re-derivation — it stays consistent with, and traceable to, the requirements (FR1-65) and architecture documents, and does not reopen decisions already made there (transport strategy, session lifetime, event-based updates, jog v1 scope, discovery, FR14 detection trigger). Every property/action row below is traceable to requirements §6.6 (FR31-36, state/capability/position reporting) and architecture §6.2 (`ITelescopeSession` contract), §6.4 (GreenSwamp SignalR provider), and §6.8 (GreenSwamp-class detection).

This revision also incorporates the now-**approved** `greenswamp-telescopestate-signalr-spec.md` (server-side `TelescopeStateHub` specification), which resolves most of the previous "not currently exposed" markers into "planned via `TelescopeStateHub`" — see §2 and §3 for how this is reflected.

**Naming convention used throughout this revision** (per your correction): **`AtHome`/`AtPark` are properties**; **`FindHome`/`Park` are commands**. The document no longer uses "Home" as a command/action name anywhere — the action is always called **FindHome**.

## 2. Scope of This Iteration

**In scope for this pass:**
- A property taxonomy and sourcing methodology (§2a) — the legend/column structure used to classify every property.
- The full **property table**: every property in GreenSwamp's `TelescopeStateModel` class (§3), and every standard ASCOM `ITelescopeV3`/`ITelescopeV4` property — both the 11-field `DeviceState` snapshot and the wider individually-gettable interface (§4) — each marked with its remote-availability status per device class.
- Cross-class reconciliation (§5), including a concrete neutral pier-side enum and a concrete `GreenSwampTelescopeState` extension type.
- The **initial UI action set**: `Connect`, `FindHome`, `Park`, `Abort` only (§6) — their preconditions, capability gating, GreenSwamp-specific business-logic findings, and mapping onto `ITelescopeSession` members.

**Explicitly out of scope for this pass** (deferred to a later revision of this same document):
- The full UI command surface (slew-to-coordinates, sync, tracking on/off, tracking rate selection, jog/manual-control, set-park, guide-rate configuration, etc.) — placeholder only, §7.
- Settings/connection-descriptor design (already deferred by the requirements document).
- Any UI/visual design (prototype UI is deferred to a later stage per stakeholder direction).
- Full hand-controller-parity jog design (already deferred to a future architecture pass — architecture §6.7).
- Discovery UI/UX detail (the discovery *service* is architected; how the ViewModel/View surfaces discovered devices is a later design pass).

## 2a. Property Taxonomy and Sourcing Methodology

Every property row in §3–5 uses this legend:

| Column | Meaning |
|---|---|
| **Property** | ViewModel-facing name (kept close to its source name unless noted). |
| **Type** | .NET type as it will reach the ViewModel. |
| **Concept source** | The originating member: `TelescopeStateModel` field, or standard `ITelescopeV3`/`ITelescopeV4` member. |
| **Availability today** | One of the codes below — what a client can retrieve **right now**, without any server change. |
| **Availability once `TelescopeStateHub` ships** | For GreenSwamp-class only — reflects the now-approved SignalR spec's decision to broadcast the **full** `TelescopeStateModel` (spec §6). |
| **ViewModel exposure decision** | Expose now / expose but capability-gate / defer. |
| **Notes** | Units, nullability, GreenSwamp-specific caveats, known gaps. |

**Availability codes:**
- **DS** — Bundled in the standard Alpaca `DeviceState` snapshot (one REST call returns all 11 DS fields together).
- **AP** — Available today via an individual standard Alpaca REST property (separate GET, and PUT where settable) — not bundled in `DeviceState`, costs its own round-trip.
- **CH** — Available today via the existing `ChartHub` SignalR (raw axis-step / pulse-guide points only — narrow, chart-specific, not a general state broadcast).
- **SR (planned)** — Not available today by any transport; will become available once `GreenSwampAlpacaServer` implements the approved `TelescopeStateHub` specification (full-model broadcast, per that document's §6 decision). This column only applies to GreenSwamp-class devices.
- **NX** — Not currently exposed by any transport today (used in the "Availability today" column for fields that will become `SR (planned)` once the hub ships).
- **N/C** — No standard-Alpaca concept exists for this at all (used only in §4/§5 for GreenSwamp-specific concepts that have no counterpart in `ITelescopeV3`/`ITelescopeV4`).

## 3. GreenSwamp-Class Telescope: Supported ViewModel Properties

Source: `T:\source\repos\GreenSwampAlpacaServer\GreenSwamp.Alpaca.Server\Models\TelescopeStateModel.cs` (confirmed by direct inspection), cross-referenced against `T:\source\repos\GreenSwampAlpacaServer\GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs` (which standard Alpaca properties GreenSwamp actually implements) and `greenswamp-telescopestate-signalr-spec.md` (confirmed: full-model broadcast once implemented).

**Key finding restated:** because the approved SignalR spec broadcasts the complete `TelescopeStateModel` unconditionally, **every row below reaches at least "SR (planned)"** — there are no permanently-unreachable GreenSwamp-class properties once that hub ships. The "Availability today" column is what matters until then.

**Confirmed decision (your item 6): every property below is marked "Expose" — none are omitted.** Rows previously marked "Defer" in revision 1 are relabelled "Expose (UI use TBD)" to make clear that non-exposure was never intended; only the property's role in the UI is undetermined, not whether the ViewModel surfaces it. Rows that are genuinely internal bookkeeping the ViewModel itself doesn't need (e.g. `LimitWarningSequence`, a server-side de-dup counter) are still exposed on the model per this decision, simply with a note that the ViewModel/View may choose not to bind to them.

### 3.1 Core Positioning

| Property | Type | Concept source | Availability today | Once `TelescopeStateHub` ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| Altitude | double | `Altitude` | DS | SR | Expose | Degrees. Also standard `ITelescopeV3.Altitude` (AP), redundant with DS. |
| Azimuth | double | `Azimuth` | DS | SR | Expose | Degrees, 0-360. |
| Declination | double | `Declination` | DS | SR | Expose | Degrees. |
| RightAscension | double | `RightAscension` | DS | SR | Expose | Hours. |
| SideOfPier | `TelescopePierSide` (app-level enum; see §5) | `SideOfPier` (`PointingState`) | DS | SR | Expose | See §5 — mapped 1:1 to a neutral app-level enum for protocol insulation, not because of any type mismatch (there is none; see §5 correction). |
| LocalHourAngle | double | `LocalHourAngle` | NX | SR (planned) | Expose (UI use TBD) | No standard Alpaca property; derivable client-side from RA/SiderealTime if ever needed instead. |
| UTCDate | DateTime | `UTCDate` | DS, AP (gettable; set throws `PropertyNotImplementedException` per GreenSwamp's `Telescope.cs`) | SR | Expose | AP setter path confirmed non-functional on GreenSwamp — do not offer a "set UTC date" UI action against GreenSwamp. |
| LocalDate | DateTime | `LocalDate` | NX | SR (planned) | Expose (UI use TBD) | No standard Alpaca equivalent; derivable client-side from UTCDate + site longitude if needed. |

### 3.2 Mount / Motion State

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| Slewing | bool | `Slewing` | DS | SR | Expose | |
| Tracking | bool | `Tracking` | DS, AP (settable) | SR | Expose | GreenSwamp's getter is `_mount.Tracking \|\| SlewState == SlewRaDec` — slightly richer than the raw field; see §6 FindHome/Park notes on tracking side-effects. |
| AtPark | bool | `AtPark` | DS | SR | Expose | **Property** — see §6 for the `Park` **command**. |
| AtHome | bool | `AtHome` | DS | SR | Expose | **Property** — see §6 for the `FindHome` **command**. |
| LimitTriggered | bool | `LimitTriggered` | NX | SR (planned) | Expose (safety-relevant) | No standard Alpaca equivalent. |
| LimitsOn | bool | `LimitsOn` | NX | SR (planned) | Expose | GreenSwamp soft-limit feature; no standard equivalent. |
| LimitWarningActive | bool | `LimitWarningActive` | NX | SR (planned) | Expose (safety-relevant) | |
| LimitWarningMessage | string | `LimitWarningMessage` | NX | SR (planned) | Expose | |
| LimitWarningSequence | long | `LimitWarningSequence` | NX | SR (planned) | Expose (UI use TBD) | Internal de-dup counter (detects a *new* warning vs. a repeat); ViewModel may bind only to the boolean + message and ignore this, but it is not withheld from the model. |
| SlewState | `SlewType` enum | `SlewState` | NX | SR (planned) | Expose (UI use TBD) | Finer-grained than standard `Slewing` bool (distinguishes home/park/RA-Dec/Alt-Az slew *kind*); potentially useful for status text. |
| FlipOnNextGoto | bool | `FlipOnNextGoto` | NX | SR (planned) | Expose (UI use TBD) | Pier-flip-on-next-slew indicator; relevant to a future slew-command UI (§7), not Connect/FindHome/Park/Abort. |
| IsMountRunning | bool | `IsMountRunning` | NX | SR (planned) | Expose | Server/mount-process-level "is the control loop alive" flag, distinct from Alpaca `Connected`. |
| ComPort | string | `ComPort` | NX | SR (planned) | Expose (UI use TBD) | |
| ConnectedClientCount | int | `ConnectedClientCount` | NX | SR (planned) | Expose (useful multi-client awareness) | Relevant since GreenSwamp allows multiple simultaneous Alpaca clients per §6 Connect notes. |
| HasEverBeenConnected | bool | `HasEverBeenConnected` | NX | SR (planned) | Expose (UI use TBD) | |

### 3.3 Park Positions

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| ParkSelectedName | string? | `ParkSelectedName` | NX | SR (planned) | Expose — **informational only for `Park` in this pass**, see §6 | GreenSwamp-specific "named park position" concept; no standard-Alpaca equivalent (N/C in §4/§5). Per your decision (items 3/4), this is **not** used to pre-validate/gate the `Park` command client-side. |
| ParkPositionNames | `List<string>` | `ParkPositionNames` | NX | SR (planned) | Expose (needed for a future "select park position" UI, §7) | |

### 3.4 Target

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| TargetRightAscension | double | `TargetRightAscension` | AP (standard `ITelescopeV3.TargetRightAscension`, gated by `CanSlew` per GreenSwamp's `Telescope.cs`) | SR | Expose (needed for §7 slew-to-target, not the four in-scope actions) | |
| TargetDeclination | double | `TargetDeclination` | AP (same gating) | SR | Expose (UI use TBD) | |

### 3.5 Axis Positions (Mount-Specific)

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| ActualAxisX / ActualAxisY | double | `ActualAxisX/Y` | NX | SR (planned) | Expose (UI use TBD) | Raw mount-axis positions (degrees), not sky coordinates. |
| AppAxisX / AppAxisY | double | `AppAxisX/Y` | NX | SR (planned) | Expose (UI use TBD) | |
| AxisSteps | double[2] | `AxisSteps` | **CH** (existing `ChartHub`, `ReceiveAxisPoint`, chart-only) | SR (planned, full snapshot; distinct from `ChartHub`'s narrow chart stream) | Expose (chart/diagnostic use) | The one field with an *existing* SignalR path today, but purely for charting, not general state (architecture §6.4). |

### 3.6 Rates and Guiding

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| TrackingRate | `DriveRate` enum | `TrackingRate` | AP (standard `ITelescopeV3.TrackingRate`, settable) | SR | Expose (needed for §7 tracking-rate UI) | Client mirror resolved to neutral `TelescopeState` (not `GreenSwampTelescopeState`), joining the 30s AP-fallback cadence with `SiteLatitude`/`SiteLongitude`/`SiteElevation`/`AlignmentMode` — see §5.4 and `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5. Once the hub ships, the server must read the mount's live tracking rate in `TelescopeStateService.BuildSnapshot` rather than the current hardcoded `DriveRate.Sidereal` (spec §11.7 item 11). |
| IsPulseGuidingRa | bool | `IsPulseGuidingRa` | NX (only the combined standard `IsPulseGuiding` bool is DS/AP-available) | SR (planned) | Expose (UI use TBD) | GreenSwamp splits per-axis; standard Alpaca has one combined flag. |
| IsPulseGuidingDec | bool | `IsPulseGuidingDec` | NX | SR (planned) | Expose (UI use TBD) | |

### 3.7 Performance / Diagnostics

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| LoopCounter | ulong | `LoopCounter` | NX | SR (planned) | Expose (UI use TBD) | Diagnostic; likely a future "advanced/diagnostics" panel, not the primary control surface. |
| TimerOverruns | int | `TimerOverruns` | NX | SR (planned) | Expose (UI use TBD) | Same as above. |
| LastUpdate | DateTime | `LastUpdate` | NX (conceptually mirrored by `DeviceState`'s own `TimeStamp`, but that's the *DS response* timestamp, not this field) | SR (planned) | Expose (UI use TBD) | Internal freshness bookkeeping, likely consumed by `TelescopeSession` reconciliation logic rather than bound directly by the View, but still exposed on the model per item 6. |

### 3.8 SkyWatcher-Specific Telemetry

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| ControllerVoltage | double | `ControllerVoltage` | NX | SR (planned) | Expose (useful health indicator) | Hardware-specific; `double.NaN` on simulator per constructor default. |
| LowVoltageEvent | bool | `LowVoltageEvent` | NX | SR (planned) | Expose (safety-relevant) | |
| VoiceActive | bool | `VoiceActive` | NX | SR (planned) | Expose (UI use TBD) | Accessibility feature. |
| VoiceName | string | `VoiceName` | NX | SR (planned) | Expose (UI use TBD) | |
| VoiceVolume | int | `VoiceVolume` | NX | SR (planned) | Expose (UI use TBD) | |

### 3.9 AutoHome

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| IsAutoHomeRunning | bool | `IsAutoHomeRunning` | NX | SR (planned) | Expose (relevant to `FindHome` action status, §6) | GreenSwamp has a distinct "AutoHome" routine beyond standard `FindHome` — see §6 FindHome notes; not conflated with the in-scope `FindHome`-mapped command in this pass. |
| AutoHomeProgressBar | int | `AutoHomeProgressBar` | NX | SR (planned) | Expose (progress UI) | |
| AutoHomeAxisX / AutoHomeAxisY | double | `AutoHomeAxisX/Y` | NX | SR (planned) | Expose (UI use TBD) | |
| IsGermanPolarMode | bool | `IsGermanPolarMode` | NX | SR (planned) | Expose (UI use TBD) | Derived/simplified flag; `AlignmentMode` (below) is the standard source of truth. |

### 3.10 Mount Gearing Details

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| StepsPerRevolution | long[2] | `StepsPerRevolution` | NX | SR (planned) | Expose (UI use TBD) | Per architecture §6.4, this is exactly the gearing data a *future* axis-step-to-sky-coordinate conversion phase would need. |
| StepsWormPerRevolution | double[2] | `StepsWormPerRevolution` | NX | SR (planned) | Expose (UI use TBD) | Same as above. |
| StepsTimeFreq | long[2] | `StepsTimeFreq` | NX | SR (planned) | Expose (UI use TBD) | Same as above. |
| TrackingOffsetRate | `Vector` (`GreenSwamp.Alpaca.Shared.Vector`: `{ double X, double Y }`) | `TrackingOffsetRate` | NX | SR (planned) | Expose (UI use TBD) | |

### 3.11 Mount Capabilities (GreenSwamp-Specific)

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| CanPPec | bool | `CanPPec` | NX | SR (planned) | Expose (UI use TBD) | No standard `Can*` equivalent (periodic-error-correction is not an ASCOM concept). |
| CanHomeSensor | bool | `CanHomeSensor` | NX | SR (planned) | Expose (UI use TBD) | Distinct from standard `CanFindHome` (that's about the `FindHome` *method* being supported; this is about physical home-sensor hardware presence). |
| CanPolarLed | bool | `CanPolarLed` | NX | SR (planned) | Expose (UI use TBD) | |
| CanAdvancedCmdSupport | bool | `CanAdvancedCmdSupport` | NX | SR (planned) | Expose (UI use TBD) | |
| MountName | string | `MountName` | NX | SR (planned) | Expose (identification/diagnostics) | |
| MountVersion | string[2] | `MountVersion` | NX | SR (planned) | Expose (UI use TBD) | |
| Capabilities | string | `Capabilities` | NX | SR (planned) | Expose (UI use TBD) | Free-text capability summary; likely superseded by structured `TelescopeCapabilities`/`GreenSwampTelescopeState` (§5) rather than parsed. |

### 3.12 Site / Alignment (Read Largely Once)

| Property | Type | Concept source | Availability today | Once hub ships | Exposure decision | Notes |
|---|---|---|---|---|---|---|
| SiteLatitude | double | `SiteLatitude` | AP (standard `ITelescopeV3.SiteLatitude`, gated by `CanLatLongElev` — GreenSwamp confirms this gate in `Telescope.cs`) | SR | Expose (not needed for Connect/FindHome/Park/Abort) | Comment in `TelescopeStateModel.cs` notes this is "read once at scene init," but the planned hub broadcasts it every tick regardless (harmless, unchanging value). |
| SiteLongitude | double | `SiteLongitude` | AP (standard `ITelescopeV3.SiteLongitude`, gated by `CanLatLongElev` — GreenSwamp confirms this gate in `Telescope.cs`) | SR (requires a server-side `TelescopeStateModel`/`BuildSnapshot` addition, following the exact `SiteLatitude` pattern — see `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5/§11.7 item 10, not yet implemented) | Expose (not needed for Connect/FindHome/Park/Abort) | Confirmed for addition to the neutral `TelescopeState` (Finding A11, RESOLVED at the specification level); see §5.4. |
| SiteElevation | double | `SiteElevation` | AP (standard `ITelescopeV3.SiteElevation`, gated by `CanLatLongElev` — GreenSwamp confirms this gate in `Telescope.cs`) | SR (requires a server-side `TelescopeStateModel`/`BuildSnapshot` addition, following the exact `SiteLatitude` pattern — see `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5/§11.7 item 10, not yet implemented) | Expose (not needed for Connect/FindHome/Park/Abort) | Confirmed for addition to the neutral `TelescopeState` (Finding A11, RESOLVED at the specification level); see §5.4. |
| AlignmentMode | `AlignmentMode` enum | `AlignmentMode` | AP (standard `ITelescopeV3.AlignmentMode`, read-only) | SR | Expose (UI use TBD) | |
| MountType | `MountType` enum | `MountType` | NX | SR (planned) | Expose (UI use TBD) | GreenSwamp/simulator-internal concept (e.g. distinguishing simulator vs. real SkyWatcher hardware); no standard Alpaca equivalent. |

## 4. Standard Alpaca-Class Telescope: Supported ViewModel Properties

Source: `ASCOM.Common.DeviceInterfaces.ITelescopeV3`/`ITelescopeV4` (full interface member list, confirmed via `ASCOM.Common.Components` 4.0.0 XML docs and reflection) and `ASCOM.Common.DeviceStateClasses.TelescopeState` (the `DeviceState` snapshot shape, confirmed by reflection). This table represents what a **generic, third-party Alpaca-only** device (no GreenSwamp SignalR) can offer the ViewModel — there is no "planned" column here, since there is no cross-repository capability to wait for; a third-party driver only ever exposes what standard Alpaca defines.

### 4.1 `DeviceState` Snapshot Fields (bundled, one REST call — DS)

| Property | Type | Notes |
|---|---|---|
| Altitude | double | |
| AtHome | bool | |
| AtPark | bool | |
| Azimuth | double | |
| Declination | double | |
| IsPulseGuiding | bool | Combined RA+Dec flag (unlike GreenSwamp's split, §3.6). |
| RightAscension | double | |
| SideOfPier | `PointingState?` (nullable) | Standard ASCOM enum — confirmed by reflection that `TelescopeState.SideOfPier` is `Nullable<PointingState>`, i.e. it may be `null` if the mount cannot report pointing state (e.g. non-German-equatorial mounts). No mapping/reconciliation is actually required (see §5 correction) — this is the same `PointingState` enum GreenSwamp uses. |
| SiderealTime | double | Not modeled as a distinct field in `TelescopeStateModel` (GreenSwamp likely computes on demand); available for any Alpaca device via DS. |
| Slewing | bool | |
| Tracking | bool | |
| UTCDate | DateTime | |
| *(TimeStamp)* | DateTime | Snapshot capture time, part of the DS response envelope, not a telescope property itself. |

### 4.2 Individually-Gettable/Settable Standard Properties (AP — separate round-trip each)

| Property | Type | Get/Set | Notes |
|---|---|---|---|
| AlignmentMode | `AlignmentMode` enum | Get | |
| ApertureArea | double | Get | Optics metadata, static. |
| ApertureDiameter | double | Get | |
| FocalLength | double | Get | |
| CanFindHome | bool | Get | Capability flag — gates `FindHome` command (§6). |
| CanPark | bool | Get | Capability flag — gates `Park` command (§6). |
| CanUnpark | bool | Get | |
| CanSetPark | bool | Get | Gates whether `SetPark()` (capturing current position as *the* park position) is available — see §5 note on GreenSwamp's richer named-park-position model having no standard equivalent. |
| CanPulseGuide | bool | Get | |
| CanSetTracking | bool | Get | |
| CanSetDeclinationRate / CanSetRightAscensionRate | bool | Get | |
| CanSetGuideRates | bool | Get | |
| CanSetPierSide | bool | Get | |
| CanSlew / CanSlewAsync / CanSlewAltAz / CanSlewAltAzAsync | bool | Get | |
| CanSync / CanSyncAltAz | bool | Get | |
| CanMoveAxis(axis) | bool | Get (method) | Per-axis; relevant to jog v1 (architecture §6.7). |
| DeclinationRate / RightAscensionRate | double | Get/Set | Gated by `CanSetDeclinationRate`/`CanSetRightAscensionRate`. |
| DoesRefraction | bool | Get/Set | |
| EquatorialSystem | `EquatorialCoordinateType` enum | Get | JNow vs. J2000 etc. — flagged as a requirements-level risk (requirements §11, coordinate-frame consistency). |
| GuideRateDeclination / GuideRateRightAscension | double | Get/Set | |
| SiteLatitude / SiteLongitude / SiteElevation | double | Get/Set | |
| SlewSettleTime | double | Get/Set | |
| TargetDeclination / TargetRightAscension | double | Get/Set | Required before `SlewToTarget`/`SlewToTargetAsync` (§7). |
| TrackingRate | `DriveRate` enum | Get/Set | |
| TrackingRates | `ITrackingRates` (collection) | Get | Enumerable of supported `DriveRate` values. |

### 4.3 GreenSwamp-Specific Concepts With No Standard Equivalent (N/C)

For completeness/traceability, these GreenSwamp §3 concepts have **no** standard-Alpaca counterpart at all, not even an individually-gettable property — a generic Alpaca-only device simply has no way to expose them: `ParkSelectedName`/`ParkPositionNames` (named park positions — standard Alpaca's park model is a single implicit position via `SetPark()`), `LimitsOn`/`LimitWarning*`/`LimitTriggered` (soft-limit safety feature), `IsAutoHomeRunning`/`AutoHomeProgressBar`/`AutoHomeAxisX/Y` (AutoHome routine, distinct from `FindHome`), `ControllerVoltage`/`LowVoltageEvent`/`Voice*` (SkyWatcher hardware telemetry), `StepsPerRevolution`/`StepsWormPerRevolution`/`StepsTimeFreq`/`TrackingOffsetRate` (mount gearing), `CanPPec`/`CanHomeSensor`/`CanPolarLed`/`CanAdvancedCmdSupport`/`MountName`/`MountVersion`/`Capabilities`/`MountType` (GreenSwamp capability/identity model), `IsMountRunning`/`ComPort`/`ConnectedClientCount`/`HasEverBeenConnected`/`LoopCounter`/`TimerOverruns`/`FlipOnNextGoto`/`SlewState`/`ActualAxisX/Y`/`AppAxisX/Y`/`LocalHourAngle`/`LocalDate`/`IsGermanPolarMode` (mount-internal diagnostics/state). These are the fields carried by `GreenSwampTelescopeState` (§5.3).

## 5. Cross-Class Property Reconciliation

### 5.1 `SideOfPier` — corrected finding (supersedes revision 1)

**Revision 1 incorrectly claimed a type mismatch requiring value-by-value mapping between a GreenSwamp `PointingState` enum and a standard ASCOM `PierSide` enum. This is wrong and is corrected here:**

- Confirmed by reflection against `ASCOM.Common.Components` 4.0.0 (`net10.0`): `ITelescopeV3.SideOfPier` is typed `ASCOM.Common.DeviceInterfaces.PointingState`, and `TelescopeState.SideOfPier` (the `DeviceState` snapshot field) is typed `PointingState?` (nullable). **There is no separate `PierSide` type in the current ASCOM.Common library** — the XML docs state explicitly that `PointingState` "is called `PierSide` in traditional Platform code," i.e. `PierSide` is a legacy/COM-era **name** for the same concept, not a coexisting type in the modern library.
- Confirmed by grep across the entire GreenSwampAlpacaServer solution (`Axes.cs`, `Mount.cs`, `Mount.HandController.cs`, `Mount.Tracking.cs`, `Mount.Lifecycle.cs`, `TelescopeDriver/Telescope.cs`): every usage of side-of-pier is `ASCOM.Common.DeviceInterfaces.PointingState` directly (`Normal`/`ThroughThePole`/`Unknown`). GreenSwamp does not define or use its own `PierSide`/pier-side type.
- Enum values confirmed by reflection: `Normal = 0` (≡ legacy `pierEast`), `ThroughThePole = 1` (≡ legacy `pierWest`), `Unknown = -1` (≡ legacy `pierUnknown`).
- **Conclusion:** GreenSwamp-class and standard Alpaca-class `SideOfPier` are **the same underlying type today** — there is no mismatch to reconcile. However, per your decision on item 1 — **"follow the `TelescopeAxis` pattern"** — a neutral app-level enum is still defined below, for the same **protocol-insulation** reason `TelescopeAxis` exists (architecture §6.2's `Abstractions` layer must never reference `ASCOM.Common` directly), not to bridge an actual type difference.

**Concrete definition (in `Abstractions`, mirroring `TelescopeAxis`):**

```csharp
namespace GreenSwampAlpacaClient.Abstractions;

/// <summary>
/// Neutral, protocol-agnostic mirror of ASCOM.Common.DeviceInterfaces.PointingState.
/// Defined here (not referenced from ASCOM.Common) so the ViewModel/Abstractions layer
/// never takes a compile-time dependency on the ASCOM client library, consistent with
/// the TelescopeAxis insulation pattern (architecture §6.2). Values and meanings are a
/// direct 1:1 mirror — no value remapping occurs.
/// </summary>
public enum TelescopePierSide
{
	Normal,          // ASCOM PointingState.Normal (legacy pierEast)
	ThroughThePole,  // ASCOM PointingState.ThroughThePole (legacy pierWest)
	Unknown          // ASCOM PointingState.Unknown (legacy pierUnknown)
}
```

`AlpacaTelescopeProvider`/`GreenSwampSignalRProvider` each perform the trivial 1:1 conversion from `PointingState`/`TelescopeStateModel.SideOfPier` to `TelescopePierSide` at the provider boundary (the same boundary that already exists for `TelescopeAxis`), so `Abstractions` and the ViewModel only ever see `TelescopePierSide`. Note the standard `DeviceState.SideOfPier` field is nullable (§4.1) — the provider maps a `null` to `TelescopePierSide.Unknown` (matching GreenSwamp's own use of `Unknown` as its "don't know" value; no information is lost since `Unknown` is a real, meaningful enum member on both sides).

### 5.2 `TelescopeCapabilities` GreenSwamp-extension shape — confirmed decision

**Confirmed (your item 2): `bool IsGreenSwampClass`.** `TelescopeCapabilities` (architecture §6.2) gains a single boolean flag, populated using the same GreenSwamp-class detection logic already architected (architecture §6.8/§6.10 — device identification via the Alpaca REST API's telescope-class/description fields confirmed to read `"GreenSwampServer"`, per your item 2 correction to the architecture document). No richer capability-negotiation shape (e.g. a version number, a feature-flags bitmask) is introduced — a plain bool is sufficient because GreenSwamp-only functionality is entirely carried by the separate `GreenSwampTelescopeState` object (§5.3), not by finer-grained capability flags on `TelescopeCapabilities` itself.

```csharp
namespace GreenSwampAlpacaClient.Abstractions;

public sealed class TelescopeCapabilities
{
	// ... existing standard Can* members (CanFindHome, CanPark, CanUnpark, CanSetTracking, etc.) ...

	/// <summary>
	/// True when the connected device has been identified as a GreenSwampAlpacaServer-class
	/// telescope (architecture §6.8/§6.10 detection). When true, TelescopeSession's state stream
	/// additionally populates GreenSwampTelescopeState (§5.3); when false, that property is null.
	/// </summary>
	public bool IsGreenSwampClass { get; init; }
}
```

### 5.3 `GreenSwampTelescopeState` — concrete definition (your item 7)

A dedicated, GreenSwamp-only extension type carrying every field from §3 that has **no standard-Alpaca counterpart** (the N/C list, §4.3) — i.e. every property that would otherwise force a standard-Alpaca-only ViewModel to handle an always-absent field. Populated only when `TelescopeCapabilities.IsGreenSwampClass` is `true`; `null` otherwise. This directly answers architecture §11's open shape question: **"one bool + one nullable extension object."**

```csharp
namespace GreenSwampAlpacaClient.Abstractions;

/// <summary>
/// GreenSwampAlpacaServer-only telescope state and capability data with no standard-Alpaca
/// equivalent (implementation design §4.3). Non-null only when TelescopeCapabilities.IsGreenSwampClass
/// is true. Field set is a direct, evidence-based mirror of TelescopeStateModel's GreenSwamp-specific
/// members (see design §3 for the exposure decision behind each field) — no fields are invented.
/// </summary>
public sealed class GreenSwampTelescopeState
{
	// --- Safety / soft limits (§3.2) ---
	public bool LimitsOn { get; init; }
	public bool LimitTriggered { get; init; }
	public bool LimitWarningActive { get; init; }
	public string LimitWarningMessage { get; init; } = string.Empty;
	public long LimitWarningSequence { get; init; }

	// --- Server / connection diagnostics (§3.2) ---
	public bool IsMountRunning { get; init; }
	public string ComPort { get; init; } = string.Empty;
	public int ConnectedClientCount { get; init; }
	public bool HasEverBeenConnected { get; init; }

	// --- Named park positions (§3.3) ---
	public string? ParkSelectedName { get; init; }
	public IReadOnlyList<string> ParkPositionNames { get; init; } = [];

	// --- Mount-specific axis positions (§3.5) ---
	public double ActualAxisX { get; init; }
	public double ActualAxisY { get; init; }
	public double AppAxisX { get; init; }
	public double AppAxisY { get; init; }
	public IReadOnlyList<double> AxisSteps { get; init; } = [];

	// --- Per-axis pulse guiding (§3.6) ---
	public bool IsPulseGuidingRa { get; init; }
	public bool IsPulseGuidingDec { get; init; }

	// --- Slew kind / pier flip (§3.2) ---
	public GreenSwampSlewType SlewState { get; init; }
	public bool FlipOnNextGoto { get; init; }

	// --- Performance / diagnostics (§3.7) ---
	public ulong LoopCounter { get; init; }
	public int TimerOverruns { get; init; }
	public DateTime LastUpdate { get; init; }

	// --- Timing not standard-modeled (§3.1) ---
	public double LocalHourAngle { get; init; }
	public DateTime LocalDate { get; init; }

	// --- SkyWatcher hardware telemetry (§3.8) ---
	public double ControllerVoltage { get; init; }
	public bool LowVoltageEvent { get; init; }
	public bool VoiceActive { get; init; }
	public string VoiceName { get; init; } = string.Empty;
	public int VoiceVolume { get; init; }

	// --- AutoHome routine (§3.9, distinct from standard FindHome) ---
	public bool IsAutoHomeRunning { get; init; }
	public int AutoHomeProgressBar { get; init; }
	public double AutoHomeAxisX { get; init; }
	public double AutoHomeAxisY { get; init; }
	public bool IsGermanPolarMode { get; init; }

	// --- Mount gearing details (§3.10) ---
	public IReadOnlyList<long> StepsPerRevolution { get; init; } = [];
	public IReadOnlyList<double> StepsWormPerRevolution { get; init; } = [];
	public IReadOnlyList<long> StepsTimeFreq { get; init; } = [];
	public (double X, double Y) TrackingOffsetRate { get; init; }

	// --- Mount capabilities / identity (§3.11) ---
	public bool CanPPec { get; init; }
	public bool CanHomeSensor { get; init; }
	public bool CanPolarLed { get; init; }
	public bool CanAdvancedCmdSupport { get; init; }
	public string MountName { get; init; } = string.Empty;
	public IReadOnlyList<string> MountVersion { get; init; } = [];
	public string Capabilities { get; init; } = string.Empty;

	// --- Site / mount type (§3.12) ---
	public GreenSwampMountType MountType { get; init; }
}

/// <summary>Neutral mirror of GreenSwamp.Alpaca.MountControl.SlewType (protocol insulation, same rationale as TelescopeAxis/TelescopePierSide).</summary>
public enum GreenSwampSlewType
{
	None, Settle, MoveAxis, RaDec, AltAz, Park, Home, Handpad, Complete
}

/// <summary>Neutral mirror of GreenSwamp.Alpaca.MountControl.MountType.</summary>
public enum GreenSwampMountType
{
	Simulator, SkyWatcher
}
```

Notes on this definition:
- Field names and types are taken directly from `TelescopeStateModel.cs` (§3), with .NET-idiomatic adjustments only where the ViewModel-facing contract benefits from immutability/read-only collections (`init`-only properties, `IReadOnlyList<T>` instead of mutable arrays/`List<T>`, a value-tuple instead of the mutable `Vector` struct) — no fields are added or dropped versus the §3 inventory's N/C set.
- `SlewState` and `MountType` get their own neutral enums (`GreenSwampSlewType`, `GreenSwampMountType`) rather than referencing `GreenSwamp.Alpaca.MountControl.SlewType`/`MountType` directly, for the same protocol-insulation reason as `TelescopePierSide` (§5.1) — `Abstractions` must not take a compile-time dependency on GreenSwamp's own libraries any more than on `ASCOM.Common`.
- `Capabilities` (free-text) is retained as-is per item 6 ("expose, UI use TBD") even though `TelescopeCapabilities`/`GreenSwampTelescopeState`'s own structured fields likely make the free-text summary redundant for programmatic use — it may still be useful for diagnostics display.

### 5.4 Remaining reconciliation notes

| Property | GreenSwamp-class (today) | GreenSwamp-class (once hub ships) | Standard Alpaca-class | Reconciliation note |
|---|---|---|---|---|
| Altitude, Azimuth, Declination, RightAscension | DS | SR | DS | Fully aligned across both classes at all times. |
| SideOfPier | DS | SR | DS | See §5.1 — no type mismatch; both map to the same `TelescopePierSide` app-level enum for insulation only. |
| Slewing, Tracking, AtPark, AtHome | DS | SR | DS | Aligned. Note GreenSwamp's `Tracking` getter also considers `SlewState == SlewRaDec` (§3.2) — a GreenSwamp-specific richness a generic Alpaca device won't have; app-level `Tracking` should simply reflect whatever the device reports, no client-side reinterpretation needed. |
| UTCDate | DS/AP (get-only on GreenSwamp) | SR | DS/AP (get/set) | GreenSwamp does not support setting `UTCDate` (throws); a generic Alpaca device's support for the setter is unknown/device-dependent — capability-gate any future "set date" UI per-device, not per-class. |
| IsPulseGuiding | N/C (split Ra/Dec on GreenSwamp, carried on `GreenSwampTelescopeState`, §5.3) | SR (split fields) | DS (combined) | App-level property should be the standard **combined** boolean (`IsPulseGuidingRa \|\| IsPulseGuidingDec`) for cross-class consistency, with the per-axis split available as the GreenSwamp-only extra on `GreenSwampTelescopeState`, not the primary contract. |
| TargetRightAscension, TargetDeclination, SiteLatitude, SiteLongitude, SiteElevation, AlignmentMode, TrackingRate | AP (standard-Alpaca-class: polled at session-start + every 30s for SiteLatitude/SiteLongitude/SiteElevation/AlignmentMode/TrackingRate; on Slewing false→true transition + every 1s for TargetRightAscension/TargetDeclination — Get Well Guidance.md Stage 2 and end-of-stages clean-up) | SR (GreenSwamp-class: single source of truth for these and all other fields while SignalR is connected; AP-sourced, same cadences as standard-Alpaca-class, only after irrecoverable SignalR loss — 5 failed reconnects at 2/5/10/10/10s intervals) | AP (same cadences as above) | **Superseded by Get Well Guidance.md Stage 2, extended by the end-of-stages clean-up** (see SignalR-transport-implementation-plan.md Decision B/D, Finding A2/A11, all RESOLVED): GreenSwamp-class treats SignalR as the sole source of truth for these seven fields (and all others) with a precisely-defined, irrecoverable-loss-only AP fallback, not a continuous freshness-based merge with AP. `TrackingRate`'s placement and `SiteLongitude`/`SiteElevation`'s field-coverage gap, both previously open, are now resolved on this same model — see the two rows immediately below for their individual remaining implementation status. |
| SiteLongitude, SiteElevation — field-coverage gap, RESOLVED at the specification level | AP (standard-Alpaca-class: same cadence as SiteLatitude/AlignmentMode/TrackingRate above) | SR (GreenSwamp-class: confirmed for addition to `greenswamp-telescopestate-signalr-spec.md` §11.4/§11.5, closing §11.7 item 10 — requires a server-side `TelescopeStateModel`/`BuildSnapshot` field addition, following the exact `SiteLatitude` pattern, which has not yet been implemented; AP-sourced as the Decision D fallback in the interim and after irrecoverable SignalR loss, same as the row above) | AP (same cadence as above) | **RESOLVED (end-of-stages clean-up)**, see SignalR-transport-implementation-plan.md Finding A11 and `greenswamp-telescopestate-signalr-spec.md` §11.7 item 10: both fields are now confirmed for addition to the neutral `TelescopeState` on the same per-class sourcing model as `SiteLatitude`. The server-side `TelescopeStateModel`/`BuildSnapshot` addition and the client-side `TelescopeState` properties remain to be implemented — the spec is now the precise interface control specification for that work. |
| TrackingRate — placement discrepancy, RESOLVED at the specification level | AP (standard-Alpaca-class: 30s site/alignment cadence, alongside SiteLatitude/SiteLongitude/SiteElevation/AlignmentMode) | SR (GreenSwamp-class: single source of truth while SignalR is connected, same as the other six fields above; AP-fallback on the 30s cadence after irrecoverable SignalR loss) | AP (same cadence as above) | **RESOLVED (end-of-stages clean-up)**, see SignalR-transport-implementation-plan.md Finding A2 and `greenswamp-telescopestate-signalr-spec.md` §11.7 item 11: `TrackingRate` follows the same per-class sourcing model as `SiteLatitude`/`AlignmentMode`, mirroring onto the neutral `TelescopeState`, joining the 30-second AP-fallback cadence. A previously-undocumented server-side defect is also now recorded: `TelescopeStateService.BuildSnapshot` hardcodes `TrackingRate = DriveRate.Sidereal` instead of reading the mount's live value — this must be fixed as part of implementing this row, not merely a client-side mirror gap. |
| ParkSelectedName, ParkPositionNames | NX → SR (planned) | SR | N/C | **GreenSwamp-only concept**, carried on `GreenSwampTelescopeState` (§5.3). A generic Alpaca device has no "named park position" model at all — only `SetPark()`/`CanSetPark` (capture current position as *the* single park position). Per your items 3/4, the `Park` command (§6.3) does **not** branch its execution logic on this value — it is exposed for UI *information* only. |
| LimitsOn, LimitWarning\*, LimitTriggered, ControllerVoltage, LowVoltageEvent, Voice\*, AutoHome\*, Steps\*/TrackingOffsetRate, Can{PPec,HomeSensor,PolarLed,AdvancedCmdSupport}, MountName/MountVersion/Capabilities/MountType, IsMountRunning/ComPort/ConnectedClientCount/HasEverBeenConnected/LoopCounter/TimerOverruns/FlipOnNextGoto/SlewState/ActualAxis\*/AppAxis\*/LocalHourAngle/LocalDate/IsGermanPolarMode | NX → SR (planned) | SR | N/C | **GreenSwamp-only**, no standard equivalent whatsoever (§4.3). Carried entirely by `GreenSwampTelescopeState` (§5.3), gated by `TelescopeCapabilities.IsGreenSwampClass` (§5.2) — this fully resolves architecture §11's open shape question. |
| AxisSteps | CH (today) | SR (planned, full snapshot) | N/C | Chart-only today (`ChartHub`); becomes part of the general snapshot (via `GreenSwampTelescopeState.AxisSteps`) once the new hub ships, but remains GreenSwamp-only — no standard Alpaca equivalent for raw axis steps. |
| ApertureArea, ApertureDiameter, FocalLength, EquatorialSystem, DoesRefraction, GuideRateDeclination/RightAscension, SlewSettleTime, DeclinationRate, RightAscensionRate, TrackingRates, all `Can*` (FindHome/Park/Unpark/SetPark/PulseGuide/SetTracking/SetDeclinationRate/SetRightAscensionRate/SetGuideRates/SetPierSide/Slew*/Sync*/MoveAxis) | AP (GreenSwamp implements the full standard interface, confirmed by direct inspection of `Telescope.cs`) | SR (once hub ships, for the fields also present in `TelescopeStateModel`; several of these — e.g. `ApertureArea`, `EquatorialSystem`, `TrackingRates`, all `Can*` flags — are **not** in `TelescopeStateModel` at all and remain AP-only even after the hub ships) | AP | Standard baseline properties available identically on both classes via Alpaca REST; GreenSwamp's SignalR hub does not (per its approved spec) add these unless they happen to already be `TelescopeStateModel` fields — most `Can*` capability flags do **not** appear in `TelescopeStateModel` and so stay AP-only regardless of the hub. Note: `SiteLongitude`/`SiteElevation` are **not** in this catch-all row — see the dedicated row above (row for "SiteLongitude, SiteElevation — field-coverage gap"), since they are now confirmed for addition to `TelescopeStateModel`/the SignalR payload, unlike the other properties in this row which remain AP-only even after the hub ships. |

## 6. Initial Supported UI Actions

All four actions below map onto `ITelescopeSession` members (architecture §6.2, amended per §6.5 below): `ConnectAsync`, `FindHome`, `ParkAsync`, `AbortSlewAsync`.

**Design-wide simplification confirmed for this pass (your items 3/4):** for `FindHome` and `Park`, `ITelescopeSession` performs **no client-side duplicate business-logic or precondition checking** beyond the standard `Can*` capability gate. If the relevant `Can*` capability property (`CanFindHome`/`CanPark`) is `true`, the command is passed straight through to the underlying provider (`AlpacaTelescopeProvider`/`GreenSwampSignalRProvider`), and any resulting exception (e.g. GreenSwamp's `ParkedException` on `FindHome` while parked, or `DriverException` on `Park` with no park position selected) is caught once, at the `ITelescopeSession` boundary, and surfaced to the ViewModel/UI as an application-facing error per the existing FR44-45 mechanism — never as a raw/unhandled exception, but also never pre-validated or silently blocked before being attempted. This removes the "conservative default" gating and "interim limitation" framing from revision 1 entirely: it is now a **permanent design choice**, not a stopgap pending richer state availability.

### 6.1 Connect

- **Maps to:** `ITelescopeSession.ConnectAsync()` → internally, `AlpacaTelescopeProvider` calls `ASCOM.Common.ClientExtensions.ConnectAsync` (confirmed signature: polls `IAscomDeviceV2.Connecting` at a configurable interval, default 1000ms, until it returns `false`, with cancellation support) rather than a bare `Connected = true` set-and-forget.
- **GreenSwamp-confirmed behavior** (`Telescope.cs`, `Connected` setter): sets `_mount.SetConnected(clientKey, value)` using a real per-client `AlpacaRequestContext.ClientId`, then blocks (`while (Connecting) Thread.Sleep(10);`) until the mount reports not-connecting. This confirms GreenSwamp tracks **multiple simultaneous Alpaca clients by client ID** (consistent with `TelescopeStateModel.ConnectedClientCount`, §3.2) — connecting this client does not exclude other Alpaca clients from also being connected. No divergent behavior identified for standard Alpaca-only devices; `ConnectedClientCount` itself is GreenSwamp-only telemetry (NX today, SR-planned, carried on `GreenSwampTelescopeState`).
- **Resulting state:** `TelescopeConnectionStatus` (architecture §6.2) transitions through a connecting state to connected; `ConnectionStatusChanged` event raised. No GreenSwamp-specific side effects on other properties were identified for Connect itself.
- **Error handling:** connection failures surface through `TelescopeConnectionStatus`, not as raw exceptions to the ViewModel (requirements FR44-45).

### 6.2 FindHome (property: `AtHome`; command: `FindHome`)

- **Maps to:** `ITelescopeSession.FindHome(CancellationToken ct = default)` (see §6.5 for the exact member shape/naming resolution) → `AlpacaTelescopeProvider` calls `ASCOM.Common.ClientExtensions.FindHomeAsync` (confirmed: initiates via `ITelescopeV3.FindHome()`, and the returned task **completes when `Slewing` becomes `false`** — not when `AtHome` becomes `true`; these are documented as separate completion criteria in the ASCOM XML docs).
- **Capability gate:** `CanFindHome` (standard, AP-available on both classes, §4.2) — the `FindHome` command must be disabled/unavailable when `false`. This is the **only** client-side gate applied.
- **No client-side precondition checking (per your items 3/4):** GreenSwamp's own `FindHome()` implementation calls `CheckParked("FindHome")`, which throws `ParkedException` if `AtPark` is `true` — i.e. on GreenSwamp, calling `FindHome` while parked will throw. Revision 1 proposed disabling `FindHome` in the UI whenever `AtPark` is `true` as a "conservative default." **That proposal is withdrawn per your decision.** `ITelescopeSession.FindHome()` is called whenever `CanFindHome` is `true`, regardless of `AtPark`; if the device throws (GreenSwamp's `ParkedException`, or any other device-specific rejection), that exception is caught at the `ITelescopeSession` boundary and surfaced to the UI as an application-facing error (FR44-45). No `AtPark`-based gating logic is implemented in the client.
- **GreenSwamp-confirmed side effect:** `Mount.GoToHome()` calls `ApplyTracking(false)` before slewing — **tracking is turned off automatically as part of homing.** This is GreenSwamp-internal business logic (in `GreenSwamp.Alpaca.MountControl`, not the Alpaca REST driver layer itself, but it is what actually executes when a REST client calls `FindHome()`). Not confirmed as universal standard-Alpaca behavior (a third-party driver may or may not do the same) — the ViewModel should treat `Tracking` as an independently-reported property that may change as a side effect of `FindHome` completing, not assume it stays constant. This is observational, not a precondition check, so it requires no client-side logic change.
- **Distinct from GreenSwamp's "AutoHome" routine:** `TelescopeStateModel.IsAutoHomeRunning`/`AutoHomeProgressBar` (§3.9, carried on `GreenSwampTelescopeState`) represent a **separate, richer** homing routine than the standard `FindHome()` method covers. This design pass's `FindHome` command maps only to standard `FindHome()`/`CanFindHome()`, identically for both device classes — the richer AutoHome routine (if it needs its own UI surfacing, e.g. a progress bar) is **deferred to §7** as a distinct future command, not folded into this pass's `FindHome` command.
- **Resulting state:** `Slewing` transitions true→false (the task's own completion signal); `AtHome` should be checked afterward to confirm success (not guaranteed synchronous with task completion, per the note above) — `TelescopeSession`'s existing position/state event stream (architecture §6.5-6.6) naturally reports the updated `AtHome` value once the next poll/push tick occurs after the task completes, so no additional polling logic is needed in the `FindHome` command itself.

### 6.3 Park (property: `AtPark`; command: `Park`)

- **Maps to:** `ITelescopeSession.ParkAsync(CancellationToken ct = default)` (already present in the architecture's §6.2 sketch) → `AlpacaTelescopeProvider` calls `ASCOM.Common.ClientExtensions.ParkAsync` (confirmed: initiates via `ITelescopeV3.Park()`, completes when `AtPark` becomes `true`).
- **Capability gate:** `CanPark` (standard, AP-available on both classes) — confirmed enforced by GreenSwamp's `Park()` via `CheckCapability(_mount.Settings.CanPark, "Park")`, throwing `MethodNotImplementedException` if unsupported. This is the **only** client-side gate applied.
- **GreenSwamp-confirmed behavior, idempotency:** if already parked (`_mount.AtPark == true`), GreenSwamp's `Park()` is a **silent no-op** (logs "Already Parked", does not throw, does not re-slew) — safe to call repeatedly/defensively from the UI.
- **No client-side precondition checking (per your items 3/4):** if not already parked, GreenSwamp's `Park()` calls `Mount.GoToParkAsync()`, which **requires a previously-selected named park position** (`ParkSelected`, surfaced to the ViewModel as `GreenSwampTelescopeState.ParkSelectedName`/`ParkPositionNames`, §5.3). If none is selected, or the selected position has `NaN` coordinates, `GoToParkAsync` returns a failed `SlewResult` and GreenSwamp's `Park()` throws `DriverException` with a message to the effect of "Park could not be initiated: no park position selected." **This has no standard-Alpaca counterpart** — a generic Alpaca device's `CanSetPark`/`SetPark()` model captures a single implicit position and has no "nothing selected" failure mode once `SetPark()` has ever been called once. **Per your decision, this is not pre-validated client-side.** `ITelescopeSession.ParkAsync()` is called whenever `CanPark` is `true`; the resulting `DriverException` (if no park position is selected) is caught at the `ITelescopeSession` boundary and surfaced to the UI as an application-facing error (FR44-45), exactly like any other device-rejected command. `ParkSelectedName`/`ParkPositionNames` remain exposed on `GreenSwampTelescopeState` purely as **UI information** (e.g. so the View can show the user which park position is currently selected before they press Park), not as a gating input to the command itself.
- **GreenSwamp-confirmed side effect:** `Mount.GoToParkAsync()` calls `ApplyTracking(false)` before slewing to park — **tracking is turned off automatically as part of parking**, identical pattern to `FindHome` (§6.2).
- **Resulting state:** `AtPark` transitions false→true (task completion signal); `Slewing` observed true during the move via the existing state-update stream.

### 6.4 Abort

- **Maps to:** `ITelescopeSession.AbortSlewAsync()` (already in the architecture's §6.2 sketch) → `AlpacaTelescopeProvider` calls `ASCOM.Common.ClientExtensions.AbortSlewAsync` (confirmed: initiates via `ITelescopeV3.AbortSlew()`, completes when `Slewing` becomes `false`).
- **No capability gate needed:** `AbortSlew` has no corresponding `Can*` flag in `ITelescopeV3`/`ITelescopeV4` — it is a mandatory member every compliant Alpaca device must implement (may throw `NotImplementedException` per the interface's documented exception contract, but there is no `CanAbortSlew` to check beforehand). The Abort command should simply be enabled whenever `Connected` is `true`.
- **Confirmed standard behavior (not GreenSwamp-specific):** the `ITelescopeV4.AbortSlew` XML documentation **explicitly lists `ASCOM.ParkedException`** as a possible exception ("If the telescope is parked (ITelescopeV4 and later)"). GreenSwamp's implementation (`CheckParked("AbortSlew")` before calling `_mount.AbortSlewAsync(true)`) directly matches this **standard, documented** V4 behavior — this is not a GreenSwamp quirk, and the same behavior should be expected from any compliant `ITelescopeV4` third-party device. Consistent with items 3/4, this precondition is likewise **not** pre-checked client-side: Abort is simply always enabled when connected, and a `ParkedException` (if it occurs) is surfaced to the UI the same way as any other device-rejected command.
- **Confirmed standard semantics (both classes):** per the `ITelescopeV4.AbortSlew` summary, this "stops any motion in progress: slewing, parking, find-home, and move-axis" — i.e. Abort is the correct universal "stop everything" action for this pass's four in-scope commands (it can interrupt an in-progress `FindHome` or `Park` initiated via §6.2/§6.3).
- **Resulting state:** `Slewing` transitions to `false`; `AtHome`/`AtPark` remain whatever they were at the moment of interruption (neither is asserted true by Abort itself).

### 6.5 `ITelescopeSession` member naming — resolved (your item 5)

Architecture §6.2's `ITelescopeSession` sketch already includes `AbortSlewAsync`, `ParkAsync`, and `ConnectAsync`, but did not yet include a `FindHome` member (it was out of scope when that interface was drafted). Per your item 5 — **"`FindHome()` is formally defined to be async; there is no additional `FindHomeAsync()`; we must support `FindHome()`"** — the member is added as:

```csharp
Task FindHome(CancellationToken ct = default);
```

**Naming note, stated explicitly for traceability:** this intentionally does **not** follow the `XxxAsync` suffix convention used elsewhere on `ITelescopeSession` (`ConnectAsync`, `ParkAsync`, `AbortSlewAsync`, `JogAsync`, etc.). Your instruction establishes `FindHome` (no suffix) as the one deliberate exception, matching the ASCOM method name `FindHome()` itself (which is asynchronous by the standard's own convention/completion semantics — see §6.2 — without an `Async`-suffixed name at the ASCOM interface level either). This is recorded as a confirmed, deliberate naming decision, not an inconsistency to fix later. `telescope-architecture.md` §6.2 should be amended to add this single member the next time that document is revised (a small, additive, non-breaking amendment — no other §6.2 member changes).

## 7. Deferred UI Command Analysis (placeholder)

The following commands are known to be needed in a future revision of this document, once §3–6 are reviewed and approved, but are **not analyzed in this pass**:
- Slew-to-coordinates / slew-to-target (`SlewToCoordinatesAsync`/`SlewToTargetAsync`, requires `TargetRightAscension`/`TargetDeclination` — §3.4/§4.2).
- Sync-to-coordinates/target.
- Tracking on/off toggle and tracking-rate selection (`TrackingRate`/`TrackingRates` — §3.6/§4.2).
- Unpark (`UnparkAsync`/`CanUnpark` — straightforward mirror of Park, but not yet analyzed for GreenSwamp-specific preconditions).
- Set-park (`SetPark`/`CanSetPark`, and GreenSwamp's richer named-park-position selection UI, §3.3/§5.3).
- Jog v1 (`JogAsync`/`StopJogAsync` per architecture §6.2/§6.7 — already scoped there, but its own action write-up in this document's format is deferred; note per your architecture-review feedback this will require a full GreenSwamp hand-controller business-logic analysis before implementation).
- Guide-rate configuration (`GuideRateDeclination`/`GuideRateRightAscension` — §4.2).
- GreenSwamp's AutoHome routine, if it warrants its own distinct UI action beyond standard `FindHome` (§6.2 note).

## 8. Status of Prior Open Items

All seven items raised in revision 1 are now resolved by your decisions, as reflected throughout §3–6 above:

1. **`SideOfPier` reconciliation — resolved.** No actual type mismatch exists (§5.1 corrects revision 1's incorrect claim); a neutral `TelescopePierSide` enum is defined for protocol-insulation consistency with `TelescopeAxis`, not mismatch-resolution.
2. **`TelescopeCapabilities` GreenSwamp-extension shape — resolved.** `bool IsGreenSwampClass` (§5.2), paired with the now-concretely-defined `GreenSwampTelescopeState` (§5.3).
3. **`FindHome`'s `AtPark` precondition — resolved.** No client-side gating; pass-through + surfaced exception (§6.2).
4. **`Park`'s "no park position selected" precondition — resolved.** Same pass-through approach as item 3 (§6.3).
5. **`FindHome` vs. `FindHomeAsync` naming — resolved.** `Task FindHome(CancellationToken ct = default)`, no suffix, as a deliberate one-off exception to the `XxxAsync` convention (§6.5).
6. **"Expose once available" judgment calls — resolved.** All GreenSwamp-only properties are exposed; none are deferred/omitted (§3, throughout).
7. **`GreenSwampTelescopeState` shape — resolved.** Concretely defined in §5.3.

No new open items are raised by this revision. The one item flagged for your awareness (not requiring an immediate decision) is the §6.5 note that `telescope-architecture.md` §6.2 will need a small additive amendment (the `FindHome` member) the next time that document is revised — this can be batched with any other future architecture amendment rather than actioned in isolation.

## 9. Process Note

Per the established review cadence: this revision applies all of your naming corrections and seven decisions to §3–6, and adds concrete type definitions for `TelescopePierSide` (§5.1) and `GreenSwampTelescopeState` (§5.2/§5.3) that were previously only described informally. This revision was approved, and §3–6 were then implemented end-to-end (see §10).

## 10. As-Built Implementation Status and Open Items for the Next Phase

This section was added after the first implementation slice (Connect/FindHome/Park/Abort) was built and tested, to record what actually exists in code, confirm which design decisions in §3–6 hold up unchanged, note the few places implementation diverged from or narrowed this document, and give a clear, evidence-based starting point for the next phase of work (§7's deferred command analysis, or the GreenSwamp SignalR work).

### 10.1 What was built (confirmed by passing tests, not assumption)
- **Assemblies**: `GreenSwamp.Alpaca.Telescope.Abstractions` (contracts/DTOs), `GreenSwamp.Alpaca.Telescope.Model` (`AlpacaTelescopeProvider`, `TelescopeSession`, `TelescopeSessionFactory`, `GreenSwampClassDetector`, `TelescopeStateMapper` — all `internal`), `GreenSwamp.Alpaca.Client.ViewModels` (`TelescopeTabViewModel`, `TelescopeTabViewModelFactory`, `IUiDispatcher`), exactly matching architecture §5's sketch (that document's "two assemblies vs. one" open question is now resolved — see architecture §11).
- **`ITelescopeSession`** implements `ConnectAsync`, `DisconnectAsync`, `FindHome`, `ParkAsync`, `AbortSlewAsync`, `StateUpdated`, `ConnectionStatusChanged`, `Capabilities`, `Status`, `InstanceId` — exactly the four in-scope commands plus session lifecycle, per §6's scope statement. The wider command surface (§7) is correctly absent, not stubbed.
- **No client-side precondition/duplicate business logic exists anywhere in `TelescopeSession`** — confirmed by inspection: `FindHome`/`ParkAsync`/`AbortSlewAsync` are one-line pass-throughs wrapped by a single shared `ExecuteCommandAsync` helper that catches any exception once and surfaces it via `Status.ErrorMessage`, exactly per items 3/4 (§6, design-wide simplification). No `AtPark`/`CanFindHome`-adjacent gating logic exists beyond the single capability check already specified.
- **`TelescopeCapabilities.IsGreenSwampClass`** and **`GreenSwampTelescopeState`** are defined exactly as specified in §5.2/§5.3 — but see §10.2 below for a confirmed detection gap and the fact that `GreenSwampTelescopeState` is never yet populated.
- **`FindHome` naming** (§6.5) is built exactly as decided: `Task FindHome(CancellationToken ct = default)`, the one deliberate non-`Async`-suffixed member.
- **Testing**: Level 1 (15 tests — `TelescopeStateMapper`, `GreenSwampClassDetector`, pure logic only), Level 2 (12 tests — `TelescopeTabViewModel` against a fake `ITelescopeSession`), and Level 3 (1 consolidated scenario test — Connect→FindHome→Abort→Park→Disconnect against the real, running GreenSwampAlpacaServer reference simulator) all pass. This is real evidence the four-command slice works end-to-end against a real device, not just against fakes.

### 10.2 Confirmed deviations/gaps found via implementation and real-server testing (not hypothetical — each was directly observed)
1. **`IsGreenSwampClass` detection does not currently work against the real reference server.** `GreenSwampClassDetector` matches only per-device `Description`/`DriverInfo` against three hard-coded markers. The real running server's `Description` (`"GreenSwamp ASCOM Alpaca Telescope Simulator"`) and `DriverInfo` (`"GreenSwamp.Alpaca.Server, Version=0.0.0.0, ..."`) match none of them — only the server-level `management/v1/description` endpoint's `ServerName` (`"Green Swamp Alpaca Server"`) matches, and that endpoint is not queried by this implementation. **Confirmed by direct query against the live server, not assumption.** Stakeholder decision: not fixed for this slice, since nothing in scope (§6) consumes `IsGreenSwampClass` yet. **Must be fixed before any future GreenSwamp-class-gated work** (SignalR provider, `GreenSwampTelescopeState` population, discovery class-badging) can be verified against a real server. Architecture §6.8/§11 has been updated with this same finding.
2. **No continuous state-polling loop exists yet.** `TelescopeSession` publishes a `StateUpdated` snapshot only once at successful connect, and once immediately after each of `FindHome`/`Park`/`Abort` completes — there is no background timer re-publishing state for a connected-but-otherwise-idle session. This is sufficient for this slice's own commands (each command's own "resulting state" need, §6.1-6.4, is satisfied) but does **not** yet satisfy requirements FR37/38/41 ("ongoing... updates", "internal polling mechanism"). This was not called out explicitly in this document's §6 action write-ups (which focus on per-command resulting state, not idle-session behavior) — recorded here as a real, confirmed scope gap for the next phase, not a design decision. A `TimeProvider`-driven poll loop (architecture §6.3 already anticipates this dependency) is the natural next piece of Model-layer work.
   - **Get Well Guidance.md Stage 3 note — RESOLVED:** the missing poll loop's cadences are now fixed. Per Stage 3 (see `SignalR-transport-implementation-plan.md` Decision C, RESOLVED), a poll loop is required for both classes: a **250ms SignalR ingestion loop** for GreenSwamp-class telescopes while the SignalR connection is healthy, and a **1 second ASCOM Alpaca `DeviceState` loop** for standard Alpaca-class telescopes (continuously) and GreenSwamp-class telescopes (as the Decision D fallback only, after irrecoverable SignalR loss). This closes the FR37/38/41 gap at the cadence-decision level; internal timer composition and thread-safety of the fallback transition remain Stage 4 ("Threading and Data Reconciliation") work, not resolved here.
   - **Get Well Guidance.md Stage 4 note — RESOLVED:** the internal timer composition/thread-safety work flagged above is now closed. Per Stage 4 (see `SignalR-transport-implementation-plan.md` Decision C/D, Stage 4, RESOLVED): no bespoke cross-thread signaling or transition-boundary staleness handling is needed, because SignalR and the fallback loop are never concurrent active sources for a GreenSwamp-class session; and the only multi-cadence concurrency case (AlpacaClass polling) is resolved by time-ordering/sequencing the fixed-cadence blocking REST calls, with standard OnChange semantics applying per field. FR37/38/41's poll-loop gap and FR43's reconciliation concern are both now fully closed at the design level.
3. **Real-device behavior confirming the exception-boundary design works correctly**: the reference simulator starts **parked**, and both `FindHome` and `AbortSlew` are rejected with `"Telescope parked"` while parked (the latter despite `AbortSlew` having no `Can*` gate in the standard interface at all) — both were observed to be caught and surfaced via `Status.ErrorMessage`, never thrown to the caller, exactly as §6.2/§6.4/§6's design-wide simplification specifies. This is a positive confirmation, not a defect — recorded here as evidence the "pass-through + surface any exception" decision (items 3/4) behaves correctly against genuinely rejecting real hardware/simulator behavior, not just against a well-behaved fake.
4. **Level 1 testing ended up narrower than the test strategy's original Level 1 description.** Rather than substituting `AlpacaTelescope`'s internal HTTP transport with a fake handler (test strategy §5.1's original framing), Level 1 tests instead isolate and test only the genuinely pure, transport-free logic (`TelescopeStateMapper`, `GreenSwampClassDetector`) directly. `AlpacaTelescopeProvider` itself has no dedicated Level 1 test; its correctness is instead confirmed at Level 3 against the real server. Flagged for awareness (architecture §11) — not treated as a defect, since real-protocol coverage exists, but worth a conscious decision at the next design pass on whether a fake-HTTP Level 1 layer for `AlpacaTelescopeProvider` is still wanted.

### 10.3 Carried-forward guidance for the next phase
- Before starting §7 (deferred command surface) or GreenSwamp SignalR work, **decide how/when to fix the `IsGreenSwampClass` detection gap** (§10.2, item 1) — it blocks verifying any GreenSwamp-class-gated feature against the real server.
- **Decide whether/when to add the missing continuous poll loop** (§10.2, item 2) before treating FR37/38/41 as met. **Get Well Guidance.md Stage 3 note:** the loop's cadences are now RESOLVED (250ms SignalR / 1s `DeviceState`, per `SignalR-transport-implementation-plan.md` Decision C) — remaining work is the loop's internal timer/thread-safety implementation, tracked as Stage 4. **Get Well Guidance.md Stage 4 note — RESOLVED:** that remaining work is now closed too — see `SignalR-transport-implementation-plan.md` Decision C/D (Stage 4, RESOLVED): no dual-source reconciliation or bespoke cross-thread signaling is required (SignalR and the fallback loop are never concurrent active sources), and the only multi-cadence case (AlpacaClass polling) is resolved by time-ordering/sequencing the fixed-cadence REST calls. No further design decision is outstanding for this item; only the code itself remains to be written.
- The Level 3 integration test against the real server requires a **manually-confirmed device precondition** (unparked, tracking, non-trivial position) before each run, since `ITelescopeSession` does not yet expose `Unpark` and the test's own `Park` step leaves the device parked afterward for the next run. This is a standing process note for whoever runs that test suite next, not a code change.
