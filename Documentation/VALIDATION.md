# Kit 1 validation

## Automated project check

In Unity, select:

**Tools → Kit 1 Digital Twin → Validate Project**

The validator checks the segmented model, station prefab, required moving components, logical PLC map, viewer scene, and enabled build scene. A successful run prints `KIT1_VALIDATION_PASS` in the Console.

## Play-mode acceptance checklist

Run this checklist before creating a release.

| Area | Test | Expected result |
|---|---|---|
| Startup | Enter Play mode | No compiler errors; station and both bins render |
| Camera | Orbit, pan, normal/fast zoom | Smooth motion; UI blocks camera input beneath panels |
| Dashboard | Toggle `F1`; open every tab | Readable, responsive, live values |
| Feed axis | Extend/retract Cylinder 1 | 40 mm stroke; rod and pusher remain attached |
| Lower ejector | Extend/retract Cylinder 2 | 50 mm stroke toward lower route |
| Upper ejector | Extend/retract Cylinder 3 | 50 mm stroke toward upper route |
| Lift | Move up/down | 31.2 mm stroke; platform follows rod |
| Automatic metal | Sort a blue workpiece | Upper ejector routes it to the correct bin |
| Automatic plastic | Sort an orange workpiece | Lift lowers; lower ejector routes it to the correct bin |
| Magazine | Run several pieces | Stack advances under gravity and empty state stops feed |
| Reset | Press `M` from normal and out-of-order states | Mechanisms return home without teleporting the workpiece outside the station |
| Safe stop | Press `X` during a cycle | Outputs return safe and an aborted cycle is recorded |
| Power fault | Remove 24 VDC during a cycle | Utility interlock aborts control and records an alarm |
| Pressure fault | Lower pressure below 3.5 bar | Cycle start/motion is blocked; pressure alarm is visible |
| Leak fault | Enable air leak | Effective pressure drops by 2 bar |
| Actuator fault | Select a stuck actuator | Selected axis stops; sequence cannot falsely complete |
| Sensor fault | Select a failed sensor | Corresponding logical PLC input remains false |
| Historian | Record, stop, replay | Timeline and four actuator positions replay |
| CSV | Export a recorded session | CSV contains timestamps, state, I/O, utilities, KPIs, and axis positions |

## Current validation record

- Unity version: `6000.6.0f1`
- Build scene: `Assets/Scenes/Kit1Viewer.unity`
- Script compilation: passing
- Manual mechanism, mixed-workpiece, bin, reset, camera, HMI, utilities, and fault tests: exercised during development
- Physical PLC integration: not tested; PLC details are not yet available
- XR/Meta Quest integration: not started

## Release rule

Do not describe a release as hardware-connected until the real PLC tag map, read-only monitoring, controlled output tests, disconnect handling, and safety review have all passed.
