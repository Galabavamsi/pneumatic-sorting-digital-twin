import type { SimDef } from '../sim/engine';
import { kit1Sim } from './kit1.sim';
import { kit2Sim } from './kit2.sim';
import { kit3Sim } from './kit3.sim';
import { kit4Sim, kit5Sim } from './kit45.sim';

// The stations on the floor. The id matches the kit id the gateway reports
// (Tools/PlcGateway/profiles), which is how a live controller is matched to a station.
export type Station = { id: string; name: string; title: string; sim?: SimDef };

export const STATIONS: Station[] = [
  { id: 'kit1', name: 'Kit 1', title: 'Metal / plastic sort', sim: kit1Sim },
  { id: 'kit2', name: 'Kit 2', title: 'Two-station stamp and sort', sim: kit2Sim },
  { id: 'kit3', name: 'Kit 3', title: 'Stamp and vacuum sort', sim: kit3Sim },
  { id: 'kit4', name: 'Kit 4', title: 'Conveyor sort', sim: kit4Sim },
  { id: 'kit5', name: 'Kit 5', title: 'Assembly and inspection', sim: kit5Sim },
];
