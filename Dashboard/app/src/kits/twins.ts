import * as THREE from 'three';

// How each kit's CAD model moves. The component numbers, strokes and directions
// are the ones the Unity twin's scripts use; the models are the same CAD exports
// (Assets/Models/KitN/KitN_components.obj) converted by Dashboard/tools/obj-to-glb.mjs.
//
// Coordinates are the CAD's: millimetres, Z up. Unity's OBJ importer mirrors X,
// so a Unity "transform.right" is CAD -X; the directions below are CAD directions.
export type V3 = [number, number, number];

export type PartState = {
  mode: 'magazine' | 'table' | 'held' | 'falling' | 'gone';
  pos: THREE.Vector3;          // centre of the workpiece in play, CAD space
  target: THREE.Vector3;       // where a released part comes to rest
  bin: number;                 // which pile it joins: 0 or 1
  material: number;            // 0 unknown, 1 metal, 2 plastic
  data: Record<string, number>;
};

export type TwinDef = {
  url: string;
  centre: V3;                  // point the camera looks at
  distance: number;            // camera distance, in scene units (cm)
  azimuth: number;
  groups: { id: string; parent?: string; components: string[]; pivot?: V3 }[];
  bins: string[];              // drawn see-through so dropped parts stay visible
  stock: [number, number];     // component number range of the waiting workpieces
  activePart: string;
  partHome: V3;
  partHeight: number;
  highlight?: { component: string; actuator: string };     // e.g. the vacuum cup
  fullNames?: boolean;         // meshes are matched by their whole name (P087), not the last three digits
  stockList?: string[];        // waiting workpieces, when they are not one number range
  hidden?: string[];           // CAD parts not drawn (a duplicate of the workpiece in play)
  // extra pieces that travel with the workpiece (Kit 5's inserts): the mesh is moved by
  // data[key + 'x' | 'y' | 'z'] millimetres from its CAD position, hidden when data[key + 'h'] is 1
  props?: { mesh: string; key: string }[];
  // extra jobs on the machine besides the one in play, drawn as copies of the workpiece:
  // shown when data[key + 'v'] is 1, at data[key + 'x' | 'y' | 'z'] (CAD mm), coloured by data[key + 'm']
  extras?: string[];
  // the waiting jobs in the magazine, bottom first (not counting the active part), so the stack
  // can drop by one each time the feed cylinder has gone out and come back
  stack?: { order: string[]; pitch: number; feed: string };
  rest: V3[];                  // where parts pile up, per bin
  // What each PLC step commands (actuator id -> travel 0..1) and the orders a cycle runs in.
  // The live feed is sampled every 250-400 ms, so short steps are often missed; with this
  // the viewer plays the skipped steps itself, so the workpiece logic sees every step.
  drive?: Drive;
  // v: eased travel (0..1) of each actuator, by the snapshot's actuator id
  pose: (groups: Record<string, THREE.Group>, v: Record<string, number>) => void;
  part: (s: PartState, v: Record<string, number>, step: number, def: TwinDef, dt: number) => void;
};

export type Drive = {
  commands: Record<number, Record<string, number>>;
  paths: { steps: number[]; material?: number }[];      // one per branch, from the waiting step to the count
};

// The steps the machine must have passed through going from `from` to `to`, not counting
// either; null when `to` is not reachable along a cycle (a fault, homing, idle).
export const stepsBetween = (drive: Drive, from: number, to: number, material: number): number[] | null => {
  const paths = [...drive.paths].sort((a, b) => Number(b.material === material) - Number(a.material === material));
  for (const p of paths) {
    const a = p.steps.indexOf(from);
    const b = p.steps.indexOf(to);
    if (a >= 0 && b > a) return p.steps.slice(a + 1, b);
  }

  // into the next cycle
  const pa = paths.find((p) => p.steps.includes(from));
  const pb = drive.paths.find((p) => p.steps.includes(to));      // the new job's branch is not known yet
  if (pa && pb) return [...pa.steps.slice(pa.steps.indexOf(from) + 1), ...pb.steps.slice(0, pb.steps.indexOf(to))];
  return null;
};

