# Hardware integration — real rig to digital twin

**Status as of 2026-11-05.** Working notes for collaborators, written from a photo survey of the
physical trainer plus a read of the existing Unity codebase. Read this before starting work on the
PLC link so we do not duplicate findings.

Confidence is marked throughout: **confirmed** means read directly off a label or verified in code,
**probable** means inferred and worth checking at the machine, **unknown** means nobody has
measured it yet.

> **Update, later on 2026-11-05.** A second pass over all 52 photos corrected several points
> below. Where they disagree, these corrections win:
>
> - **PLC catalogue** is partly legible: `…E-48QBB`, so probably `2080-L50E-48QBB` (28 DI / 20 DO).
>   Only DI 0–15 and DO 0–19 reach the patch panel; terminals I-16…I-27 are unwired. Input
>   common is 0 V DC (PNP sensors).
> - **2A is horizontal**, on top of the KIT-1(B) frame (the photo used before was rotated 90°).
>   It is the **upper ejector**.
> - **3A sits under the KIT-1(A) plate**, below 1A. It is the **lower ejector**. Its part
>   number (19201) matches 1A, so the 50 mm stroke is inferred rather than read.
> - The black clamps on 1A/2A/3A are **empty sensor brackets**, not reed switches. No reed switch
>   or lead is visible in any photo. Reed switches and the fibre amplifier are being fitted for
>   bring-up.
> - BPRS-620 has **no amplifier attached** yet, so it is not a DI until one is fitted.
> - The operator push buttons, selector and E-stop each have two unlabelled jacks and are **not
>   hardwired** to the PLC.
> - The conveyor AI(V)/AI(C) jacks are the VFD speed reference, so they need a PLC AO, not an AI.
> - **Section 1 has a workaround after all.** Only the git-lfs batch API is refused; GitHub's
>   media server (the file page's Raw/download link) serves all three meshes intact, with SHA-256
>   matching the pointers. See "Getting the meshes" in [KIT1_BRINGUP.md](KIT1_BRINGUP.md).
>
> The resulting Kit 1 PLC program, patch list and Modbus map are in [`PLC/Kit1`](../PLC/Kit1/README.md).
> The read-only twin link described in sections 5 and 8 is implemented in
> `Assets/Scripts/Runtime/PlcLink`. The hardware-day checklist is [KIT1_BRINGUP.md](KIT1_BRINGUP.md).
> The proposed register block in section 5 was refined into 32 coils plus 10 holding
> registers; `PLC/Kit1/Kit1_Modbus_Map.csv` is authoritative.

---

## 1. Blocker: the CAD meshes cannot be downloaded

This stops anyone new from running the project at all, so it comes first.

All three kit meshes are Git LFS pointers that will not resolve:

| File | On disk | Actual size |
|---|---:|---:|
| `Assets/Models/Kit1/Kit1_components.obj` | 133 B | 75,076,043 B |
| `Assets/Models/Kit2/Kit2_components.obj` | 133 B | 73,569,105 B |
| `Assets/Models/Kit3/Kit3_components.obj` | 134 B | 170,488,521 B |

`git lfs pull` fails with:

```
batch response: This repository exceeded its LFS budget.
The account responsible for the budget should increase it to restore access.
```

That is a server-side refusal, so there is no client-side workaround. Verified as blocked: repeated
pulls, per-kit pulls, authenticated and unauthenticated requests, forks (there are none), and
release attachments (there are none).

Without the meshes Unity opens and compiles fine, but every controller script fails its
`GameObject.Find("Component_NNN")` lookup, logs an error and sets `enabled = false`. The scene
renders an empty station and nothing responds.

### How to fix it

**Preferred — send the three `.obj` files directly** (Drive, WeTransfer, USB). Drop them into
`Assets/Models/Kit1|Kit2|Kit3/`. The matching `.meta` files are already committed, so asset GUIDs
are preserved and every prefab reference and component-name binding reconnects with no re-import
work.

**Alternative** — the repo owner buys a GitHub LFS data pack (~$5/month per 50 GB bandwidth) and
the existing `git lfs pull` starts working.

**Longer term** — 319 MB of mesh in LFS on a free account will hit this ceiling for every person
who clones. Moving the `.obj` files to a GitHub Release attachment sidesteps LFS bandwidth
entirely and costs nothing.

---

## 2. The physical rig

**Gravitaz Automation "MATK-R5M" Modular Automation Training Kit.** Five kit modules (KIT-1 to
KIT-5) bolt onto a shared slotted aluminium table and are driven from one control panel.

Photo evidence lives in `system/` (whole rig) and `kit-1-pics/` (Kit 1 detail).

### Control panel — confirmed

| Component | Part |
|---|---|
| PLC | Allen-Bradley **Micro850** |
| Analog input expansion | `2080-IF2` (2 channel) |
| Analog output expansion | `2080-OF2` (2 channel) |
| Power supply | MEAN WELL `EDR-120-24` — 24 VDC, 5 A |
| VFD | SELEC 0.4 kW 230 V |
| Conveyor motor | DKM `A8IG25NH` AC induction |
| Load cell controller | MULTISPAN `LD-1252A` |
| HMI | 7" touchscreen, panel-labelled MATK-R5M |

External ports: `ETHERNET-PLC`, `ETHERNET-HMI`, 220 V inlet, VACUUM.

> The exact Micro850 catalogue number (`2080-LC50-…`) has **not** been read yet. It determines the
> onboard I/O count and needs confirming from the device label.

### Patch panel I/O — confirmed

The I/O is **not hardwired**. Everything terminates in 4 mm banana jacks and is patched per
experiment:

- `DI-0` … `DI-15` — 16 digital inputs
- `DO-0` … `DO-19` — 20 digital outputs, plus a 24 VDC rail
- `AI-0 (V)/(C)`, `AI-1 (V)/(C)` + `AI-0V` — 2 analog inputs, voltage or current
- `AO-0 (V)/(C)`, `AO-1 (V)/(C)` + `AO-0V` — 2 analog outputs
- `SOL-01` … `SOL-10`, `VC` (vacuum), `SOL (0V)`
- `CONVEYOR CONTROL` — AI(C), AI(V), FWD, REV, NC, NO, COM, U, V, W
- `LOAD CELL CONTROL` — I/P, O/P, AO(C), NO, NC, COM
- Operator devices — selector switch, green PB, red PB, black PB, E-stop mushroom

**Every DI channel has its own 24V and 0V jack beside it.** Each sensor is therefore self-contained:
three cables, no shared rail, and mis-patching one sensor cannot disturb another.

This validates the existing architecture. `Kit1PlcIo` keeps logical `DI_*` / `DO_*` tags with
physical addresses deliberately left `TBD` — that was the correct call, because on this rig the
address map is a cabling decision, not a fixed property of the machine.

### Pneumatics — confirmed

Festo valve manifold, 10 solenoid coils driving 5 double-acting cylinder channels:

```
SOL-01 (1S1) / SOL-02 (1S2)  →  cylinder 1
SOL-03 (2S1) / SOL-04 (2S2)  →  cylinder 2
SOL-05 (3S1) / SOL-06 (3S2)  →  cylinder 3
SOL-07 (4S1) / SOL-08 (4S2)  →  cylinder 4
SOL-09 (5S1) / SOL-10 (5S2)  →  cylinder 5
```

Pneumatic patch points `PP-01` … `PP-10` plus VACUUM. Air prep is a shut-off valve
(PRESSURE ON/OFF), an FRL unit, and a COMPRESSOR INPUT LINE.

---

## 3. Kit 1 in detail

### Cylinder labels are per-kit

Each kit plate carries its own `1A`–`4A` labels. KIT-3's `2A` is a different physical cylinder from
KIT-1's `2A`. Do not treat these as global IDs.

### Kit 1 actuators

| Label | Part | Orientation | Probable twin axis | Confidence |
|---|---|---|---|---|
| `1A` | Festo `DSNU-16-50` | horizontal, KIT-1(A) plate | feed cylinder | part number partly obscured in photo |
| `2A` | Festo `DSNU-16-…` | vertical, KIT-1(B) | ejector | stroke unread |
| `3A` | Festo `DSNU-16-50` | horizontal | ejector | stroke unread |
| `4A` | Festo `ADNGF-16-30-P-A` (554217) | guided, vertical | **lift** | confirmed |

All are 16 mm bore, p max 1 MPa (145 psi).

`4A` being a **30 mm guided** cylinder is a good corroboration: the twin models the lift at 31.2 mm,
and a guided cylinder is exactly what a lift platform needs. The twin's 40 mm feed stroke against a
`DSNU-16-50` still needs measuring.

Each cylinder carries clamp-on reed switches — visible as black plastic clamps on the barrels.
The count per cylinder has not been established.

### Kit 1 sensors

- `IS-01` — inductive proximity, metal detection, PNP NO
- `CS-01` — capacitive proximity, PNP NO
- `BPRS-620` — fibre-optic sensor head, thin twin-core fibre. **Purpose unconfirmed**; magazine
  part-present is the working guess.

`IS-01` and `CS-01` are mounted **side by side on one bracket** above the bins. That is precisely
the inductive/capacitive pair modelled by `Kit1SensorSimulation`.

A sensor label reading `BTH BCR12-PN6` — DC 10–40 V, max 150 mA, PNP NO, 1–6 mm sensing, 100 Hz —
was photographed among the Kit 1 sensor leads. It is one of `IS-01` / `CS-01`, most likely the
capacitive one, but the association was inferred rather than traced.

### Wiring convention — confirmed and consistent

All 3-wire PNP sensors:

```
BROWN  →  +24 V
BLACK  →  OUT (signal)
BLUE   →  0 V
```

Each sensor cable is spliced through a `PCT-228` lever connector (32 A 250 V, 0.08–2.5 mm²) into
three labelled banana leads: `<TAG> (24V)`, `<TAG> (0V)`, `<TAG> (OUT)`.

To patch one sensor: 24V lead into that DI channel's 24V jack, 0V lead into its 0V jack, OUT lead
into the DI jack itself.

---

## 4. The constraint that shapes the whole design

**The rig has no continuous position feedback.** Reed switches report *extended* or *retracted* and
nothing in between. There are only 2 analog inputs on the whole panel, and they are spoken for by
the load cell and conveyor.

A live-linked Unity twin therefore **cannot read true mid-stroke position**. It must animate between
end states on a travel time calibrated against the real cylinders. Endpoints will match the machine
exactly; mid-stroke is honest interpolation.

Consequence: **measuring each axis's travel time at working pressure is a required calibration
step**, not an optional nicety. Without it the twin will visibly lead or lag the machine.

---

## 5. Proposed live-link architecture

### Transport

**Modbus TCP over the `ETHERNET-PLC` port.** The Micro850 includes a Modbus TCP server, mature C#
client libraries exist, and read-only polling is low risk. EtherNet/IP is the native Rockwell
protocol and remains an option, but needs a heavier client for no near-term benefit.

### PLC side

Pack every input, output, state value and counter into **one contiguous holding-register block** so
Unity needs a single read per cycle rather than many. A first cut:

| Register | Contents |
|---|---|
| `HR0` | digital input bitfield (16 bits — all DI) |
| `HR1` | solenoid output bitfield |
| `HR2` | sequence state enum |
| `HR3` | parts completed |
| `HR4` | metal count |
| `HR5` | plastic count |
| `HR6` | fault word |
| `HR7` | cycle time, ms |

Eight registers polled at 10–20 Hz is trivial load for both ends.

### Unity side

`Kit1PlcIo` is already the correct injection point and needs no structural change. Today its `DI_*`
properties are fed by `Kit1SensorSimulation`. A live mode swaps that source for the polled register
block.

Everything above the PLC layer — `Kit1AutomaticSequence`, `Kit1EngineeringDashboard`,
`ProductionMonitor`, `DigitalTwinHistorian` — keeps working untouched. That is the payoff from the
original controller-neutral design.

Poll on a background thread, not in `Update()`. Start strictly read-only; guarded writes come only
after the read path is proven and reviewed.

---

## 6. Environment notes

### Unity version

The project is stamped for editor `6000.6.0f1`. If only a different patch release is installed,
launching from the command line **exits immediately with code 1** — there is no Hub to mediate the
mismatch, and the log shows nothing between licensing and exit.

Either install `6000.6.0f1` exactly, open through Unity Hub, or set
`ProjectSettings/ProjectVersion.txt` to the installed version. The revision hash is printed in the
first lines of the editor log.

### Compile health

Verified on `6000.6.8f1`: **0 errors**, 184 warnings. Every warning is the same `CS0618` across 35
files — `FindFirstObjectByType` is deprecated in favour of `FindAnyObjectByType`. Functionally
harmless today, and `FindAnyObjectByType` is the semantically correct call for these singleton
lookups. A mechanical cleanup whenever someone wants it.

### Git LFS

The repo ships a git-lfs binary in `.tools/` and a `setup-env.sh` bootstrap. If `git-lfs` is not on
PATH, `git lfs install --local` leaves hooks that warn on every checkout. Install git-lfs properly
before configuring the hooks.

---

## 7. Open questions — need someone at the machine

**Blocking the PLC link:**

1. Full Micro850 catalogue number from the device label.
2. Is the `ETHERNET-PLC` port configured with an IP, and on what subnet?
3. Does the Micro850 already hold a working Kit 1 program, or is it blank?
4. Is Connected Components Workbench available? (Free edition covers Micro800.)
5. Is Modbus TCP enabled in the controller configuration?

**Needed to wire Kit 1:**

6. How many reed switches per cylinder — one or two? Are they labelled `1S1`/`1S2` style?
7. Which `SOL-xx` channels does Kit 1 occupy? Are all four cylinders double-acting?
8. What does `BPRS-620` actually sense?
9. Is there a magazine-empty sensor distinct from part-present? The twin models both
   (`DI_MagazineEmpty`, `DI_MagazinePartPresent`).

**Measure once powered:**

10. Actual stroke of `1A`, `2A`, `3A`. (`4A` is confirmed 30 mm.)
11. **Travel time per axis at working pressure** — the animation calibration.
12. FRL regulator setting, in bar. The twin currently assumes 6 bar nominal / 3.5 bar minimum as
    simulation defaults, not measured values.

**Context:**

13. Is Kit 1 currently assembled and plumbed on the table? Several photos show it part-packed.

---

## 8. Suggested order of work

1. Unblock the CAD (section 1) — nothing visual can be verified until this lands.
2. Answer questions 1–5 and establish that a laptop can reach the PLC over Ethernet.
3. Decide the Kit 1 DI/DO allocation and write it up as a tag map, taken to the machine and patched
   against.
4. Write or inspect the Micro850 program, exposing the register block in section 5.
5. Build the Unity Modbus client, read-only, behind a feature switch so offline simulation keeps
   working.
6. Measure travel times and calibrate the twin's animation.
7. Only then consider guarded writes from Unity.

---

## Safety

This repository is a simulation and training tool, not a safety controller. Unity, a PC application
and network communications are never substitutes for hardwired emergency stops, guards, pressure
isolation or safety-rated PLC functions. Live pneumatic commissioning must be supervised by
qualified personnel at the machine.
