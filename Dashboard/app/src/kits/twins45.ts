import { PartState, pileCounts, release, TwinDef } from './twins';

// Kit 4 and Kit 5 come from the Solid Edge IGES exports ("4th_Kit final.igs",
// "5th_kit final.igs"), converted by Dashboard/tools/iges-to-glb.mjs. Mesh names are
// P001.. in assembly-tree order; the numbers below were read from the converter's
// report (each part's box). There is no Unity twin or PLC program for these kits yet:
// the strokes and the sequence here are worked out from the geometry and are ASSUMED
// until checked against the real kits.
//
// Each cylinder is three CAD parts: barrel, mounting and rod. The rod is its own part,
// so it is the rod (with the pusher fixed to it) that moves, not the barrel.
const BELT_SPEED = 70;      // mm/s, assumed

// ----------------------------------------------------------------------- Kit 4
// Magazine -> 1A pushes the bottom part onto the conveyor -> the conveyor carries it
// under the sensor -> either it rides on into the end bin, or 2A pushes it off the
// belt sideways and 3A pushes it into the side bin. A fourth cylinder (4A) faces 2A
// across the belt; its job is not known, so it stays still.
export const kit4: TwinDef = {
  url: '/models/kit4.glb',
  fullNames: true,
  centre: [10, 20, 95],
  distance: 116,
  azimuth: -0.75,
  groups: [
    { id: 'feed', components: ['P092', 'P025'] },
    { id: 'divert', components: ['P095', 'P030'] },
    { id: 'eject', components: ['P089', 'P044'] },
    { id: 'spare', components: ['P098', 'P029'] },
  ],
  bins: ['P048', 'P054', 'P099'],
  stock: [0, -1],
  stockList: ['P056', 'P057', 'P058', 'P059', 'P063', 'P064', 'P065'],
  activePart: 'P066',
  partHome: [26.5, 80.5, 107],
  partHeight: 10,
  rest: [
    [27, -195, 5],        // end of the conveyor
    [-38, -150, 5],       // side bin
  ],
  pose(g, v) {
    g.feed.position.y = -50 * (v['1A'] ?? 0);
    g.divert.position.x = -56 * (v['2A'] ?? 0);
    g.eject.position.y = -55 * (v['3A'] ?? 0);
    g.spare.position.x = 50 * (v['4A'] ?? 0);
  },
  part(s, v, step, def, dt) {
    const [hx, hy, hz] = def.partHome;
    if ((s.mode === 'magazine' || s.mode === 'gone') && step === 11) {
      s.mode = 'table';
      s.data = { fed: 0, belt: 0, side: 0, out: 0 };
    } else if (s.mode === 'gone' && (step === 10 || step === 0)) {
      s.mode = 'magazine';
      s.material = 0;
      s.pos.set(hx, hy, hz);
    }

    if (s.mode !== 'table') return;
    const d = s.data;
    d.fed = Math.max(d.fed, 50 * (v['1A'] ?? 0) - 1);
    const onBelt = d.fed >= 48;
    const underSensor = 100.5;                       // belt travel from the feed point to the sensor
    if (onBelt && (v.M1 ?? 0) > 0.5 && d.side <= 0) {
      d.belt = Math.min(d.belt + BELT_SPEED * dt, step === 15 ? 400 : underSensor);
    }

    if (d.belt >= underSensor - 0.5) d.side = Math.max(d.side, 56 * (v['2A'] ?? 0) - 6);
    if (d.side >= 49) d.out = Math.max(d.out, 55 * (v['3A'] ?? 0) - 3.5);
    s.pos.set(hx - Math.max(0, d.side), hy - Math.max(0, d.fed) - d.belt - Math.max(0, d.out), hz);
    if (d.out >= 45) release(s, def, 1, pileCounts);
    else if (s.pos.y < -150) release(s, def, 0, pileCounts);
  },
};

