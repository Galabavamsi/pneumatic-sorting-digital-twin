# Kit 1 PLC program (Micro850, Connected Components Workbench)

Metal/plastic sorting for the MATK-R5M Kit 1 module. The program also publishes
its state over Modbus TCP so the Unity twin can mirror the machine read-only.

| File | What it is |
|---|---|
| `Kit1_Sort.st` | The program, Structured Text. Paste into a CCW ST program named `Kit1_Sort`. |
| `Kit1_Variables.csv` | Every variable: scope (Global/Local), type, initial value, Modbus address. |
| `Kit1_IO_Allocation.csv` | Patch list: which lead goes in which DI/DO jack, which SOL/PP feeds which cylinder. |
| `Kit1_Modbus_Map.csv` | Modbus addresses the twin reads. Contract with `Assets/Scripts/Runtime/PlcLink/Kit1ModbusMap.cs`. |

`Tools/Kit1PlcSimulator` runs a line-by-line C# port of `Kit1_Sort.st` against a
simulated Kit 1, so logic changes can be tested without the rig. Change both files together.

## Sequence

Home = feed (1A) retracted, both ejectors (2A, 3A) retracted, lift (4A) up.

1. **Start** (green PB) → home the machine (ejectors and feed first, then lift up).
2. Wait for a part at the magazine base (fibre sensor), then **1A feeds** it onto the lift platform and retracts.
3. **Detect** after 300 ms: IS-01 on → metal; only CS-01 on → plastic.
4. **Metal** → 2A (upper ejector) pushes it off the raised platform → TRAY at the upper chute.
   **Plastic** → lift down → 3A (lower ejector) pushes it off → lift up.
   (`CFG_MetalToUpper := FALSE` swaps the routes.)
5. Count, then repeat until the magazine is empty (selector ON) or stop after one part (selector OFF).

Stop (red PB) finishes the current part and homes. Stop pressed during the homing
that follows Start cancels the run before anything is fed. Every move has a timeout
that raises a fault; all coils drop (the 5/2 bistable valves hold position). E-stop
faults immediately. Reset (black PB) clears a fault when the E-stop is released and
re-homes; in Idle it also zeroes the counters.

If a part is still on the lift platform when the next cycle starts (for example after
an E-stop mid-sort), the PLC sorts that part first and never feeds onto an occupied
platform. A part left **half-fed** in the feed channel is not detected: clear it by
hand before pressing Reset.

Fault codes (register 400007): 1 E-stop, 2/3 feed extend/retract timeout, 4 part fed
but not seen by IS/CS, 5 upper ejector timeout, 6 lift down timeout, 7 lower ejector
timeout, 8 lift up timeout, 9 homing timeout.

### Bring-up switches (change online in the Variable Monitor)

| Variable | Default | Use |
|---|---|---|
| `CFG_UseReeds` | TRUE | FALSE runs on timed moves (`CFG_TravelTime`) before the reed switches are fitted |
| `CFG_UseMagSensor` | TRUE | FALSE if the fibre amp is not working: "fed but nothing seen" ends the batch instead of faulting |
| `CFG_MetalToUpper` | TRUE | Swap which tray gets metal |
| `CFG_EStopNC` | TRUE | Set FALSE only if the E-stop contact you patched is NO |

## Build the CCW project

CCW and RSLinx are installed on Lab WS1 (desktop shortcut "Connected Components Workbench").

1. **Back up what is on the PLC first.** Connect a USB A-B cable to the Micro850's USB port.
   In CCW: *File → New*, then *Device Toolbox → Discover → Browse Connections*, pick the
   Micro850 under the USB driver, *Upload*. Save that project as
   `MATK-R5M_original_backup.ccwsln` somewhere safe. Note its IP address
   (*Controller → Ethernet → Internet Protocol*).
2. Read the full catalogue number from the uploaded device or the PLC label
   (expected `2080-L50E-48QBB`). Use that same catalogue and firmware for the new project.
3. *File → New* project, add the Micro850 with that catalogue number.
4. **Ethernet:** *Controller → Ethernet → Internet Protocol*. If the backup already had a
   static IP, keep it. Otherwise set a static address, e.g. `192.168.1.10 / 255.255.255.0`,
   no gateway.
5. **Global variables:** *Controller → Global Variables*. Add every `Global` row of
   `Kit1_Variables.csv` with its data type and initial value.
6. **Program:** *Programs → Add → New ST: Structured Text*, name it `Kit1_Sort`.
   Add every `Local` row of `Kit1_Variables.csv` to its Local Variables (TON and R_TRIG
   are instance types). Paste `Kit1_Sort.st` into the editor.
7. **Modbus mapping:** *Controller → Modbus Mapping*. Add every row of
   `Kit1_Modbus_Map.csv`: BOOL variables as coils at `000001…000032`, INT variables as
   holding registers at `400001…400010`. The Micro850 serves Modbus TCP on port 502;
   the mapping table decides what it exposes. If your firmware's Ethernet settings show
   a Modbus TCP enable option, make sure it is on.
8. *Build*. Fix anything it flags (see "If CCW rejects a line" below).
9. *Download* over USB, then switch the controller to *Run*.
10. Open a Variable Monitor on the globals. Check `HR_Heartbeat` counts up and
    `HR_MapVersion = 1`.

### If CCW rejects a line

The code sticks to plain IEC 61131-3 that Micro800 supports (CASE, TON, R_TRIG,
`ANY_TO_DINT`, `ANY_TO_INT`). The two lines most likely to need a tweak on an older
CCW release:

- `cycleMs := ANY_TO_DINT(tCycle.ET);` converts TIME to milliseconds. If it is refused,
  use `cycleMs := TIME_TO_DINT(tCycle.ET);`.
- Boolean assignments from comparisons, e.g. `stepChanged := HR_Step <> stepPrev;`.
  If refused, wrap them: `stepChanged := (HR_Step <> stepPrev);`.

## Test without the rig

```bash
cd Tools/Kit1PlcSimulator
dotnet run -c Release -- selftest   # 9 logic scenarios in simulated time
dotnet run -c Release -- linktest   # real Modbus TCP, read with the Unity client code
dotnet run -c Release -- serve      # soft PLC on 127.0.0.1:502 for the Unity twin
```

In `serve` mode: S start, X stop, R reset, E E-stop, C continuous/single, L load parts,
M/P add metal/plastic, J jam the feed cylinder, Q quit. Options: `--port N` (default 502),
`--bind IP` (default 127.0.0.1), `--demo` (press Start and reload the magazine automatically),
`--parts MPMP…` (exact magazine order: M = metal, P = plastic).