// Where the n-th job in a tray comes to rest: two side by side, then the next layer.
export const pileSpot = (def: TwinDef, bin: number, n: number): V3 => {
  const r = def.rest[bin];
  const side = n % 2 === 0 ? -1 : 1;
  return [r[0] + side * 14, r[1] + (n % 3 - 1) * 9, r[2] + def.partHeight / 2 + Math.floor(n / 2) * def.partHeight];
};

export const release = (s: PartState, def: TwinDef, bin: number, piles: number[]) => {
  s.bin = bin;
  s.target.set(...pileSpot(def, bin, Math.min(piles[bin], 5)));
  s.mode = 'falling';
};

// piles: how many parts already lie in each bin (set by the viewer before part())
export const pileCounts = [0, 0];

// ----------------------------------------------------------------------- Kit 1
// PneumaticCylinderTest.cs (feed 025 + 028, 40 mm, -Y), SecondCylinderTest.cs (lower
// ejector 026 + 055, 50 mm, -Y), ThirdCylinderTest.cs (upper ejector 027 + 056, 50 mm, -X),
// LiftCylinderTest.cs (029 + 052, 31.2 mm up; the CAD pose is lift DOWN),
// MagazineFeedTest.cs (approach 12 / 8 mm, release at 37.5 / 41.5 mm).
const kit1: TwinDef = {
  url: '/models/kit1.glb',
  centre: [0, 10, 110],
  distance: 88,
  azimuth: -0.8,
  groups: [
    { id: 'feed', components: ['025', '028'] },
    { id: 'lower', components: ['026', '055'] },
    { id: 'upper', components: ['027', '056'] },
    { id: 'lift', components: ['029', '052'] },
  ],
  bins: ['023', '024', '059'],
  stock: [30, 46],
  activePart: '042',
  stack: { order: ['030', '044', '032', '041', '037', '040', '031', '045', '036', '039', '033', '046', '035', '043', '034'], pitch: 10, feed: '1A' },
  partHome: [-20.7, -6.7, 111],
  partHeight: 10,
  rest: [
    [-103, -55, 0],      // off the upper level, towards -X
    [-20, -131, 0],      // off the lower level, towards -Y
  ],
  // PLC/Kit1/Kit1_Sort.st
  drive: {
    commands: { 3: { '1A': 1 }, 4: { '1A': 0 }, 6: { '2A': 1 }, 7: { '2A': 0 }, 8: { '4A': 1 }, 9: { '3A': 1 }, 10: { '3A': 0 }, 11: { '4A': 0 } },
    paths: [
      { material: 2, steps: [2, 3, 4, 5, 6, 7, 12] },
      { material: 1, steps: [2, 3, 4, 5, 8, 9, 10, 11, 12] },
    ],
  },
  pose(g, v) {
    g.feed.position.y = -40 * (v['1A'] ?? 0);
    g.upper.position.x = -50 * (v['2A'] ?? 0);
    g.lower.position.y = -50 * (v['3A'] ?? 0);
    g.lift.position.z = 31.2 * (1 - (v['4A'] ?? 0));      // 4A = 1 is lift down
  },
  part(s, v, step, def) {
    const [hx, hy, hz] = def.partHome;
    if ((s.mode === 'magazine' || s.mode === 'gone') && (step === 3 || step === 4)) {
      s.mode = 'table';
      s.data = { fed: 0, up: 0, low: 0 };
    } else if (s.mode === 'gone' && (step === 2 || step === 0 || step === 1)) {
      s.mode = 'magazine';
      s.material = 0;
      s.pos.set(hx, hy, hz);
    }

    if (s.mode !== 'table') return;
    const d = s.data;
    const liftDown = v['4A'] ?? 0;
    d.fed = Math.max(d.fed, 40 * (v['1A'] ?? 0));
    const onLift = d.fed >= 39.5;
    if (onLift && liftDown <= 0.02) d.up = Math.max(d.up, 50 * (v['2A'] ?? 0) - 12);
    if (onLift && liftDown >= 0.98) d.low = Math.max(d.low, 50 * (v['3A'] ?? 0) - 8);
    s.pos.set(hx - Math.max(0, d.up), hy - d.fed - Math.max(0, d.low), hz - (onLift && d.up <= 0 ? 31.2 * liftDown : 0));
    if (d.up >= 37.5) release(s, def, 0, pileCounts);
    else if (d.low >= 41.5) release(s, def, 1, pileCounts);
    else if (step < 3 && d.fed < 1) {
      s.mode = 'magazine';
      s.pos.set(hx, hy, hz);
    }
  },
};

