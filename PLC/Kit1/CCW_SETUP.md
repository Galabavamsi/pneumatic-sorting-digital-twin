# Kit 1: build and first run in Connected Components Workbench

Step-by-step for the rig as it stood on 2026-11-10: CCW project `mqt1` in `Documents\CCW`,
Micro850 `2080-L50E-48QBB` reachable over Ethernet, an empty project on the controller, and all
inputs and outputs patched and checked by hand (see `Kit1_IO_Allocation.csv`).

The program has **not been built in CCW yet**. Menu names below are from CCW 22 and may differ
slightly in other versions.

## 0. Before you start

- Air **off** (PRESSURE OFF). It stays off until step 6.
- Magazine empty, E-stop released.
- **Output power lead in place:** one lead from a red `24V` jack to the red `24VDC` jack at the
  bottom of the DIGITAL OUTPUTS block. Without it the PLC's OUT lights come on but no valve
  moves (found on 2026-11-14).
- In CCW click **Disconnect**. Variables and code are edited offline.

## 1. Create the global variables (55)

Project Organizer → double-click **Global Variables**. For each variable, type the name in the
empty bottom row and pick the data type. Leave every other column alone unless an initial value
is listed. Names must match exactly; they are not case-sensitive.

**BOOL, initial value TRUE (4)**

`CFG_UseReeds`, `CFG_UseMagSensor`, `CFG_EStopFitted`, `CFG_EStopNC`

**BOOL, no initial value (34)**

- `CFG_MetalToUpper`, `CFG_LiftSensors`
- `IN_MagazinePartPresent`, `IN_MagazineEmpty`, `IN_PartOnLift`, `IN_Inductive`, `IN_Capacitive`
- `IN_LiftUp`, `IN_LiftDown`, `IN_FeedExtended`, `IN_FeedRetracted`
- `IN_LowerEjExtended`, `IN_LowerEjRetracted`, `IN_UpperEjExtended`, `IN_UpperEjRetracted`
- `IN_EStopOk`, `IN_StartPB`, `IN_StopPB`, `IN_ResetPB`, `IN_ContinuousMode`
- `OUT_FeedExtend`, `OUT_FeedRetract`, `OUT_LowerEjExtend`, `OUT_LowerEjRetract`
- `OUT_UpperEjExtend`, `OUT_UpperEjRetract`, `OUT_LiftUp`, `OUT_LiftDown`
- `ST_AutoRunning`, `ST_Faulted`, `ST_Homed`
- `HMI_Start`, `HMI_Stop`, `HMI_Reset`

**INT (10)**

`HR_Heartbeat`, `HR_Step`, `HR_Material`, `HR_PartsDone`, `HR_MetalCount`, `HR_PlasticCount`,
`HR_FaultCode`, `HR_LastCycleMs`, `HR_MapVersion`, `HR_Reserved`

**TIME, with these initial values (7)**

| Name | Initial value |
|---|---|
| `CFG_TravelTime` | `T#1s` |
| `CFG_MoveTimeout` | `T#3s` |
| `CFG_HomeTimeout` | `T#6s` |
| `CFG_DetectSettle` | `T#300ms` |
| `CFG_DetectWindow` | `T#1500ms` |
| `CFG_EmptyDebounce` | `T#2s` |
| `CFG_FeedDelay` | `T#300ms` |

**Faster: paste them.** The CCW variable grid copies and pastes rows as plain XML text, and
`Copy-CcwVariables.ps1` puts the whole list on the clipboard in that layout (this is how the
variables were created on 2026-11-14). In PowerShell, from `PLC/Kit1`:

```powershell
.\Copy-CcwVariables.ps1 -Scope Global
```

Then click the grey box at the left of the empty bottom row of the Global Variables grid and
press Ctrl+V. For the locals in step 2, create the program first, then:

```powershell
.\Copy-CcwVariables.ps1 -Scope Local -FullNamePrefix 'Controller.Micro850.Micro850.Kit1_Sort'
```

Use `-Exclude name1,name2` to leave out variables that already exist. The grid only copies and
pastes while CCW is disconnected.

## 2. Create the program and its local variables (44)

1. Right-click **Programs** → Add → **New ST: Structured Text**.
2. Right-click the new program → Rename → `Kit1_Sort`.
3. Expand `Kit1_Sort` and double-click **Local Variables**. Add:

**BOOL (27)**: only `cmdLiftUp` has an initial value, **TRUE**

- `rawEStop`, `rawMagPart`, `rawFeedRet`, `rawFeedExt`, `rawUpperRet`, `rawUpperExt`
- `rawLowerRet`, `rawLowerExt`, `rawLiftDown`, `rawLiftUp`
- `outEnable`, `liftHold`, `cmdFeedExt`, `cmdUpperExt`, `cmdLowerExt`, `cmdLiftUp`
- `wantAuto`, `stopRequest`, `cycleActive`, `magEmptyLatched`, `routeUpper`, `detected`
- `hbPulse`, `stepChanged`, `allRetracted`, `liftAtUp`, `liftAtDown`

**INT (1)**: `stepPrev`

**DINT (1)**: `cycleMs`

**TON (12)**: type `TON` in the Data Type column

`tvFeedExt`, `tvFeedRet`, `tvUpperExt`, `tvUpperRet`, `tvLowerExt`, `tvLowerRet`, `tvLiftUp`,
`tvLiftDown`, `tEmpty`, `tHb`, `tStep`, `tCycle`

**R_TRIG (3)**: type `R_TRIG` in the Data Type column

