# Kit 1 setup guide: the working configuration

This is the record of Kit 1 exactly as it ran on the real rig on 2026-11-14. Follow it top to
bottom to set Kit 1 up again from nothing. Where another document disagrees, this one is right.

- Rig: Gravitaz MATK-R5M trainer, Kit 1 (metal / plastic sorting)
- PLC: Allen-Bradley Micro850 `2080-L50E-48QBB`
- Software: Connected Components Workbench (CCW) 22, project `mqt1`
- Program: [`PLC/Kit1/Kit1_Sort.st`](../PLC/Kit1/Kit1_Sort.st)

## 1. What the machine does

1. A light sensor at the bottom of the magazine sees that a part is waiting.
2. Cylinder **1A** pushes the bottom part onto the raised lift platform and pulls back.
3. Two sensors look at the part: the short one (IS-01) sees only metal, the long one (CS-01)
   sees any part.
4. **Plastic:** cylinder **2A** pushes it off the raised platform into **Tray 2**.
5. **Metal:** the lift (**4A**) goes down, cylinder **3A** pushes it into **Tray 1**, the lift
   comes back up.
6. Repeat until the magazine is empty (selector on) or stop after one part (selector off).

## 2. Air connections (rear of the panel to the cylinders)

Each `PP` port is pressurised while the solenoid of the same number is on.

| Port | Solenoid | Goes to | Effect |
|---|---|---|---|
| PP-01 | SOL-01 | 1A | pushes 1A out |
| PP-02 | SOL-02 | 1A | pulls 1A in |
| PP-03 | SOL-03 | 2A | pushes 2A out |
| PP-04 | SOL-04 | 2A | pulls 2A in |
| PP-05 | SOL-05 | 3A | pulls 3A **in** (3A is the other way round) |
| PP-06 | SOL-06 | 3A | pushes 3A out |
| PP-07 | SOL-07 | 4A | raises the platform |
| PP-08 | SOL-08 | 4A | lowers the platform |

PP-09 and PP-10 stay plugged. Compressor line into COMPRESSOR INPUT LINE.

To check a direction by hand: PLC stopped, air on, touch a lead from a red `24V` jack onto the
`SOL` jack for a moment.

## 3. Electrical patching (front panel)

Every 3-wire sensor has three leads: `(24V)` into that channel's red 24V jack, `(0V)` into its
black 0V jack, `(OUT)` into the DI jack. Each button, the selector and the E-stop have two jacks:
one lead to the channel's red 24V jack, the other to the DI jack. **Never put a button lead in a
0V jack.**

### Inputs

| Jack | Device | PLC input light is on when |
|---|---|---|
| DI-0 | 1A reed switch | 1A is pulled in |
| DI-1 | 1A reed switch | 1A is pushed out |
| DI-2 | 2A reed switch | 2A is pulled in |
| DI-3 | 2A reed switch | 2A is pushed out |
| DI-4 | 3A reed switch | 3A is pulled in |
| DI-5 | 3A reed switch | 3A is pushed out |
| DI-6 | CS-01 capacitive (long) sensor | any part is in front of it |
| DI-7 | IS-01 inductive (short) sensor | a metal part is in front of it |
| DI-8 | BTS-01 light sensor, magazine base | a part is in the magazine |
| DI-9 | Green button | pressed (Start) |
| DI-10 | Red button | pressed (Stop) |
| DI-11 | Black button | pressed (Reset) |
| DI-12 | Red mushroom E-stop | **released** (off when pushed in) |
| DI-13 | Selector switch | on (continuous) |

Cylinder 4A (the lift) has no sensors. The program times it instead.

With everything at rest, the mushroom up and no parts loaded, input lights **0, 2, 4 and 12** are on.

### Outputs

| Jack | To | Effect |
|---|---|---|
| DO-0 | SOL-01 | 1A out |
| DO-1 | SOL-02 | 1A in |
| DO-2 | SOL-03 | 2A out |
| DO-3 | SOL-04 | 2A in |
| DO-4 | SOL-05 | 3A in |
| DO-5 | SOL-06 | 3A out |
| DO-6 | SOL-07 | platform up |
| DO-7 | SOL-08 | platform down |

The label is under its jack. DO-0 to DO-6 run down the first column of the DIGITAL OUTPUTS
block; DO-7 is the top jack of the second column.

### Two extra leads that are easy to forget

| From | To | Why |
|---|---|---|
| `SOL (0V)` | a black `0V` jack | return path for all solenoids |
| a red `24V` jack | the red **`24VDC`** jack (bottom of the DIGITAL OUTPUTS block, under DO-19) | **power for the PLC outputs.** Without it the PLC's OUT lights come on but no cylinder moves |

### Network

One Ethernet cable from the PC to the panel's **ETHERNET-PLC** port. CCW and the digital twin
both use this one cable, at the same time if needed. The **ETHERNET-HMI** port belongs to the
panel's own touchscreen and is not used.

## 4. Mechanical settings that matter

- The fed part must come to rest in front of **both** sensors, within a few millimetres. If it
  stops beside CS-01, plastic parts are not seen and the machine stops on fault 4.
- Reed switches sit at each end of 1A, 2A and 3A. Push each rod to its end by hand (air off) and
  check the matching input light.
- 2A takes about 1.9 s to push out; the program allows 3 s. If its flow control is closed
  further, expect fault 5.

## 5. The PLC program

