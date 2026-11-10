# Kit 1 PLC program (Micro850, Connected Components Workbench)

Metal/plastic sorting for the MATK-R5M Kit 1 module. The program also publishes
its state over Modbus TCP so the Unity twin can mirror the machine read-only.

| File | What it is |
|---|---|
| `Kit1_Sort.st` | The program, Structured Text. Paste into a CCW ST program named `Kit1_Sort`. |
| `Kit1_Variables.csv` | Every variable: scope (Global/Local), type, initial value, Modbus address. |
| `Kit1_IO_Allocation.csv` | Patch list: which lead goes in which DI/DO jack, which SOL/PP feeds which cylinder. |
| `Kit1_Modbus_Map.csv` | Modbus addresses the twin reads. Contract with `Assets/Scripts/Runtime/PlcLink/Kit1ModbusMap.cs`. |
| `CCW_SETUP.md` | Click-by-click: create the variables, build, download, dry run, first homing, first parts. |

`Tools/Kit1PlcSimulator` runs a line-by-line C# port of `Kit1_Sort.st` against a
simulated Kit 1, so logic changes can be tested without the rig. Change both files together.

## Sequence

Confirmed at the rig on 2026-11-10. Home = feed (1A) retracted, both ejectors (2A, 3A)
retracted, lift (4A) up. The raised platform, 1A, 2A and the two material sensors are all at
one level.

1. **Start** (green PB, or `HMI_Start`) → home the machine (ejectors and feed first, then lift up).
2. Wait for a part at the magazine base (`BTS-01`), then **1A feeds** it onto the lift platform and retracts.
3. **Detect** after 300 ms: IS-01 on → metal; only CS-01 on → plastic.
4. **Plastic** → 2A (upper ejector) pushes it off the raised platform → **Tray 2**.
   **Metal** → lift down → 3A (lower ejector) pushes it off → **Tray 1** → lift up.
   (`CFG_MetalToUpper := TRUE` swaps the routes.)
5. Count, then repeat until the magazine is empty (selector ON) or stop after one part
   (selector OFF or not patched).

4A has no position sensors. The PLC treats a lift move as finished once the coil has been
held for `CFG_TravelTime` (1 s; the real lift takes just under that), and remembers the position while the coils
are off. Faults 6 and 8 therefore cannot occur unless sensors are fitted and
`CFG_LiftSensors` is set TRUE.

Stop (red PB) finishes the current part and homes. Stop pressed during the homing
that follows Start cancels the run before anything is fed. Every move has a timeout
that raises a fault; all coils drop (the 5/2 bistable valves hold position). E-stop
faults immediately. Reset (black PB) clears a fault when the E-stop is released and
re-homes; in Idle it also zeroes the counters.

The batch only ends on "magazine empty" after `CFG_EmptyDebounce` in WaitPart, so pressing
Start right after dropping new parts into an empty magazine runs them instead of
returning to Idle on the previous batch's empty flag.

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
| `CFG_UseReeds` | TRUE | FALSE runs 1A, 2A and 3A on timed moves (`CFG_TravelTime`) instead of their reed switches |
| `CFG_LiftSensors` | FALSE | TRUE only if position sensors are fitted to 4A and patched to DI-14 (down) / DI-15 (up) |
| `CFG_TravelTime` | 1 s | Lift travel time. The real lift takes just under 1 s |
| `CFG_UseMagSensor` | TRUE | FALSE if `BTS-01` is not working: "fed but nothing seen" ends the batch instead of faulting |
| `CFG_MetalToUpper` | FALSE | Swap which tray gets metal |
| `CFG_EStopFitted` | TRUE | FALSE only for a bench test with no E-stop patched to DI-12 (unsafe) |
| `CFG_EStopNC` | TRUE | Set FALSE only if the E-stop contact you patched is NO |

## First power-on check

There is no jog mode. Homing is the direction check: it drives 1A, 2A and 3A to retract and
the lift up, and nothing else. Keep hands clear and a hand on the PRESSURE ON/OFF valve.

Coil directions were checked at the rig on 2026-11-10 by touching a 24V lead to each `SOL`
jack: `SOL-01` pushes 1A out, `SOL-03` pushes 2A out, `SOL-05` pulls 3A in, `SOL-07` raises
the lift. So homing energises `SOL-02`, `SOL-04`, `SOL-05` and then `SOL-07`. To re-check a
coil later, do the same with the PLC in Program mode, one coil at a time.

