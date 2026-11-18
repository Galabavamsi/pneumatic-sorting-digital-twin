import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Platform, Pressable, ScrollView, View } from 'react-native';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { Palette, radius, space } from '../theme';
import type { Snapshot } from '../types';
import { useFactory } from '../data/useFactory';
import { createSim } from '../sim/engine';
import { STATIONS } from '../kits/stations';
import { TwinDef, TWINS } from '../kits/twins';
import { build, Built, Live, liveFrom, Rig } from './Twin3D';
import { Body, Label } from './ui';

// AR view for a headset browser (WebXR "immersive-ar", tested target: Meta Quest 3).
// The stations stand in the room at real size, moved by the same rules as the 3D
// view on the dashboard. A station that a controller is running follows the live
// feed; every other station plays the in-app simulation, and each one says which.
//
// In the headset: point at a station and hold the trigger to carry it; while
// holding, the thumbstick turns it (left / right) and moves it nearer or further
// (up / down), A / X makes it smaller, B / Y larger, and the grip sets it back to
// full size.

const MM = 0.001;                    // CAD millimetres -> metres
const ROOT_SCALE = 0.1;              // the scale build() gives its root (mm -> cm)
const TABLE = 0.75;                  // height a station first stands at, metres

export type ArPreset = { kits: string[]; scale: number };

type Info = { name: string; title: string; live: boolean; state: string; step: string };
type Loaded = { id: string; def: TwinDef; built: Built; size: THREE.Vector3; shift: THREE.Vector3 };
type Place = { position: [number, number, number]; yaw: number; scale: number };
type Entry = { holder: THREE.Group; proxy: THREE.Mesh; hot: boolean; name: string; azimuth: number; home: Place };
type Registry = Map<string, Entry>;

const STATE_WORD: Record<string, string> = { running: 'Running', idle: 'Idle', homing: 'Homing', fault: 'Fault', unknown: 'Unknown program' };
const INK = { bg: 'rgba(18,19,17,0.86)', ink: '#ECE9DF', sub: '#A9A498', data: '#63C2B1', warn: '#E2AC43', alert: '#F26A45' };

// ------------------------------------------------------------------ the data
// One feed per station: the live snapshot when a controller is running that
// station's program, the simulation otherwise.
function useStationFeeds(ids: string[]) {
  const { machines } = useFactory('live');
  const latest = useRef(machines);
  latest.current = machines;
  const feeds = useMemo(() => {
    const out: Record<string, { live: React.MutableRefObject<Live>; info: React.MutableRefObject<Info>; read: (() => Snapshot) | null }> = {};
    for (const id of ids) {
      const station = STATIONS.find((s) => s.id === id);
      const read = station?.sim ? createSim(station.sim) : null;
      out[id] = {
        live: { current: { travel: {}, step: 0, material: 0 } },
        info: { current: { name: station?.name ?? id, title: station?.title ?? '', live: false, state: '', step: '' } },
        read,
      };
    }

    return out;
  }, [ids]);

  useEffect(() => {
    const tick = () => {
      for (const id of ids) {
        const feed = feeds[id];
        const m = latest.current.find((x) => x.id === id);
        const real = m && m.attached && m.snapshot && m.snapshot.link.connected && (m.freshness === 'live' || m.freshness === 'stale') ? m.snapshot : null;
        const snap = real ?? feed.read?.() ?? null;
        if (!snap) continue;
        feed.live.current = liveFrom(snap);
        feed.info.current = {
          ...feed.info.current,
          live: !!real,
          state: STATE_WORD[snap.status?.state ?? 'unknown'] ?? '',
          step: snap.status?.faultText ?? snap.status?.stepName ?? '',
        };
      }
    };

    tick();
    const timer = setInterval(tick, 100);
    return () => clearInterval(timer);
  }, [ids, feeds]);

  return feeds;
}

// ----------------------------------------------------------------- the label
const LABEL_W = 640;
const LABEL_H = 208;
const LABEL_M = 0.38;                // width of a label in the room, metres

