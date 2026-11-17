import profile from './kit1.profile.json';
import type { Profile, SimDef } from '../sim/engine';

// Kit 1 as in PLC/Kit1/Kit1_Sort.st: plastic leaves at the upper level (2A, Tray 2),
// metal is lowered and leaves at the lower level (3A, Tray 1).
// kit1.profile.json is a copy of Tools/PlcGateway/profiles/kit1.json.
export const kit1Sim: SimDef = {
  profile: profile as Profile,
  plans: {
    plastic: [[2, 1200], [3, 900], [4, 900], [5, 700], [6, 1300], [7, 1100], [12, 200]],
    metal: [[2, 1200], [3, 900], [4, 900], [5, 700], [8, 1100], [9, 1300], [10, 1100], [11, 1100], [12, 200]],
  },
  cycleStartStep: 3,
  materialStep: 5,
  initial: { feed: false, upper: false, lower: false, liftUp: true },
  apply(step, cmd) {
    switch (step) {
      case 3: cmd.feed = true; break;
      case 4: cmd.feed = false; break;
      case 6: cmd.upper = true; break;
      case 7: cmd.upper = false; break;
      case 8: cmd.liftUp = false; break;
      case 9: cmd.lower = true; break;
      case 10: cmd.lower = false; break;
      case 11: cmd.liftUp = true; break;
    }
  },
  io: (cmd, step, ms, metal) => {
    const moving = ms < 450;
    const onLift = (step === 4 && !moving) || step === 5 || (step === 6 && moving) || step === 8 || (step === 9 && moving);
    return {
      _IO_EM_DI_00: !cmd.feed && !(step === 4 && moving),
      _IO_EM_DI_01: cmd.feed && !moving,
      _IO_EM_DI_02: !cmd.upper && !(step === 7 && moving),
      _IO_EM_DI_03: cmd.upper && !moving,
      _IO_EM_DI_04: !cmd.lower && !(step === 10 && moving),
      _IO_EM_DI_05: cmd.lower && !moving,
      _IO_EM_DI_06: onLift && cmd.liftUp,
      _IO_EM_DI_07: onLift && cmd.liftUp && metal,
      _IO_EM_DI_08: true,
      _IO_EM_DI_12: true,
      _IO_EM_DI_13: true,
      _IO_EM_DO_00: cmd.feed,
      _IO_EM_DO_01: !cmd.feed,
      _IO_EM_DO_02: cmd.upper,
      _IO_EM_DO_03: !cmd.upper,
      _IO_EM_DO_04: !cmd.lower,
      _IO_EM_DO_05: cmd.lower,
      _IO_EM_DO_06: cmd.liftUp,
      _IO_EM_DO_07: !cmd.liftUp,
    };
  },
  settings: {
    CFG_TRAVELTIME: 1000, CFG_MOVETIMEOUT: 3000, CFG_HOMETIMEOUT: 6000, CFG_DETECTSETTLE: 300,
    CFG_DETECTWINDOW: 1500, CFG_FEEDDELAY: 300, CFG_METALTOUPPER: 0,
  },
  rest: { everyParts: 8, ms: 7000 },
};