1. Magazine empty. Set `HMI_Reset` TRUE in the Variable Monitor (or press the black PB).
2. Watch the machine. All three cylinders should pull in and the platform should rise.
   Within a few seconds `HR_Step` returns to 0 and `ST_Homed` is TRUE.
3. If it stops with `HR_Step = 90` and `HR_FaultCode = 9`:
   - **A cylinder pushed out, or the platform dropped:** that valve is working backwards.
     Swap that cylinder's two `DO` plugs (or its two air tubes), then Reset.
   - **Everything is physically home but it still faulted:** a reed switch is on the wrong
     input. Look at `IN_FeedRetracted`, `IN_UpperEjRetracted` and `IN_LowerEjRetracted`. The
     one that is FALSE has its two inputs the wrong way round: swap that pair of lines in
     section 1 of `Kit1_Sort.st` (or swap the two OUT leads on the panel).
4. `HR_FaultCode = 1` means the E-stop input (DI-12) is off: not patched, pressed, or an
   NO contact.
5. Then run one part at a time: selector OFF, one part in the magazine, Start.

## Build the CCW project

**Follow [CCW_SETUP.md](CCW_SETUP.md).** It is the click-by-click version for the rig as it
stands: the variables grouped by type for typing in, build, download, a dry run with the air
off, first homing, first parts, and a batch.

In outline:

1. **Global variables:** add every `Global` row of `Kit1_Variables.csv` (55) with its data
   type and initial value.
2. **Program:** *Programs → Add → New ST: Structured Text*, name it `Kit1_Sort`. Add every
   `Local` row (44) to its Local Variables (`TON` and `R_TRIG` are instance types). Paste
   `Kit1_Sort.st` into the editor.
3. *Build*. Fix anything it flags (see "If CCW rejects a line" below).
4. *Download* and switch the controller to *Remote Run*. Check `HR_Heartbeat` counts up.
5. Only once the machine sorts reliably, for the twin link:
   - **Ethernet:** give the PLC a static address, e.g. `192.168.1.10 / 255.255.255.0`. It was
     found on an automatic `169.254.x.x` address, which can change after a power cycle.
   - **Modbus mapping:** *Controller → Modbus Mapping*. Add every row of
     `Kit1_Modbus_Map.csv`: BOOL variables as coils at `000001…000032`, INT variables as
     holding registers at `400001…400010`. The Micro850 serves Modbus TCP on port 502 and
     the server is disabled by default in recent CCW versions, so enable it. Check
     `HR_MapVersion = 1`.

The program that was on the controller when it arrived was not backed up before the first
download.

### If CCW rejects a line

The code sticks to plain IEC 61131-3 that Micro800 supports (CASE, TON, R_TRIG,
`ANY_TO_DINT`, `ANY_TO_INT`). It has not been built in CCW yet. Function blocks are called
with positional arguments, `tStep(NOT stepChanged, T#1h);`, which is the form the Micro800
instruction manual uses. The lines most likely to need a tweak:

- `cycleMs := ANY_TO_DINT(tCycle.ET);` converts TIME to milliseconds. If it is refused,
  use `cycleMs := TIME_TO_DINT(tCycle.ET);`.
- Boolean assignments from comparisons, e.g. `stepChanged := HR_Step <> stepPrev;`.
  If refused, wrap them: `stepChanged := (HR_Step <> stepPrev);`.

## Test without the rig

```bash
cd Tools/Kit1PlcSimulator
dotnet run -c Release -- selftest   # logic scenarios in simulated time
dotnet run -c Release -- linktest   # real Modbus TCP, read with the Unity client code
dotnet run -c Release -- serve      # soft PLC on 127.0.0.1:502 for the Unity twin
```

In `serve` mode: S start, X stop, R reset, E E-stop, C continuous/single, L load parts,
M/P add metal/plastic, J jam the feed cylinder, Q quit. Options: `--port N` (default 502),
`--bind IP` (default 127.0.0.1), `--demo` (press Start and reload the magazine automatically),
`--parts MPMP…` (exact magazine order: M = metal, P = plastic).