function drawLabel(canvas: HTMLCanvasElement, info: Info, scale: number, hint: boolean) {
  const g = canvas.getContext('2d');
  if (!g) return;
  g.clearRect(0, 0, LABEL_W, LABEL_H);
  g.fillStyle = INK.bg;
  g.beginPath();
  g.roundRect(0, 0, LABEL_W, LABEL_H, 22);
  g.fill();

  g.textBaseline = 'alphabetic';
  g.fillStyle = INK.ink;
  g.font = '600 50px BarlowCondensed_600SemiBold, "Arial Narrow", sans-serif';
  const name = info.name.toUpperCase();
  g.fillText(name, 26, 62);
  const nameWidth = g.measureText(name).width;
  g.fillStyle = INK.sub;
  g.font = '30px Barlow_400Regular, Arial, sans-serif';
  g.fillText(info.title, 26 + nameWidth + 18, 60, LABEL_W - nameWidth - 70);

  // where the data comes from, in words and colour
  const fault = info.state === 'Fault';
  const tone = fault ? INK.alert : info.live ? INK.data : INK.warn;
  const source = info.live ? 'LIVE FROM THE PLC' : 'SIMULATED';
  g.fillStyle = tone;
  g.beginPath();
  if (info.live) {
    g.arc(38, 108, 11, 0, Math.PI * 2);
    g.fill();
  } else {
    g.lineWidth = 4;
    g.strokeStyle = tone;
    g.arc(38, 108, 9, 0, Math.PI * 2);
    g.stroke();
  }

  g.font = '600 34px BarlowCondensed_600SemiBold, "Arial Narrow", sans-serif';
  g.fillText(source, 60, 120);
  const sourceWidth = g.measureText(source).width;
  g.fillStyle = fault ? INK.alert : INK.ink;
  g.font = '30px Barlow_500Medium, Arial, sans-serif';
  g.fillText(`${info.state}${info.step ? ' · ' + info.step : ''}`, 60 + sourceWidth + 20, 119, LABEL_W - sourceWidth - 106);

  g.fillStyle = INK.sub;
  g.font = '25px Barlow_400Regular, Arial, sans-serif';
  const size = Math.abs(scale - 1) < 0.01 ? 'Full size' : `${Math.round(scale * 100)} % of full size`;
  g.fillText(hint ? 'Trigger: carry · stick: turn, near / far · A B: size · grip: full size' : size, 26, 178, LABEL_W - 52);
}

// The panels that float near the viewer during a session: "Exit AR" and "Show".
const EXIT_W = 0.2;
const EXIT_H = 0.07;

function drawPanel(canvas: HTMLCanvasElement, text: string, hot: boolean) {
  const g = canvas.getContext('2d');
  if (!g) return;
  g.clearRect(0, 0, canvas.width, canvas.height);
  g.fillStyle = hot ? INK.ink : INK.bg;
  g.beginPath();
  g.roundRect(3, 3, canvas.width - 6, canvas.height - 6, 24);
  g.fill();
  g.lineWidth = 4;
  g.strokeStyle = INK.ink;
  g.stroke();
  g.fillStyle = hot ? '#121311' : INK.ink;
  g.font = '600 62px BarlowCondensed_600SemiBold, "Arial Narrow", sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(text, canvas.width / 2, canvas.height / 2 + 4, canvas.width - 40);
}

