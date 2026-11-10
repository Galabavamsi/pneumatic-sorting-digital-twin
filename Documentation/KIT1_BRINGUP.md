# Kit 1 hardware bring-up checklist

Goal for the day: the real Kit 1 sorts metal and plastic under the Micro850, and the
Unity twin mirrors it live over Modbus TCP (read-only).

Everything below is in this repo:

- PLC program and patch list: [`PLC/Kit1`](../PLC/Kit1/README.md)
- Soft PLC for rehearsal: `Tools/Kit1PlcSimulator`
- Twin live link: `Assets/Scripts/Runtime/PlcLink`, config `Assets/StreamingAssets/Kit1PlcLink.json`

Take a printout of [`PLC/Kit1/Kit1_IO_Allocation.csv`](../PLC/Kit1/Kit1_IO_Allocation.csv) to the rig.

## As found at the rig on 2026-11-10

The sections below were written before the rig was wired. Where they differ, this section and
`Kit1_IO_Allocation.csv` are correct.

| Item | Finding |
|---|---|
| PLC | `2080-L50E-48QBB`, CCW controller project version 22 |
| PLC address | `169.254.226.108` over Ethernet. This is an automatic link-local address and can change after a power cycle: set a static one before using the twin link |
| Previous PLC program | Not backed up. An empty project was downloaded over it |
| Inputs wired | `DI-0`/`DI-1` 1A retracted/extended, `DI-2`/`DI-3` 2A retracted/extended, `DI-4`/`DI-5` 3A retracted/extended, `DI-6` CS-01, `DI-7` IS-01, `DI-8` BTS-01. Confirmed by the PLC input lights: 0, 2 and 4 are on with all three cylinders pulled in. The handwritten sheet has 1A and 2A the wrong way round |
| Outputs and operator devices | Patched: `DO-0`…`DO-7` to `SOL-01`…`SOL-08` in order; green `DI-9`, red `DI-10`, black `DI-11`, E-stop `DI-12`, selector `DI-13`. Each device has one lead in the channel's 24V jack and one in the DI jack |
| Valve directions | Checked at the rig with a 24V lead on each coil: `SOL-01` extends 1A, `SOL-03` extends 2A, `SOL-05` **retracts** 3A (the other way round), `SOL-07` raises the lift |
| 4A lift | No position sensors. The PLC times it (`CFG_LiftSensors = FALSE`, `CFG_TravelTime` = 1 s). Measured travel is just under 1 s |
| CS-01 | OFF with nothing in front of it (checked) |
| Magazine sensor | Labelled `BTS-01`, a light sensor at the magazine base. ON while any part is in the magazine |
| Material sensors | Metal lights IS-01 and CS-01; plastic lights CS-01 only (photographed) |
| Levels | The raised platform, 1A, 2A and both material sensors are at one level. 2A is the upper ejector, 3A the lower |
| Routing | Plastic → 2A → Tray 2. Metal → lift down → 3A → Tray 1 (`CFG_MetalToUpper = FALSE`) |
| Solenoid labels | `SOL-01 (1S1)` on the rear panel is the air port `PP-01`: it is pressurised while coil `SOL-01` is on. The coil itself is the `SOL-01` jack on the front panel |
| `SOL (0V)` | Patched to 0 V |

Inputs and coil directions are now all checked by hand. Not yet done: building the program in
CCW and the first homing.

The offline twin sequence (`Kit1AutomaticSequence`) still sends metal to the upper ejector.
The live mirror follows the PLC, so it is unaffected; the offline routing is the opposite of
the rig until it is changed.

## Rig to twin naming

