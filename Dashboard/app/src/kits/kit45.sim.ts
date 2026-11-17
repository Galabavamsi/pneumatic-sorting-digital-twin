import type { Profile, SimDef } from '../sim/engine';

// Kit 4 and Kit 5 have no PLC program, wiring list or Unity sequence yet. Their steps
// below are worked out from the CAD models (which cylinder can reach what) and are
// ASSUMED until someone confirms how the real kits are meant to run. I/O names are
// placeholders: nothing is wired.
const io = (prefix: string, labels: string[]) => labels.map((label, i) => ({ id: `${prefix}-${i + 1}`, label, var: `${prefix}${i + 1}` }));
const two = (id: string, label: string, aVar: string, aLabel = 'Out', bLabel = 'In') => ({ id, label, kind: 'linear', a: { var: aVar, label: aLabel }, b: { var: null, label: bLabel } });

// ----------------------------------------------------------------------- Kit 4
const kit4Profile: Profile = {
  id: 'kit4',
  name: 'Kit 4',
  title: 'Conveyor sort',
  description:
    'A part is pushed from the magazine onto a conveyor and carried under a sensor. Plastic rides on into the bin at the end of the belt; metal is pushed off the belt and then into the side bin. Simulation only: Kit 4 has no PLC program or wiring yet, and this sequence is worked out from the CAD model, not confirmed. The fourth cylinder is not used because its job is not known.',
  steps: [
    { id: 0, name: 'Idle' },
    { id: 10, name: 'Waiting for a part' },
    { id: 11, name: 'Feed onto the conveyor' },
    { id: 12, name: 'Feed back' },
    { id: 13, name: 'Convey to the sensor' },
    { id: 14, name: 'Detect material' },
    { id: 15, name: 'Convey to the end bin' },
    { id: 20, name: 'Push off the belt' },
    { id: 21, name: 'Diverter back' },
    { id: 22, name: 'Push into the side bin' },
    { id: 23, name: 'Ejector back' },
    { id: 40, name: 'Part counted' },
    { id: 90, name: 'Fault' },
  ],
  sequence: [11, 12, 13, 14, 15, 20, 21, 22, 23, 40],
  faults: { '1': 'Emergency stop pressed', '2': 'Part did not reach the sensor' },
  counters: [
    { id: 'parts', label: 'Parts sorted', var: 'parts' },
    { id: 'metal', label: 'Metal · side bin', var: 'metal' },
    { id: 'plastic', label: 'Plastic · end bin', var: 'plastic' },
  ],
  inputs: io('IN', ['Part in magazine', 'Feed home', 'Feed out', 'Part at sensor', 'Metal detected', 'Diverter home', 'Diverter out', 'Ejector home', 'Ejector out']),
  outputs: io('OUT', ['Feed out', 'Diverter out', 'Ejector out', 'Conveyor motor']),
  actuators: [
    two('1A', 'Feed pusher', 'OUT1'),
    two('2A', 'Diverter', 'OUT2'),
    two('3A', 'Side ejector', 'OUT3'),
    two('4A', 'Fourth cylinder (job not known)', 'none'),
    { id: 'M1', label: 'Conveyor', kind: 'switch', a: { var: 'OUT4', label: 'Running' }, b: { var: null, label: 'Stopped' } },
  ],
  settings: [
    { label: 'Feed stroke (assumed)', var: 'feed', unit: 'mm' },
    { label: 'Diverter stroke (assumed)', var: 'divert', unit: 'mm' },
    { label: 'Ejector stroke (assumed)', var: 'eject', unit: 'mm' },
  ],
};

const start: [number, number][] = [[10, 1200], [11, 1000], [12, 900], [13, 1700], [14, 600]];

export const kit4Sim: SimDef = {
  profile: kit4Profile,
  plans: {
    plastic: [...start, [15, 1900], [40, 200]],
    metal: [...start, [20, 1100], [21, 900], [22, 1100], [23, 900], [40, 200]],
  },
  cycleStartStep: 11,
  materialStep: 14,
  initial: { feed: false, belt: false, divert: false, eject: false },
  apply(step, cmd) {
    switch (step) {
      case 11: cmd.feed = true; break;
      case 12: cmd.feed = false; break;
      case 13: cmd.belt = true; break;
      case 14: cmd.belt = false; break;
      case 15: cmd.belt = true; break;
      case 20: cmd.divert = true; break;
      case 21: cmd.divert = false; break;
      case 22: cmd.eject = true; break;
      case 23: cmd.eject = false; break;
      case 40: cmd.belt = false; break;
    }
  },
  io: (cmd, step, ms, metal) => {
    const settled = ms > 500;
    return {
      IN1: true,
      IN2: !cmd.feed && !(step === 12 && !settled),
      IN3: cmd.feed && settled,
      IN4: step === 14 || (step === 13 && ms > 1400) || (step === 20 && !settled),
      IN5: metal && (step === 14 || (step === 20 && !settled)),
      IN6: !cmd.divert && !(step === 21 && !settled),
      IN7: cmd.divert && settled,
      IN8: !cmd.eject && !(step === 23 && !settled),
      IN9: cmd.eject && settled,
      OUT1: cmd.feed,
      OUT2: cmd.divert,
      OUT3: cmd.eject,
      OUT4: cmd.belt,
    };
  },
  settings: { feed: 50, divert: 56, eject: 55 },
  startDelayMs: 2600,
};

