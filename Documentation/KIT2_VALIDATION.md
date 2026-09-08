# Kit 2 validation

## Automated project check

In Unity, select **Tools → Kit 2 Digital Twin → Validate Project**.

The validator checks the segmented model, stamping-station prefab, mapped moving components, all 16 magazine workpieces, logical PLC map, viewer scene, and enabled build scene. A successful run prints `KIT2_VALIDATION_PASS` in the Console.

## Play-mode acceptance checklist

| Area | Test | Expected result |
|---|---|---|
| Startup | Enter Play mode | No compiler errors; station, ramps, and both bins render |
| Camera | Orbit, pan, zoom while dashboard is open and closed | Smooth motion; dashboard blocks camera and selection input beneath it |
| Manual feed | Stop auto; use `E`, `R`, and `Shift+E` | Cylinder 1 stages, returns, and ejects without retaining a workpiece |
| Manual transfer | Stop auto; use `T`, `G`, and `Shift+T` | Cylinder 2 stages, returns, and ejects without passing through a workpiece |
| Manual stamps | Use `U`/`J` and `I`/`K` | Each rod/tool pair travels together and returns home |
| Magazine | Repeatedly stage workpieces | One 25 × 25 × 10 mm workpiece advances per pitch; stored pieces do not overlap or collapse |
| Generalized flow | Mix manual feed and transfer commands | FIFO workpieces remain independent rigid bodies and can be pushed in any valid order |
| Automatic metal | Start a cycle with a blue workpiece | Inductive detection selects Stamp A and the part reaches Bin 1 |
| Automatic plastic | Start a cycle with an orange workpiece | Capacitive detection selects Stamp B and the part reaches Bin 2 |
| Continuous | Press `S` | The available 16-piece batch processes without manual intervention |
| Step mode | Press `N` repeatedly | Exactly one sequence transition is authorized per press |
| Pause | Press `P` during a cycle, then resume | State and commands hold, then continue without losing the active part |
| Safe stop | Press `X` during a cycle | Outputs command home and the sequence reports a stopped state |
| Reset | Stop auto and press `M` | Magazine and mechanisms return to their initialized state |
| Dashboard | Press `F1`; open every tab | Overview, logical I/O, utilities, and diagnostics remain readable and live |
| Power fault | Disable 24 VDC | New motion is inhibited and the automatic sequence reports the utility fault |
| Pressure fault | Lower pressure below 3.5 bar | Motion is inhibited; pressure readiness is false |
| Leak fault | Enable air leak | Effective pressure falls by 2 bar and motion speed follows the available pressure |
| Actuator fault | Cycle the stuck-actuator selector | Only the selected axis is immobilized and the sequence cannot falsely advance |
| Sensor fault | Cycle the failed-sensor selector | Only the selected logical input is forced false and timeout diagnostics identify the stall |

## Current validation record

- Unity version: `6000.6.0f1`
- Build scene: `Assets/Scenes/Kit2Viewer.unity`
- Script compilation: passing
- Play-mode acceptance: confirmed on 2026-09-08
- Manual mechanism, workpiece, stamping, routing, continuous, paused, stepped, and safe-stop behavior: validated
- Logical PLC tags: defined; physical controller addresses intentionally remain `TBD`
- Physical PLC integration: not tested
- XR/Meta Quest integration: not started

## Release rule

The Kit 2 simulation may be described as an offline, PLC-ready digital-twin prototype after the play-mode checklist passes. It must not be described as hardware-connected until controller mapping, communications, guarded output tests, disconnect behavior, and a physical safety review have passed.
