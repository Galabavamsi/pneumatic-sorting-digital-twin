// Converts a kit's IGES assembly (Solid Edge export) into a GLB for the dashboard,
// one named mesh per part, with each part split into its separate solid pieces.
//
//   node --max-old-space-size=6000 iges-to-glb.mjs <in.igs> <out.glb> [deflectionMm]
//
// Why not just OpenCascade: its IGES reader returns the whole kit as one shape, so
// nothing could be moved on its own. The file does say which faces belong to which
// part (entities 308 / 408), and OpenCascade keeps the faces in file order, so the
// two are matched here by walking the assembly tree in the same order.
//
// A part is then split wherever its faces do not touch (see `pieces`): this is what
// separates a cylinder's rod from its barrel when the CAD has them as one part.
// Mesh names: P014 for a part, P014_2 for its second piece. Coordinates stay as in
// the CAD: millimetres, Z up. A report with each mesh's box goes next to the GLB.
import fs from 'node:fs';
import { createRequire } from 'node:module';
import { readIgesStructure } from './iges-structure.mjs';

const require = createRequire(import.meta.url);
const [, , inPath, outPath, deflectionArg] = process.argv;
if (!inPath || !outPath) {
  console.error('usage: node iges-to-glb.mjs <in.igs> <out.glb> [deflectionMm]');
  process.exit(1);
}

// ---- assembly tree -> list of parts, each with its number of faces
const s = readIgesStructure(inPath);
const referenced = new Set();
for (const d of s.definitions.values()) d.members.forEach((m) => referenced.add(m));
const byDe = new Map(s.instances.map((i) => [i.de, i]));
const parts = [];        // { def, path, faces }
const walk = (inst, path) => {
  const d = s.definitions.get(inst.def);
  const own = d.members.filter((m) => s.dir.get(m)?.type === 144).length;
  const here = [...path, inst.def];
  if (own > 0) parts.push({ def: inst.def, path: here.join('/'), faces: own });
  for (const m of d.members) if (byDe.has(m)) walk(byDe.get(m), here);
};
s.instances.filter((i) => !referenced.has(i.de)).forEach((i) => walk(i, []));

// ---- tessellate with OpenCascade
const occt = await require('occt-import-js')();
const result = occt.ReadIgesFile(new Uint8Array(fs.readFileSync(inPath)), {
  linearUnit: 'millimeter',
  linearDeflectionType: 'absolute_value',
  linearDeflection: Number(deflectionArg ?? 0.4),
  angularDeflection: 0.4,
});
if (!result.success || result.meshes.length !== 1) {
  throw new Error(`expected one flattened mesh from OpenCascade, got ${result.meshes?.length}`);
}
const mesh = result.meshes[0];
const pos = mesh.attributes.position.array;
const idx = mesh.index.array;
const faces = mesh.brep_faces;
const expected = parts.reduce((n, p) => n + p.faces, 0);
if (faces.length !== expected) {
  throw new Error(`face count mismatch: OpenCascade ${faces.length}, assembly tree ${expected}. The order-based matching cannot be trusted for this file.`);
}

