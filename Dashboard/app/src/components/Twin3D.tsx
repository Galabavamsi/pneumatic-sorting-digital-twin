import React, { useEffect, useMemo, useRef, useState } from 'react';
import { PanResponder, Platform, Pressable, View } from 'react-native';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { Palette, radius, space } from '../theme';
import type { Snapshot } from '../types';
import { PartState, pileCounts, pileSpot, TwinDef, TWINS } from '../kits/twins';
import { Card, Body, Label } from './ui';

// 3D view of a station: the kit's own CAD model (the one the Unity twin uses),
// moved by the rules in src/kits/twins.ts. Positions follow the PLC's commands,
// eased over a fixed time, because most cylinders on the rig have no sensors.
const SCALE = 0.1;      // mm -> scene units

type Live = { travel: Record<string, number>; step: number; material: number };

function liveFrom(snap: Snapshot): Live {
  const travel: Record<string, number> = {};
  for (const a of snap.actuators ?? []) {
    travel[a.id] = a.travel ?? (a.position === 'a' ? 1 : 0);
  }

  return { travel, step: snap.status?.step ?? 0, material: snap.status?.material ?? 0 };
}

type Built = {
  root: THREE.Group;
  groups: Record<string, THREE.Group>;
  part: THREE.Mesh;
  partMaterial: THREE.MeshStandardMaterial;
  highlightMaterial: THREE.MeshStandardMaterial | null;
  piles: THREE.Mesh[][];
  props: Record<string, THREE.Mesh>;
  extras: Record<string, THREE.Mesh>;     // extra jobs on the machine (copies of the workpiece)
  flyers: THREE.Mesh[];                   // jobs on their way into a tray
  stack: THREE.Mesh[];                    // waiting jobs in the magazine, bottom first
};

const PILE_SIZE = 6;
const FLYERS = 3;