// ---------------------------------------------------------------- one station
function Kit({
  loaded,
  feed,
  place,
  registry,
  p,
}: {
  loaded: Loaded;
  feed: { live: React.MutableRefObject<Live>; info: React.MutableRefObject<Info> };
  place: Place;
  registry: Registry;
  p: Palette;
}) {
  const { id, def, built, size, shift } = loaded;

  const parts = useMemo(() => {
    const holder = new THREE.Group();
    const box = new THREE.BoxGeometry(size.x, size.y, size.z);
    const proxy = new THREE.Mesh(box, new THREE.MeshBasicMaterial());      // only ever ray-tested
    proxy.visible = false;
    proxy.position.y = size.y / 2;
    proxy.userData.id = id;
    const outline = new THREE.LineSegments(new THREE.EdgesGeometry(box), new THREE.LineBasicMaterial({ color: INK.data }));
    outline.position.y = size.y / 2;
    outline.visible = false;

    const canvas = document.createElement('canvas');
    canvas.width = LABEL_W;
    canvas.height = LABEL_H;
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    const label = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true, depthWrite: false }));
    holder.add(proxy, outline, label);
    return { holder, proxy, outline, label, canvas, texture, drawn: '' };
  }, [id, size]);

  useEffect(() => {
    parts.holder.position.set(...place.position);
    parts.holder.rotation.set(0, place.yaw, 0);
    parts.holder.scale.setScalar(place.scale);
  }, [parts, place]);

  useEffect(() => {
    const entry: Entry = { holder: parts.holder, proxy: parts.proxy, hot: false, name: feed.info.current.name, azimuth: def.azimuth, home: place };
    registry.set(id, entry);
    return () => {
      registry.delete(id);
    };
  }, [id, parts, registry, feed, def, place]);

  useFrame(() => {
    const entry = registry.get(id);
    const hot = !!entry?.hot;
    const scale = parts.holder.scale.x;
    parts.outline.visible = hot;

    // the label keeps its real size whatever the station's scale
    parts.label.scale.set(LABEL_M / scale, (LABEL_M * LABEL_H) / LABEL_W / scale, 1);
    parts.label.position.y = size.y + (LABEL_M * 0.28) / scale;

    const info = feed.info.current;
    const key = `${info.live}|${info.state}|${info.step}|${scale.toFixed(2)}|${hot}`;
    if (key !== parts.drawn) {
      parts.drawn = key;
      drawLabel(parts.canvas, info, scale, hot);
      parts.texture.needsUpdate = true;
    }
  });

  return (
    <primitive object={parts.holder}>
      <group position={[shift.x, shift.y, shift.z]} scale={MM / ROOT_SCALE}>
        <Rig built={built} def={def} live={feed.live} p={p} />
      </group>
    </primitive>
  );
}

// ------------------------------------------------------------ the controllers
type Grab = { id: string; dist: number; offset: THREE.Vector3 };
const deadzone = (v: number | undefined) => (v !== undefined && Math.abs(v) > 0.18 ? v : 0);

