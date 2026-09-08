# Kit 3 objective

## Module purpose

Kit 3 is a stamping and color-based sorting module. Its digital twin must model the integration of a vacuum generator and rotary actuator while processing two differently colored workpieces and sorting them into two bins.

## Intended process

1. Present one workpiece from the vertical magazine at the processing plane.
2. Detect its color using a simulated color sensor.
3. Position and stamp the workpiece.
4. Enable the vacuum generator and confirm that the pickup has gripped the workpiece.
5. Rotate the transfer arm toward the bin assigned to the detected color.
6. Disable vacuum and release the workpiece under rigid-body gravity into the selected bin.
7. Return the stamp, rotary actuator, pickup, and feeder to their home states.
8. Advance the next workpiece and repeat.

The implemented automatic recipe is deliberately limited to the verified 50 mm
presentation stroke: **feed → retract → stamp down/up → extend vacuum → grip →
retract → route by color → release → return home**. The vacuum shaft remains at
its initialized 5 mm pickup height throughout the automatic recipe; its lift is a
manual commissioning axis only. Blue workpieces route to Bin 1 at 0°, and orange workpieces route to Bin 2
at -90°. The feeder never performs a second or direct-to-bin eject stroke.

The sequence must support continuous, stepped, paused, safe-stop, reset, and manual commissioning operation. Workpieces must remain independent rigid bodies after release.

## Manual commissioning controls

- `L` / `O`: rotary home/bin 1 and bin 2
- `E` / `R`: vacuum slide extend/retract
- `Q` / `A`: vacuum carriage raise/return to the 5 mm pickup approach height
- `F`: execute the safe 50 mm workpiece-presentation stroke. Feeding is interlocked while the processing position is occupied.
- `G`: retract the feeder. The remaining magazine stack begins its gravity-accelerated one-pitch drop as soon as the preceding feed reaches its endpoint, then the settled bottom piece is armed when retraction reaches home.
- `U` / `J`: stamp down/up
- `V`: vacuum on/off; a captured workpiece follows the pickup and returns to rigid-body gravity when released
- `M`: restore the complete magazine for another commissioning run

## Automatic operation and dashboard

- `S`: reset the magazine and start a continuous 16-piece batch
- `N`: start in step mode or advance one state transition
- `P`: pause/resume the automatic sequence
- `X`: safe stop; vacuum is released and every mechanism returns to its safe home
- `F1`: show/hide the Kit 3 engineering dashboard

The dashboard provides live sequence state, selected color route, remaining and
completed counts, cycle time, fault text, actuator positions, sensor states, and
clickable start/step/pause/stop/reset controls. Manual actuator keys are locked
while automatic mode owns the logical outputs.

## Verified CAD baseline

- Segmented CAD components: 69
- Mesh triangles: 1,688,481
- Magazine workpieces: `Component_050` through `Component_065`
- Workpiece dimensions: 25 × 25 × 10 mm
- Vacuum end-effector: `Component_033`
- Stamping rod/head pair: `Component_035` + `Component_044`
- Feeder cylinder rod/head pair: `Component_032` + `Component_034`
- Rotary turntable/collar: `Component_048`
- Verified rotary pickup group: `Component_014`, `Component_031`, `Component_033`, `Component_036`, `Component_040`, `Component_041`, `Component_046`, `Component_048`, and `Component_069`
- Vacuum-slide moving group: `Component_033`, `Component_040`, and `Component_041`; calibrated stroke 50 mm along the straight CAD rail axis of `Component_040`. `Component_014` rotates with the carrier but does not translate.
- Vacuum-lift moving group: `Component_014`, `Component_020`, `Component_022`, `Component_031`, `Component_033`, `Component_040`, `Component_041`, `Component_046`, and `Component_069`; the mesh contact plane is calculated from the suction-cup bottom and workpiece top. The hard lower/initialized position is 5 mm above contact, and the upper position is 20 mm above contact, so horizontal extension arrives directly within the pickup envelope without travelling below startup height. `Component_046` is the telescoping upper member, while lower sleeve `Component_036` and the rotary base remain vertically fixed.
- Stamp `Component_035` + `Component_044`: calibrated 45 mm downward contact stroke based on the CAD workpiece plane.
- Feed `Component_032` + `Component_034`: a 50 mm presentation stroke uses a kinematic pusher collider against a dynamic, continuously detected workpiece rigidbody. It never ejects directly into a bin. Only after the pusher is fully home, every remaining magazine body is released to gravity with guide-axis constraints, allowed to collide and fall one pitch, then locked to its exact CAD slot after settling. The next workpiece is armed only after that physical drop completes and after vacuum has cleared the processing position.
- Bin 1/home destination: `Component_027`
- Bin 2/-90° destination: `Component_028`
- Viewer scene: `Assets/Scenes/Kit3Viewer.unity`
- Remaining mechanism identities are intentionally marked `TBD` until visually verified.

## Control boundary

`Kit3PlcIo` exposes controller-neutral logical inputs and outputs for color, part
presence, vacuum grip, feeder, stamp, slide, lift, and rotary endpoints. The tag
contract is recorded in `Assets/Settings/Kit3_PLC_IO_Map.csv`. Physical PLC
addresses remain unassigned until the real controller and wiring are known.
