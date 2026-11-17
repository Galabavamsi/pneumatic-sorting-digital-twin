import type { MachineEvent, Snapshot } from '../types';

// Simulation engine that runs inside the app, so the dashboard can be shown
// while the rig is unplugged. A kit definition gives the step plan and what each
// step commands; the engine plays it on a clock and builds the same snapshot the
// gateway would send for a real controller.
export type Profile = {
  id: string;
  name: string;
  title: string;
  description: string;
  steps: { id: number; name: string }[];
  sequence: number[];
  faults: Record<string, string>;
  counters: { id: string; label: string; var: string }[];
  inputs: { id: string; label: string; var: string }[];
  outputs: { id: string; label: string; var: string }[];
  actuators: { id: string; label: string; kind: string; a: { var: string | null; label: string }; b: { var: string | null; label: string } }[];
  settings: { label: string; var: string; unit: string }[];
};

export type Plan = [step: number, ms: number][];
export type Cmd = Record<string, boolean>;

export type SimDef = {
  profile: Profile;
  plans: { metal: Plan; plastic: Plan };
  cycleStartStep: number;
  materialStep: number;                 // step at which the part's material becomes known
  initial: Cmd;
  apply: (step: number, cmd: Cmd) => void;
  io: (cmd: Cmd, step: number, msInStep: number, metal: boolean) => Record<string, boolean>;
  settings: Record<string, number>;
  // For cylinders with more than two positions: travel as a fraction of full stroke.
  travel?: (cmd: Cmd) => Record<string, { travel: number; label: string }>;
  classes?: { one: string; two: string };   // names of the two outcomes; default Metal / Plastic
  oneEvery?: number;                    // outcome "one" for every Nth part; default is every other part
  startDelayMs?: number;                // stagger machines so they are not in step
  rest?: { everyParts: number; ms: number };                 // magazine refill pause
  fault?: { everyParts: number; ms: number; code: number };  // an occasional simulated stop
};