function Controllers({ registry }: { registry: Registry }) {
  const { gl, scene, camera } = useThree();
  const hands = useRef<{ space: THREE.Object3D; ray: THREE.Line; source: any; grab: Grab | null; over: { id: string; dist: number; point: THREE.Vector3 } | null; panel: 'exit' | 'show' | null }[]>([]);

  // "Exit AR": a small panel that stays within reach, down and to the left of the view
  const exit = useMemo(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 400;
    canvas.height = 140;
    drawPanel(canvas, 'EXIT AR', false);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    const mesh = new THREE.Mesh(new THREE.PlaneGeometry(EXIT_W, EXIT_H), new THREE.MeshBasicMaterial({ map: texture, transparent: true, side: THREE.DoubleSide }));
    mesh.visible = false;

    // above it: which stations are shown. It steps through all of them, then each one alone at full size.
    const showCanvas = document.createElement('canvas');
    showCanvas.width = 400;
    showCanvas.height = 140;
    drawPanel(showCanvas, 'SHOW: ALL', false);
    const showTexture = new THREE.CanvasTexture(showCanvas);
    showTexture.colorSpace = THREE.SRGBColorSpace;
    const show = new THREE.Mesh(new THREE.PlaneGeometry(EXIT_W, EXIT_H), new THREE.MeshBasicMaterial({ map: showTexture, transparent: true, side: THREE.DoubleSide }));
    show.position.y = EXIT_H + 0.015;
    mesh.add(show);
    return {
      mesh,
      canvas,
      texture,
      hot: false,
      placed: false,
      head: new THREE.Vector3(),
      want: new THREE.Vector3(),
      flat: new THREE.Vector3(),
      show,
      showCanvas,
      showTexture,
      showHot: false,
      showText: 'SHOW: ALL',
      only: null as string | null,
    };
  }, []);

  useEffect(() => {
    scene.add(exit.mesh);
    return () => {
      scene.remove(exit.mesh);
    };
  }, [scene, exit]);
  const tools = useMemo(() => ({ caster: new THREE.Raycaster(), origin: new THREE.Vector3(), dir: new THREE.Vector3() }), []);

  useEffect(() => {
    const list = [0, 1].map((i) => {
      const space = gl.xr.getController(i) as THREE.Object3D;
      const ray = new THREE.Line(
        new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 0, -1)]),
        new THREE.LineBasicMaterial({ color: '#ECE9DF', transparent: true, opacity: 0.7 }),
      );
      space.add(ray);
      scene.add(space);
      return { space, ray, source: null as any, grab: null as Grab | null, over: null as { id: string; dist: number; point: THREE.Vector3 } | null, panel: null as 'exit' | 'show' | null };
    });
    hands.current = list;

    const off: (() => void)[] = [];
    list.forEach((hand, i) => {
      const listen = (type: string, fn: (e: any) => void) => {
        (hand.space as any).addEventListener(type, fn);
        off.push(() => (hand.space as any).removeEventListener(type, fn));
      };

      listen('connected', (e) => (hand.source = e.data));
      listen('disconnected', () => {
        hand.source = null;
        hand.grab = null;
        hand.over = null;
      });
      listen('selectstart', () => {
        if (hand.panel === 'exit') {
          gl.xr.getSession()?.end();
          return;
        }

        if (hand.panel === 'show') {
          // all -> the first station alone -> the next ... -> all
          const ids = [...registry.keys()];
          const next = exit.only === null ? 0 : ids.indexOf(exit.only) + 1;
          exit.only = next < ids.length ? ids[next] : null;
          registry.forEach((entry, id) => {
            const holder = entry.holder;
            holder.visible = exit.only === null || exit.only === id;
            if (exit.only === null) {
              holder.position.set(...entry.home.position);
              holder.rotation.set(0, entry.home.yaw, 0);
              holder.scale.setScalar(entry.home.scale);
            } else if (exit.only === id) {
              // alone: full size, at table height, straight ahead of the viewer and facing them
              holder.position.copy(exit.head).addScaledVector(exit.flat, 0.9);
              holder.position.y = TABLE;
              holder.rotation.set(0, -Math.atan2(exit.flat.x, -exit.flat.z) - entry.azimuth, 0);
              holder.scale.setScalar(1);
            }
          });
          return;
        }

        const over = hand.over;
        const entry = over ? registry.get(over.id) : undefined;
        if (!over || !entry || list[1 - i].grab?.id === over.id) return;
        hand.grab = { id: over.id, dist: over.dist, offset: entry.holder.position.clone().sub(over.point) };
      });
      listen('selectend', () => (hand.grab = null));
      listen('squeezestart', () => {
        const target = hand.grab?.id ?? hand.over?.id;
        const entry = target ? registry.get(target) : undefined;
        entry?.holder.scale.setScalar(1);
      });
    });

    return () => {
      off.forEach((f) => f());
      list.forEach((hand) => {
        hand.space.remove(hand.ray);
        scene.remove(hand.space);
      });
      hands.current = [];
    };
  }, [gl, scene, registry]);

  useFrame((_, dtRaw) => {
    const dt = Math.min(dtRaw, 0.1);
    const { caster, origin, dir } = tools;
    const presenting = gl.xr.isPresenting;
    registry.forEach((entry) => (entry.hot = false));

    // the exit panel follows the viewer, but only once they have moved or turned well away from it
    exit.mesh.visible = presenting;
    if (!presenting) {
      exit.placed = false;
    } else {
      exit.head.setFromMatrixPosition(camera.matrixWorld);
      exit.flat.set(0, 0, -1).transformDirection(camera.matrixWorld).setY(0);
      if (exit.flat.lengthSq() > 0.01) {
        exit.flat.normalize();
        exit.want.copy(exit.head).addScaledVector(exit.flat, 0.55);
        exit.want.x += exit.flat.z * 0.32;        // to the viewer's left
        exit.want.z -= exit.flat.x * 0.32;
        exit.want.y = Math.max(0.3, exit.head.y - 0.42);
        const away = exit.mesh.position.distanceTo(exit.want);
        if (!exit.placed) {
          exit.mesh.position.copy(exit.want);
          exit.placed = true;
        } else if (away > 0.45) {
          exit.mesh.position.lerp(exit.want, Math.min(1, dt * 3));
        }
      }

      exit.mesh.lookAt(exit.head);
    }

    let exitHot = false;
    let showHot = false;
    exit.show.visible = registry.size > 1;

    for (const hand of hands.current) {
      hand.ray.visible = presenting;
      hand.panel = null;
      if (!presenting || !hand.space.visible) {
        hand.over = null;
        continue;
      }

      origin.setFromMatrixPosition(hand.space.matrixWorld);
      dir.set(0, 0, -1).transformDirection(hand.space.matrixWorld);

      const grab = hand.grab;
      const held = grab ? registry.get(grab.id) : undefined;
      if (grab && held) {
        const pad = hand.source?.gamepad as Gamepad | undefined;
        if (pad) {
          held.holder.rotation.y -= deadzone(pad.axes[2]) * dt * 1.6;
          grab.dist = Math.min(8, Math.max(0.15, grab.dist - deadzone(pad.axes[3]) * dt * 1.5));
          const grow = (pad.buttons[5]?.pressed ? 1 : 0) - (pad.buttons[4]?.pressed ? 1 : 0);
          if (grow !== 0) {
            held.holder.scale.setScalar(Math.min(2, Math.max(0.05, held.holder.scale.x * Math.exp(grow * dt * 0.9))));
          }
        }

        held.holder.position.copy(origin).addScaledVector(dir, grab.dist).add(grab.offset);
        held.holder.position.y = Math.max(0, held.holder.position.y);
        held.hot = true;
        hand.ray.scale.z = grab.dist;
        continue;
      }

      hand.grab = null;
      caster.set(origin, dir);
      const proxies: THREE.Object3D[] = [];
      registry.forEach((entry) => {
        if (!entry.holder.visible) return;
        entry.holder.updateWorldMatrix(true, true);
        proxies.push(entry.proxy);
      });
      exit.mesh.updateWorldMatrix(true, true);
      proxies.push(exit.mesh);
      if (exit.show.visible) proxies.push(exit.show);
      const hit = caster.intersectObjects(proxies, false)[0];
      if (hit && (hit.object === exit.mesh || hit.object === exit.show)) {
        hand.over = null;
        hand.panel = hit.object === exit.mesh ? 'exit' : 'show';
        if (hand.panel === 'exit') exitHot = true;
        else showHot = true;
        hand.ray.scale.z = hit.distance;
        continue;
      }

      hand.over = hit ? { id: hit.object.userData.id as string, dist: hit.distance, point: hit.point.clone() } : null;
      hand.ray.scale.z = hit ? hit.distance : 3;
      if (hit) {
        const entry = registry.get(hit.object.userData.id as string);
        if (entry) entry.hot = true;
      }
    }

    if (exitHot !== exit.hot) {
      exit.hot = exitHot;
      drawPanel(exit.canvas, 'EXIT AR', exitHot);
      exit.texture.needsUpdate = true;
    }

    const showText = 'SHOW: ' + (exit.only === null ? 'ALL' : (registry.get(exit.only)?.name ?? '').toUpperCase());
    if (showHot !== exit.showHot || showText !== exit.showText) {
      exit.showHot = showHot;
      exit.showText = showText;
      drawPanel(exit.showCanvas, showText, showHot);
      exit.showTexture.needsUpdate = true;
    }
  });

  return null;
}

