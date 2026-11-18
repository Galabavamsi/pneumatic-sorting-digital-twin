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
// It is built for bare hands first (hand tracking), and controllers work the same
// way: point at a station and pinch (or hold the trigger) to carry it. With both
// hands on it, pulling them apart resizes it and turning them turns it. A quick
// pinch opens the station's information board (jobs, cycle times, log). The menu
// beside the viewer can be pinched from a distance or pressed with a fingertip.
// Controllers add: thumbstick to turn and reel in or out, A / X and B / Y to
// resize, grip for full size. Where a station is put is held to the room with the
// headset's anchors and remembered.

const MM = 0.001;                    // CAD millimetres -> metres
const ROOT_SCALE = 0.1;              // the scale build() gives its root (mm -> cm)
const TABLE = 0.75;                  // height a station first stands at, metres

export type ArPreset = { kits: string[]; scale: number };

type Info = { name: string; title: string; live: boolean; state: string; step: string };
type Feed = { live: React.MutableRefObject<Live>; info: React.MutableRefObject<Info>; snap: React.MutableRefObject<Snapshot | null>; read: (() => Snapshot) | null };
type Loaded = { id: string; def: TwinDef; built: Built; size: THREE.Vector3; shift: THREE.Vector3 };
type Place = { position: [number, number, number]; yaw: number; scale: number };
// One station in the room. Everything that moves a station writes `target`; the station
// eases towards it every frame, which is what makes carrying and resizing feel smooth.
type Entry = {
  holder: THREE.Group;
  proxy: THREE.Mesh;
  hot: boolean;
  name: string;
  azimuth: number;
  home: Place;
  target: { pos: THREE.Vector3; yaw: number; scale: number };
  info: boolean;            // its information board is open
  placed: boolean;          // the viewer has put it somewhere themselves
  anchor: any;              // XRAnchor holding it to the room, once placed
  anchorUuid: string | null;
  anchorDirty: boolean;     // it was moved: make a new anchor at the next frame
  snap: boolean;            // jump to the target instead of easing (a restored placement)
};
type Registry = Map<string, Entry>;

const STATE_WORD: Record<string, string> = { running: 'Running', idle: 'Idle', homing: 'Homing', fault: 'Fault', unknown: 'Unknown program' };
const INK = { bg: 'rgba(18,19,17,0.86)', ink: '#ECE9DF', sub: '#A9A498', data: '#63C2B1', warn: '#E2AC43', alert: '#F26A45', line: 'rgba(236,233,223,0.22)' };
const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
const MIN_SCALE = 0.05;
const MAX_SCALE = 2;

// Where a station was put is kept in the browser, keyed by station: the id of the
// headset's persistent anchor and the station's size.
const storeKey = (id: string) => 'rig-ar-place:' + id;
function readPlace(id: string): { uuid: string; scale: number } | null {
  try {
    const raw = window.localStorage.getItem(storeKey(id));
    const value = raw ? JSON.parse(raw) : null;
    return value && typeof value.uuid === 'string' ? { uuid: value.uuid, scale: Number(value.scale) || 1 } : null;
  } catch {
    return null;
  }
}

function writePlace(id: string, value: { uuid: string; scale: number } | null) {
  try {
    if (value) window.localStorage.setItem(storeKey(id), JSON.stringify(value));
    else window.localStorage.removeItem(storeKey(id));
  } catch {
    // storage is unavailable: the placement just is not remembered
  }
}