// ---- per part: weld vertices, find pieces that do not touch
const WELD = 0.02;       // mm
const meshes = [];
let cursor = 0;
parts.forEach((part, pi) => {
  const key = new Map();
  const xyz = [];
  const tris = [];          // local vertex indices
  const triFace = [];
  for (let f = cursor; f < cursor + part.faces; f++) {
    for (let t = faces[f].first; t <= faces[f].last; t++) {
      const v = [0, 1, 2].map((c) => {
        const g = idx[t * 3 + c];
        const x = pos[g * 3], y = pos[g * 3 + 1], z = pos[g * 3 + 2];
        const k = Math.round(x / WELD) + ',' + Math.round(y / WELD) + ',' + Math.round(z / WELD);
        let li = key.get(k);
        if (li === undefined) {
          li = xyz.length / 3;
          key.set(k, li);
          xyz.push(x, y, z);
        }
        return li;
      });
      if (v[0] !== v[1] && v[1] !== v[2] && v[0] !== v[2]) {
        tris.push(v[0], v[1], v[2]);
        triFace.push(f);
      }
    }
  }
  cursor += part.faces;

  // union-find over vertices: triangles sharing a vertex are one piece
  const parent = new Int32Array(xyz.length / 3).map((_, i) => i);
  const find = (a) => {
    while (parent[a] !== a) {
      parent[a] = parent[parent[a]];
      a = parent[a];
    }
    return a;
  };
  for (let t = 0; t < tris.length; t += 3) {
    const a = find(tris[t]);
    parent[find(tris[t + 1])] = a;
    parent[find(tris[t + 2])] = a;
  }
  const groups = new Map();
  for (let t = 0; t < tris.length; t += 3) {
    const root = find(tris[t]);
    if (!groups.has(root)) groups.set(root, []);
    groups.get(root).push(t);
  }
  const pieces = [...groups.values()].sort((a, b) => b.length - a.length);

  pieces.forEach((list, k) => {
    const remap = new Map();
    const p = [];
    const ind = [];
    const min = [Infinity, Infinity, Infinity];
    const max = [-Infinity, -Infinity, -Infinity];
    const faceSet = new Set();
    for (const t of list) {
      faceSet.add(triFace[t / 3]);
      for (let c = 0; c < 3; c++) {
        const li = tris[t + c];
        let ni = remap.get(li);
        if (ni === undefined) {
          ni = p.length / 3;
          remap.set(li, ni);
          for (let a = 0; a < 3; a++) {
            const val = xyz[li * 3 + a];
            p.push(val);
            if (val < min[a]) min[a] = val;
            if (val > max[a]) max[a] = val;
          }
        }
        ind.push(ni);
      }
    }
    meshes.push({
      name: 'P' + String(pi + 1).padStart(3, '0') + (pieces.length > 1 ? '_' + (k + 1) : ''),
      def: part.def,
      positions: new Float32Array(p),
      indices: p.length / 3 > 65535 ? new Uint32Array(ind) : new Uint16Array(ind),
      min: min.map(Math.fround),
      max: max.map(Math.fround),
      faces: faceSet.size,
    });
  });
});

// ---- write GLB
const json = {
  asset: { version: '2.0', generator: 'iges-to-glb.mjs' },
  scene: 0,
  scenes: [{ nodes: meshes.map((_, i) => i) }],
  nodes: meshes.map((m, i) => ({ name: m.name, mesh: i })),
  meshes: [],
  accessors: [],
  bufferViews: [],
  buffers: [{ byteLength: 0 }],
};
const chunks = [];
let offset = 0;
const addView = (typed, target) => {
  const bytes = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
  const pad = (4 - (bytes.length % 4)) % 4;
  json.bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: bytes.length, target });
  chunks.push(bytes, Buffer.alloc(pad));
  offset += bytes.length + pad;
  return json.bufferViews.length - 1;
};
for (const m of meshes) {
  const pv = addView(m.positions, 34962);
  json.accessors.push({ bufferView: pv, componentType: 5126, count: m.positions.length / 3, type: 'VEC3', min: m.min, max: m.max });
  const iv = addView(m.indices, 34963);
  json.accessors.push({ bufferView: iv, componentType: m.indices instanceof Uint32Array ? 5125 : 5123, count: m.indices.length, type: 'SCALAR' });
  json.meshes.push({ name: m.name, primitives: [{ attributes: { POSITION: json.accessors.length - 2 }, indices: json.accessors.length - 1 }] });
}
json.buffers[0].byteLength = offset;
let jsonBytes = Buffer.from(JSON.stringify(json));
jsonBytes = Buffer.concat([jsonBytes, Buffer.alloc((4 - (jsonBytes.length % 4)) % 4, 0x20)]);
const bin = Buffer.concat(chunks);
const header = Buffer.alloc(12);
header.writeUInt32LE(0x46546c67, 0);
header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonBytes.length + 8 + bin.length, 8);
const jsonHead = Buffer.alloc(8);
jsonHead.writeUInt32LE(jsonBytes.length, 0);
jsonHead.writeUInt32LE(0x4e4f534a, 4);
const binHead = Buffer.alloc(8);
binHead.writeUInt32LE(bin.length, 0);
binHead.writeUInt32LE(0x004e4942, 4);
fs.writeFileSync(outPath, Buffer.concat([header, jsonHead, jsonBytes, binHead, bin]));

const report = meshes.map((m) => ({ name: m.name, def: m.def, faces: m.faces, tris: m.indices.length / 3, min: m.min.map((v) => +v.toFixed(1)), max: m.max.map((v) => +v.toFixed(1)) }));
fs.writeFileSync(outPath.replace(/\.glb$/, '.report.json'), JSON.stringify(report, null, 1));
console.log(`${parts.length} parts -> ${meshes.length} meshes, ${idx.length / 3} triangles, ${(fs.statSync(outPath).size / 1e6).toFixed(1)} MB`);