// Outside a headset session the scene is shown from a fixed standing viewpoint.
function PreviewCamera({ presenting }: { presenting: boolean }) {
  const { camera } = useThree();
  useEffect(() => {
    if (presenting) return;
    camera.position.set(0, 1.55, 1.25);
    camera.rotation.set(0, 0, 0);
    camera.lookAt(0, 0.95, -1.2);
    camera.updateProjectionMatrix();
  }, [camera, presenting]);
  return null;
}

// Where the stations first stand: one alone straight ahead, several on an arc
// around the viewer, each turned to face them.
function layout(defs: Loaded[], scale: number) {
  const n = defs.length;
  const widest = Math.max(LABEL_M / 1.1, Math.max(...defs.map((d) => Math.hypot(d.size.x, d.size.z))) * scale);
  const radius = n === 1 ? 0.9 : Math.max(1.2, (widest * 1.15 * n) / 2.4);
  const spread = n === 1 ? 0 : Math.min(2.4, ((n - 1) * widest * 1.15) / radius);
  return defs.map((d, i) => {
    const a = n === 1 ? 0 : -spread / 2 + (spread * i) / (n - 1);
    return {
      position: [radius * Math.sin(a), scale >= 0.99 ? TABLE : 0.95, -radius * Math.cos(a)] as [number, number, number],
      yaw: -a - d.def.azimuth,
      scale,
    };
  });
}

