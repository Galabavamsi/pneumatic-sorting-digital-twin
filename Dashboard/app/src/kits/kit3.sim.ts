import profile from './kit3.profile.json';
import type { Profile, SimDef } from '../sim/engine';

// Kit 3 as in PLC/Kit3/Kit3_Sort.st, with shorter times.
// kit3.profile.json is a copy of Tools/PlcGateway/profiles/kit3.json.
export const kit3Sim: SimDef = {
  profile: profile as Profile,
  plans: {
    plastic: [
      [10, 1500], [11, 800], [12, 800], [13, 500], [14, 1200], [15, 1200], [16, 1300],
      [17, 1200], [18, 1300], [19, 1300], [20, 1300], [23, 900], [26, 200],
    ],
    metal: [
      [10, 1500], [11, 800], [12, 800], [13, 500], [14, 1200], [15, 1200], [16, 1300],
      [17, 1200], [18, 1300], [19, 1300], [20, 1300], [21, 1500], [23, 900], [25, 1500], [26, 200],
    ],
  },
  cycleStartStep: 11,
  materialStep: 13,
  initial: { feedExt: false, stampDown: false, slideExt: false, liftUp: true, rotTray2: false, vacuum: false },
  apply(step, cmd) {
    switch (step) {
      case 11: cmd.feedExt = true; break;
      case 12: cmd.feedExt = false; break;
      case 14: cmd.stampDown = true; break;
      case 15: cmd.stampDown = false; break;
      case 16: cmd.slideExt = true; break;
      case 17: cmd.liftUp = false; break;
      case 18: cmd.vacuum = true; break;
      case 19: cmd.liftUp = true; break;
      case 20: cmd.slideExt = false; break;
      case 21: cmd.rotTray2 = true; break;
      case 23: cmd.vacuum = false; break;
      case 25: cmd.rotTray2 = false; break;
    }
  },
  io: (cmd, step, ms, metal) => ({
    _IO_EM_DI_00: !cmd.feedExt && !(step === 12 && ms < 400),
    _IO_EM_DI_01: cmd.feedExt && ms > 400,
    _IO_EM_DI_02: true,
    _IO_EM_DI_03: metal && (step === 12 || step === 13),
    _IO_EM_DI_12: true,
    _IO_EM_DI_13: true,
    _IO_EM_DO_00: cmd.feedExt,
    _IO_EM_DO_01: !cmd.feedExt,
    _IO_EM_DO_02: cmd.stampDown,
    _IO_EM_DO_03: !cmd.stampDown,
    _IO_EM_DO_04: !cmd.slideExt,
    _IO_EM_DO_05: cmd.slideExt,
    _IO_EM_DO_06: cmd.liftUp,
    _IO_EM_DO_07: !cmd.liftUp,
    _IO_EM_DO_08: !cmd.rotTray2,
    _IO_EM_DO_09: cmd.rotTray2,
    _IO_EM_DO_10: cmd.vacuum,
  }),
  settings: {
    K3_T_CAMERASETTLE: 500, K3_T_STAMP: 3000, K3_T_SLIDE: 3000, K3_T_LIFT: 3000,
    K3_T_ROTARY: 3000, K3_T_VACUUM: 3000, K3_T_RELEASE: 2000, K3_CFG_CAMERAONISMETAL: 1,
  },
  startDelayMs: 1800,
};