function build(scene: THREE.Object3D, def: TwinDef): Built {
  const flat = { flatShading: true };
  const metal = new THREE.MeshStandardMaterial({ color: '#959A9E', metalness: 0.4, roughness: 0.5, ...flat });
  const mechanism = new THREE.MeshStandardMaterial({ color: '#34373A', metalness: 0.45, roughness: 0.5, ...flat });
  const bin = new THREE.MeshStandardMaterial({ color: '#B9BDBF', metalness: 0.2, roughness: 0.6, transparent: true, opacity: 0.32, side: THREE.DoubleSide, depthWrite: false, ...flat });
  const stock = new THREE.MeshStandardMaterial({ color: '#8C9093', metalness: 0.3, roughness: 0.6, ...flat });
  const partMaterial = new THREE.MeshStandardMaterial({ color: '#8C9093', metalness: 0.3, roughness: 0.5, ...flat });
  const highlightMaterial = def.highlight ? new THREE.MeshStandardMaterial({ color: '#2B2D30', roughness: 0.7, ...flat }) : null;

  const model = new THREE.Group();            // CAD space: mm, Z up
  model.position.set(-def.centre[0], -def.centre[1], -def.centre[2]);
  const root = new THREE.Group();
  root.rotation.x = -Math.PI / 2;             // Z up -> Y up
  root.scale.setScalar(SCALE);
  root.add(model);

  // moving groups, nested as the definition says
  const groups: Record<string, THREE.Group> = {};
  const origin: Record<string, THREE.Vector3> = {};     // where each group's local zero is, in CAD space
  const owner: Record<string, string> = {};
  for (const g of def.groups) {
    const group = new THREE.Group();
    const parentOrigin = g.parent ? origin[g.parent] : new THREE.Vector3();
    const pivot = g.pivot ? new THREE.Vector3(...g.pivot) : parentOrigin.clone();
    group.position.copy(pivot).sub(parentOrigin);
    origin[g.id] = pivot;
    groups[g.id] = group;
    (g.parent ? groups[g.parent] : model).add(group);
    g.components.forEach((c) => (owner[c] = g.id));
  }

  let part: THREE.Mesh | null = null;
  const meshes: THREE.Mesh[] = [];
  scene.traverse((o) => {
    if ((o as THREE.Mesh).isMesh) meshes.push(o as THREE.Mesh);
  });

  const props: Record<string, THREE.Mesh> = {};
  const stackById: Record<string, THREE.Mesh> = {};
  for (const mesh of meshes) {
    const id = def.fullNames ? mesh.name : mesh.name.slice(-3);
    const n = Number(id);
    mesh.geometry.computeVertexNormals();
    if (def.hidden?.includes(id)) {
      mesh.visible = false;
      continue;
    }

    const prop = def.props?.find((x) => x.mesh === id);
    if (prop) {
      mesh.material = stock;
      props[prop.key] = mesh;
      model.add(mesh);
      continue;
    }

    if (id === def.activePart) {
      mesh.material = partMaterial;
      part = mesh;
      model.add(mesh);
    } else if (owner[id]) {
      mesh.material = def.highlight?.component === id && highlightMaterial ? highlightMaterial : mechanism;
      mesh.position.sub(origin[owner[id]]);
      groups[owner[id]].add(mesh);
    } else {
      // waiting workpieces are 10 mm slabs of 12 triangles; anything else in the range is structure
      const isStock = def.stockList
        ? def.stockList.includes(id)
        : n >= def.stock[0] && n <= def.stock[1] && (mesh.geometry.index?.count ?? 0) <= 36;
      mesh.material = isStock ? stock : def.bins.includes(id) ? bin : metal;
      if (def.stack?.order.includes(id)) stackById[id] = mesh;
      model.add(mesh);
    }
  }

  if (!part) {
    throw new Error(`Workpiece Component_${def.activePart} is missing from the model.`);
  }

  const copyOfPart = () => {
    const copy = new THREE.Mesh(part!.geometry, partMaterial.clone());
    copy.visible = false;
    model.add(copy);
    return copy;
  };
  const piles: THREE.Mesh[][] = [[], []];
  for (let b = 0; b < 2; b++) for (let i = 0; i < PILE_SIZE; i++) piles[b].push(copyOfPart());
  const extras: Record<string, THREE.Mesh> = {};
  for (const key of def.extras ?? []) extras[key] = copyOfPart();
  const flyers: THREE.Mesh[] = [];
  for (let i = 0; i < FLYERS; i++) flyers.push(copyOfPart());
  const stack = (def.stack?.order ?? []).map((id) => stackById[id]).filter(Boolean);

  return { root, groups, part, partMaterial, highlightMaterial, piles, props, extras, flyers, stack };
}

type Flyer = { pos: THREE.Vector3; target: THREE.Vector3; bin: number; material: number; t: number; turn: number };