| Rig | Part | Role | PLC tags | Twin script |
|---|---|---|---|---|
| 1A | Festo DSNU-16-50 (19201), top of KIT-1(A) | Feed | `OUT_Feed*`, `IN_Feed*` | `PneumaticCylinderTest` (Cylinder 1) |
| 2A | Festo DSNU-16-?, horizontal on top of KIT-1(B) frame | Upper ejector | `OUT_UpperEj*` | `ThirdCylinderTest` (Cylinder 3) |
| 3A | Festo DSNU-16-50 (19201), under the KIT-1(A) plate | Lower ejector | `OUT_LowerEj*` | `SecondCylinderTest` (Cylinder 2) |
| 4A | Festo ADNGF-16-30-P-A (554217), vertical, guided | Lift | `OUT_Lift*` | `LiftCylinderTest` |
| IS-01 | inductive, PNP NO | Metal | `IN_Inductive` | `DI_MetalDetected` |
| CS-01 | capacitive BTH BCR12-PN6, PNP NO | Any part | `IN_Capacitive` | `DI_PlasticDetected` = CS and not IS |
| BTS-01 | light sensor at magazine base | Part present | `IN_MagazinePartPresent` | `DI_MagazinePartPresent` |

The twin's Cylinder 2/3 numbering is the reverse of the rig's 2A/3A. The Modbus map
uses functional names (upper/lower), so nothing breaks, but say "upper/lower" at the rig.

The 2A/3A to upper/lower assignment was confirmed at the rig on 2026-11-10 (2A level with the
sensors and the raised platform, 3A below).

## 0. Before you go to the rig

- [ ] **CAD meshes.** Already in place on Lab WS1 (downloaded and checksum-verified
      2026-11-05). On any other PC, see "Getting the meshes" at the end. Without them the twin
      opens to an empty station; the PLC side does not need them.
- [ ] Decide which PC goes to the rig. Lab WS1 already has CCW, RSLinx, the 2080 USB
      driver, Unity 6000.6.0f1, the .NET SDK and this repo. Another PC needs all of those.
- [ ] Rehearse the live link on the soft PLC:
      `dotnet run -c Release --project Tools/Kit1PlcSimulator -- serve --demo --parts MPMPMPMP`, open
      `Kit1Viewer` in Unity, press Play, press **F8**. The badge at the bottom left turns
      **LIVE** and the station runs from the soft PLC. The Console logs
      `Twin routed part UPPER/LOWER while PLC is at … In sync.` for every part.

## 1. Safety

- [ ] Air off (SHUT-OFF VALVE to PRESSURE OFF) and panel off while patching.
- [ ] The valves are 5/2 **bistable**: dropping the coils does **not** stop a cylinder
      that is already moving, it finishes its stroke. The PLC's E-stop handling only stops
      new commands. Keep a hand on the PRESSURE ON/OFF valve for the first runs.
- [ ] Find out what the panel E-stop does. If it only has two contact jacks (as the photos
      suggest), it does nothing until patched to DI-12, and even then it cannot dump air.
- [ ] Do not touch the CONVEYOR CONTROL U/V/W jacks. They may carry VFD motor voltage,
      and Kit 1 does not use them.
- [ ] Hands clear of 2A/3A/4A whenever air is on.

## 2. PLC identity, backup and network

- [ ] Read the full catalogue number on the Micro850 label; unplug the Ethernet cable to
      see it. Expected `2080-L50E-48QBB`. Note the firmware revision.
- [ ] USB A-B cable to the PLC. In CCW, discover the PLC and **upload and save a backup**
      before downloading anything (PLC README step 1).
- [ ] Note the PLC's IP and subnet from the upload. Keep it if it is static. The panel HMI
      may depend on it.
- [ ] Patch cable from the PC to the **ETHERNET-PLC** port on the panel.
- [ ] Put the PC's Ethernet port on the PLC subnet. On Lab WS1 the spare port is
      "Ethernet 2". Example for a PLC at `192.168.1.10`, run in an **admin** PowerShell:

      New-NetIPAddress -InterfaceAlias "Ethernet 2" -IPAddress 192.168.1.20 -PrefixLength 24

      Then check it answers: `ping 192.168.1.10`.

## 3. Mechanical and pneumatic

- [ ] Fit the reed switches: two each on 1A, 2A and 3A (rod-in end and rod-out end) and two
      slot sensors on 4A (platform down and platform up). With air off, push each rod by
      hand to its end stop and slide the switch until its LED lights, then lock it.
