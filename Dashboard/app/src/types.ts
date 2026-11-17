// Shape of the snapshot published by Tools/PlcGateway.
export type Step = { id: number; name: string };

export type Snapshot = {
  ts?: string;
  gateway?: { version: string; mode: 'plc' | 'demo'; readOnly: boolean; startedAt: string };
  link: { connected: boolean; plc?: string | null; product?: string | null; error?: string | null; readMs?: number };
  kit?: { id: string; name: string; title: string; description: string; steps: Step[]; sequence: number[] };
  status?: {
    state: 'running' | 'idle' | 'homing' | 'fault' | 'unknown';
    step: number;
    stepName: string | null;
    stepSince: string;
    faultCode: number;
    faultText: string | null;
    material: number;
    materialName: string;
    heartbeat?: number | null;
  };
  cycle?: { inProgressSince: string | null; lastSeconds: number | null; averageSeconds: number | null; history: number[] };
  counters?: { id: string; label: string; value: number | null }[];
  inputs?: IoPoint[];
  outputs?: IoPoint[];
  actuators?: Actuator[];
  settings?: { label: string; unit: string; value: number | null }[];
  events?: MachineEvent[];
};

export type IoPoint = { id: string; label: string; on: boolean | null };

export type Actuator = {
  id: string;
  label: string;
  kind: 'linear' | 'rotary' | 'switch';
  position: 'a' | 'b' | null;
  positionLabel: string;
  aLabel: string;
  bLabel: string;
  driven: boolean;
  travel?: number;      // 0..1 of full stroke, for cylinders with more than two positions
};

export type MachineEvent = { ts: string; kind: 'info' | 'warn' | 'step' | 'fault' | 'part'; text: string };

export type Freshness = 'connecting' | 'live' | 'stale' | 'unreachable';