// ----------------------------------------------------------------------- Kit 5
const kit5Profile: Profile = {
  id: 'kit5',
  name: 'Kit 5',
  title: 'Assembly and inspection',
  description:
    'A block is pushed to the middle, an insert is pushed into it from each of two side magazines, and the conveyor carries the assembly under a sensor. Good assemblies ride on into the end bin; rejects are pushed off into the side bin. Simulation only: Kit 5 has no PLC program or wiring yet, and this sequence is worked out from the CAD model, not confirmed.',
  steps: [
    { id: 0, name: 'Idle' },
    { id: 10, name: 'Waiting for parts' },
    { id: 11, name: 'Feed the block' },
    { id: 12, name: 'Feed back' },
    { id: 13, name: 'Push in insert A' },
    { id: 14, name: 'Insert A cylinder back' },
    { id: 15, name: 'Push in insert B' },
    { id: 16, name: 'Insert B cylinder back' },
    { id: 17, name: 'Convey to the sensor' },
    { id: 18, name: 'Inspect' },
    { id: 19, name: 'Convey to the end bin' },
    { id: 20, name: 'Reject off the belt' },
    { id: 21, name: 'Reject cylinder back' },
    { id: 40, name: 'Assembly counted' },
    { id: 90, name: 'Fault' },
  ],
  sequence: [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 40],
  faults: { '1': 'Emergency stop pressed', '2': 'Insert A did not seat' },
  counters: [
    { id: 'parts', label: 'Assemblies made', var: 'parts' },
    { id: 'metal', label: 'Rejected · side bin', var: 'metal' },
    { id: 'plastic', label: 'Good · end bin', var: 'plastic' },
  ],
  inputs: io('IN', ['Blocks in magazine', 'Insert A in magazine', 'Insert B in magazine', 'Block feed home', 'Block in the middle', 'Insert A seated', 'Insert B seated', 'Assembly at sensor', 'Inspection passed', 'Reject cylinder home']),
  outputs: io('OUT', ['Block feed out', 'Insert A out', 'Insert B out', 'Reject out', 'Conveyor motor']),
  actuators: [
    two('1A', 'Block feed', 'OUT1'),
    two('2A', 'Insert A cylinder', 'OUT2'),
    two('3A', 'Insert B cylinder', 'OUT3'),
    two('4A', 'Reject cylinder', 'OUT4'),
    { id: 'M1', label: 'Conveyor', kind: 'switch', a: { var: 'OUT5', label: 'Running' }, b: { var: null, label: 'Stopped' } },
  ],
  settings: [
    { label: 'Block feed stroke (assumed)', var: 'feed', unit: 'mm' },
    { label: 'Insert stroke (assumed)', var: 'insert', unit: 'mm' },
    { label: 'Reject stroke (assumed)', var: 'reject', unit: 'mm' },
  ],
};

const build: [number, number][] = [[10, 1200], [11, 900], [12, 800], [13, 1000], [14, 800], [15, 1000], [16, 800], [17, 1900], [18, 700]];

export const kit5Sim: SimDef = {
  profile: kit5Profile,
  plans: {
    plastic: [...build, [19, 1900], [40, 200]],
    metal: [...build, [20, 1100], [21, 900], [40, 200]],
  },
  classes: { one: 'Reject', two: 'Good' },
  oneEvery: 4,
  cycleStartStep: 11,
  materialStep: 18,
  initial: { feed: false, a: false, b: false, belt: false, reject: false },
  apply(step, cmd) {
    switch (step) {
      case 11: cmd.feed = true; break;
      case 12: cmd.feed = false; break;
      case 13: cmd.a = true; break;
      case 14: cmd.a = false; break;
      case 15: cmd.b = true; break;
      case 16: cmd.b = false; break;
      case 17: cmd.belt = true; break;
      case 18: cmd.belt = false; break;
      case 19: cmd.belt = true; break;
      case 20: cmd.reject = true; break;
      case 21: cmd.reject = false; break;
      case 40: cmd.belt = false; break;
    }
  },
  io: (cmd, step, ms, reject) => {
    const settled = ms > 500;
    return {
      IN1: true,
      IN2: true,
      IN3: true,
      IN4: !cmd.feed && !(step === 12 && !settled),
      IN5: (step === 11 && settled) || (step >= 12 && step <= 16) || (step === 17 && !settled),
      IN6: (step === 13 && settled) || (step >= 14 && step <= 16),
      IN7: (step === 15 && settled) || step === 16,
      IN8: step === 18 || (step === 17 && ms > 1600) || (step === 20 && !settled),
      IN9: step === 18 && !reject,
      IN10: !cmd.reject && !(step === 21 && !settled),
      OUT1: cmd.feed,
      OUT2: cmd.a,
      OUT3: cmd.b,
      OUT4: cmd.reject,
      OUT5: cmd.belt,
    };
  },
  settings: { feed: 35, insert: 48, reject: 60 },
  startDelayMs: 3400,
};