- [ ] Connect the BPRS-620 fibres to the amplifier. Teach or adjust it so its output is
      **ON with a part sitting at the magazine base** and OFF without one.
- [ ] Tubing per the allocation: PP-01 to the 1A cap end, PP-02 to the 1A rod end, PP-03/04
      to 2A, PP-05/06 to 3A, PP-07/08 to 4A. PP-07 must **raise** the platform.
- [ ] Open the flow-control elbows about two turns to start with.
- [ ] Set the FRL to 4–5 bar to begin (cylinders are rated 10 bar max). **Write the value down.**
- [ ] Air on slowly. Listen for leaks.
- [ ] Use each valve's manual override to check every cylinder moves the right way.
      Swap tubes where a direction is reversed.

## 4. Electrical patching

Follow `Kit1_IO_Allocation.csv`. Panel power stays off while patching.

- [ ] Sensors: `<TAG> (24V)` into that DI channel's 24V jack, `<TAG> (0V)` into its 0V jack,
      `<TAG> (OUT)` into the DI jack itself.
- [ ] Push buttons, selector and E-stop: one jack to the DI channel's 24V jack, the other to
      the DI jack. Use the E-stop's **NC** contact (DI-12 ON when released).
- [ ] DO-0…DO-7 to SOL-01…SOL-08. Check whether SOL (0V) needs a lead to 0V.
- [ ] Panel on. Walk every input and tick it off by watching the PLC input LED:

| DI | Device | Action | LED OK |
|---|---|---|---|
| 0/1 | 1A reeds | extended / retracted | ☐ |
| 2/3 | 2A reeds | extended / retracted | ☐ |
| 4/5 | 3A reeds | retracted / extended | ☐ |
| 6 | CS-01 | metal ON, plastic ON (adjust the pot) | ☐ |
| 7 | IS-01 | metal part in front, ON; plastic, OFF | ☐ |
| 8 | BTS-01 | part at magazine base | ☐ |
| 9 | Green PB | press | ☐ |
| 10 | Red PB | press | ☐ |
| 11 | Black PB | press | ☐ |
| 12 | E-stop | ON when released, OFF when pressed | ☐ |
| 13 | Selector | ON in "continuous" | ☐ |
| 14/15 | spare | 4A has no sensors | n/a |

## 5. Download and first runs

- [ ] Build the CCW project (PLC README steps 3–10). Download, Run, and check
      `HR_Heartbeat` counts up.
- [ ] Output check, one at a time: with the PLC in Run and Idle, force each DO in CCW
      and confirm the cylinder and direction. Then remove all forces.
- [ ] Selector OFF (single cycle). One **metal** part in the magazine, press green.
      Expected: home, feed, detect, lift down, lower ejector (3A), Tray 1, lift up.
- [ ] One **plastic** part. Expected: feed, detect, upper ejector (2A), Tray 2.
- [ ] Selector ON, mixed batch of 6–8 parts. Counts in `HR_MetalCount` / `HR_PlasticCount`
      should match the trays.
- [ ] Test red PB mid-batch (finishes the part, homes), red PB right after green (cancels
      before feeding), E-stop mid-cycle (fault 1), black PB to recover. After an E-stop, a
      part left on the platform is sorted first on the next Start; clear any half-fed part
      from the feed channel by hand before Reset.

| Fault | Meaning | Usual cause |
|---|---|---|
| 1 | E-stop | E-stop pressed, or DI-12 not patched / NO contact (`CFG_EStopNC`) |
| 2 / 3 | Feed extend / retract timeout | 1A reed not reaching, flow control too closed, tubing swapped |
| 4 | Part fed but IS/CS saw nothing | Sensors too far from the part, CS-01 pot, magazine sensor false ON |
| 5 | Upper ejector timeout | 2A reeds or tubing |
| 6 / 8 | Lift down / up timeout | Only with `CFG_LiftSensors = TRUE`; the timed lift cannot raise these |
| 7 | Lower ejector timeout | 3A reeds or tubing |
| 9 | Homing timeout | Any axis not reaching home, or a reed missing |