// ----------------------------------------------------------------------- Kit 2
// Components and directions from the Unity scripts: Kit2FeedCylinderCalibration.cs
// (029 + 018, -Y), Kit2SecondCylinderCalibration.cs (030 + 031, -X),
// Kit2StampingCylinderCalibration.cs (A: 032 + 034, B: 033 + 035, 30 mm down).
//
// The sequence follows the real rig (PLC/Kit2/Kit2_Sort.st), which differs from the
// Unity twin: the cylinders have two positions, and a stamped job stays at its stamp
// until the next job of that kind pushes it on, off the edge, into its tray.
const K2_FEED = 50;        // mm, magazine to Stamp 1 (stroke taken from the Unity twin)
const K2_ACROSS = 56;      // mm, Stamp 1 to Stamp 2
const K2_JOB = 25;         // mm, length of a job: the gap the next job closes before it pushes
const K2_TIP = 20;         // mm a waiting job is pushed before it tips into the tray

const kit2: TwinDef = {
  url: '/models/kit2.glb',
  centre: [0, 0, 120],
  distance: 92,
  azimuth: -0.8,
  groups: [
    { id: 'feed', components: ['029', '018'] },
    { id: 'transfer', components: ['030', '031'] },
    { id: 'stampA', components: ['032', '034'] },
    { id: 'stampB', components: ['033', '035'] },
  ],
  bins: ['027', '028', '058', '016'],     // the two trays, the magazine tube and the plate over the stamps
  stock: [36, 51],
  activePart: '036',
  partHome: [-1.7, -24.8, 119.8],
  partHeight: 10,
  // magazine order, bottom to top, from Kit2MagazineFeedCalibration.cs
  stack: { order: ['051', '041', '045', '040', '044', '039', '050', '038', '049', '037', '048', '043', '047', '042', '046'], pitch: 10, feed: 'FEED' },
  extras: ['a', 'b'],        // the job waiting at Stamp 1, and the one waiting at Stamp 2
  rest: [
    [-2, -150, 5],       // Tray 1, straight on from Stamp 1
    [-134, -75, 5],      // Tray 2, straight on from Stamp 2
  ],
  // PLC/Kit2/Kit2_Sort.st
  drive: {
    commands: { 11: { FEED: 1 }, 12: { FEED: 0 }, 20: { SA: 1 }, 21: { SA: 0 }, 30: { XFER: 1 }, 31: { XFER: 0 }, 33: { SB: 1 }, 34: { SB: 0 } },
    paths: [
      { material: 1, steps: [10, 11, 12, 13, 20, 21, 40] },
      { material: 2, steps: [10, 11, 12, 13, 30, 31, 32, 33, 34, 40] },
    ],
  },
  pose(g, v) {
    g.feed.position.y = -K2_FEED * (v.FEED ?? 0);
    g.transfer.position.x = -K2_ACROSS * (v.XFER ?? 0);
    g.stampA.position.z = -30 * (v.SA ?? 0);
    g.stampB.position.z = -30 * (v.SB ?? 0);
  },
  part(s, v, step, def) {
    const [hx, hy, hz] = def.partHome;
    const ay = hy - K2_FEED;             // Stamp 1 is straight ahead of the magazine
    const bx = hx - K2_ACROSS;           // Stamp 2 is across from Stamp 1
    let d = s.data;
    const inTransfer = step >= 30 && step <= 34;

    // plastic, stamped: from now on it is "the job waiting at Stamp 2"
    if (s.mode === 'table' && (d.across ?? 0) >= K2_ACROSS - 0.5 && !inTransfer) {
      d.bv = 1;
      d.bm = s.material;
      d.across = 0;
      s.mode = 'gone';
    }

    // a new feed starts
    if (step === 11 && d.started !== 1) {
      const waitingAtA = s.mode === 'table';      // metal, stamped, still at Stamp 1
      s.data = d = { started: 1, fed: 0, across: 0, av: waitingAtA ? 1 : (d.av ?? 0), am: waitingAtA ? s.material : (d.am ?? 0), bv: d.bv ?? 0, bm: d.bm ?? 0 };
      s.mode = 'table';
      s.material = 0;
      s.pos.set(hx, hy, hz);
    }

    if (step !== 11) d.started = 0;
    if (s.mode === 'gone' && (step === 10 || step === 0 || step === 1)) {
      s.mode = 'magazine';
      s.material = 0;
      s.pos.set(hx, hy, hz);
    }

    let pushA = 0;
    let pushB = 0;
    if (s.mode === 'table') {
      d.fed = Math.max(d.fed ?? 0, K2_FEED * (v.FEED ?? 0));
      if (d.fed >= K2_FEED - 0.5) d.across = Math.max(d.across ?? 0, K2_ACROSS * (v.XFER ?? 0));
      s.pos.set(hx - (d.across ?? 0), hy - d.fed, hz);
      // the incoming job closes the gap, then pushes the waiting one ahead of it
      pushA = Math.max(0, d.fed - (K2_FEED - K2_JOB));
      pushB = Math.max(0, (d.across ?? 0) - (K2_ACROSS - K2_JOB));
    }

    if (d.av === 1) {
      d.ax = hx;
      d.ay = ay - pushA;
      d.az = hz;
      if (pushA >= K2_TIP) {
        d.av = 0;
        d.fly = 1; d.flyX = d.ax; d.flyY = d.ay; d.flyZ = hz; d.flyBin = 0; d.flyMat = d.am;
      }
    }

    if (d.bv === 1) {
      d.bx = bx - pushB;
      d.by = ay;
      d.bz = hz;
      if (pushB >= K2_TIP) {
        d.bv = 0;
        d.fly = 1; d.flyX = d.bx; d.flyY = d.by; d.flyZ = hz; d.flyBin = 1; d.flyMat = d.bm;
      }
    }
  },
};

