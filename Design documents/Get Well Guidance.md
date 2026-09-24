# Get Well Guidance for GreenSwampAlpacaClient – SignalR Transport Implementation Plan

Remedial action will follow a defined sequence with guidance being progressively refined as each stage is completed. The aim is to complete the implementation plan stage by stage, updating the plan at each stage. The scope of each stage will be limited to that stage only. Previous stages will not be modified and subsequent stages will not be considered. Once a stage has been completed the next stage will be addressed. At the end of all stages I will conduct a clean-up review to address gaps, inconsistencies and errors.

The stages are:

1. GreenSwampClass Detector
2. Telescope state for GreenSwampClass and AlpacaClass telescopes
3. Poll loop
4. Threading and Data Reconciliation
5. Client-Server lifecycle mechanics
6. Data transport mechanics
7. Testing and Acceptance

This document will be updated after each stage has been completed to provide guidance for the next stage

## GreenSwampClass Detector

The class detection mechanism will use the ASCOM Alpaca driverinfo request to obtain the recognition string. The server returns this information "GreenSwamp.Alpaca.Server, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null" which is derived directly from the server build assemblies. Client recognition checks should compare the name string in the first field and should check for a valid .NET version string which will always be in the format a.b.c.d and may be 0.0.0.0

## Telescope state for GreenSwampClass and AlpacaClass telescopes

AlpacaClass telescopes will use ASCOM Alpaca devicestate as the source for telescope state. Fields not in device state (site latitude, longitude, elevation, target right ascension and target declination, alignmentmode, tracking rate) will rely on ASCOM Alpaca property requests to obtain the information. The ASCOM Alpaca property requests will be used to update the device state fields. Trackingrate, alignmentmode and site latitude, longitude and elevation will be queried at the start of a session and subsequently at 30 seconds intervals. Target right ascension and target declination will be updated when the telescope slewing property changes from false to true and at one second intervals.

GreenSwampClass telescopes will use the SignalR communicated telescope state as the single source of truth for all information fields. GreenSwampClass telescopes will revert to AlpacaClass telescope state information only if SignalR state is unavailable due to irrecoverable loss of SignalR connection. Irrecoverable loss means failure to re-establish a connection after 5 retries at intervals of 2, 5, 10, 10 and 10 seconds.

Actioning this guidance will require updates to other documents in the "Design Documents" folder.

## Poll Loop

A poll loop is required for GreenSwampClass and AlpacaClass telescopes. The SignalR loop will operate at 250mS cadence and the ASCOM Alpaca device state loop will operate at 1 second cadence.

Actioning this guidance may require updates to other documents in the "Design Documents" folder.

## Threading and Data Reconciliation

There are three residual concerns:

1. Transition boundary staleness when switching between SignalR and fallback loops
2. Cross-thread signaling of "irrecoverable" reconnect failure
3. Thread-safety of concurrent independent cadence timers writing to a shared TelescopeState snapshot

Concern 1 guidance: the design states that a GreenSwampClass telescope will fallback to AlpacaClass device state and ASCOM properties when SignalR has failed. There can be no staleness because either SignalR is operating correctly or it is marked as failed in which case AlpacaClass state is then obtained and used. Their are no parallel active sources for telescope

Concern 2: the GreenSwamp telescope model handles all telescope transport concerns and hence the source and validity of the telescope model. Which thread is used to get telescope state information (SignalR or ASCOM Alpaca device state and properties) should not be an issue.

Concern 3: the cadence is specified and well known. The only case of multiple cadences is for AlpacaClass telescopes and it should  be possible to to time order / sequence these REST calls which are all blocking REST calls so that individual values can be saved in the model. Standard OnChange property patterns should then apply

Actioning this guidance may require updates to other documents in the "Design Documents" folder.

## Testing

Decision E

A fake-hub is required for testing with the following scope:

1. Connect / reconnect / retry / backoff
2. Confidence test to verify correct population of the telescope model
3. Deserialization of enums

The tests should build verification starting at level 1. There will need to be integration testing to verify that the GreenSwampClass server provides a fully populated data set. 

Decision F — (from SignalR-transport-implementation-plan.md) - the recommendation to continue the manual precedent for this pass is accepted.