function Rig({ built, def, live, p }: { built: Built; def: TwinDef; live: React.MutableRefObject<Live>; p: Palette }) {
  const eased = useRef<Record<string, number>>({});
  const home = useMemo(() => new THREE.Vector3(...def.partHome), [def]);
  const state = useRef<PartState>({ mode: 'magazine', pos: new THREE.Vector3(...def.partHome), target: new THREE.Vector3(), bin: 0, material: 0, data: {} });
  const dropped = useRef<{ pos: THREE.Vector3; material: number; turn: number }[][]>([[], []]);
  const flying = useRef<Flyer[]>([]);
  const fall = useRef(0);
  const mag = useRef({ taken: 0, shown: 0, out: false });      // magazine: jobs taken, eased drop, pusher has been out
  const colors = useMemo(
    () => ({
      metal: new THREE.Color('#C9CDD0'),
      plastic: new THREE.Color('#1E1F21'),
      unknown: new THREE.Color('#8C9093'),
      on: new THREE.Color(p.data),
      off: new THREE.Color('#2B2D30'),
    }),
    [p],
  );
  const colorOf = (m: number) => (m === 1 ? colors.metal : m === 2 ? colors.plastic : colors.unknown);

  // a job lands in a tray: it takes the next free spot of that pile
  const land = (bin: number, material: number, turn: number) => {
    const pile = dropped.current[bin];
    if (pile.length >= PILE_SIZE) pile.shift();
    pile.push({ pos: new THREE.Vector3(), material, turn });
    pile.forEach((d, i) => d.pos.set(...pileSpot(def, bin, i)));
  };

  useFrame((_, dtRaw) => {
    const dt = Math.min(dtRaw, 0.1);
    const t = live.current;
    const v = eased.current;
    const s = state.current;
    const step = dt * 1.6;       // full travel in about 0.6 s
    for (const id of Object.keys(t.travel)) {
      const now = v[id] ?? t.travel[id];
      const target = t.travel[id];
      v[id] = Math.abs(target - now) <= step ? target : now + Math.sign(target - now) * step;
    }

    def.pose(built.groups, v);
    if (built.highlightMaterial && def.highlight) {
      built.highlightMaterial.color.copy((t.travel[def.highlight.actuator] ?? 0) > 0.5 ? colors.on : colors.off);
    }

    if (t.material !== 0) s.material = t.material;
    pileCounts[0] = dropped.current[0].length;
    pileCounts[1] = dropped.current[1].length;
    const before = s.mode;
    def.part(s, v, t.step, def, dt);
    const d = s.data;

    // pieces that travel with the workpiece (Kit 5's inserts)
    for (const key of Object.keys(built.props)) {
      const mesh = built.props[key];
      mesh.visible = !d[key + 'h'];
      mesh.position.set(d[key + 'x'] ?? 0, d[key + 'y'] ?? 0, d[key + 'z'] ?? 0);
    }

    // other jobs still on the machine (Kit 2: the one waiting at a stamp)
    for (const key of Object.keys(built.extras)) {
      const mesh = built.extras[key];
      mesh.visible = d[key + 'v'] === 1;
      mesh.position.set((d[key + 'x'] ?? 0) - home.x, (d[key + 'y'] ?? 0) - home.y, (d[key + 'z'] ?? 0) - home.z);
      (mesh.material as THREE.MeshStandardMaterial).color.copy(colorOf(d[key + 'm'] ?? 0));
    }

    // a job pushed off the edge by the next one: it tips over the edge and drops into its tray
    if (d.fly === 1) {
      d.fly = 0;
      const bin = d.flyBin ?? 0;
      const spot = pileSpot(def, bin, Math.min(dropped.current[bin].length, PILE_SIZE - 1));
      flying.current.push({ pos: new THREE.Vector3(d.flyX, d.flyY, d.flyZ), target: new THREE.Vector3(...spot), bin, material: d.flyMat ?? 0, t: 0, turn: (dropped.current[bin].length * 37) % 90 });
    }

    flying.current = flying.current.filter((f) => {
      f.t += dt;
      const k = Math.min(1, dt * 4);
      f.pos.x += (f.target.x - f.pos.x) * k;
      f.pos.y += (f.target.y - f.pos.y) * k;
      f.pos.z = Math.max(f.target.z, f.pos.z - 9810 * f.t * dt * 0.1);
      if (f.pos.z <= f.target.z && f.t > 0.45) {
        land(f.bin, f.material, f.turn);
        return false;
      }

      return true;
    });
    built.flyers.forEach((mesh, i) => {
      const f = flying.current[i];
      mesh.visible = !!f;
      if (f) {
        mesh.position.copy(f.pos).sub(home);
        mesh.rotation.set(Math.min(1.2, f.t * 3), 0, 0);
        (mesh.material as THREE.MeshStandardMaterial).color.copy(colorOf(f.material));
      }
    });

    // the workpiece in play, when a cylinder or the vacuum cup lets go of it
    if (s.mode === 'falling' && before !== 'falling') fall.current = 0;
    if (s.mode === 'falling') {
      fall.current += dt;
      const k = Math.min(1, dt * 5);
      s.pos.x += (s.target.x - s.pos.x) * k;
      s.pos.y += (s.target.y - s.pos.y) * k;
      s.pos.z = Math.max(s.target.z, s.pos.z - 9810 * fall.current * dt * 0.12);
      if (s.pos.z <= s.target.z && fall.current > 0.5) {
        land(s.bin, s.material, (dropped.current[s.bin].length * 37) % 90);
        s.mode = 'gone';
      }
    }

    built.part.visible = s.mode !== 'gone';
    built.part.position.copy(s.pos).sub(home);      // the mesh's vertices already sit at its home
    built.partMaterial.color.copy(colorOf(s.material));
    built.piles.forEach((copies, b) => {
      copies.forEach((copy, i) => {
        const item = dropped.current[b][i];
        copy.visible = !!item;
        if (item) {
          copy.position.copy(item.pos).sub(home);
          copy.rotation.set(0, 0, (item.turn * Math.PI) / 180);
          (copy.material as THREE.MeshStandardMaterial).color.copy(colorOf(item.material));
        }
      });
    });

    // the magazine: once the pusher has gone out and come back, the stack drops by one job
    if (def.stack && built.stack.length > 0) {
      const m = mag.current;
      const feed = v[def.stack.feed] ?? 0;
      if (feed > 0.9) m.out = true;
      if (m.out && feed < 0.1) {
        m.out = false;
        m.taken += 1;
        if (m.taken > built.stack.length) {      // empty: show a refilled magazine
          m.taken = 0;
          m.shown = 0;
        }
      }

      m.shown = Math.min(m.taken, m.shown + dt * 4);
      const atHome = s.mode === 'magazine' || s.pos.distanceTo(home) < 2;
      built.stack.forEach((mesh, i) => {
        const slot = i + 1 - m.taken;             // 0 is the bottom slot, where the next job waits
        mesh.position.z = -def.stack!.pitch * m.shown;
        mesh.visible = slot > 0 || (slot === 0 && !atHome);
      });
    }
  });

  // development aid: lets the motion be sampled from the browser console
  if (__DEV__) {
    (globalThis as { __twin?: unknown }).__twin = { state: state.current, dropped: dropped.current, flying, mag: mag.current, eased: eased.current, live };
  }

  return <primitive object={built.root} />;
}