// ------------------------------------------------------------------ the page
export function ArView({ p, initial, auto, onClose }: { p: Palette; initial?: ArPreset | null; auto?: boolean; onClose: () => void }) {
  const [preset, setPreset] = useState<ArPreset | null>(initial ?? null);

  if (Platform.OS !== 'web') {
    return (
      <View style={{ flex: 1, backgroundColor: p.bg, padding: space.xl, justifyContent: 'center' }}>
        <Body p={p}>The AR view runs in a headset browser. Open the web version of this dashboard on the headset.</Body>
      </View>
    );
  }

  if (!preset) {
    return <Picker p={p} onPick={setPreset} onClose={onClose} />;
  }

  return <Scene key={preset.kits.join(',') + preset.scale} p={p} preset={preset} auto={auto} onBack={() => setPreset(null)} onClose={onClose} />;
}

function Button({ p, label, note, onPress, strong, disabled }: { p: Palette; label: string; note?: string; onPress: () => void; strong?: boolean; disabled?: boolean }) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      style={{
        paddingHorizontal: space.lg,
        paddingVertical: space.md,
        borderRadius: radius.md,
        borderWidth: 1,
        borderColor: strong ? p.ink : p.line,
        backgroundColor: strong ? p.ink : p.surface,
        opacity: disabled ? 0.45 : 1,
        minHeight: 48,
        justifyContent: 'center',
      }}
    >
      <Label p={p} style={{ fontSize: 15, color: strong ? p.onInk : p.ink }}>
        {label}
      </Label>
      {note ? (
        <Body p={p} style={{ fontSize: 13, lineHeight: 17, color: strong ? p.onInk : p.sub }}>
          {note}
        </Body>
      ) : null}
    </Pressable>
  );
}

