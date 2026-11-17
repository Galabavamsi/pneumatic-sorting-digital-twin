import { useEffect, useMemo, useRef, useState } from 'react';
import { Platform } from 'react-native';
import { createClient } from '@supabase/supabase-js';
import type { Freshness, Snapshot } from '../types';
import { createSim } from '../sim/engine';
import { STATIONS } from '../kits/stations';

// Where machine data comes from. All routes are read-only.
//
//   demo     every station is simulated inside the app (no network)
//   cloud    the gateway PC pushes snapshots to a Supabase table; used when the
//            EXPO_PUBLIC_SUPABASE_* values are set at build time
//   gateway  talk straight to Tools/PlcGateway on the same network
//
// On the web, "?gateway=http://host:8787" forces the direct route.
const SUPABASE_URL = process.env.EXPO_PUBLIC_SUPABASE_URL;
const SUPABASE_KEY = process.env.EXPO_PUBLIC_SUPABASE_ANON_KEY;
const MACHINE_ID = process.env.EXPO_PUBLIC_MACHINE_ID ?? 'rig-1';
const ENV_GATEWAY = process.env.EXPO_PUBLIC_GATEWAY_URL;

export type Source = { kind: 'cloud' | 'gateway' | 'demo'; label: string; url: string };
export type View = 'live' | 'demo';

// One station on the floor. `snapshot` is null when nothing is reporting for it.
export type Machine = {
  id: string;
  name: string;
  title: string;
  snapshot: Snapshot | null;
  freshness: Freshness;
  attached: boolean;        // a controller is running this station's program
};

// Which view opens first. "demo" while the rig is unplugged (lab move).
export const DEFAULT_VIEW: View = process.env.EXPO_PUBLIC_DEFAULT_VIEW === 'demo' ? 'demo' : 'live';

function resolveSource(view: View): Source {
  if (view === 'demo') {
    return { kind: 'demo', label: 'Simulation inside the app', url: 'no network needed' };
  }

  let override: string | null = null;
  if (Platform.OS === 'web' && typeof window !== 'undefined') {
    override = new URLSearchParams(window.location.search).get('gateway');
  }

  if (!override && SUPABASE_URL && SUPABASE_KEY) {
    return { kind: 'cloud', label: 'Cloud relay', url: SUPABASE_URL };
  }

  let url = override ?? ENV_GATEWAY ?? 'http://localhost:8787';
  if (!override && !ENV_GATEWAY && Platform.OS === 'web' && typeof window !== 'undefined') {
    const { port, origin } = window.location;
    if (port === '8787' || port === '8788') {
      url = origin;       // served by the gateway itself
    }
  }

  return { kind: 'gateway', label: 'Direct to gateway', url: url.replace(/\/$/, '') };
}

// How long without a new snapshot before the data is called stale.
const STALE_MS = { gateway: 4000, cloud: 20000 };

export function useFactory(view: View) {
  const source = useMemo(() => resolveSource(view), [view]);
  const [machines, setMachines] = useState<Machine[]>([]);
  const [feed, setFeed] = useState<{ snapshot: Snapshot | null; freshness: Freshness }>({ snapshot: null, freshness: 'connecting' });
  const [now, setNow] = useState(Date.now());
  const lastTs = useRef<string | undefined>(undefined);
  const lastReceived = useRef(0);
  const rig = useRef<Snapshot | null>(null);

  useEffect(() => {
    let closed = false;
    lastTs.current = undefined;
    lastReceived.current = 0;
    rig.current = null;
    setMachines([]);
    setFeed({ snapshot: null, freshness: 'connecting' });
    const cleanups: (() => void)[] = [];

    if (source.kind === 'demo') {
      const sims = STATIONS.filter((s) => s.sim).map((s) => ({ station: s, read: createSim(s.sim!) }));
      const tick = () => {
        if (closed) return;
        setNow(Date.now());
        setMachines(
          sims.map(({ station, read }) => ({
            id: station.id,
            name: station.name,
            title: station.title,
            snapshot: read(),
            freshness: 'live' as Freshness,
            attached: true,
          })),
        );
        setFeed({ snapshot: null, freshness: 'live' });
      };

      tick();
      const timer = setInterval(tick, 100);
      return () => {
        closed = true;
        clearInterval(timer);
      };
    }

    const accept = (next: Snapshot | null) => {
      if (closed || !next) return;
      if (next.ts !== lastTs.current) {
        lastTs.current = next.ts;
        lastReceived.current = Date.now();
      }

      rig.current = next;
    };

    if (source.kind === 'cloud') {
      const supabase = createClient(SUPABASE_URL!, SUPABASE_KEY!, { auth: { persistSession: false } });
      const load = async () => {
        const { data } = await supabase.from('machine_state').select('snapshot').eq('id', MACHINE_ID).maybeSingle();
        accept((data?.snapshot as Snapshot) ?? null);
      };

      load();
      const channel = supabase
        .channel('machine_state:' + MACHINE_ID)
        .on(
          'postgres_changes',
          { event: '*', schema: 'public', table: 'machine_state', filter: 'id=eq.' + MACHINE_ID },
          (payload) => accept(((payload.new as { snapshot?: Snapshot })?.snapshot as Snapshot) ?? null),
        )
        .subscribe();
      const poll = setInterval(load, 10000);       // safety net if the realtime socket drops silently
      cleanups.push(() => {
        clearInterval(poll);
        supabase.removeChannel(channel);
      });
    } else if (Platform.OS === 'web' && typeof EventSource !== 'undefined') {
      const stream = new EventSource(source.url + '/api/stream');
      stream.onmessage = (message) => {
        try {
          accept(JSON.parse(message.data));
        } catch {
          // a broken frame is replaced by the next one
        }
      };
      cleanups.push(() => stream.close());
    } else {
      const load = async () => {
        try {
          accept(await (await fetch(source.url + '/api/state')).json());
        } catch {
          // unreachable: freshness below reports it
        }
      };

      load();
      const poll = setInterval(load, 500);
      cleanups.push(() => clearInterval(poll));
    }

    const started = Date.now();
    const staleAfter = STALE_MS[source.kind as 'cloud' | 'gateway'];
    const tick = setInterval(() => {
      const t = Date.now();
      setNow(t);
      const freshness: Freshness =
        lastReceived.current === 0
          ? t - started > 6000
            ? 'unreachable'
            : 'connecting'
          : t - lastReceived.current > staleAfter
            ? 'stale'
            : 'live';
      const snapshot = rig.current;
      setFeed({ snapshot, freshness });

      // One controller on the rig runs one kit's program at a time: that station
      // is live, the others have nothing attached.
      const running = snapshot && snapshot.link.connected ? snapshot.kit?.id : undefined;
      const list: Machine[] = STATIONS.map((s) => ({
        id: s.id,
        name: s.name,
        title: s.title,
        snapshot: running === s.id ? snapshot : null,
        freshness: running === s.id ? freshness : 'unreachable',
        attached: running === s.id,
      }));
      if (snapshot && running && !STATIONS.some((s) => s.id === running)) {
        list.push({ id: 'controller', name: 'Controller', title: snapshot.kit?.title ?? 'Unknown program', snapshot, freshness, attached: true });
      }

      setMachines(list);
    }, 250);

    return () => {
      closed = true;
      clearInterval(tick);
      cleanups.forEach((c) => c());
    };
  }, [source]);

  return { machines, feed, source, now };
}
