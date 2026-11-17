// Reads the assembly structure of an IGES file: which trimmed faces (entity 144)
// belong to which sub-assembly definition (308) and where each instance (408) sits.
// OpenCascade's IGES reader flattens all of that into one shape, so this is what
// lets the converter give every part its own mesh.
import fs from 'node:fs';

export function readIgesStructure(path) {
  const text = fs.readFileSync(path, 'latin1');
  const dir = new Map();          // DE number -> { type, pd, matrix, status, form, label, lines }
  const pLines = [];              // parameter section records, in order
  let pending = null;
  let start = 0;
  while (start < text.length) {
    let end = text.indexOf('\n', start);
    if (end < 0) end = text.length;
    const line = text.slice(start, end).replace(/\r$/, '');
    start = end + 1;
    const section = line[72];
    if (section === 'D') {
      const seq = parseInt(line.slice(73, 80), 10);
      const f = (i) => line.slice(i * 8, i * 8 + 8).trim();
      if (seq % 2 === 1) {
        pending = { de: seq, type: +f(0), pd: +f(1), matrix: +f(6) || 0, status: f(8).padStart(8, '0') };
      } else if (pending) {
        pending.lineCount = +f(3);
        pending.form = +f(4) || 0;
        pending.label = f(7);
        dir.set(pending.de, pending);
        pending = null;
      }
    } else if (section === 'P') {
      pLines.push(line.slice(0, 64));
    }
  }

  // parameter data of one entity as a list of values (strings keep their text)
  const params = (e) => {
    let raw = '';
    for (let i = 0; i < e.lineCount; i++) raw += pLines[e.pd - 1 + i];
    const out = [];
    let i = 0;
    while (i < raw.length) {
      let j = i;
      while (j < raw.length && raw[j] === ' ') j++;
      const m = /^(\d+)H/.exec(raw.slice(j, j + 12));
      if (m) {
        const from = j + m[0].length;
        out.push(raw.slice(from, from + +m[1]));
        i = from + +m[1];
        while (i < raw.length && raw[i] !== ',' && raw[i] !== ';') i++;
      } else {
        let k = j;
        while (k < raw.length && raw[k] !== ',' && raw[k] !== ';') k++;
        out.push(raw.slice(j, k).trim());
        i = k;
      }
      if (raw[i] === ';') break;
      i++;
    }
    return out;
  };

  const num = (s) => parseFloat(String(s).replace(/[dD]/, 'e')) || 0;
  const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];
  const matrixOf = (de) => {
    if (!de || !dir.has(de)) return identity;
    const e = dir.get(de);
    const p = params(e).slice(1, 13).map(num);
    const own = p.length === 12 ? p : identity;
    return e.matrix ? multiply(matrixOf(e.matrix), own) : own;
  };

  const definitions = new Map();   // DE of 308 -> { name, members: DE[] }
  const instances = [];            // { de, def, matrix (3x4, row major) }
  for (const e of dir.values()) {
    if (e.type === 308) {
      const p = params(e);
      const n = +p[3];
      definitions.set(e.de, { de: e.de, name: p[2], members: p.slice(4, 4 + n).map(Number) });
    } else if (e.type === 408) {
      const p = params(e);
      const s = num(p[5]) || 1;
      const place = [s, 0, 0, num(p[2]), 0, s, 0, num(p[3]), 0, 0, s, num(p[4])];
      instances.push({ de: e.de, def: +p[1], matrix: multiply(matrixOf(e.matrix), place), status: e.status });
    }
  }

  return { dir, definitions, instances, params, matrixOf };
}

// 3x4 affine matrices, row major: [r11 r12 r13 t1, r21 ..., r31 ...]
export function multiply(a, b) {
  const out = new Array(12);
  for (let r = 0; r < 3; r++) {
    for (let c = 0; c < 4; c++) {
      out[r * 4 + c] = a[r * 4] * b[c] + a[r * 4 + 1] * b[4 + c] + a[r * 4 + 2] * b[8 + c] + (c === 3 ? a[r * 4 + 3] : 0);
    }
  }
  return out;
}

if (process.argv[1] && process.argv[1].endsWith('iges-structure.mjs')) {
  const s = readIgesStructure(process.argv[2]);
  const referenced = new Set();
  for (const d of s.definitions.values()) d.members.forEach((m) => referenced.add(m));
  const faceCount = (defDe) => {
    const d = s.definitions.get(defDe);
    let n = 0;
    for (const m of d.members) {
      const e = s.dir.get(m);
      if (!e) continue;
      if (e.type === 144) n++;
      else if (e.type === 408) n += faceCount(+s.params(e)[1]);
    }
    return n;
  };
  const top = s.instances.filter((i) => !referenced.has(i.de));
  const looseFaces = [...s.dir.values()].filter((e) => e.type === 144 && !referenced.has(e.de)).length;
  let total = looseFaces;
  console.log('definitions', s.definitions.size, 'instances', s.instances.length, 'top-level instances', top.length, 'loose faces', looseFaces);
  for (const i of top) {
    const d = s.definitions.get(i.def);
    const n = faceCount(i.def);
    total += n;
    const kids = d.members.map((m) => s.dir.get(m)?.type).reduce((a, t) => ((a[t] = (a[t] ?? 0) + 1), a), {});
    console.log(String(i.de).padStart(6), (d.name || '').padEnd(34), 'faces', String(n).padStart(4), JSON.stringify(kids), 't', i.matrix.filter((_, k) => k % 4 === 3).map((v) => Math.round(v)).join(','));
  }
  console.log('total faces through instances', total);
}
