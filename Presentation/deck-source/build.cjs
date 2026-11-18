// Builds the project deck. Run: node build.cjs
// Slides follow the assertion-evidence pattern: a sentence headline that states the point,
// and a photo, recording, screenshot or chart from the project as the evidence.
const path = require('path');
const fs = require('fs');
const NM = 'C:/Users/mtq-asish-sir/quest-pwa-tools/node_modules/';
const pptxgen = require(NM + 'pptxgenjs');
const sharp = require(NM + 'sharp');
const { applyTheme } = require('C:/Users/mtq-asish-sir/AppData/Roaming/Claude/local-agent-mode-sessions/skills-plugin/f6b79808-250f-4fed-8abe-415f5b704d6a/87b0fc2e-e02c-4a75-8f5e-1293f9b49162/skills/pptx/scripts/apply_theme.js');

const M = 'C:/Users/mtq-asish-sir/deck-build/media/';
const SYS = path.join(process.env.TEMP, 'claude/d45f3715ae300f57/07bf262e-7d2b-4472-b406-1d59c51df17e/scratchpad/sys/');
const OUT = 'C:/Users/mtq-asish-sir/pneumatic-sorting-digital-twin/Presentation/Manufacturing-Execution-System.pptx';

// The dashboard's own colours: graphite, off-white, one teal for live data, amber for "not real".
const THEME = {
  name: 'Rig Monitor',
  headFontFace: 'Calibri',
  bodyFontFace: 'Calibri',
  colors: {
    dk1: '1B1A17', lt1: 'FFFFFF', dk2: '121311', lt2: 'F1EFE8',
    accent1: '0E5E57', accent2: '63C2B1', accent3: '9A6400', accent4: 'C2351B', accent5: '6B675E', accent6: 'ECE9DF',
    hlink: '0E5E57', folHlink: '6B675E',
  },
};

const W = 13.333;
const H = 7.5;
const MX = 0.6;                       // side margin

// Crops a picture to the box it will fill, so nothing is stretched.
async function fit(src, name, w, h, opts = {}) {
  const out = M + 'prep-' + name + '.jpg';
  const px = Math.round(w * 190);
  await sharp(src).rotate().resize(px, Math.round((px * h) / w), { fit: 'cover', position: opts.position || 'centre' }).jpeg({ quality: 88 }).toFile(out);
  return out;
}

const b64 = (file) => 'image/jpeg;base64,' + fs.readFileSync(file).toString('base64');

