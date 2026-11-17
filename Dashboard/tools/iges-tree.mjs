// Prints the assembly tree of an IGES file: node iges-tree.mjs <file.igs>
import { readIgesStructure } from './iges-structure.mjs';
const s = readIgesStructure(process.argv[2]);
const referenced = new Set();
for (const d of s.definitions.values()) d.members.forEach((m) => referenced.add(m));
const faces = (defDe) => s.definitions.get(defDe).members.reduce((n, m) => { const e = s.dir.get(m); return n + (e?.type === 144 ? 1 : e?.type === 408 ? faces(+s.params(e)[1]) : 0); }, 0);
const byDe = new Map(s.instances.map((i) => [i.de, i]));
const show = (inst, depth) => {
  const d = s.definitions.get(inst.def);
  const own = d.members.filter((m) => s.dir.get(m)?.type === 144).length;
  const t = inst.matrix.filter((_, k) => k % 4 === 3).map((v) => Math.round(v)).join(',');
  console.log('  '.repeat(depth) + (d.name || '?') + `  [def ${inst.def}] faces ${faces(inst.def)} (own ${own}) at ${t}`);
  for (const m of d.members) if (byDe.has(m)) show(byDe.get(m), depth + 1);
};
s.instances.filter((i) => !referenced.has(i.de)).forEach((i) => show(i, 0));