// ------------------------------------------------------------------ the data
// One feed per station: the live snapshot when a controller is running that
// station's program, the simulation otherwise.
function useStationFeeds(ids: string[]) {
  const { machines } = useFactory('live');
  const latest = useRef(machines);
  latest.current = machines;
  const feeds = useMemo(() => {
    const out: Record<string, Feed> = {};
    for (const id of ids) {
      const station = STATIONS.find((s) => s.id === id);
      const read = station?.sim ? createSim(station.sim) : null;
      out[id] = {
        live: { current: { travel: {}, step: 0, material: 0 } },
        info: { current: { name: station?.name ?? id, title: station?.title ?? '', live: false, state: '', step: '' } },
        snap: { current: null },
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
        feed.snap.current = snap;
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

  g.textAlign = 'left';
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
  g.fillText(hint ? 'Pinch and hold: carry · both hands: resize, turn · quick pinch: info' : size, 26, 178, LABEL_W - 52);
}

// ------------------------------------------------------- the information board
// Jobs, cycle times and the latest events of one station, beside it in the room.
const BOARD_W = 660;
const BOARD_H = 800;
const BOARD_M = 0.44;                // width in the room, metres

function drawBoard(canvas: HTMLCanvasElement, info: Info, snap: Snapshot | null) {
  const g = canvas.getContext('2d');
  if (!g) return;
  g.clearRect(0, 0, BOARD_W, BOARD_H);
  g.fillStyle = INK.bg;
  g.beginPath();
  g.roundRect(0, 0, BOARD_W, BOARD_H, 24);
  g.fill();
  g.textAlign = 'left';
  g.textBaseline = 'alphabetic';

  const left = 30;
  const right = BOARD_W - 30;
  let y = 62;
  const heading = (text: string) => {
    g.fillStyle = INK.sub;
    g.font = '600 26px BarlowCondensed_600SemiBold, "Arial Narrow", sans-serif';
    g.fillText(text.toUpperCase(), left, y);
    y += 12;
    g.fillStyle = INK.line;
    g.fillRect(left, y, right - left, 2);
    y += 38;
  };
  const row = (label: string, value: string, tone: string = INK.ink) => {
    g.fillStyle = INK.sub;
    g.font = '28px Barlow_400Regular, Arial, sans-serif';
    g.textAlign = 'left';
    g.fillText(label, left, y, 330);
    g.fillStyle = tone;
    g.font = '500 30px IBMPlexMono_500Medium, Consolas, monospace';
    g.textAlign = 'right';
    g.fillText(value, right, y, 270);
    g.textAlign = 'left';
    y += 42;
  };

  g.fillStyle = INK.ink;
  g.font = '600 46px BarlowCondensed_600SemiBold, "Arial Narrow", sans-serif';
  g.fillText(info.name.toUpperCase(), left, y);
  const fault = info.state === 'Fault';
  g.fillStyle = fault ? INK.alert : info.live ? INK.data : INK.warn;
  g.font = '600 30px BarlowCondensed_600SemiBold, "Arial Narrow", sans-serif';
  g.textAlign = 'right';
  g.fillText(info.live ? 'LIVE' : 'SIMULATED', right, y);
  g.textAlign = 'left';
  y += 56;

  if (!snap) {
    g.fillStyle = INK.sub;
    g.font = '28px Barlow_400Regular, Arial, sans-serif';
    g.fillText('No data yet.', left, y);
    return;
  }

  heading('Now');
  row('State', info.state, fault ? INK.alert : INK.ink);
  row('Step', info.step || '—');
  row('Part on the machine', snap.status?.materialName ?? '—');
  y += 14;

  heading('Jobs');
  for (const c of (snap.counters ?? []).slice(0, 4)) {
    row(c.label, c.value === null ? '—' : String(c.value));
  }

  const seconds = (v: number | null | undefined) => (v === null || v === undefined ? '—' : v.toFixed(1) + ' s');
  row('Last cycle', seconds(snap.cycle?.lastSeconds));
  row('Average cycle', seconds(snap.cycle?.averageSeconds));
  y += 14;

  heading('Log');
  g.font = '24px Barlow_400Regular, Arial, sans-serif';
  for (const e of snap.events ?? []) {
    if (y > BOARD_H - 22) break;
    const time = new Date(e.ts);
    const clock = isNaN(time.getTime()) ? '' : time.toLocaleTimeString([], { hour12: false });
    g.fillStyle = INK.sub;
    g.font = '22px IBMPlexMono_400Regular, Consolas, monospace';
    g.fillText(clock, left, y);
    g.fillStyle = e.kind === 'fault' ? INK.alert : e.kind === 'warn' ? INK.warn : INK.ink;
    g.font = '24px Barlow_400Regular, Arial, sans-serif';
    g.fillText(e.text, left + 128, y, right - left - 128);
    y += 34;
  }
}

// The panels that float near the viewer during a session.
const PANEL_W = 0.23;
const PANEL_H = 0.078;

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

function sprite(width: number, height: number) {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  const object = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true, depthWrite: false }));
  return { canvas, texture, object };
}

// "&info=1" in the address opens every board from the start (also shows them in the desktop preview).
const BOARDS_OPEN = typeof window !== "undefined" && !!window.location && new URLSearchParams(window.location.search).get("info") === "1";

const UP = new THREE.Vector3(0, 1, 0);
const side = new THREE.Vector3();      // scratch

// ---------------------------------------------------------------- one station
function Kit({ loaded, feed, place, registry, p }: { loaded: Loaded; feed: Feed; place: Place; registry: Registry; p: Palette }) {
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
    const label = sprite(LABEL_W, LABEL_H);
    const board = sprite(BOARD_W, BOARD_H);
    board.object.visible = false;
    holder.add(proxy, outline, label.object, board.object);
    return { holder, proxy, outline, label, board, drawn: '', boardDrawn: '' };
  }, [id, size]);

  useEffect(() => {
    parts.holder.position.set(...place.position);
    parts.holder.rotation.set(0, place.yaw, 0);
    parts.holder.scale.setScalar(place.scale);
    const entry: Entry = {
      holder: parts.holder,
      proxy: parts.proxy,
      hot: false,
      name: feed.info.current.name,
      azimuth: def.azimuth,
      home: place,
      target: { pos: new THREE.Vector3(...place.position), yaw: place.yaw, scale: place.scale },
      info: BOARDS_OPEN,
      placed: false,
      anchor: null,
      anchorUuid: null,
      anchorDirty: false,
      snap: false,
    };
    registry.set(id, entry);
    return () => {
      registry.delete(id);
    };
  }, [id, parts, registry, feed, def, place]);

  useFrame((state, dtRaw) => {
    const entry = registry.get(id);
    if (!entry) return;
    const holder = parts.holder;

    // ease towards where the station has been asked to be
    const k = entry.snap ? 1 : 1 - Math.exp(-Math.min(dtRaw, 0.1) * 16);
    entry.snap = false;
    holder.position.lerp(entry.target.pos, k);
    const turn = Math.atan2(Math.sin(entry.target.yaw - holder.rotation.y), Math.cos(entry.target.yaw - holder.rotation.y));
    holder.rotation.y += turn * k;
    holder.scale.setScalar(holder.scale.x + (entry.target.scale - holder.scale.x) * k);

    const scale = holder.scale.x;
    parts.outline.visible = entry.hot;

    // the label and the board keep their real size whatever the station's scale
    parts.label.object.scale.set(LABEL_M / scale, (LABEL_M * LABEL_H) / LABEL_W / scale, 1);
    parts.label.object.position.y = size.y + (LABEL_M * 0.28) / scale;

    const info = feed.info.current;
    const key = `${info.live}|${info.state}|${info.step}|${scale.toFixed(2)}|${entry.hot}`;
    if (key !== parts.drawn) {
      parts.drawn = key;
      drawLabel(parts.label.canvas, info, scale, entry.hot);
      parts.label.texture.needsUpdate = true;
    }

    const board = parts.board.object;
    board.visible = entry.info;
    if (entry.info) {
      const height = (BOARD_M * BOARD_H) / BOARD_W;
      board.scale.set(BOARD_M / scale, height / scale, 1);
      // to the viewer's right of the station, whichever way the station is turned
      side.set(0, 0, -1).transformDirection(state.camera.matrixWorld).setY(0);
      if (side.lengthSq() < 0.01) side.set(0, 0, -1);
      side.normalize().set(-side.z, 0, side.x);
      const reach = (Math.hypot(size.x, size.z) / 2) * scale + BOARD_M / 2 + 0.04;
      board.position
        .copy(side)
        .multiplyScalar(reach)
        .applyAxisAngle(UP, -holder.rotation.y)
        .divideScalar(scale)
        .setY(Math.max(size.y / 2, height / 2 / scale));
      const snap = feed.snap.current;
      const boardKey = `${info.live}|${info.state}|${info.step}|${snap?.status?.material}|${(snap?.counters ?? []).map((c) => c.value).join(',')}|${snap?.cycle?.lastSeconds}|${snap?.events?.[0]?.ts}`;
      if (boardKey !== parts.boardDrawn) {
        parts.boardDrawn = boardKey;
        drawBoard(parts.board.canvas, info, snap);
        parts.board.texture.needsUpdate = true;
        // development aid: lets the board be looked at from the browser console
        if (__DEV__) (globalThis as { __arBoard?: unknown }).__arBoard = parts.board.canvas;
      }
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
// One hand (a controller, or a tracked hand that pinches) holding a station: the
// station hangs `offset` away from the point `dist` along the hand's ray.
type Grab = { id: string; dist: number; offset: THREE.Vector3 };
// Both hands on one station: it is stretched, turned and carried between them.
type Duo = { id: string; span: number; angle: number; mid: THREE.Vector3; pos: THREE.Vector3; yaw: number; scale: number };
type PanelKey = 'exit' | 'reset' | 'full' | 'info' | 'show';
const PANELS: PanelKey[] = ['exit', 'reset', 'full', 'info', 'show'];      // bottom to top
type Hand = {
  space: THREE.Object3D;
  ray: THREE.Line;
  source: any;
  grab: Grab | null;
  over: { id: string; dist: number; point: THREE.Vector3 } | null;
  panel: PanelKey | null;
  poked: PanelKey | null;        // the panel a fingertip is pressing, so one press counts once
  joints: any;                   // the tracked hand's joints, when the hand itself is used
  cursor: THREE.Mesh;            // a dot where the hand points
  downAt: number;
  downPoint: THREE.Vector3;
  origin: THREE.Vector3;
  dir: THREE.Vector3;
  point: THREE.Vector3;
  live: boolean;
  fresh: boolean;                // no earlier pointing direction to smooth from
};
const deadzone = (v: number | undefined) => (v !== undefined && Math.abs(v) > 0.18 ? v : 0);
const TAP_MS = 280;             // a press shorter than this is a tap (controller)
const PINCH_TAP_MS = 480;       // a pinch takes longer to make and let go
const TAP_REACH = 0.06;         // and a tap does not travel further than this, metres

function Controllers({ registry }: { registry: Registry }) {
  const { gl, scene, camera } = useThree();
  const hands = useRef<Hand[]>([]);
  const duo = useRef<Duo | null>(null);
  const restoredFor = useRef<unknown>(null);
  const tools = useMemo(() => ({ caster: new THREE.Raycaster(), v: new THREE.Vector3(), q: new THREE.Quaternion(), e: new THREE.Euler(0, 0, 0, 'YXZ'), up: new THREE.Vector3(0, 1, 0) }), []);

  // The menu: a short stack of panels that stays within reach, down and to the left of the view.
  const menu = useMemo(() => {
    const group = new THREE.Group();
    group.visible = false;
    const panels = PANELS.map((key, i) => {
      const canvas = document.createElement('canvas');
      canvas.width = 400;
      canvas.height = 140;
      const texture = new THREE.CanvasTexture(canvas);
      texture.colorSpace = THREE.SRGBColorSpace;
      const mesh = new THREE.Mesh(new THREE.PlaneGeometry(PANEL_W, PANEL_H), new THREE.MeshBasicMaterial({ map: texture, transparent: true, side: THREE.DoubleSide }));
      mesh.position.y = i * (PANEL_H + 0.012);
      mesh.userData.panel = key;
      group.add(mesh);
      return { key, canvas, texture, mesh, drawn: '' };
    });
    return { group, panels, placed: false, only: null as string | null, picked: null as string | null, head: new THREE.Vector3(), want: new THREE.Vector3(), flat: new THREE.Vector3() };
  }, []);

  useEffect(() => {
    scene.add(menu.group);
    return () => {
      scene.remove(menu.group);
    };
  }, [scene, menu]);

  // a station brought in front of the viewer, facing them
  const bringInFront = (entry: Entry, scale: number) => {
    entry.target.pos.copy(menu.head).addScaledVector(menu.flat, 0.9);
    entry.target.pos.y = TABLE;
    entry.target.yaw = -Math.atan2(menu.flat.x, -menu.flat.z) - entry.azimuth;
    entry.target.scale = scale;
  };

  const forget = (id: string, entry: Entry) => {
    const session = gl.xr.getSession() as any;
    try {
      entry.anchor?.delete?.();
    } catch {
      // already gone
    }

    if (entry.anchorUuid) session?.deletePersistentAnchor?.(entry.anchorUuid)?.catch?.(() => {});
    entry.anchor = null;
    entry.anchorUuid = null;
    entry.anchorDirty = false;
    entry.placed = false;
    writePlace(id, null);
  };

  const press = (key: PanelKey) => {
    if (key === 'exit') {
      gl.xr.getSession()?.end();
    } else if (key === 'info') {
      // open every board, or close them all if they are all open
      let allOpen = true;
      registry.forEach((entry) => {
        if (entry.holder.visible && !entry.info) allOpen = false;
      });
      registry.forEach((entry) => (entry.info = !allOpen));
    } else if (key === 'full') {
      // the station last touched goes to real size; with none touched yet, every station does
      const one = menu.picked ? registry.get(menu.picked) : undefined;
      registry.forEach((entry) => {
        if (one ? entry !== one : !entry.holder.visible) return;
        entry.target.scale = 1;
        entry.placed = true;
        entry.anchorDirty = true;
      });
    } else if (key === 'reset') {
      // back to the arc in front of the viewer; remembered placements are dropped
      menu.only = null;
      menu.picked = null;
      registry.forEach((entry, id) => {
        forget(id, entry);
        entry.holder.visible = true;
        entry.target.pos.set(...entry.home.position);
        entry.target.yaw = entry.home.yaw;
        entry.target.scale = entry.home.scale;
      });
    } else {
      // all -> the first station alone -> the next ... -> all
      const ids = [...registry.keys()];
      const next = menu.only === null ? 0 : ids.indexOf(menu.only) + 1;
      menu.only = next < ids.length ? ids[next] : null;
      registry.forEach((entry, id) => {
        entry.holder.visible = menu.only === null || menu.only === id;
        // a station picked alone comes to the viewer at full size, unless they have already put it somewhere
        if (menu.only === id && !entry.placed) bringInFront(entry, 1);
        if (menu.only === null && !entry.placed) {
          entry.target.pos.set(...entry.home.position);
          entry.target.yaw = entry.home.yaw;
          entry.target.scale = entry.home.scale;
        }
      });
    }
  };

  useEffect(() => {
    const list: Hand[] = [0, 1].map((i) => {
      const space = gl.xr.getController(i) as THREE.Object3D;
      const ray = new THREE.Line(
        new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 0, -1)]),
        new THREE.LineBasicMaterial({ color: '#ECE9DF', transparent: true, opacity: 0.7 }),
      );
      space.add(ray);
      scene.add(space);
      const joints = gl.xr.getHand(i) as THREE.Object3D;      // filled in by the headset while a bare hand is tracked
      scene.add(joints);
      const cursor = new THREE.Mesh(new THREE.SphereGeometry(0.009, 16, 12), new THREE.MeshBasicMaterial({ color: '#ECE9DF' }));
      cursor.visible = false;
      scene.add(cursor);
      return {
        space,
        ray,
        source: null,
        grab: null,
        over: null,
        panel: null,
        poked: null,
        joints,
        cursor,
        downAt: 0,
        downPoint: new THREE.Vector3(),
        origin: new THREE.Vector3(),
        dir: new THREE.Vector3(),
        point: new THREE.Vector3(),
        live: false,
        fresh: true,
      };
    });
    hands.current = list;

    const off: (() => void)[] = [];
    list.forEach((hand, i) => {
      const other = list[1 - i];
      const listen = (type: string, fn: (e: any) => void) => {
        (hand.space as any).addEventListener(type, fn);
        off.push(() => (hand.space as any).removeEventListener(type, fn));
      };
      const release = () => {
        const grab = hand.grab;
        hand.grab = null;
        if (!grab) return;
        const entry = registry.get(grab.id);
        if (!entry) return;
        if (other.grab?.id === grab.id) {
          // the other hand carries on alone from where the station is now
          duo.current = null;
          other.grab.offset.copy(entry.target.pos).sub(other.point);
          return;
        }

        const quick = performance.now() - hand.downAt < (hand.source?.hand ? PINCH_TAP_MS : TAP_MS);
        if (quick && hand.point.distanceTo(hand.downPoint) < TAP_REACH) {
          entry.info = !entry.info;      // a quick tap opens or closes the station's board
        } else {
          entry.placed = true;
          entry.anchorDirty = true;
        }
      };

      listen('connected', (e) => (hand.source = e.data));
      listen('disconnected', () => {
        hand.source = null;
        release();
        hand.over = null;
      });
      listen('selectstart', () => {
        if (hand.panel) {
          press(hand.panel);
          return;
        }

        const over = hand.over;
        const entry = over ? registry.get(over.id) : undefined;
        if (!over || !entry) return;
        hand.downAt = performance.now();
        hand.grab = { id: over.id, dist: over.dist, offset: entry.target.pos.clone().sub(over.point) };
        hand.point.copy(over.point);
        hand.downPoint.copy(over.point);
        menu.picked = over.id;
      });
      listen('selectend', release);
      listen('squeezestart', () => {
        const target = hand.grab?.id ?? hand.over?.id;
        const entry = target ? registry.get(target) : undefined;
        if (!entry || !target) return;
        menu.picked = target;
        entry.target.scale = 1;
        entry.placed = true;
        entry.anchorDirty = true;
      });
    });

    return () => {
      off.forEach((f) => f());
      list.forEach((hand) => {
        hand.space.remove(hand.ray);
        scene.remove(hand.space);
        scene.remove(hand.joints);
        scene.remove(hand.cursor);
      });
      hands.current = [];
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gl, scene, registry]);

  useFrame((_, dtRaw, frame: any) => {
    const dt = Math.min(dtRaw, 0.1);
    const { caster, v, q, e, up } = tools;
    const presenting = gl.xr.isPresenting;
    const [a, b] = hands.current;
    registry.forEach((entry) => (entry.hot = false));

    // ---- the menu follows the viewer, but only once they have moved or turned well away from it
    menu.group.visible = presenting;
    if (!presenting) {
      menu.placed = false;
    } else {
      menu.head.setFromMatrixPosition(camera.matrixWorld);
      menu.flat.set(0, 0, -1).transformDirection(camera.matrixWorld).setY(0);
      if (menu.flat.lengthSq() > 0.01) {
        menu.flat.normalize();
        menu.want.copy(menu.head).addScaledVector(menu.flat, 0.5);
        menu.want.x += menu.flat.z * 0.32;        // to the viewer's left
        menu.want.z -= menu.flat.x * 0.32;
        menu.want.y = Math.max(0.3, menu.head.y - 0.62);
        if (!menu.placed) {
          menu.group.position.copy(menu.want);
          menu.placed = true;
        } else if (menu.group.position.distanceTo(menu.want) > 0.45) {
          menu.group.position.lerp(menu.want, Math.min(1, dt * 3));
        }
      }

      v.copy(menu.head).setY(menu.group.position.y);
      menu.group.lookAt(v);
    }

    // ---- placements held to the room by the headset's anchors
    const session = presenting ? (gl.xr.getSession() as any) : null;
    const refSpace = presenting ? (gl.xr.getReferenceSpace() as any) : null;
    const held = (id: string) => a?.grab?.id === id || b?.grab?.id === id;
    if (session && refSpace && frame) {
      if (restoredFor.current !== session) {
        // a new session: bring back what was placed last time
        restoredFor.current = session;
        registry.forEach((entry, id) => {
          const saved = readPlace(id);
          entry.anchor = null;
          if (!saved || !session.restorePersistentAnchor || !session.persistentAnchors?.has?.(saved.uuid)) return;
          session.restorePersistentAnchor(saved.uuid).then(
            (anchor: any) => {
              entry.anchor = anchor;
              entry.anchorUuid = saved.uuid;
              entry.target.scale = clamp(saved.scale, MIN_SCALE, MAX_SCALE);
              entry.placed = true;
              entry.snap = true;
            },
            () => writePlace(id, null),
          );
        });
      }

      registry.forEach((entry, id) => {
        if (held(id)) return;
        if (entry.anchorDirty && frame.createAnchor && (globalThis as any).XRRigidTransform) {
          // it was moved: let go of the old anchor and make one where it stands now
          entry.anchorDirty = false;
          const previous = entry.anchorUuid;
          try {
            entry.anchor?.delete?.();
          } catch {
            // already gone
          }

          entry.anchor = null;
          q.setFromAxisAngle(up, entry.target.yaw);
          const pose = new (globalThis as any).XRRigidTransform(
            { x: entry.target.pos.x, y: entry.target.pos.y, z: entry.target.pos.z },
            { x: q.x, y: q.y, z: q.z, w: q.w },
          );
          const scale = entry.target.scale;
          frame.createAnchor(pose, refSpace)?.then(
            (anchor: any) => {
              if (held(id)) {
                anchor.delete?.();
                return;
              }

              entry.anchor = anchor;
              anchor.requestPersistentHandle?.()?.then(
                (uuid: string) => {
                  if (previous && previous !== uuid) session.deletePersistentAnchor?.(previous)?.catch?.(() => {});
                  entry.anchorUuid = uuid;
                  writePlace(id, { uuid, scale });
                },
                () => {},
              );
            },
            () => {},
          );
        } else if (entry.anchor) {
          // the headset keeps correcting where the anchor is in the room; the station follows
          const pose = frame.getPose?.(entry.anchor.anchorSpace, refSpace);
          if (pose) {
            const t = pose.transform;
            entry.target.pos.set(t.position.x, t.position.y, t.position.z);
            q.set(t.orientation.x, t.orientation.y, t.orientation.z, t.orientation.w);
            entry.target.yaw = e.setFromQuaternion(q).y;
          }
        }
      });
    }

    // ---- where each hand points
    for (const hand of hands.current) {
      hand.live = presenting && hand.space.visible;
      hand.ray.visible = hand.live;
      hand.cursor.visible = false;
      hand.panel = null;
      if (!hand.live) {
        hand.over = null;
        hand.fresh = true;
        hand.poked = null;
        continue;
      }

      // A bare hand's pointing ray trembles, more so at a distance, so it is eased.
      const bare = !!hand.source?.hand;
      const ease = bare && !hand.fresh ? 1 - Math.exp(-dt * 20) : 1;
      hand.fresh = false;
      hand.origin.lerp(v.setFromMatrixPosition(hand.space.matrixWorld), ease);
      hand.dir.lerp(v.set(0, 0, -1).transformDirection(hand.space.matrixWorld), ease).normalize();
      // the drawn ray follows the steadied one, and is shorter and fainter for a bare hand
      hand.ray.quaternion.copy(hand.space.getWorldQuaternion(q).invert()).multiply(q.setFromUnitVectors(v.set(0, 0, -1), hand.dir));
      (hand.ray.material as THREE.LineBasicMaterial).opacity = bare ? 0.35 : 0.7;
      if (hand.grab) {
        if (!registry.get(hand.grab.id)) hand.grab = null;
        else hand.point.copy(hand.origin).addScaledVector(hand.dir, hand.grab.dist);
      }
    }

    // ---- both hands on one station: stretch to resize, turn the hands to turn it
    const both = a?.live && b?.live && a.grab && b.grab && a.grab.id === b.grab.id ? registry.get(a.grab.id) : undefined;
    if (both && a.grab && b.grab) {
      const dx = b.point.x - a.point.x;
      const dz = b.point.z - a.point.z;
      const span = Math.max(0.02, a.point.distanceTo(b.point));
      const angle = Math.atan2(dz, dx);
      v.copy(a.point).add(b.point).multiplyScalar(0.5);
      if (!duo.current || duo.current.id !== a.grab.id) {
        duo.current = { id: a.grab.id, span, angle, mid: v.clone(), pos: both.target.pos.clone(), yaw: both.target.yaw, scale: both.target.scale };
      }

      const d = duo.current;
      const scale = clamp((d.scale * span) / d.span, MIN_SCALE, MAX_SCALE);
      const turned = -(angle - d.angle);
      both.target.scale = scale;
      both.target.yaw = d.yaw + turned;
      // the point between the hands stays on the same spot of the station
      both.target.pos.copy(d.pos).sub(d.mid).multiplyScalar(scale / d.scale).applyAxisAngle(up, turned).add(v);
      both.target.pos.y = Math.max(0, both.target.pos.y);
      both.hot = true;
      for (const hand of [a, b]) {
        hand.ray.scale.z = hand.grab ? hand.grab.dist : 1;
        hand.cursor.position.copy(hand.point);
        hand.cursor.visible = true;
      }

      return;
    }

    duo.current = null;

    // ---- one hand: carry; the thumbstick turns and reels in or out, A / B resizes
    const menuMeshes = menu.panels.filter((panel) => panel.mesh.visible).map((panel) => panel.mesh);
    for (const hand of hands.current) {
      if (!hand.live) continue;
      const grab = hand.grab;
      const entry = grab ? registry.get(grab.id) : undefined;
      if (grab && entry) {
        const pad = hand.source?.gamepad as Gamepad | undefined;
        if (pad) {
          entry.target.yaw -= deadzone(pad.axes[2]) * dt * 1.6;
          grab.dist = clamp(grab.dist - deadzone(pad.axes[3]) * dt * 1.5, 0.15, 8);
          const grow = (pad.buttons[5]?.pressed ? 1 : 0) - (pad.buttons[4]?.pressed ? 1 : 0);
          if (grow !== 0) entry.target.scale = clamp(entry.target.scale * Math.exp(grow * dt * 0.9), MIN_SCALE, MAX_SCALE);
        }

        hand.point.copy(hand.origin).addScaledVector(hand.dir, grab.dist);
        entry.target.pos.copy(hand.point).add(grab.offset);
        entry.target.pos.y = Math.max(0, entry.target.pos.y);
        entry.hot = true;
        hand.ray.scale.z = grab.dist;
        hand.cursor.position.copy(hand.point);
        hand.cursor.visible = true;
        continue;
      }

      // a fingertip pushed into a panel presses it
      const tip = hand.source?.hand ? hand.joints?.joints?.['index-finger-tip'] : null;
      let touching: PanelKey | null = null;
      if (tip && tip.visible !== false) {
        menu.group.updateWorldMatrix(true, true);
        for (const mesh of menuMeshes) {
          v.copy(tip.position);
          mesh.worldToLocal(v);
          if (Math.abs(v.x) < PANEL_W / 2 && Math.abs(v.y) < PANEL_H / 2 && v.z < 0.05 && v.z > -0.05) {
            hand.panel = mesh.userData.panel as PanelKey;      // lights up as the finger comes close
            if (v.z < 0.012) touching = hand.panel;
          }
        }
      }

      if (touching && hand.poked !== touching) press(touching);
      if (touching || !hand.panel) hand.poked = touching;
      if (hand.panel) {
        hand.over = null;
        hand.ray.scale.z = 0.02;
        continue;
      }

      caster.set(hand.origin, hand.dir);
      const targets: THREE.Object3D[] = [...menuMeshes];
      registry.forEach((item) => {
        if (!item.holder.visible) return;
        item.holder.updateWorldMatrix(true, true);
        targets.push(item.proxy);
      });
      menu.group.updateWorldMatrix(true, true);
      const hit = caster.intersectObjects(targets, false)[0];
      hand.ray.scale.z = hit ? hit.distance : hand.source?.hand ? 0.5 : 3;
      hand.over = null;
      if (!hit) continue;
      hand.cursor.position.copy(hit.point);
      hand.cursor.visible = true;
      if (hit.object.userData.panel) {
        hand.panel = hit.object.userData.panel as PanelKey;
      } else {
        const id = hit.object.userData.id as string;
        hand.over = { id, dist: hit.distance, point: hit.point.clone() };
        const item = registry.get(id);
        if (item) item.hot = true;
      }
    }

    // ---- the menu's wording
    let boards = 0;
    let shown = 0;
    registry.forEach((entry) => {
      if (!entry.holder.visible) return;
      shown++;
      if (entry.info) boards++;
    });
    for (const panel of menu.panels) {
      panel.mesh.visible = panel.key !== 'show' || registry.size > 1;
      const text =
        panel.key === 'exit'
          ? 'EXIT AR'
          : panel.key === 'reset'
            ? 'RESET PLACES'
            : panel.key === 'full'
              ? 'FULL SIZE'
            : panel.key === 'info'
              ? boards > 0 && boards === shown
                ? 'INFO: ON'
                : 'INFO: OFF'
              : 'SHOW: ' + (menu.only === null ? 'ALL' : (registry.get(menu.only)?.name ?? '').toUpperCase());
      const hot = hands.current.some((hand) => hand.panel === panel.key);
      const key = text + hot;
      if (key !== panel.drawn) {
        panel.drawn = key;
        drawPanel(panel.canvas, text, hot);
        panel.texture.needsUpdate = true;
      }
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
      const session = await xr.requestSession('immersive-ar', { requiredFeatures: ['local-floor'], optionalFeatures: ['hand-tracking', 'anchors'] });
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
                : 'In AR, with your hands or the controllers: point at a station and pinch (or hold the trigger) to carry it; take it with both hands and pull them apart or turn them to resize and turn it. A quick pinch opens its information board. Where you put a station is remembered. The panels down to your left can be pinched or pressed with a finger: show one station or all, boards, full size, reset the places, leave AR.'}
            </Body>
          </View>
        </View>
      </View>
    </View>
  );
}