| File | Purpose |
|---|---|
| [`Kit1_Sort.st`](../PLC/Kit1/Kit1_Sort.st) | the program (Structured Text) |
| [`Kit1_Variables.csv`](../PLC/Kit1/Kit1_Variables.csv) | all 99 variables: 55 global, 44 local |
| [`Kit1_IO_Allocation.csv`](../PLC/Kit1/Kit1_IO_Allocation.csv) | the patch list above, as data |
| [`CCW_SETUP.md`](../PLC/Kit1/CCW_SETUP.md) | click-by-click CCW instructions |
| [`Copy-CcwVariables.ps1`](../PLC/Kit1/Copy-CcwVariables.ps1) | pastes the variables into CCW instead of typing them |
| [`Read-PlcVariables.ps1`](../PLC/Kit1/Read-PlcVariables.ps1) | shows the running PLC's values, read-only |

### Steps (`HR_Step`)

| Step | Name | What happens | Moves on when |
|---|---|---|---|
| 0 | Idle | all solenoids off | green (Start) or black (Reset) |
| 1 | Homing | 1A, 2A, 3A in; then platform up | all three "in" switches on and 1 s of lift time |
| 2 | Wait part | waits for the magazine sensor | part present; or magazine empty for 2 s (batch ends) |
| 3 | Feeding | 1A out | 1A "out" switch |
| 4 | Feed retract | 1A in | 1A "in" switch |
| 5 | Detect | after 0.3 s reads IS-01 and CS-01 | metal, or plastic; nothing seen for 1.5 s is fault 4 |
| 6 | Upper eject | 2A out | 2A "out" switch |
| 7 | Upper retract | 2A in | 2A "in" switch |
| 8 | Lift down | platform down | 1 s |
| 9 | Lower eject | 3A out | 3A "out" switch |
| 10 | Lower retract | 3A in | 3A "in" switch |
| 11 | Lift up | platform up | 1 s |
| 12 | Cycle done | counts the part | next part, or stop |
| 13 | Stopping | homes and returns to Idle | |
| 90 | Fault | all solenoids off | black (Reset), with the E-stop released |

The lift never moves unless 1A, 2A and 3A are all in.

### Settings as run (initial values in CCW)

| Variable | Value | Meaning |
|---|---|---|
| `CFG_UseReeds` | TRUE | wait for the reed switches |
| `CFG_UseMagSensor` | TRUE | use BTS-01 |
| `CFG_MetalToUpper` | FALSE | plastic leaves at the upper level, metal at the lower |
| `CFG_LiftSensors` | FALSE | lift is timed |
| `CFG_EStopFitted` / `CFG_EStopNC` | TRUE / TRUE | E-stop on DI-12, on when released |
| `CFG_TravelTime` | 1 s | lift travel time |
| `CFG_MoveTimeout` | 3 s | longest allowed cylinder move |
| `CFG_HomeTimeout` | 6 s | longest allowed homing |
| `CFG_DetectSettle` / `CFG_DetectWindow` | 0.3 s / 1.5 s | sensor read timing |
| `CFG_EmptyDebounce` | 2 s | magazine empty this long ends the batch |
| `CFG_FeedDelay` | 0.3 s | pause before each feed |

### Fault codes (`HR_FaultCode`)

| Code | Meaning | Seen on this rig because |
|---|---|---|
| 1 | E-stop | mushroom pushed in |
| 2 | 1A did not push out | the 24VDC output-power lead was missing |
| 3 | 1A did not pull in | |
| 4 | part fed but no sensor saw it | part stopped beside CS-01 |
| 5 | 2A timeout | |
| 7 | 3A timeout | |
| 9 | homing did not finish | the 24VDC output-power lead was missing |

Codes 6 and 8 (lift) cannot occur while the lift is timed.

## 6. Setting it up from nothing

1. **Air:** connect the tubes as in section 2. Air off.
2. **Patch:** all leads in section 3, including the two extra ones.
3. **Power on.** Mushroom up, no parts. Check input lights 0, 2, 4 and 12 are on, then press
   each button and check its light.
4. **CCW:** open `mqt1`. If starting a new project, follow
   [`CCW_SETUP.md`](../PLC/Kit1/CCW_SETUP.md): paste the variables with
   `Copy-CcwVariables.ps1`, paste `Kit1_Sort.st`, Build (0 errors expected).
5. **Download.** If CCW says "Connection failed", the PLC has changed its address: in the
   Connection Browser expand `AB_ETHIP-1` and pick the Micro850 line without a red X. Choose
   plain **Download**, then **Yes** to Remote Run.
6. **Air on**, hands clear. Cylinders may jump to where they were last left.
7. **Black** button: everything goes home.
8. Selector off, one plastic part, **green**: it should land in Tray 2.
9. One metal part, **green**: Tray 1.
10. Selector on, several parts, **green**: runs until the magazine is empty.

Buttons: green starts, red finishes the current part and stops, black recovers after any stop,
the mushroom stops new moves at once.

To see what the PLC is doing without CCW, from `PLC/Kit1` in PowerShell:

```powershell
.\Read-PlcVariables.ps1
```

## 7. Digital twin

Nothing extra is set up in the PLC. With the machine running and the Ethernet cable in:

1. Open the project in Unity `6000.6.0f1` and open `Assets/Scenes/Kit1Viewer`.
2. Press Play, then **F8**.
3. The badge at the bottom left shows **PLC LINK LIVE** with the PLC's step and counts.

The twin finds the PLC by itself and only reads from it. Settings are in
`Assets/StreamingAssets/Kit1PlcLink.json`.

## 8. Known limits

- The PLC's address changes by itself (it is on automatic addressing). Tools here find it each
  time; CCW needs it re-picked.
- The valves hold their last position, so the E-stop does not pull cylinders back and does not
  cut the air. Use the air shut-off valve for that.
- The program that was in the PLC when the rig arrived was not backed up.