(async () => {
  const pres = new pptxgen();
  pres.layout = 'LAYOUT_WIDE';
  pres.theme = { headFontFace: THEME.headFontFace, bodyFontFace: THEME.bodyFontFace };
  pres.title = 'Manufacturing Execution System';
  pres.author = 'Galaba Vamsi, Gajanand Kumawat, Kushagra Katare';
  const C = pres.SchemeColor;

  const footer = (dark) => [
    { text: { text: 'Manufacturing Execution System', options: { x: MX, y: 7.0, w: 6, h: 0.3, fontSize: 10, color: dark ? C.accent6 : C.accent5, margin: 0 } } },
  ];
  const number = (dark) => ({ x: W - MX - 0.8, y: 7.0, w: 0.8, h: 0.3, fontSize: 10, align: 'right', color: dark ? C.accent6 : C.accent5, margin: 0 });
  const titleHolder = (dark) => ({
    placeholder: { options: { name: 'title', type: 'title', x: MX, y: 0.42, w: W - 2 * MX, h: 1.25, fontSize: 30, bold: true, color: dark ? C.background1 : C.text1, align: 'left', valign: 'top', margin: 0, fit: 'shrink' }, text: '' },
  });

  pres.defineSlideMaster({ title: 'COVER', background: { color: THEME.colors.dk2 }, objects: [] });
  pres.defineSlideMaster({ title: 'CONTENT', background: { color: THEME.colors.lt1 }, objects: [...footer(false), titleHolder(false)], slideNumber: number(false) });
  pres.defineSlideMaster({ title: 'CONTENT_DARK', background: { color: THEME.colors.dk2 }, objects: [...footer(true), titleHolder(true)], slideNumber: number(true) });

  // ---- small parts used on several slides
  // A status chip, worded like the labels in the dashboard and the AR view.
  const chip = (slide, x, y, label, tone, dark) => {
    const color = tone === 'live' ? (dark ? C.accent2 : C.accent1) : tone === 'alert' ? C.accent4 : dark ? 'E2AC43' : C.accent3;
    const w = 0.42 + label.length * 0.092;
    slide.addShape(pres.ShapeType.roundRect, { x, y, w, h: 0.34, rectRadius: 0.17, fill: { color, transparency: dark ? 78 : 86 }, line: { color, width: 0.75 }, objectName: 'Chip ' + label });
    slide.addShape(pres.ShapeType.ellipse, { x: x + 0.13, y: y + 0.115, w: 0.11, h: 0.11, fill: { color }, line: { color, width: 0 }, objectName: 'Chip dot' });
    slide.addText(label, { x: x + 0.3, y, w: w - 0.34, h: 0.34, fontSize: 11, bold: true, color, valign: 'middle', margin: 0, charSpacing: 1, isTextBox: true, objectName: 'Chip text' });
    return w;
  };
  const caption = (slide, text, x, y, w, dark) =>
    slide.addText(text, { x, y, w, h: 0.5, fontSize: 12, color: dark ? C.accent6 : C.accent5, valign: 'top', margin: 0, isTextBox: true, objectName: 'Caption' });
  const body = (slide, text, x, y, w, h, opts = {}) =>
    slide.addText(text, { x, y, w, h, fontSize: 16, color: C.text1, valign: 'top', margin: 0, isTextBox: true, paraSpaceAfter: 8, objectName: 'Text', ...opts });
  const video = (slide, file, cover, x, y, w, h, name) => slide.addMedia({ type: 'video', path: file, cover: b64(cover), x, y, w, h, objectName: name });
  const section = (title) => pres.addSection({ title });
  const add = (masterName, sectionTitle) => pres.addSlide({ masterName, sectionTitle });

  // =================================================================== 1. cover
  section('Opening');
  {
    const s = add('COVER', 'Opening');
    const img = await fit(M + 'ar-still.jpg', 'cover', 6.5, H, { position: 'centre' });
    s.addImage({ path: img, x: W - 6.5, y: 0, w: 6.5, h: H, objectName: 'AR view beside the real kit', altText: 'Headset view: the 3D twin of Kit 2 standing beside the real kit in the lab' });
    s.addText('PROJECT', { x: MX, y: 1.25, w: 5.6, h: 0.3, fontSize: 12, bold: true, color: C.accent2, charSpacing: 3, margin: 0, isTextBox: true, objectName: 'Label' });
    s.addText('Manufacturing Execution System', { x: MX, y: 1.6, w: 5.7, h: 1.7, fontSize: 44, bold: true, color: C.background1, valign: 'top', margin: 0, isTextBox: true, objectName: 'Title' });
    s.addText('A live digital twin, remote dashboard and AR view for a five-kit pneumatic training rig', { x: MX, y: 3.45, w: 5.5, h: 1.0, fontSize: 18, color: C.accent6, valign: 'top', margin: 0, isTextBox: true, objectName: 'Subtitle' });
    const team = [['Galaba Vamsi', '12340770'], ['Gajanand Kumawat', '12340760'], ['Kushagra Katare', '']];
    team.forEach(([name, roll], i) => {
      s.addText(name, { x: MX, y: 5.05 + i * 0.42, w: 3.0, h: 0.36, fontSize: 16, bold: true, color: C.background1, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Member ' + (i + 1) });
      if (roll) s.addText(roll, { x: MX + 3.0, y: 5.05 + i * 0.42, w: 1.6, h: 0.36, fontSize: 16, color: C.accent6, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Roll ' + (i + 1) });
    });
    s.addNotes('Open on the photo: this is what a viewer sees in the headset, the 3D twin of Kit 2 beside the real kit, following the real controller. The project connects one pneumatic training rig to three views of itself: a Unity twin, a web dashboard and an AR view.');
  }

  // ============================================================ 2. the problem
  section('The rig');
  {
    const s = add('CONTENT', 'The rig');
    s.addText('One controller runs five interchangeable kits, but the machine could only be watched standing next to it', { placeholder: 'title' });
    const a = await fit(SYS + '10.jpg', 'rig-hero', 7.0, 4.75);
    s.addImage({ path: a, x: MX, y: 1.85, w: 7.0, h: 4.75, objectName: 'Kit on the rig', altText: 'A pneumatic kit mounted on the trainer, with the patch panel behind it' });
    const b = await fit(SYS + '31.jpg', 'rig-panel', 4.7, 2.5, { position: 'top' });
    s.addImage({ path: b, x: 8.0, y: 1.85, w: 4.73, h: 2.5, objectName: 'Trainer panel', altText: 'The trainer panel with its touch screen, push buttons and rows of input and output jacks' });
    const facts = [['Trainer', 'Gravitaz MATK-R5M, patched by hand'], ['Controller', 'Allen-Bradley Micro850 PLC'], ['Kits', 'Five small machines, one on the controller at a time']];
    facts.forEach(([k, v], i) => {
      s.addText(k.toUpperCase(), { x: 8.0, y: 4.62 + i * 0.68, w: 1.45, h: 0.3, fontSize: 11, bold: true, color: C.accent1, charSpacing: 2, margin: 0, isTextBox: true, objectName: 'Fact label' });
      s.addText(v, { x: 9.45, y: 4.56 + i * 0.68, w: 3.28, h: 0.6, fontSize: 15, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Fact' });
    });
    s.addNotes('The trainer has one PLC and interchangeable mechanical kits. Each kit feeds workpieces from a magazine, tells them apart, and sorts or assembles them with pneumatic cylinders. Before this project its state could only be read at the panel, by someone in the lab.');
  }

  // ========================================================== 3. architecture
  {
    const s = add('CONTENT', 'The rig');
    s.addText('The machine now reports to a browser and a headset, and nothing in that chain can write to it', { placeholder: 'title' });
    const boxes = [
      ['Micro850 PLC', 'runs the kit program'],
      ['Gateway on the rig PC', 'reads about 8 times a second'],
      ['Cloud relay', 'one row per machine'],
      ['Dashboard and AR', 'browser, phone, Quest 3'],
    ];
    const bw = 2.55, gap = 0.62, by = 2.2, bh = 1.25;
    boxes.forEach(([t, sub], i) => {
      const x = MX + i * (bw + gap);
      const last = i === boxes.length - 1;
      s.addShape(pres.ShapeType.roundRect, { x, y: by, w: bw, h: bh, rectRadius: 0.08, fill: { color: last ? C.accent1 : C.background2 }, line: { color: last ? C.accent1 : C.background2, width: 0 }, objectName: 'Box ' + t });
      s.addText(t, { x: x + 0.18, y: by + 0.2, w: bw - 0.36, h: 0.45, fontSize: 17, bold: true, color: last ? C.background1 : C.text1, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Box title' });
      s.addText(sub, { x: x + 0.18, y: by + 0.68, w: bw - 0.36, h: 0.4, fontSize: 13, color: last ? C.accent6 : C.accent5, margin: 0, valign: 'top', isTextBox: true, objectName: 'Box note' });
      if (!last) {
        s.addShape(pres.ShapeType.line, { x: x + bw + 0.08, y: by + bh / 2, w: gap - 0.16, h: 0, line: { color: C.accent1, width: 2, endArrowType: 'triangle' }, objectName: 'Arrow' });
      }
    });
    const lab = ['EtherNet/IP, read only', 'HTTPS, outbound only', 'live updates'];
    lab.forEach((t, i) => s.addText(t, { x: MX + i * (bw + gap) + bw - 0.6, y: by - 0.42, w: gap + 1.2, h: 0.3, fontSize: 11, color: C.accent1, align: 'center', margin: 0, isTextBox: true, objectName: 'Link label' }));
    // the Unity twin reads the same controller directly
    s.addShape(pres.ShapeType.roundRect, { x: MX, y: 4.05, w: bw, h: 0.9, rectRadius: 0.08, fill: { color: C.background2 }, line: { color: C.background2, width: 0 }, objectName: 'Box Unity twin' });
    s.addText('Unity twin', { x: MX + 0.18, y: 4.12, w: bw - 0.36, h: 0.4, fontSize: 17, bold: true, color: C.text1, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Box title' });
    s.addText('reads the PLC directly', { x: MX + 0.18, y: 4.5, w: bw - 0.36, h: 0.35, fontSize: 13, color: C.accent5, margin: 0, isTextBox: true, objectName: 'Box note' });
    s.addShape(pres.ShapeType.line, { x: MX + bw / 2, y: by + bh + 0.06, w: 0, h: 4.05 - by - bh - 0.12, line: { color: C.accent1, width: 2, endArrowType: 'triangle' }, objectName: 'Arrow' });
    const stats = [['26 ms', 'to read the whole PLC state'], ['~40', 'raw values turned into one snapshot'], ['0', 'ways to write to the PLC']];
    stats.forEach(([n, t], i) => {
      const x = 4.2 + i * 2.95;
      s.addText(n, { x, y: 4.55, w: 2.7, h: 1.0, fontSize: 54, bold: true, color: C.accent1, margin: 0, valign: 'bottom', isTextBox: true, objectName: 'Stat' });
      s.addText(t, { x, y: 5.6, w: 2.6, h: 0.7, fontSize: 14, color: C.accent5, margin: 0, valign: 'top', isTextBox: true, objectName: 'Stat label' });
    });
    s.addNotes('Everything hangs off one Ethernet cable between the rig PC and the PLC. The gateway finds the controller, works out which kit program is loaded, and turns about forty raw values into one snapshot with the step name, fault text, cycle time and an event log. The rig PC only makes outbound connections, so nothing on the lab network is exposed. The gateway refuses every request except reads.');
  }

  // =============================================================== 4. the kits
  section('The kits');
  {
    const s = add('CONTENT', 'The kits');
    s.addText('All five kits are modelled from their own CAD, and three of them also run on the real controller', { placeholder: 'title' });
    const img = await fit(M + 'all-kits-assembled-overview.png', 'allkits', 6.3, 3.55);
    s.addImage({ path: img, x: MX, y: 1.95, w: 6.3, h: 3.55, objectName: 'Five kit assemblies', altText: 'CAD renders of the five kit assemblies, coloured by component' });
    caption(s, 'CAD assemblies, coloured by component. Colours do not show real materials.', MX, 5.6, 6.3);
    const head = (t) => ({ text: t, options: { bold: true, color: C.background1, fill: { color: C.accent1 }, fontSize: 12 } });
    const cell = (t, o = {}) => ({ text: t, options: { color: C.text1, fontSize: 13, ...o } });
    const rows = [
      [head('Kit'), head('What it does'), head('PLC program'), head('Dashboard and AR')],
      [cell('1', { bold: true }), cell('Sorts metal from plastic'), cell('Runs on the rig'), cell('Live', { color: C.accent1, bold: true })],
      [cell('2', { bold: true }), cell('Stamps at two stations, sorts'), cell('Runs on the rig'), cell('Live', { color: C.accent1, bold: true })],
      [cell('3', { bold: true }), cell('Stamps, vacuum pick, sorts'), cell('Runs, one fault open'), cell('Live', { color: C.accent1, bold: true })],
      [cell('4', { bold: true }), cell('Sorts on a conveyor'), cell('Not written yet'), cell('Simulated', { color: C.accent3, bold: true })],
      [cell('5', { bold: true }), cell('Assembles and inspects'), cell('Not written yet'), cell('Simulated', { color: C.accent3, bold: true })],
    ];
    s.addTable(rows, { x: 7.25, y: 1.95, w: 5.48, colW: [0.55, 2.2, 1.45, 1.28], rowH: 0.56, valign: 'middle', border: { type: 'solid', color: 'D1CCC0', pt: 0.75 }, margin: 0.08, objectName: 'Kit status table' });
    caption(s, 'One controller runs one kit program at a time, so at most one kit is live at any moment.', 7.25, 5.6, 5.48);
    s.addNotes('Each kit is a small machine. Kits 1, 2 and 3 have PLC programs and have run on the rig. Kits 4 and 5 exist only as CAD for now, so they run as simulations, and their sequences are our reading of the geometry.');
  }

  // ================================================================= 5. kit 1
  const kitSlide = async (n, title, flow, photoIdx, photoAlt, chipLabel, chipTone, facts, notes) => {
    const s = add('CONTENT', 'The kits');
    s.addText(title, { placeholder: 'title' });
    video(s, M + `unity-kit${n}.mp4`, M + `unity-kit${n}.jpg`, MX, 1.95, 7.2, 4.05, `Kit ${n} twin recording`);
    caption(s, `Recording of the Kit ${n} twin in Unity. Click to play.`, MX, 6.1, 7.2);
    const img = await fit(SYS + photoIdx + '.jpg', 'kit' + n + 'photo', 4.93, 2.5);
    s.addImage({ path: img, x: 8.2, y: 1.95, w: 4.53, h: 2.3, objectName: `Kit ${n} photo`, altText: photoAlt });
    chip(s, 8.2, 4.45, chipLabel, chipTone, false);
    s.addText(flow, { x: 8.2, y: 4.95, w: 4.53, h: 0.75, fontSize: 15, bold: true, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Process' });
    s.addText(facts, { x: 8.2, y: 5.75, w: 4.53, h: 0.95, fontSize: 14, color: C.accent5, margin: 0, valign: 'top', isTextBox: true, objectName: 'Detail' });
    s.addNotes(notes);
  };
  await kitSlide(
    1,
    'Kit 1 sorts metal from plastic on the real rig, and its twin follows the PLC as it runs',
    'Feed, detect, then plastic is pushed off at the top and metal rides the lift down',
    '16',
    'The real Kit 1 on the trainer: magazine tower, cylinders and two black trays',
    'RUNS ON THE RIG',
    'live',
    'Two sensors decide: the inductive one sees only metal, the capacitive one sees any part.',
    'Cylinder 1A pushes the bottom part from the magazine onto a raised lift. Plastic is pushed off at the upper level into Tray 2. For metal the lift goes down, the lower ejector pushes it into Tray 1 and the lift comes back up. Kit 1 was the first kit proven end to end.',
  );
  await kitSlide(
    2,
    'Kit 2 stamps each part at the station for its material, and the live views follow it',
    'Metal is stamped at Stamp 1; plastic moves on and is stamped at Stamp 2',
    '12',
    'The real Kit 2 on the trainer: two horizontal cylinders and the stamp tower',
    'RUNS ON THE RIG',
    'live',
    'The next part pushes the finished one into its tray, so parts interlock through the machine.',
    'Cylinder 1A feeds a job to Stamp 1, where the inductive sensor looks for metal. Metal is stamped there; plastic is fed on to Stamp 2. This is the kit shown live in the web app recording and in the headset recording later in the deck.',
  );
  {
    const s = add('CONTENT', 'The kits');
    s.addText('Kit 3 stamps, lifts the part by vacuum and turns it to the right tray; one fault is still open', { placeholder: 'title' });
    video(s, M + 'unity-kit3.mp4', M + 'unity-kit3.jpg', MX, 1.95, 7.2, 4.05, 'Kit 3 twin recording');
    caption(s, 'Recording of the Kit 3 twin in Unity. Click to play.', MX, 6.1, 7.2);
    chip(s, 8.2, 1.95, 'ONE FAULT OPEN', 'alert', false);
    s.addText('26.1 s', { x: 8.2, y: 2.4, w: 4.5, h: 1.05, fontSize: 60, bold: true, color: C.accent1, margin: 0, valign: 'bottom', isTextBox: true, objectName: 'Stat' });
    s.addText('for one cycle, recorded from the PLC on 15 November by reading its step variable every 20 ms', { x: 8.2, y: 3.5, w: 4.5, h: 0.8, fontSize: 14, color: C.accent5, margin: 0, valign: 'top', isTextBox: true, objectName: 'Stat label' });
    s.addText('Open problem', { x: 8.2, y: 4.55, w: 4.5, h: 0.35, fontSize: 15, bold: true, color: C.accent4, margin: 0, isTextBox: true, objectName: 'Heading' });
    s.addText('After the grip, the PLC switches the arm-up coil on, but the arm stays down until the head has pulled in. Unplugging the vacuum changes nothing, so the vacuum is not starving the lift.', { x: 8.2, y: 4.92, w: 4.5, h: 1.6, fontSize: 14, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Detail' });
    s.addNotes('Kit 3 has only two position switches, both on the feed cylinder, so every other move is timed. A read-only recorder that logged every output during one cycle settled which coils belong to which valve, after two attempts at re-pairing by guesswork made things worse. The lesson we wrote down: measure before changing the program.');
  }

  // =========================================================== 8. kits 4 and 5
  {
    const s = add('CONTENT', 'The kits');
    s.addText('Kits 4 and 5 run as simulated twins built from CAD; their sequences are assumed, not yet confirmed', { placeholder: 'title' });
    const cols = [
      ['4', 'Conveyor sort', 'A part is pushed onto the belt and carried under a sensor. One class rides on to the end bin; the other is pushed off into a side bin.', 'dash-kit4.png', 'kit-4-assembled-colored.png'],
      ['5', 'Assembly and inspection', 'A block gets an insert from each side, then the belt carries it under a sensor. Good assemblies ride on; rejects are pushed off.', 'dash-kit5.png', 'kit-5-assembled-colored.png'],
    ];
    for (let i = 0; i < cols.length; i++) {
      const [n, name, text, shotFile] = cols[i];
      const x = MX + i * 6.2;
      const shot = M + 'prep-shot' + n + '.jpg';
      // the 3D model card from the station page
      await sharp(M + shotFile).extract({ left: 210, top: 368, width: 708, height: 428 }).jpeg({ quality: 92 }).toFile(shot);
      s.addImage({ path: shot, x, y: 1.95, w: 5.93, h: 3.585, objectName: `Kit ${n} twin in the dashboard`, altText: `The Kit ${n} 3D model as shown on its station page in the dashboard` });
      s.addText(`Kit ${n}  ${name}`, { x, y: 5.68, w: 3.6, h: 0.36, fontSize: 17, bold: true, color: C.text1, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Kit name' });
      chip(s, x + 3.75, 5.69, 'SIMULATED', 'warn', false);
      s.addText(text, { x, y: 6.1, w: 5.9, h: 0.75, fontSize: 13, color: C.accent5, margin: 0, valign: 'top', isTextBox: true, objectName: 'Detail' });
    }
    s.addNotes('These two kits arrived as a single lump of CAD each. We rebuilt the part tree so each cylinder rod moves on its own, then wrote the sequence from the geometry: strokes, directions and the accept or reject rule are our assumption. Kit 4 has a fourth cylinder whose job we do not know, so it is left still. Neither kit has a PLC program yet.');
  }

  // ============================================================ 9. dashboard
  section('The live views');
  {
    const s = add('CONTENT', 'The live views');
    s.addText('The dashboard opens on the whole floor, then takes one station down to each input and output', { placeholder: 'title' });
    const floor = M + 'prep-floor.jpg';
    await sharp(M + 'dash-floor.png').extract({ left: 196, top: 78, width: 1208, height: 640 }).jpeg({ quality: 92 }).toFile(floor);
    s.addImage({ path: floor, x: MX, y: 1.95, w: 7.9, h: 4.185, objectName: 'Floor view', altText: 'Dashboard floor view: counts of running, idle and faulted stations and a card per kit' });
    caption(s, 'Floor view: which stations run, which need attention, parts sorted.', MX, 6.22, 7.9);
    const st = M + 'prep-station.jpg';
    await sharp(M + 'dash-kit3.png').extract({ left: 196, top: 122, width: 730, height: 640 }).jpeg({ quality: 92 }).toFile(st);
    s.addImage({ path: st, x: 8.8, y: 1.95, w: 3.93, h: 3.445, objectName: 'Station page', altText: 'Dashboard station page: machine state, current step and the 3D model' });
    caption(s, 'Station page: state, step, 3D model, cycle times, signals and log.', 8.8, 5.48, 3.93);
    s.addText('Written once in React Native; runs in any browser at rig-monitor.vercel.app', { x: 8.8, y: 6.05, w: 3.93, h: 0.7, fontSize: 13, color: C.accent1, bold: true, margin: 0, valign: 'top', isTextBox: true, objectName: 'Note' });
    s.addNotes('The style follows high-performance HMI practice, ISA-101: a calm surface where normal operation is shown in ink and one data colour, and strong colour is kept for faults and lost data. If readings stop arriving the app marks values as last known and then hides them; it never shows an old value as live. These screenshots show the built-in simulation.');
  }

  // ======================================================== 10. live recording
  {
    const s = add('CONTENT', 'The live views');
    s.addText('On the hosted app, the 3D model and the counters follow the real Kit 2 while it runs', { placeholder: 'title' });
    video(s, M + 'web-clip.mp4', M + 'web-cover.jpg', MX, 1.95, 4.45, 4.45, 'Web app recording');
    caption(s, 'Phone recording in the lab, 18 November. Click to play.', MX, 6.48, 4.45);
    chip(s, 5.9, 1.95, 'LIVE FROM THE PLC', 'live', false);
    s.addText('It has already found a real fault from a distance', { x: 5.9, y: 2.6, w: 6.8, h: 0.5, fontSize: 20, bold: true, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Heading' });
    s.addText('Fault 9', { x: 5.9, y: 3.25, w: 3.0, h: 1.05, fontSize: 60, bold: true, color: C.accent4, margin: 0, valign: 'bottom', isTextBox: true, objectName: 'Stat' });
    s.addText('"Homing did not finish", reported by the hosted app on 18 November', { x: 5.9, y: 4.35, w: 6.6, h: 0.4, fontSize: 14, color: C.accent5, margin: 0, isTextBox: true, objectName: 'Stat label' });
    s.addText('The live inputs showed cylinders 1A and 3A reporting "pulled in" but not 2A. That pointed straight at the 2A reed switch or its lead, before anyone walked to the rig.', { x: 5.9, y: 4.95, w: 6.6, h: 1.4, fontSize: 16, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Detail' });
    s.addNotes('Play the clip. It was filmed on a phone in the lab: first the real Kit 2 sorting, then the hosted web app on the monitor, where the 3D model moves with the machine and the parts counter climbs. The fault story is the reason the dashboard exists: the signals page showed which input was missing.');
  }

  // =============================================================== 11. the CAD
  {
    const s = add('CONTENT', 'The live views');
    s.addText('Converting the CAD cut each model from as much as 163 MB to about 2 MB, small enough for a phone or a headset', { placeholder: 'title' });
    s.addChart(
      pres.charts.BAR,
      [
        { name: 'CAD export (OBJ)', labels: ['Kit 1', 'Kit 2', 'Kit 3'], values: [72, 70, 163] },
        { name: 'Web model (GLB)', labels: ['Kit 1', 'Kit 2', 'Kit 3'], values: [1.7, 1.7, 2.3] },
      ],
      {
        x: MX, y: 1.95, w: 7.2, h: 4.6, barDir: 'col', barGrouping: 'clustered', barGapWidthPct: 60,
        chartColors: [THEME.colors.accent5, THEME.colors.accent1],
        showTitle: true, title: 'Model file size, MB', titleFontSize: 13, titleColor: THEME.colors.dk1, titleFontFace: '+mn-lt',
        showValue: true, dataLabelPosition: 'outEnd', dataLabelFontSize: 12, dataLabelColor: THEME.colors.dk1, dataLabelFontFace: '+mn-lt',
        catAxisLabelColor: THEME.colors.accent5, valAxisLabelColor: THEME.colors.accent5, catAxisLabelFontSize: 12, valAxisLabelFontSize: 11,
        catAxisLabelFontFace: '+mn-lt', valAxisLabelFontFace: '+mn-lt',
        valGridLine: { color: 'E4E1D8', size: 0.75 }, catGridLine: { style: 'none' },
        showLegend: true, legendPos: 'b', legendFontSize: 12, legendColor: THEME.colors.dk1, legendFontFace: '+mn-lt',
        objectName: 'Model size chart',
      },
    );
    s.addText('How', { x: 8.3, y: 1.95, w: 4.4, h: 0.35, fontSize: 15, bold: true, color: C.accent1, margin: 0, isTextBox: true, objectName: 'Heading' });
    s.addText('Our converter merges vertices on a 0.6 mm grid and keeps every component name, so each part can still be moved on its own.', { x: 8.3, y: 2.32, w: 4.4, h: 1.25, fontSize: 15, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Detail' });
    s.addText('Kits 4 and 5', { x: 8.3, y: 3.85, w: 4.4, h: 0.35, fontSize: 15, bold: true, color: C.accent1, margin: 0, isTextBox: true, objectName: 'Heading' });
    s.addText('These came as IGES files that load as one lump. The converter reads the assembly tree from the file itself, so each cylinder comes out as barrel, mounting and rod, and the rod can move. Their web models are 2.6 and 2.3 MB.', { x: 8.3, y: 4.22, w: 4.4, h: 2.2, fontSize: 15, color: C.text1, margin: 0, valign: 'top', isTextBox: true, objectName: 'Detail' });
    s.addNotes('The 3D view uses each kit\'s own CAD, not a drawing of it. The exports were far too large for a phone. The same small models are what the browser, the phone and the headset all load.');
  }

  // ================================================================== 12. AR
  {
    const s = add('CONTENT_DARK', 'The live views');
    s.addText('In AR the twin stands at full size beside the real kit, and is carried, resized and turned by hand', { placeholder: 'title' });
    video(s, M + 'ar-clip.mp4', M + 'ar-still.jpg', MX, 1.95, 8.2, 4.61, 'AR recording');
    caption(s, 'Recorded inside a Meta Quest 3 in the lab, 19 November. Click to play.', MX, 6.62, 8.2, true);
    chip(s, 9.2, 1.95, 'LIVE FROM THE PLC', 'live', true);
    const points = [
      ['Hands, no controllers', 'Pinch to carry; two hands to resize and turn.'],
      ['It stays where you put it', 'Each kit is anchored to the room and is there again next session.'],
      ['Information on the spot', 'A board beside the kit shows jobs, cycle times and the log.'],
    ];
    points.forEach(([t, d], i) => {
      s.addText(t, { x: 9.2, y: 2.6 + i * 1.3, w: 3.53, h: 0.36, fontSize: 16, bold: true, color: C.background1, margin: 0, isTextBox: true, objectName: 'Point' });
      s.addText(d, { x: 9.2, y: 2.98 + i * 1.3, w: 3.53, h: 0.8, fontSize: 14, color: C.accent6, margin: 0, valign: 'top', isTextBox: true, objectName: 'Point detail' });
    });
    s.addNotes('Play the clip. The headset shows the room through its cameras with the twin drawn in it. This is the same web app, opened in the headset through WebXR and packaged as an installable Quest app. A kit whose program is in the controller follows the live feed; every other kit plays the simulation, and each label says which.');
  }

  // ====================================================== 13. proven and open
  section('Where it stands');
  {
    const s = add('CONTENT', 'Where it stands');
    s.addText('Three kits run on the rig and are watched live; the last two are waiting for hardware', { placeholder: 'title' });
    const colsData = [
      ['Proven', C.accent1, [
        'Kit 1 sorts on the rig and its Unity twin follows it live',
        'Kit 2 runs on the rig, followed live by the web app and the headset',
        'A read-only path from the PLC to a browser anywhere',
        'All five CAD models running on a phone and a headset',
      ]],
      ['Still open', C.accent3, [
        'Kit 3: the arm does not lift after the grip while the head is out',
        'Kits 4 and 5: no PLC program, sequences assumed from CAD',
        'The hosted app has no sign-in: anyone with the address can read it',
        'No commands to the machine; the system only reads',
      ]],
    ];
    colsData.forEach(([head, color, items], i) => {
      const x = MX + i * 6.2;
      s.addShape(pres.ShapeType.roundRect, { x, y: 1.95, w: 5.93, h: 4.75, rectRadius: 0.08, fill: { color: C.background2 }, line: { color: C.background2, width: 0 }, objectName: 'Card ' + head });
      s.addShape(pres.ShapeType.ellipse, { x: x + 0.35, y: 2.33, w: 0.2, h: 0.2, fill: { color }, line: { color, width: 0 }, objectName: 'Dot' });
      s.addText(head, { x: x + 0.7, y: 2.2, w: 4.8, h: 0.46, fontSize: 20, bold: true, color, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Card heading' });
      s.addText(items.map((t, k) => ({ text: t, options: { breakLine: k < items.length - 1 } })), { x: x + 0.35, y: 2.95, w: 5.25, h: 3.6, fontSize: 18, color: C.text1, margin: 0, valign: 'top', paraSpaceAfter: 20, isTextBox: true, objectName: 'Card items' });
    });
    s.addNotes('We have tried to claim only what we measured. The left column was seen working. The right column is what a reader should not assume: in particular the Kit 4 and Kit 5 sequences are our reading of the geometry until they are checked on the real kits.');
  }

  // =============================================================== 14. close
  {
    const s = add('COVER', 'Where it stands');
    const img = await fit(M + 'ar-still2.jpg', 'close', 5.6, H);
    s.addImage({ path: img, x: W - 5.6, y: 0, w: 5.6, h: H, objectName: 'AR view', altText: 'Headset view of the twin with its information board' });
    s.addText('NEXT', { x: MX, y: 0.9, w: 6, h: 0.3, fontSize: 12, bold: true, color: C.accent2, charSpacing: 3, margin: 0, isTextBox: true, objectName: 'Label' });
    s.addText('Confirm Kits 4 and 5 on hardware, close the Kit 3 fault, and add sign-in before any command can be sent', { x: MX, y: 1.25, w: 6.5, h: 2.3, fontSize: 30, bold: true, color: C.background1, valign: 'top', margin: 0, isTextBox: true, objectName: 'Title' });
    const links = [['Web app', 'rig-monitor.vercel.app'], ['Source', 'github.com/Galabavamsi/pneumatic-sorting-digital-twin']];
    links.forEach(([k, v], i) => {
      s.addText(k.toUpperCase(), { x: MX, y: 4.15 + i * 0.62, w: 1.3, h: 0.34, fontSize: 11, bold: true, color: C.accent2, charSpacing: 2, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Link label' });
      s.addText(v, { x: MX + 1.3, y: 4.15 + i * 0.62, w: 5.4, h: 0.34, fontSize: 15, color: C.background1, margin: 0, valign: 'middle', isTextBox: true, objectName: 'Link' });
    });
    s.addText('Galaba Vamsi  ·  Gajanand Kumawat  ·  Kushagra Katare', { x: MX, y: 6.1, w: 6.6, h: 0.4, fontSize: 15, color: C.accent6, margin: 0, isTextBox: true, objectName: 'Team' });
    s.addNotes('Close on the plan. Kits 4 and 5 need their real wiring and a PLC program to replace the assumed sequences. Sign-in comes before any write feature, so that start, stop and reset can be added behind a separate, deliberately enabled path. Then take questions.');
  }

  await pres.writeFile({ fileName: OUT });
  await applyTheme(OUT, THEME);
  console.log('written', OUT, (fs.statSync(OUT).size / 1048576).toFixed(1) + ' MB');
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
