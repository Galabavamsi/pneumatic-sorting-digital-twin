# Kit 3 acceptance checklist

## Verified manual commissioning baseline

- [x] Feeder presents one workpiece with a 50 mm stroke and retracts without a direct eject
- [x] Magazine bodies fall one CAD pitch and settle without intersection
- [x] Stamp reaches the workpiece and returns home
- [x] Vacuum slide follows its physical rail axis
- [x] Vacuum lift stays between the 5 mm approach and 20 mm clearance endpoints
- [x] Vacuum captures and carries an independent rigid-body workpiece
- [x] Rotary reaches Bin 1 at 0° and Bin 2 at -90°

## Automatic batch validation

1. Open `Assets/Scenes/Kit3Viewer.unity` and enter Play mode.
2. Press `S` or click **START BATCH**.
3. Confirm every cycle follows feed, retract, stamp, pickup, retract, route,
   release, and return-home order, with no automatic vacuum-shaft lift motion.
4. Confirm blue pieces fall into Bin 1 and orange pieces into Bin 2.
5. During a cycle, verify `P` pauses without losing the held part, then resumes.
6. Run step mode with `N` and verify exactly one state transition per press.
7. Press `X` with and without a held part and verify a controlled safe-home return.
8. Confirm the final dashboard total is 16, the magazine is empty, and no timeout
   or collision fault is shown.

## Acceptance result

Passed in Unity Play mode on 2026-09-08. The continuous sequence, color routes,
workpiece pickup/release, dashboard, step, pause, and safe-stop behavior were
observed and confirmed against this checklist.