// ----------------------------------------------------------------------- Kit 3
// Kit3ManualCommissioning.cs (feed 032 + 034, 50 mm; stamp 035 + 044, 45 mm; workpieces
// 050..065; pick-up 033), Kit3RotaryCalibration.cs (arm turns 90 degrees about the centre
// of 048; slide 033 / 040 / 041, 50 mm; lift group, 20 mm).
const K3_PIVOT: V3 = [408, 285, 0];
const K3_CUP: V3 = [310.55, 283.9, 115.9];      // cup centre and underside in the CAD pose
const K3_CONTACT = 4.6;                         // lifts the cup out of the part it sits in in the CAD pose

const kit3: TwinDef = {
  url: '/models/kit3.glb',
  centre: [230, 245, 105],
  distance: 92,
  azimuth: 0.45,
  groups: [
    { id: 'feed', components: ['032', '034'] },
    { id: 'stamp', components: ['035', '044'] },
    { id: 'turn', components: ['023', '024', '036', '048'], pivot: K3_PIVOT },
    { id: 'lift', parent: 'turn', components: ['014', '020', '022', '031', '046', '069'] },
    { id: 'slide', parent: 'lift', components: ['033', '040', '041'] },
  ],
  bins: ['027', '028', '039'],
  stock: [50, 65],
  activePart: '050',
  partHome: [212.3, 285.5, 115.2],
  partHeight: 10,
  highlight: { component: '033', actuator: 'VC' },
  rest: [
    [310, 284, 5],       // Tray 1, under the pulled-in head
    [409, 187, 5],       // Tray 2, a quarter turn away
  ],
  // PLC/Kit3/Kit3_Sort.st (22 and 24 only run when the head reaches Tray 2 pushed out)
  drive: {
    commands: {
      11: { '1A': 1 }, 12: { '1A': 0 }, 14: { '2A': 1 }, 15: { '2A': 0 }, 16: { '3A': 1 }, 17: { '4A': 1 },
      18: { VC: 1 }, 19: { '4A': 0 }, 20: { '3A': 0 }, 21: { '5A': 1 }, 22: { '3A': 1 }, 23: { VC: 0 }, 24: { '3A': 0 }, 25: { '5A': 0 },
    },
    paths: [
      { material: 2, steps: [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23, 24, 25, 26] },
      { material: 1, steps: [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24, 25, 26] },
      { material: 1, steps: [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26] },
    ],
  },
  pose(g, v) {
    g.feed.position.x = 50 * (v['1A'] ?? 0);
    g.stamp.position.z = -45 * (v['2A'] ?? 0);
    g.turn.rotation.z = (Math.PI / 2) * (v['5A'] ?? 0);
    g.lift.position.z = K3_CONTACT + 20 * (1 - (v['4A'] ?? 0));     // 4A = 1 is arm down
    g.slide.position.x = -50 * (v['3A'] ?? 0);                      // 3A pushes towards the stamp station
  },
  part(s, v, step, def) {
    const [hx, hy, hz] = def.partHome;
    const angle = (Math.PI / 2) * (v['5A'] ?? 0);
    const rx = K3_CUP[0] - 50 * (v['3A'] ?? 0) - K3_PIVOT[0];
    const ry = K3_CUP[1] - K3_PIVOT[1];
    const cupX = K3_PIVOT[0] + rx * Math.cos(angle) - ry * Math.sin(angle);
    const cupY = K3_PIVOT[1] + rx * Math.sin(angle) + ry * Math.cos(angle);
    const cupZ = K3_CUP[2] + K3_CONTACT + 20 * (1 - (v['4A'] ?? 0));
    const vacuum = (v.VC ?? 0) > 0.5;
    const feeding = step === 11 || step === 12;

    if ((s.mode === 'magazine' || s.mode === 'gone') && feeding) {
      s.mode = 'table';
      s.pos.set(hx, hy, hz);
    } else if (s.mode === 'gone' && (step === 10 || step === 0)) {
      s.mode = 'magazine';
      s.material = 0;
      s.pos.set(hx, hy, hz);
    }

    if (s.mode === 'table') {
      s.pos.x = Math.max(s.pos.x, hx + 50 * (v['1A'] ?? 0));      // pushed, then left there
      const top = s.pos.z + def.partHeight / 2;
      // reach tolerances from the Unity twin: 18 mm sideways, 12 mm above
      if (vacuum && Math.hypot(cupX - s.pos.x, cupY - s.pos.y) < 18 && cupZ - top < 12) {
        s.mode = 'held';
      } else if (!feeding && step < 11) {
        s.mode = 'magazine';
        s.pos.set(hx, hy, hz);
      }
    }

    if (s.mode === 'held') {
      s.pos.set(cupX, cupY, cupZ - def.partHeight / 2);
      if (!vacuum) {
        const bin = (v['5A'] ?? 0) > 0.5 ? 1 : 0;
        def.rest[bin][0] = cupX;      // it drops straight down from wherever the head is
        def.rest[bin][1] = cupY;
        release(s, def, bin, pileCounts);
      }
    }
  },
};

import { kit4, kit5 } from './twins45';

export const TWINS: Record<string, TwinDef> = { kit1, kit2, kit3, kit4, kit5 };