`rtStart`, `rtStop`, `rtReset`

4. Double-click `Kit1_Sort` to open the code editor and delete anything in it.
5. Open `PLC/Kit1/Kit1_Sort.st` in Notepad, select all, copy, and paste it into the editor.
6. Save (Ctrl+S).

## 3. Build

Right-click **Micro850** → **Build**. Look at the Error List at the bottom.

| Error mentions | Usual cause |
|---|---|
| unknown / undefined identifier `X` | `X` is missing, misspelled, or created as Local when it should be Global (or the other way) |
| type mismatch on a `tv…`, `t…` or `rt…` line | That variable was created as BOOL instead of `TON` / `R_TRIG` |
| a line with `ANY_TO_DINT` | Replace with `TIME_TO_DINT` (see README, "If CCW rejects a line") |
| anything else | Send the Error List text or a screenshot |

Do not download until the build shows 0 errors.

## 4. Download

1. Click **Download**. If CCW says "Connection failed", the PLC has given itself a new
   `169.254.x.x` address: in the Connection Browser expand `AB_ETHIP-1` and pick the Micro850
   line without a red X.
2. CCW asks to put the controller in Program mode: accept.
3. When it finishes it asks "Change the controller to Remote Run?": answer **Yes**.

Starting the program moves nothing. It sits in Idle (`HR_Step = 0`) with every coil off until
Start or Reset.

## 5. Dry run with the air still off

While connected, the Global Variables grid shows live values in the **Logical Value** column.
A BOOL can be written by ticking its box there.

`Read-PlcVariables.ps1` shows the same values without CCW. It finds the PLC by itself and only
reads:

```powershell
.\Read-PlcVariables.ps1              # status summary
.\Read-PlcVariables.ps1 -Watch 60    # print every change for 60 seconds
```

Check, in this order:

| Check | Expected |
|---|---|
| `HR_Heartbeat` | counting up |
| `HR_Step` | 0 |
| `IN_EStopOk` | TRUE with the E-stop released. If `HR_Step = 90` and `HR_FaultCode = 1`, the E-stop input (`DI-12`) is off: wrong contact pair, or not patched |
| `IN_FeedRetracted`, `IN_UpperEjRetracted`, `IN_LowerEjRetracted` | all TRUE (input lights 0, 2, 4) |
| Press green, red, black; turn the selector | `IN_StartPB`, `IN_StopPB`, `IN_ResetPB`, `IN_ContinuousMode` follow |
| Hold a metal part, then a plastic part, in front of the sensors | metal: `IN_Inductive` and `IN_Capacitive`; plastic: `IN_Capacitive` only |
| Drop a part in the magazine | `IN_MagazinePartPresent` TRUE; take it out again |
| Press the E-stop | `HR_Step = 90`, `HR_FaultCode = 1` |
| Release the E-stop, press black (Reset) | `HR_Step` goes 1 then 0, `HR_FaultCode = 0`, `ST_Homed` TRUE after about 1 s |

With the air off nothing moves, so this proves the inputs, the buttons and the fault handling
without any risk to the mechanics. Output lights 1, 3, 4 and 6 come on during homing: those are
`SOL-02`, `SOL-04`, `SOL-05` and `SOL-07`, the coils that pull 1A, 2A and 3A in and raise the lift.

## 6. First homing with air

1. Hands clear. One hand on the PRESSURE ON/OFF valve. Magazine empty.
2. Air on, slowly.
3. Press black (Reset). The three cylinders stay or pull in, then the platform rises.
   `ST_Homed` becomes TRUE.
4. If anything pushes out or the platform drops, shut the air and stop: a coil direction is
   wrong. Do not continue until it is fixed.
5. `HR_FaultCode = 9` after 6 s means one of the three "retracted" inputs never came on.

## 7. First parts, one at a time

Selector **OFF** (one part per Start).

1. One **plastic** part in the magazine, press green.
   Expected: 1A pushes it onto the platform and pulls back, 2A pushes it into Tray 2 and pulls
   back. `HR_PlasticCount = 1`.
2. One **metal** part, press green.
   Expected: 1A feeds and pulls back, platform goes down, 3A pushes it into Tray 1 and pulls
   back, platform comes up. `HR_MetalCount = 1`.

`HR_Step` shows where it is: 2 wait, 3 feed, 4 feed back, 5 detect, 6/7 upper ejector,
8 lift down, 9/10 lower ejector, 11 lift up, 12 count. If it stops at 90, `HR_FaultCode` says
why (README, fault codes). Press black to recover.

If a plastic part goes to Tray 1 or metal to Tray 2, set `CFG_MetalToUpper` TRUE.

## 8. A batch

Selector **ON**, 6 to 8 mixed parts, press green. It runs until the magazine has been empty
for 2 s, then homes and stops. Red finishes the current part and stops.

## 9. After it sorts reliably

Nothing more is needed in CCW. Open `Kit1Viewer` in Unity, press Play, then **F8**. The twin
finds the PLC on the cable, reads its variables by name over EtherNet/IP and mirrors the
machine (checklist section 6 in `Documentation/KIT1_BRINGUP.md`). It only reads.

To check the link without Unity, from `Tools/Kit1PlcSimulator`:

```bash
dotnet run -c Release -- plcread
```

Optional, only if you want Modbus TCP instead: give the PLC a static IP, add the mapping from
`Kit1_Modbus_Map.csv` (Micro850 → Modbus Mapping), enable the Modbus TCP server, and set
`"protocol": "modbus"` and the IP in `Assets/StreamingAssets/Kit1PlcLink.json`.
