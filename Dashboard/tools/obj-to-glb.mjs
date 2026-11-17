// Converts a kit's CAD export (the same OBJ the Unity twin imports) into a compact
// GLB for the dashboard app. No dependencies.
//
//   node obj-to-glb.mjs <in.obj> <out.glb> [cellMm]
//
// Each OBJ group ("g Component_014") becomes one named mesh, so the app can move
// the same components the Unity scripts move. Vertices are merged on a small grid
// (cellMm, default 0.6 mm) to shrink the file: the CAD export is far denser than
// a screen needs. Coordinates stay as in the file: millimetres, Z up.
import fs from 'node:fs';
import readline from 'node:readline';

const [, , inPath, outPath, cellArg] = process.argv;
if (!inPath || !outPath) {
  console.error('usage: node obj-to-glb.mjs <in.obj> <out.glb> [cellMm]');
  process.exit(1);
}
const cell = Number(cellArg ?? 0.6);

let verts = new Float32Array(3 * 1_500_000);
let vertCount = 0;
const groups = [];          // { name, tris: number[] (global vertex indices) }
let current = null;

const rl = readline.createInterface({ input: fs.createReadStream(inPath), crlfDelay: Infinity });
for await (const line of rl) {
  const c0 = line.charCodeAt(0);
  if (c0 === 118 /* v */ && line.charCodeAt(1) === 32) {
    if (vertCount * 3 + 3 > verts.length) {
      const bigger = new Float32Array(verts.length * 2);
      bigger.set(verts);
      verts = bigger;
    }
    const p = line.split(/\s+/);
    verts[vertCount * 3] = +p[1];
    verts[vertCount * 3 + 1] = +p[2];
    verts[vertCount * 3 + 2] = +p[3];
    vertCount++;
  } else if (c0 === 102 /* f */ && line.charCodeAt(1) === 32) {
    if (!current) {
      current = { name: 'Component_000', tris: [] };
      groups.push(current);
    }
    const p = line.trim().split(/\s+/);
    const idx = [];
    for (let i = 1; i < p.length; i++) {
      let v = parseInt(p[i], 10);          // "12//7" -> 12
      idx.push(v < 0 ? vertCount + v : v - 1);
    }
    for (let i = 1; i + 1 < idx.length; i++) {
      current.tris.push(idx[0], idx[i], idx[i + 1]);
    }
  } else if ((c0 === 103 /* g */ || c0 === 111 /* o */) && line.charCodeAt(1) === 32) {
    current = { name: line.slice(2).trim(), tris: [] };
    groups.push(current);
  }
}

// ---- simplify each group by merging vertices on a grid
const meshes = [];
let trisIn = 0;
let trisOut = 0;
for (const g of groups) {
  if (g.tris.length === 0) continue;
  trisIn += g.tris.length / 3;
  const cellOf = new Map();          // grid key -> local index
  const sums = [];                   // x, y, z, n per local vertex
  const remap = new Map();           // global index -> local index
  const local = (gi) => {
    let li = remap.get(gi);
    if (li !== undefined) return li;
    const x = verts[gi * 3], y = verts[gi * 3 + 1], z = verts[gi * 3 + 2];
    const key = Math.round(x / cell) + ',' + Math.round(y / cell) + ',' + Math.round(z / cell);
    li = cellOf.get(key);
    if (li === undefined) {
      li = sums.length / 4;
      cellOf.set(key, li);
      sums.push(0, 0, 0, 0);
    }
    sums[li * 4] += x; sums[li * 4 + 1] += y; sums[li * 4 + 2] += z; sums[li * 4 + 3] += 1;
    remap.set(gi, li);
    return li;
  };

  const seen = new Set();
  const indices = [];
  for (let i = 0; i < g.tris.length; i += 3) {
    const a = local(g.tris[i]), b = local(g.tris[i + 1]), c = local(g.tris[i + 2]);
    if (a === b || b === c || a === c) continue;                // collapsed
    const lo = Math.min(a, b, c), hi = Math.max(a, b, c), mid = a + b + c - lo - hi;
    const key = lo + '_' + mid + '_' + hi;
    if (seen.has(key)) continue;                                // duplicate
    seen.add(key);
    indices.push(a, b, c);
  }
  if (indices.length === 0) continue;

  const n = sums.length / 4;
  const positions = new Float32Array(n * 3);
  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < n; i++) {
    for (let k = 0; k < 3; k++) {
      const v = sums[i * 4 + k] / sums[i * 4 + 3];
      positions[i * 3 + k] = v;
      if (v < min[k]) min[k] = v;
      if (v > max[k]) max[k] = v;
    }
  }
  trisOut += indices.length / 3;
  meshes.push({
    name: g.name,
    positions,
    indices: n > 65535 ? new Uint32Array(indices) : new Uint16Array(indices),
    min: min.map(Math.fround),
    max: max.map(Math.fround),
  });
}

// ---- write GLB
const json = {
  asset: { version: '2.0', generator: 'obj-to-glb.mjs' },
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

// ---- report, including each component's box (used to work out the mechanisms)
const report = meshes.map((m) => ({ name: m.name, verts: m.positions.length / 3, tris: m.indices.length / 3, min: m.min.map((v) => +v.toFixed(1)), max: m.max.map((v) => +v.toFixed(1)) }));
fs.writeFileSync(outPath.replace(/\.glb$/, '.report.json'), JSON.stringify(report, null, 1));
console.log(`${meshes.length} components, ${Math.round(trisIn)} -> ${Math.round(trisOut)} triangles, ${(fs.statSync(outPath).size / 1e6).toFixed(1)} MB`);
