import profile from './kit2.profile.json';
import type { Profile, SimDef } from '../sim/engine';

// Kit 2 as in PLC/Kit2/Kit2_Sort.st, with shorter times. 1A feeds a job to Stamp 1;
// metal is stamped there by 3A, plastic is pushed across by 2A and stamped by 4A.
// A stamped job stays put until the next job of that kind pushes it into its tray.
// kit2.profile.json is a copy of Tools/PlcGateway/profiles/kit2.json.
const start: [number, number][] = [[10, 1200], [11, 1000], [12, 900], [13, 600]];

export const kit2Sim: SimDef = {
  profile: profile as Profile,
  plans: {
    metal: [...start, [20, 1100], [21, 900], [40, 200]],
    plastic: [...start, [30, 1100], [31, 900], [32, 400], [33, 1100], [34, 900], [40, 200]],
  },
  cycleStartStep: 11,
  materialStep: 13,
  initial: { feed: false, xfer: false, stampA: false, stampB: false },
  apply(step, cmd) {
    switch (step) {
      case 11: cmd.feed = true; break;
      case 12: cmd.feed = false; break;
      case 20: cmd.stampA = true; break;
      case 21: cmd.stampA = false; break;
      case 30: cmd.xfer = true; break;
      case 31: cmd.xfer = false; break;
      case 33: cmd.stampB = true; break;
      case 34: cmd.stampB = false; break;
    }
  },
  io: (cmd, step, ms, metal) => {
    const settled = ms > 500;
    return {
      _IO_EM_DI_00: !cmd.feed && !(step === 12 && !settled),
      _IO_EM_DI_01: cmd.feed && settled,
      _IO_EM_DI_02: !cmd.xfer && !(step === 31 && !settled),
      _IO_EM_DI_03: cmd.xfer && settled,
      _IO_EM_DI_04: !cmd.stampA && !(step === 21 && !settled),
      _IO_EM_DI_05: cmd.stampA && settled,
      _IO_EM_DI_06: !cmd.stampB && !(step === 34 && !settled),
      _IO_EM_DI_07: cmd.stampB && settled,
      _IO_EM_DI_08: metal && step >= 12 && step <= 21,
      _IO_EM_DI_09: !metal && step >= 31 && step <= 40,
      _IO_EM_DI_10: true,
      _IO_EM_DI_12: true,
      _IO_EM_DI_13: true,
      _IO_EM_DO_00: cmd.feed,
      _IO_EM_DO_01: !cmd.feed,
      _IO_EM_DO_02: cmd.xfer,
      _IO_EM_DO_03: !cmd.xfer,
      _IO_EM_DO_04: cmd.stampA,
      _IO_EM_DO_05: !cmd.stampA,
      _IO_EM_DO_06: cmd.stampB,
      _IO_EM_DO_07: !cmd.stampB,
    };
  },
  settings: { K2_T_MOVETIMEOUT: 4000, K2_T_DETECTSETTLE: 500, K2_T_STAMPDWELL: 500, K2_T_SEENATB: 1500, K2_CFG_S1ISRETRACTED: 1 },
  startDelayMs: 900,
  fault: { everyParts: 9, ms: 12000, code: 4 },
};