function Picker({ p, onPick, onClose }: { p: Palette; onPick: (preset: ArPreset) => void; onClose: () => void }) {
  const stations = STATIONS.filter((s) => TWINS[s.id]);
  return (
    <ScrollView style={{ flex: 1, backgroundColor: p.bg }} contentContainerStyle={{ padding: space.xl, alignItems: 'center' }}>
      <View style={{ width: '100%', maxWidth: 640, gap: space.lg }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
          <View style={{ flex: 1 }}>
            <Label p={p} style={{ fontSize: 22, color: p.ink }}>
              AR view
            </Label>
            <Body p={p} style={{ color: p.sub }}>
              Stations in your room, following the live feed where a controller is running and the simulation elsewhere.
            </Body>
          </View>
          <Button p={p} label="‹ Dashboard" onPress={onClose} />
        </View>

        <Button
          p={p}
          strong
          label="All stations around me"
          note="Every station on an arc in front of you at half size. For looking at the whole lab from somewhere else."
          onPress={() => onPick({ kits: stations.map((s) => s.id), scale: 0.5 })}
        />

        <Label p={p} style={{ fontSize: 12 }}>
          One station at full size, to stand beside the real one
        </Label>
        <View style={{ gap: space.sm }}>
          {stations.map((s) => (
            <Button key={s.id} p={p} label={s.name} note={s.title} onPress={() => onPick({ kits: [s.id], scale: 1 })} />
          ))}
        </View>
      </View>
    </ScrollView>
  );
}

function Scene({ p, preset, auto, onBack, onClose }: { p: Palette; preset: ArPreset; auto?: boolean; onBack: () => void; onClose: () => void }) {
  const feeds = useStationFeeds(preset.kits);
  const [loaded, setLoaded] = useState<Loaded[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [supported, setSupported] = useState<boolean | null>(null);
  const [presenting, setPresenting] = useState(false);
  const renderer = useRef<THREE.WebGLRenderer | null>(null);
  const registry = useMemo<Registry>(() => new Map(), []);

  useEffect(() => {
    const xr = (navigator as any).xr;
    if (!xr?.isSessionSupported) {
      setSupported(false);
      return;
    }

    xr.isSessionSupported('immersive-ar').then((ok: boolean) => setSupported(ok), () => setSupported(false));
  }, []);

  useEffect(() => {
    let cancelled = false;
    const done: Loaded[] = [];
    for (const id of preset.kits) {
      const def = TWINS[id];
      new GLTFLoader().load(
        def.url,
        (gltf) => {
          if (cancelled) return;
          try {
            const built = build(gltf.scene, def);
            const box = new THREE.Box3().setFromObject(built.root);     // in cm, before it is placed
            const k = MM / ROOT_SCALE;
            const size = box.getSize(new THREE.Vector3()).multiplyScalar(k);
            const centre = box.getCenter(new THREE.Vector3()).multiplyScalar(k);
            done.push({ id, def, built, size, shift: new THREE.Vector3(-centre.x, -box.min.y * k, -centre.z) });
            if (done.length === preset.kits.length) {
              setLoaded(preset.kits.map((kit) => done.find((d) => d.id === kit)!));
            }
          } catch (e) {
            setError(String((e as Error).message));
          }
        },
        undefined,
        () => setError('A 3D model file could not be loaded.'),
      );
    }

    return () => {
      cancelled = true;
    };
  }, [preset]);

  const places = useMemo(() => (loaded.length ? layout(loaded, preset.scale) : []), [loaded, preset.scale]);

  const enter = async (quiet = false) => {
    const gl = renderer.current;
    const xr = (navigator as any).xr;
    if (!gl || !xr) return;
    try {
      const session = await xr.requestSession('immersive-ar', { requiredFeatures: ['local-floor'], optionalFeatures: ['hand-tracking'] });
      session.addEventListener('end', () => setPresenting(false));
      gl.xr.setReferenceSpaceType('local-floor');
      await gl.xr.setSession(session);
      setPresenting(true);
    } catch (e) {
      if (!quiet) setError('The headset did not start AR: ' + String((e as Error).message));
    }
  };

  const ready = loaded.length > 0;

  // The installed headset app opens straight into AR: starting the app counts as the
  // user's go-ahead there. In a browser this attempt is refused and the button is used.
  const tried = useRef(false);
  useEffect(() => {
    if (!auto || tried.current || !ready || supported !== true) return;
    tried.current = true;
    // Not cancelled on re-render: this component re-renders several times a second
    // with the feed, and a cancelled timer would never start the session.
    const start = (attempt: number) => {
      if (renderer.current) enter(true);
      else if (attempt < 40) setTimeout(() => start(attempt + 1), 100);      // the canvas mounts after the models are ready
    };
    start(0);
  });
  return (
    <View style={{ flex: 1, backgroundColor: '#1A1B18' }}>
      <View style={{ flex: 1 }}>
        {ready ? (
          <Canvas
            camera={{ fov: 55, near: 0.05, far: 60 }}
            gl={{ alpha: true, antialias: true }}
            onCreated={(state) => {
              renderer.current = state.gl as unknown as THREE.WebGLRenderer;
            }}
          >
            <ambientLight intensity={1.1} />
            <directionalLight position={[1.5, 4, 2]} intensity={2.0} />
            <directionalLight position={[-2, 2, -1.5]} intensity={0.7} />
            <PreviewCamera presenting={presenting} />
            <Controllers registry={registry} />
            {loaded.map((kit, i) => (
              <Kit key={kit.id} loaded={kit} feed={feeds[kit.id]} place={places[i]} registry={registry} p={p} />
            ))}
          </Canvas>
        ) : (
          <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
            <Body p={p} style={{ color: '#A9A498' }}>
              {error ?? 'Loading the models…'}
            </Body>
          </View>
        )}
      </View>

      <View style={{ padding: space.lg, gap: space.md, backgroundColor: p.bg, borderTopWidth: 1, borderTopColor: p.line }}>
        {error && ready ? <Body p={p} style={{ color: p.alert }}>{error}</Body> : null}
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.md, alignItems: 'center' }}>
          <Button p={p} strong label="Enter AR" onPress={() => enter()} disabled={!ready || supported !== true || presenting} />
          <Button p={p} label="‹ Choose stations" onPress={onBack} />
          <Button p={p} label="Dashboard" onPress={onClose} />
          <View style={{ flex: 1, minWidth: 260 }}>
            <Body p={p} style={{ fontSize: 13, lineHeight: 18, color: p.sub }}>
              {supported === false
                ? 'This browser has no AR. Open this page in the browser of a Meta Quest headset; what you see here is a preview.'
                : 'In AR: point at a station and hold the trigger to carry it. While holding, the thumbstick turns it and moves it nearer or further, A / B changes its size, the grip sets full size. Down to your left are two panels: Show steps through all stations and each one alone at full size, Exit AR leaves.'}
            </Body>
          </View>
        </View>
      </View>
    </View>
  );
}