// ----------------------------------------------------------------------- Kit 5
// Three magazines: blocks, and two kinds of insert. 1A pushes a block to the middle,
// 2A and 3A push one insert each into it, the conveyor carries the assembly under the
// sensor, and it either rides on into the end bin or 4A pushes it off into the side bin.
export const kit5: TwinDef = {
  url: '/models/kit5.glb',
  fullNames: true,
  centre: [0, 40, 105],
  distance: 116,
  azimuth: -0.75,
  groups: [
    { id: 'feed', components: ['P105', 'P017'] },
    { id: 'insertA', components: ['P099', 'P013'] },
    { id: 'insertB', components: ['P102', 'P014'] },
    { id: 'reject', components: ['P119', 'P111'] },
  ],
  bins: ['P114', 'P074', 'P076', 'P077', 'P078'],
  stock: [0, -1],
  stockList: [
    'P020', 'P021', 'P022', 'P023',
    'P037', 'P038', 'P039', 'P040', 'P041', 'P042', 'P043', 'P046', 'P047',
    'P030', 'P031', 'P032', 'P033', 'P034', 'P035', 'P036', 'P044', 'P045',
  ],
  hidden: ['P019'],         // the block at the bottom of the magazine: the block in play starts there instead
  activePart: 'P018',       // drawn in the CAD at the middle position
  partHome: [26.5, 41.5, 130],
  partHeight: 26,
  props: [
    { mesh: 'P029', key: 'a' },
    { mesh: 'P028', key: 'b' },
  ],
  rest: [
    [26, -195, 5],        // end of the conveyor
    [-60, -69, 5],        // side bin
  ],
  pose(g, v) {
    g.feed.position.x = -35 * (v['1A'] ?? 0);
    g.insertA.position.y = -48 * (v['2A'] ?? 0);
    g.insertB.position.x = 48 * (v['3A'] ?? 0);
    g.reject.position.x = -60 * (v['4A'] ?? 0);
  },
  part(s: PartState, v, step, def, dt) {
    const [hx, hy, hz] = def.partHome;
    const d = s.data;
    if ((s.mode === 'magazine' || s.mode === 'gone') && step === 11) {
      s.mode = 'table';
      s.data = { fed: 0, a: 0, b: 0, belt: 0, out: 0 };
    } else if (s.mode === 'gone' && (step === 10 || step === 0)) {
      s.mode = 'magazine';
      s.material = 0;
      s.data = {};
    }

    if (s.mode === 'magazine') {
      s.pos.set(hx + 35, hy, hz);
      return;
    }

    if (s.mode === 'table') {
      const t = s.data;
      t.fed = Math.max(t.fed, 35 * (v['1A'] ?? 0));
      if (t.fed >= 34.5) {
        t.a = Math.max(t.a, 48 * (v['2A'] ?? 0) - 21);
        t.b = Math.max(t.b, 48 * (v['3A'] ?? 0) - 21);
      }

      const underSensor = 110.5;
      if (t.a >= 26 && t.b >= 26 && (v.M1 ?? 0) > 0.5 && t.out <= 0) {
        t.belt = Math.min(t.belt + BELT_SPEED * dt, step === 19 ? 400 : underSensor);
      }

      if (t.belt >= underSensor - 0.5) t.out = Math.max(t.out, 60 * (v['4A'] ?? 0) - 5);
      s.pos.set(hx + 35 - t.fed - Math.max(0, t.out), hy - t.belt, hz);
      // the inserts: pushed in, then carried along with the block
      t.ax = -Math.max(0, t.out);
      t.ay = -Math.max(0, t.a) - t.belt;
      t.bx = Math.max(0, t.b) - Math.max(0, t.out);
      t.by = -t.belt;
      t.rx = s.pos.x;
      t.ry = s.pos.y;
      t.rz = s.pos.z;
      if (t.out >= 50) release(s, def, 1, pileCounts);
      else if (s.pos.y < -150) release(s, def, 0, pileCounts);
      return;
    }

    if (s.mode === 'falling' && d.rx !== undefined) {
      // inserts drop with the block
      const dx = s.pos.x - d.rx, dy = s.pos.y - d.ry, dz = s.pos.z - d.rz;
      d.rx = s.pos.x;
      d.ry = s.pos.y;
      d.rz = s.pos.z;
      d.ax += dx; d.ay += dy; d.az = (d.az ?? 0) + dz;
      d.bx += dx; d.by += dy; d.bz = (d.bz ?? 0) + dz;
    } else if (s.mode === 'gone') {
      d.ah = 1;      // the finished assembly is shown as the block only once it is in the bin
      d.bh = 1;
    }
  },
};