export function createSim(def: SimDef) {
  const { profile } = def;
  const startedAt = new Date().toISOString();
  const names = def.classes ?? { one: 'Metal', two: 'Plastic' };
  const stepName = (id: number) => profile.steps.find((s) => s.id === id)?.name ?? 'Step ' + id;
  let index = 0;
  let stepStart = Date.now() + (def.startDelayMs ?? 0);
  let metal = false;
  let parts = 0;
  let metalCount = 0;
  let plasticCount = 0;
  let material = 0;
  let cycleStart: number | null = null;
  let pause: { kind: 'rest' | 'fault'; until: number; since: number } | null = null;
  const history: number[] = [];
  const cmd: Cmd = { ...def.initial };
  const events: MachineEvent[] = [{ ts: startedAt, kind: 'info', text: `Simulation started: ${profile.name}, ${profile.title.toLowerCase()}` }];

  const log = (kind: MachineEvent['kind'], text: string) => {
    events.unshift({ ts: new Date().toISOString(), kind, text });
    if (events.length > 80) {
      events.pop();
    }
  };

  return function read(): Snapshot {
    const now = Date.now();
    let plan = metal ? def.plans.metal : def.plans.plastic;

    if (pause && now >= pause.until) {
      log('info', pause.kind === 'fault' ? 'Fault cleared' : 'Magazine refilled');
      pause = null;
      stepStart = now;
    }

    if (!pause && now - stepStart >= plan[index][1]) {
      stepStart = now;
      index++;
      if (index >= plan.length) {
        index = 0;
        parts++;
        if (metal) {
          metalCount++;
        } else {
          plasticCount++;
        }

        if (cycleStart !== null) {
          const seconds = Math.round((now - cycleStart) / 100) / 10;
          history.push(seconds);
          if (history.length > 30) {
            history.shift();
          }

          log('part', `Part ${parts} sorted · ${(metal ? names.one : names.two).toLowerCase()} · ${seconds.toFixed(1)} s`);
          cycleStart = null;
        }

        metal = def.oneEvery ? (parts + 1) % def.oneEvery === 0 : !metal;
        material = 0;
        Object.assign(cmd, def.initial);
        if (def.fault && parts % def.fault.everyParts === 0) {
          pause = { kind: 'fault', until: now + def.fault.ms, since: now };
          log('fault', `Fault ${def.fault.code} · ${profile.faults[String(def.fault.code)] ?? 'Unknown fault'}`);
        } else if (def.rest && parts % def.rest.everyParts === 0) {
          pause = { kind: 'rest', until: now + def.rest.ms, since: now };
          log('info', 'Magazine empty: batch finished');
        }
      }

      if (!pause) {
        plan = metal ? def.plans.metal : def.plans.plastic;
        const entered = plan[index][0];
        log('step', `${entered} · ${stepName(entered)}`);
        if (entered === def.cycleStartStep) {
          cycleStart = now;
        }
      }
    }

    const faulted = pause?.kind === 'fault';
    const resting = pause?.kind === 'rest';
    const step = faulted ? 90 : resting ? 0 : plan[index][0];
    if (!pause) {
      def.apply(step, cmd);
      if (step === def.materialStep) {
        material = metal ? 1 : 2;
      }
    }

    const since = pause ? pause.since : Math.min(stepStart, now);
    const live = !faulted;     // a fault drops every output, as the PLC programs do
    const io = def.io(cmd, step, now - since, metal);
    const counts: Record<string, number> = { parts, metal: metalCount, plastic: plasticCount };
    const on = (name: string | null) => (name ? (io[name] ?? false) && (live || name.includes('_DI_')) : false);

    return {
      ts: new Date(now).toISOString(),
      gateway: { version: 'in-app', mode: 'demo', readOnly: true, startedAt },
      link: { connected: true, plc: 'simulated', product: 'Simulation in the app', error: null, readMs: 0 },
      kit: { id: profile.id, name: profile.name, title: profile.title, description: profile.description, steps: profile.steps, sequence: profile.sequence },
      status: {
        state: faulted ? 'fault' : resting ? 'idle' : 'running',
        step,
        stepName: stepName(step),
        stepSince: new Date(since).toISOString(),
        faultCode: faulted ? def.fault!.code : 0,
        faultText: faulted ? (profile.faults[String(def.fault!.code)] ?? 'Unknown fault') : null,
        material,
        materialName: material === 1 ? names.one : material === 2 ? names.two : 'None',
        heartbeat: Math.floor(now / 100) % 30000,
      },
      cycle: {
        inProgressSince: cycleStart !== null ? new Date(cycleStart).toISOString() : null,
        lastSeconds: history.length > 0 ? history[history.length - 1] : null,
        averageSeconds: history.length > 0 ? Math.round((history.reduce((a, b) => a + b, 0) / history.length) * 10) / 10 : null,
        history: [...history],
      },
      counters: profile.counters.map((c) => ({ id: c.id, label: c.label, value: counts[c.id] ?? null })),
      inputs: profile.inputs.map((i) => ({ id: i.id, label: i.label, on: on(i.var) })),
      outputs: profile.outputs.map((o) => ({ id: o.id, label: o.label, on: on(o.var) })),
      actuators: profile.actuators.map((a) => {
        const aOn = io[a.a.var ?? ''] ?? false;       // valves keep their place when the coils drop
        const multi = def.travel?.(cmd)[a.id];
        if (multi) {
          return {
            id: a.id,
            label: a.label,
            kind: a.kind as 'linear' | 'rotary' | 'switch',
            position: multi.travel > 0 ? ('a' as const) : ('b' as const),
            positionLabel: multi.label,
            aLabel: a.a.label,
            bLabel: a.b.label,
            driven: live,
            travel: multi.travel,
          };
        }

        return {
          id: a.id,
          label: a.label,
          kind: a.kind as 'linear' | 'rotary' | 'switch',
          position: aOn ? ('a' as const) : ('b' as const),
          positionLabel: aOn ? a.a.label : a.b.label,
          aLabel: a.a.label,
          bLabel: a.b.label,
          driven: a.kind === 'switch' ? aOn && live : live,
        };
      }),
      settings: profile.settings.map((s) => ({ label: s.label, unit: s.unit, value: def.settings[s.var] ?? null })),
      events: [...events],
    };
  };
}