Missing a reed switch or the fibre amp on the day? Set `CFG_UseReeds := FALSE` (timed moves,
`CFG_TravelTime`) or `CFG_UseMagSensor := FALSE` online and carry on.

## 6. Twin live link

- [ ] Set the PLC's IP in `Assets/StreamingAssets/Kit1PlcLink.json` (`"host"`), or start a
      built player with `-plcHost <ip>`.
- [ ] Open `Assets/Scenes/Kit1Viewer.unity`, press Play, press **F8**.
- [ ] The badge at the bottom left should read **LIVE** with the PLC step and counts.
      **F1 → I/O** shows the real inputs and solenoids.
- [ ] Run a batch on the machine. The twin's cylinders follow the real solenoids. Parts show
      dark (unclassified) until the real sensors classify them, then blue (metal) or orange (plastic).
- [ ] Each part logs `Twin routed part … In sync.` in the Console. If it says **OUT OF SYNC**
      (usually because the link was switched on mid-cycle), wait until the machine is home and
      press **M** to resync the twin's workpiece.
- [ ] Stop the PLC (Program mode): the badge goes **STALE** within 1 s and the twin freezes
      mid-move. It resumes as soon as fresh data arrives.
- [ ] While live, the twin's own Start, manual cylinder keys and simulated utility faults are
      disabled, and **F1 → Production** shows the PLC's own counts and cycle time. F8 hands
      control back to the offline simulation.

| Badge | Meaning | Fix |
|---|---|---|
| ERROR "Timed out connecting" | No route to the PLC | Cable, PC IP and subnet, ping |
| ERROR "map version 0" | Modbus mapping missing or wrong | Map `HR_MapVersion` to 400009 in CCW |
| ERROR "Modbus exception 2" | Address not mapped | Map all 32 coils and 10 registers |
| STALE | Connected but heartbeat stopped | PLC in Program mode or faulted CPU |

## 7. Measure and record (for twin calibration)

| Item | Value |
|---|---|
| PLC catalogue / firmware | |
| PLC IP / subnet | |
| FRL pressure (bar) | |
| 1A stroke / extend time / retract time | / / |
| 2A stroke / extend time / retract time | / / |
| 3A stroke / extend time / retract time | / / |
| 4A up time / down time | / |
| Which tray is metal (upper or lower chute) | |
| E-stop behaviour (contact only or cuts power) | |

Travel times: watch `tStep.ET` in CCW at the end of each move step, or film the run.
The twin's speeds are constants in `PneumaticCylinderTest`, `SecondCylinderTest`,
`ThirdCylinderTest` and `LiftCylinderTest`.

## Getting the meshes

`git lfs pull` and `git clone` fail for this repo with "exceeded its LFS budget": the
git-lfs batch API refuses every request. The owner account has no payment method and shows
0 of its 10 GB LFS bandwidth used, so this is not the monthly quota running out.

GitHub's **media server still serves the files** (verified 2026-11-05: full size, SHA-256 matches
the LFS pointers). The "Raw" / download button on each file page uses it. From the repo root:

```bash
for k in 1 2 3; do curl -L -o Assets/Models/Kit$k/Kit${k}_components.obj "https://media.githubusercontent.com/media/Galabavamsi/pneumatic-sorting-digital-twin/main/Assets/Models/Kit$k/Kit${k}_components.obj"; done
git add Assets/Models   # refreshes git's index; nothing is staged because the content matches HEAD
```

The repo ZIP does **not** contain the meshes: "Include Git LFS objects in archives" is off,
so it ships the 133-byte pointer files.

To make plain clones work again, the account owner can add a payment method and set a small Git
LFS budget (Settings → Billing → Budgets and alerts). Usage within the free 10 GB a month stays
free. If that does not clear it, contact GitHub Support. Longer term, moving the meshes to a
GitHub Release attachment avoids LFS entirely.
