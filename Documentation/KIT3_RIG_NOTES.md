# Kit 3 on the real rig: wiring notes

**Status on 2026-11-15:** wiring recorded from the handwritten sheet and photos. Nothing here has
been run from the PLC yet, and there is no Kit 3 PLC program. Items marked *unverified* still
need checking at the rig.

Same panel and PLC as Kit 1 (Micro850 `2080-L50E-48QBB`); see
[KIT1_SETUP_GUIDE.md](KIT1_SETUP_GUIDE.md) for the panel basics, including the lead from `24V`
to the `24VDC` jack that the PLC outputs need.

## What Kit 3 does

Feed a part from the magazine, stamp it, pick it up with a vacuum head, and drop it into
Tray 1 or Tray 2 according to the camera (silver metal part or black plastic part).

## Cylinders

| Label | Part | Job |
|---|---|---|
| 1A | (horizontal) | pushes the bottom part out of the magazine |
| 2A | Festo ADN-16-50-A-P-A, vertical | stamp |
| 3A | Festo ADNGF-16-50-P-A, guided, horizontal | carries the vacuum head out and back |
| 4A | Festo ADNGF-16-30-P-A, guided, vertical | raises and lowers the 3A arm |
| 5A | rotary actuator under 4A | turns the arm between the pickup side and Tray 2 |

## Air and solenoids (from the sheet)

| Port | Solenoid | Output | Effect |
|---|---|---|---|
| PP-01 | SOL-01 | DO-0 | 1A out (pushes the workpiece) |
| PP-02 | SOL-02 | DO-1 | 1A in |
| PP-03 | SOL-03 | DO-2 | 2A stamp down |
| PP-04 | SOL-04 | DO-3 | 2A stamp up |
| PP-05 | SOL-05 | DO-4 | 3A **in** (pipes swapped at the rig on 2026-11-15; the sheet says out) |
| PP-06 | SOL-06 | DO-5 | 3A **out** |
| PP-07 | SOL-07 | DO-6 | 4A raises the arm |
| PP-08 | SOL-08 | DO-7 | 4A lowers the arm |
| PP-09 | SOL-09 | DO-8 | 5A to its default side, towards the workpiece (clockwise) |
| PP-10 | SOL-10 | DO-9 | 5A to Tray 2 (anticlockwise) |
| VP | VC | DO-10 | vacuum on |

## Inputs

| Jack | Device | Sheet says | Read from the PLC on 2026-11-15 |
|---|---|---|---|
| DI-0 | 1A reed switch | 1A extended | **on** (*unverified* which end: on Kit 1 the sheet had this the wrong way round) |
| DI-1 | 1A reed switch | 1A retracted | off |
| DI-2 | workpiece / magazine detector | part present | on |
| DI-3 | camera | colour result | off (*unverified* which colour switches it on) |
| DI-12 | E-stop (left from Kit 1) | | on |

Only 1A has position switches. 2A, 3A, 4A and 5A have none, so the program will have to time
those moves. There is no vacuum "gripped" signal either.

## Still to confirm

See the questions in the session notes; the main ones are which colour switches the camera on,
which tray each material goes to, where Tray 1 is reached from, and whether 4A must lower for
the pick.

## Answers from the rig (2026-11-15)

- 1A was pulled in while `DI-0` was on, so **`DI-0` is the pulled-in switch and `DI-1` the pushed-out one** (the sheet has them the other way round, as on Kit 1).
- `DI-2` was on with a part in the magazine.
- **Tray 1 = plastic (black), Tray 2 = metal (silver).** Both are reached by the vacuum head; 1A never pushes a part into a tray.
- Every part is stamped.
- Green, red, black, E-stop and selector are patched as on Kit 1 (`DI-9` … `DI-13`).
- With air on and nothing commanded, the arm rests on the Tray 1 side, with 3A out and the arm raised.

## Program

[`PLC/Kit3/Kit3_Sort.st`](../PLC/Kit3/Kit3_Sort.st) with [`Kit3_Variables.csv`](../PLC/Kit3/Kit3_Variables.csv). Written on
2026-11-15, **not yet built or run**. Sequence: feed, read camera, stamp, head out, arm down,
vacuum on, arm up, head in; plastic is dropped there (Tray 1); metal turns to Tray 2 first.

Assumptions built into it, each a single setting to change:

| Setting | Assumed | Meaning |
|---|---|---|
| `K3_CFG_CameraOnIsMetal` | FALSE | camera output on = plastic |
| `K3_CFG_Tray2Extended` | FALSE | at Tray 2 the part is dropped with the head pulled in |
| `K3_T_*` | 0.5 to 1.5 s | travel times for the timed moves |

Also assumed: the camera sees the part where 1A leaves it, the arm must lower to pick, and
Tray 1 is under the head when 3A is pulled in on the pickup side.

## Status after the first runs (2026-11-15)

The program was built, downloaded and run on the rig. What was found:

- **Camera:** its output is on for metal, so `K3_CFG_CameraOnIsMetal` is `TRUE`. In one recorded
  cycle the camera input only pulsed on for about 0.15 s while 1A was pushing, and was off when
  the program read it; whether that part was metal was not confirmed. If metal parts are sorted
  as plastic, the program needs to latch the camera during the feed.
- **Coil pairing, confirmed by recording the outputs during a cycle:** `DO-4` / `DO-5` are the two
  coils of the 3A valve (`DO-5` head out, `DO-4` head in) and `DO-6` / `DO-7` the two coils of the
  4A valve (`DO-7` arm down, `DO-6` arm up). Two attempts to pair them differently made both
  coils of one valve come on together, so nothing moved in one step and two things moved in the next.
- **Open problem:** after the grip the PLC switches the arm-up coil on (tried for 2 s and 4 s)
  but the arm stays down until the head has pulled in. It does the same with the vacuum lead
  unplugged, so it is not the vacuum taking the air. Not yet checked: lifting the arm by hand
  with the head out (air off), and the flow-control screws on 4A.
- **Timings in the program now:** 3 s for every move, 6 s for the arm-up after the grip, 2 s for
  the drop. This last version has been downloaded but its effect was not confirmed.
