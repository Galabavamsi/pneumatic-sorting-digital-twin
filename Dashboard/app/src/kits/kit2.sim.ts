import profile from './kit2.profile.json';
import type { Profile, SimDef } from '../sim/engine';

// Kit 2 follows the Unity twin's automatic sequence (Kit2AutomaticSequence.cs):
// there is no PLC program for it yet. The feed and transfer cylinders have three
// positions: home, stage (at the stamp) and eject.
const FEED_STAGE = 50 / 90;
const TRANSFER_STAGE = 56 / 90;

export const kit2Sim: SimDef = {
  profile: profile as Profile,
  plans: {
    metal: [[10, 1300], [11, 1000], [12, 900], [13, 600], [20, 900], [21, 900], [22, 1300], [23, 1200], [40, 200]],
    plastic: [[10, 1300], [11, 1000], [12, 900], [13, 600], [30, 1100], [31, 1000], [32, 900], [33, 900], [34, 1300], [35, 1200], [40, 200]],
  },
  cycleStartStep: 11,
  materialStep: 13,
  initial: { feedStage: false, feedEject: false, xferStage: false, xferEject: false, stampA: false, stampB: false },
  apply(step, cmd) {
    switch (step) {
      case 11: cmd.feedStage = true; break;
      case 12: cmd.feedStage = false; break;
      case 20: cmd.stampA = true; break;
      case 21: cmd.stampA = false; break;
      case 22: cmd.feedEject = true; break;
      case 23: cmd.feedEject = false; break;
      case 30: cmd.xferStage = true; break;
      case 31: cmd.xferStage = false; break;
      case 32: cmd.stampB = true; break;
      case 33: cmd.stampB = false; break;
      case 34: cmd.xferEject = true; break;
      case 35: cmd.xferEject = false; break;
    }
  },
  io: (cmd, step, ms, metal) => {
    const settled = ms > 500;
    const feedHome = !cmd.feedStage && !cmd.feedEject;
    const xferHome = !cmd.xferStage && !cmd.xferEject;
    const atA = (step === 11 && settled) || step === 12 || step === 13 || step === 20 || step === 21 || (step === 22 && !settled) || (step === 30 && !settled);
    const atB = (step === 30 && settled) || step === 31 || step === 32 || step === 33 || (step === 34 && !settled);
    return {
      DI_MagazinePartPresent: true,
      DI_PartAtStampA: atA,
      DI_PartAtStampB: atB,
      DI_MetalDetected: atA && metal && step >= 13,
      DI_CapacitiveDetected: atA && !metal && step >= 13,
      DI_FeedHome: feedHome && !((step === 12 || step === 23) && !settled),
      DI_FeedStage: cmd.feedStage && settled,
      DI_FeedEject: cmd.feedEject && settled,
      DI_TransferHome: xferHome && !((step === 31 || step === 35) && !settled),
      DI_TransferStage: cmd.xferStage && settled,
      DI_TransferEject: cmd.xferEject && settled,
      DI_StampAUp: !cmd.stampA && !(step === 21 && !settled),
      DI_StampADown: cmd.stampA && settled,
      DI_StampBUp: !cmd.stampB && !(step === 33 && !settled),
      DI_StampBDown: cmd.stampB && settled,
      DI_UtilityReady: true,
      DO_FeedHome: feedHome,
      DO_FeedStage: cmd.feedStage,
      DO_FeedEject: cmd.feedEject,
      DO_TransferHome: xferHome,
      DO_TransferStage: cmd.xferStage,
      DO_TransferEject: cmd.xferEject,
      DO_StampADown: cmd.stampA,
      DO_StampBDown: cmd.stampB,
    };
  },
  travel: (cmd) => ({
    FEED: cmd.feedEject ? { travel: 1, label: 'Eject' } : cmd.feedStage ? { travel: FEED_STAGE, label: 'At Stamp A' } : { travel: 0, label: 'Home' },
    XFER: cmd.xferEject ? { travel: 1, label: 'Eject' } : cmd.xferStage ? { travel: TRANSFER_STAGE, label: 'At Stamp B' } : { travel: 0, label: 'Home' },
  }),
  settings: { feedStage: 50, feedEject: 90, transferStage: 56, stampStroke: 30 },
  startDelayMs: 900,
  fault: { everyParts: 9, ms: 12000, code: 2 },
};