// Orbit camera: it sits on a sphere around the station and always looks at its centre.
type Orbit = { azimuth: number; elevation: number; zoom: number };
const HOME_ELEVATION = 0.42;
// web-only style: stop the browser scrolling or selecting text while the model is dragged
const webOnly = (Platform.OS === 'web' ? { cursor: 'grab', touchAction: 'none', userSelect: 'none' } : {}) as object;
const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

function CameraRig({ orbit, distance }: { orbit: React.MutableRefObject<Orbit>; distance: number }) {
  const { camera } = useThree();
  useFrame(() => {
    const { azimuth: a, elevation: e, zoom } = orbit.current;
    const r = distance / zoom;
    camera.position.set(r * Math.cos(e) * Math.sin(a), r * Math.sin(e), r * Math.cos(e) * Math.cos(a));
    camera.lookAt(0, 0, 0);
  });
  return null;
}

export function Twin3D({ p, snap, height = 340 }: { p: Palette; snap: Snapshot; height?: number }) {
  const def = snap.kit ? TWINS[snap.kit.id] : undefined;
  const live = useRef<Live>(liveFrom(snap));
  live.current = liveFrom(snap);
  const [built, setBuilt] = useState<Built | null>(null);
  const [error, setError] = useState<string | null>(null);
  const home = (): Orbit => ({ azimuth: def?.azimuth ?? 0.5, elevation: HOME_ELEVATION, zoom: 1 });
  const orbit = useRef<Orbit>(home());
  const dragStart = useRef<Orbit & { pinch: number }>({ ...home(), pinch: 0 });
  const frame = useRef<any>(null);       // on the web this is the DOM element

  // Mouse wheel zooms (web). The listener must be non-passive to stop the page scrolling
  // while the pointer is over the model.
  useEffect(() => {
    const node = frame.current as unknown as HTMLElement | null;
    if (Platform.OS !== 'web' || !node || !node.addEventListener) return;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      orbit.current.zoom = clamp(orbit.current.zoom * Math.exp(-event.deltaY * 0.0012), 0.45, 4);
    };
    node.addEventListener('wheel', onWheel, { passive: false });
    return () => node.removeEventListener('wheel', onWheel);
  });

  useEffect(() => {
    if (!def) return;
    if (Platform.OS !== 'web') {
      setError('The 3D model is in the web version for now; it will be bundled into the mobile build.');
      return;
    }

    let cancelled = false;
    new GLTFLoader().load(
      def.url,
      (gltf) => {
        if (cancelled) return;
        try {
          setBuilt(build(gltf.scene, def));
        } catch (e) {
          setError(String((e as Error).message));
        }
      },
      undefined,
      () => setError('The 3D model file could not be loaded.'),
    );
    return () => {
      cancelled = true;
    };
  }, [def]);

  const pan = useMemo(
    () =>
      PanResponder.create({
        onStartShouldSetPanResponder: () => true,
        onMoveShouldSetPanResponder: () => true,
        onPanResponderTerminationRequest: () => false,
        onPanResponderGrant: () => {
          dragStart.current = { ...orbit.current, pinch: 0 };
        },
        onPanResponderMove: (event, g) => {
          const touches = event.nativeEvent.touches;
          if (touches && touches.length >= 2) {
            // two fingers: pinch to zoom
            const spread = Math.hypot(touches[0].pageX - touches[1].pageX, touches[0].pageY - touches[1].pageY);
            if (dragStart.current.pinch === 0) {
              dragStart.current = { ...orbit.current, pinch: spread };
            } else {
              orbit.current.zoom = clamp((dragStart.current.zoom * spread) / dragStart.current.pinch, 0.45, 4);
            }

            return;
          }

          // one finger or the mouse: turn left / right and tilt up / down
          orbit.current.azimuth = dragStart.current.azimuth - g.dx * 0.008;
          orbit.current.elevation = clamp(dragStart.current.elevation + g.dy * 0.006, -0.25, 1.5);
        },
      }),
    [],
  );

  if (!def) {
    return null;      // no model for this program (for example an unknown one)
  }

  return (
    <Card p={p} title="3D model" aside={
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
          <Label p={p} style={{ fontSize: 11, color: p.faint }}>drag to orbit · scroll or pinch to zoom</Label>
          <Pressable
            onPress={() => (orbit.current = home())}
            accessibilityRole="button"
            accessibilityLabel="Reset the 3D view"
            style={{ paddingHorizontal: space.sm, paddingVertical: 3, borderRadius: radius.sm, borderWidth: 1, borderColor: p.line }}
          >
            <Label p={p} style={{ fontSize: 11, color: p.ink }}>Reset view</Label>
          </Pressable>
        </View>
      }>
      <View
        ref={frame}
        {...pan.panHandlers}
        style={{ height, borderRadius: radius.md, overflow: 'hidden', backgroundColor: p.raised, marginHorizontal: -space.xs, justifyContent: 'center', ...webOnly }}
      >
        {error ? (
          <Body p={p} style={{ color: p.sub, textAlign: 'center', padding: space.lg }}>
            {error}
          </Body>
        ) : built ? (
          <Canvas camera={{ fov: 30, near: 1, far: 600 }} dpr={[1, 2]}>
            <ambientLight intensity={1.0} />
            <directionalLight position={[40, 70, 50]} intensity={2.0} />
            <directionalLight position={[-50, 30, -30]} intensity={0.7} />
            <CameraRig orbit={orbit} distance={def.distance} />
            <Rig built={built} def={def} live={live} p={p} />
          </Canvas>
        ) : (
          <Body p={p} style={{ color: p.sub, textAlign: 'center' }}>
            Loading the model…
          </Body>
        )}
      </View>
    </Card>
  );
}
