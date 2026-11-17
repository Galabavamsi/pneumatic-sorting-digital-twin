import React from 'react';
import { Text, View } from 'react-native';
import Svg, { Line, Rect } from 'react-native-svg';
import { font, Palette, radius, space } from '../theme';
import type { Actuator, Freshness, IoPoint, MachineEvent, Snapshot } from '../types';
import { Body, Card, formatClock, formatDuration, Label, Lamp, Mono } from './ui';

const STATE_WORD: Record<string, string> = {
  running: 'Running',
  idle: 'Idle',
  homing: 'Homing',
  fault: 'Fault',
  unknown: 'Monitoring',
};

// ---------------------------------------------------------------- status hero
export function StatusHero({ p, snap, freshness, now }: { p: Palette; snap: Snapshot | null; freshness: Freshness; now: number }) {
  const status = snap?.status;
  const plcDown = !snap || !snap.link.connected;
  const noData = freshness === 'unreachable' || freshness === 'connecting';
  const stale = freshness === 'stale';

  let word = 'Connecting';
  let detail = 'Waiting for the first reading';
  let tone: 'normal' | 'alert' | 'warn' | 'quiet' = 'quiet';

  if (freshness === 'unreachable') {
    word = 'No data';
    detail = 'The data source cannot be reached';
    tone = 'warn';
  } else if (!noData && plcDown) {
    word = 'PLC offline';
    detail = snap?.link.error ?? 'The gateway cannot reach the controller';
    tone = 'warn';
  } else if (!noData && status) {
    word = STATE_WORD[status.state] ?? status.state;
    tone = status.state === 'fault' ? 'alert' : status.state === 'running' ? 'normal' : 'quiet';
    detail =
      status.state === 'fault'
        ? `Fault ${status.faultCode} · ${status.faultText ?? 'Unknown fault'}`
        : status.state === 'unknown'
          ? 'No profile for the program in the controller: showing raw inputs and outputs'
          : `Step ${status.step} · ${status.stepName}`;
  }

  const color = tone === 'alert' ? p.alert : tone === 'warn' ? p.warn : tone === 'normal' ? p.data : p.ink;
  const background = tone === 'alert' ? p.alertSoft : tone === 'warn' ? p.warnSoft : p.surface;
  const inStep = status && !plcDown && !noData ? now - new Date(status.stepSince).getTime() : null;

  return (
    <View
      style={{
        backgroundColor: background,
        borderColor: tone === 'alert' ? p.alert : tone === 'warn' ? p.warn : p.line,
        borderWidth: 1,
        borderRadius: radius.lg,
        padding: space.xl,
        opacity: stale ? 0.6 : 1,
      }}
    >
      <Label p={p}>Machine state{stale ? ' · last known' : ''}</Label>
      <Text
        style={{
          fontFamily: font.label,
          fontSize: 64,
          lineHeight: 66,
          letterSpacing: -0.5,
          color,
          textTransform: 'uppercase',
          marginTop: space.xs,
        }}
      >
        {word}
      </Text>
      <Body p={p} style={{ fontSize: 17, lineHeight: 24, marginTop: space.xs }}>
        {detail}
      </Body>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.xl, marginTop: space.lg }}>
        <Field p={p} name="In this step" value={inStep !== null && status?.state !== 'unknown' ? formatDuration(inStep) : '—'} />
        <Field p={p} name="Part on the machine" value={status && status.material !== 0 ? status.materialName : '—'} />
        <Field p={p} name="Last reading" value={formatClock(snap?.ts)} />
      </View>
    </View>
  );
}

function Field({ p, name, value }: { p: Palette; name: string; value: string }) {
  return (
    <View>
      <Label p={p} style={{ fontSize: 11 }}>
        {name}
      </Label>
      <Mono p={p} style={{ fontSize: 17, marginTop: 2 }}>
        {value}
      </Mono>
    </View>
  );
}

// ------------------------------------------------------------- sequence rail
export function SequenceRail({ p, snap }: { p: Palette; snap: Snapshot }) {
  const kit = snap.kit;
  const status = snap.status;
  if (!kit || !status || kit.sequence.length === 0) {
    return null;
  }

  const index = kit.sequence.indexOf(status.step);
  const name = (id: number) => kit.steps.find((s) => s.id === id)?.name ?? String(id);

  return (
    <Card p={p} title="Cycle" aside={<Mono p={p} style={{ color: p.sub, fontSize: 12 }}>{index >= 0 ? `${index + 1} / ${kit.sequence.length}` : 'not in cycle'}</Mono>}>
      <View style={{ flexDirection: 'row', gap: 3 }}>
        {kit.sequence.map((id, i) => (
          <View
            key={id}
            style={{
              flex: 1,
              height: i === index ? 14 : 8,
              alignSelf: 'center',
              borderRadius: 2,
              backgroundColor: index >= 0 && i < index ? p.ink : i === index ? p.data : p.track,
            }}
          />
        ))}
      </View>
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginTop: space.md, gap: space.md }}>
        <View style={{ flex: 1 }}>
          <Label p={p} style={{ fontSize: 11 }}>
            Now
          </Label>
          <Body p={p} style={{ fontFamily: font.bodyMedium }}>
            {index >= 0 ? name(status.step) : (status.stepName ?? '—')}
          </Body>
        </View>
        <View style={{ flex: 1, alignItems: 'flex-end' }}>
          <Label p={p} style={{ fontSize: 11 }}>
            Next
          </Label>
          <Body p={p} style={{ color: p.sub }}>
            {index >= 0 && index + 1 < kit.sequence.length ? name(kit.sequence[index + 1]) : '—'}
          </Body>
        </View>
      </View>
    </Card>
  );
}

// ------------------------------------------------------------------ counters
export function Counters({ p, snap }: { p: Palette; snap: Snapshot }) {
  const counters = snap.counters ?? [];
  if (counters.length === 0) {
    return null;
  }

  const total = counters.find((c) => c.id === 'parts')?.value ?? 0;
  const rest = counters.filter((c) => c.id !== 'parts');
  const sum = rest.reduce((acc, c) => acc + (c.value ?? 0), 0);

  return (
    <Card p={p} title="Job">
      <Text style={{ fontFamily: font.monoMedium, fontSize: 56, lineHeight: 60, color: p.ink }}>{total}</Text>
      <Label p={p} style={{ marginBottom: space.md }}>
        Parts sorted
      </Label>
      {sum > 0 ? (
        <View style={{ flexDirection: 'row', height: 6, borderRadius: 3, overflow: 'hidden', backgroundColor: p.track, marginBottom: space.md }}>
          {rest.map((c, i) => (
            <View key={c.id} style={{ flex: Math.max(c.value ?? 0, 0.0001), backgroundColor: i === 0 ? p.ink : p.data }} />
          ))}
        </View>
      ) : null}
      <View style={{ flexDirection: 'row', gap: space.xl }}>
        {rest.map((c, i) => (
          <View key={c.id} style={{ flex: 1 }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
              <View style={{ width: 8, height: 8, borderRadius: 1, backgroundColor: i === 0 ? p.ink : p.data }} />
              <Label p={p} style={{ fontSize: 11 }}>
                {c.label}
              </Label>
            </View>
            <Mono p={p} style={{ fontSize: 26, marginTop: 2 }}>
              {c.value ?? '—'}
            </Mono>
          </View>
        ))}
      </View>
    </Card>
  );
}

// ---------------------------------------------------------------- cycle time
export function CycleTime({ p, snap, now }: { p: Palette; snap: Snapshot; now: number }) {
  const cycle = snap.cycle;
  if (!cycle || !snap.kit || snap.kit.sequence.length === 0) {
    return null;
  }

  const history = cycle.history.slice(-20);
  const max = Math.max(1, ...history);
  const running = cycle.inProgressSince ? (now - new Date(cycle.inProgressSince).getTime()) / 1000 : null;
  const width = 240;
  const height = 44;
  const bar = history.length > 0 ? Math.min(14, (width - (history.length - 1) * 3) / history.length) : 0;

  return (
    <Card p={p} title="Cycle time">
      <View style={{ flexDirection: 'row', gap: space.xl, marginBottom: space.md }}>
        <Field p={p} name="This part" value={running !== null ? running.toFixed(1) + ' s' : '—'} />
        <Field p={p} name="Last" value={cycle.lastSeconds !== null ? cycle.lastSeconds.toFixed(1) + ' s' : '—'} />
        <Field p={p} name="Average" value={cycle.averageSeconds !== null ? cycle.averageSeconds.toFixed(1) + ' s' : '—'} />
      </View>
      {history.length > 0 ? (
        <Svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none">
          <Line x1={0} y1={height - 0.5} x2={width} y2={height - 0.5} stroke={p.line} strokeWidth={1} />
          {history.map((s, i) => {
            const h = Math.max(2, (s / max) * (height - 4));
            return (
              <Rect
                key={i}
                x={i * (bar + 3)}
                y={height - h}
                width={bar}
                height={h}
                rx={1}
                fill={i === history.length - 1 ? p.data : p.faint}
              />
            );
          })}
        </Svg>
      ) : (
        <Body p={p} style={{ color: p.sub, fontSize: 13 }}>
          Cycle times appear once a part has been sorted while the gateway is watching.
        </Body>
      )}
    </Card>
  );
}

// ----------------------------------------------------------------- actuators
export function Actuators({ p, snap }: { p: Palette; snap: Snapshot }) {
  const actuators = snap.actuators ?? [];
  if (actuators.length === 0) {
    return null;
  }

  return (
    <Card p={p} title="Actuators" aside={<Label p={p} style={{ fontSize: 11, color: p.faint }}>as commanded by the PLC</Label>}>
      <View style={{ gap: space.md }}>
        {actuators.map((a) => (
          <ActuatorRow key={a.id} p={p} a={a} />
        ))}
      </View>
    </Card>
  );
}

function ActuatorRow({ p, a }: { p: Palette; a: Actuator }) {
  const atA = a.position === 'a';
  const known = a.position !== null;
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
      <View style={{ width: 34 }}>
        <Mono p={p} style={{ fontFamily: font.monoMedium, fontSize: 15 }}>
          {a.id}
        </Mono>
      </View>
      <View style={{ flex: 1 }}>
        <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginBottom: 4 }}>
          <Body p={p} style={{ fontSize: 14, lineHeight: 18 }}>
            {a.label}
          </Body>
          <Body p={p} style={{ fontSize: 14, lineHeight: 18, fontFamily: font.bodyMedium, color: known ? p.ink : p.faint }}>
            {a.positionLabel}
            {known && !a.driven && a.kind !== 'switch' ? ' · held' : ''}
          </Body>
        </View>
        {/* Two-position track: the block sits at the end the valve was last sent to. */}
        {a.travel !== undefined ? (
          <View style={{ height: 10, borderRadius: 2, backgroundColor: p.track }}>
            <View style={{ width: `${Math.max(4, a.travel * 100)}%`, height: 10, borderRadius: 2, backgroundColor: a.travel > 0 ? p.data : p.ink }} />
          </View>
        ) : (
        <View style={{ height: 10, borderRadius: 2, backgroundColor: p.track, flexDirection: 'row' }}>
          <View style={{ flex: 1, borderRadius: 2, backgroundColor: known && !atA ? (a.driven || a.kind === 'switch' ? p.ink : p.faint) : 'transparent' }} />
          <View style={{ flex: 1, borderRadius: 2, backgroundColor: atA ? (a.driven ? p.data : p.faint) : 'transparent' }} />
        </View>
        )}
        <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginTop: 3 }}>
          <Label p={p} style={{ fontSize: 10, color: p.faint }}>
            {a.bLabel}
          </Label>
          <Label p={p} style={{ fontSize: 10, color: p.faint }}>
            {a.aLabel}
          </Label>
        </View>
      </View>
    </View>
  );
}

// ----------------------------------------------------------------------- I/O
export function IoPanel({ p, title, points }: { p: Palette; title: string; points: IoPoint[] }) {
  if (points.length === 0) {
    return null;
  }

  const on = points.filter((x) => x.on).length;
  return (
    <Card p={p} title={title} aside={<Mono p={p} style={{ color: p.sub, fontSize: 12 }}>{on} on</Mono>}>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', marginHorizontal: -space.sm }}>
        {points.map((point) => (
          <View
            key={point.id}
            style={{ width: '50%', minWidth: 150, flexGrow: 1, flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingHorizontal: space.sm, paddingVertical: 7 }}
          >
            <Lamp p={p} on={point.on} />
            <Mono p={p} style={{ width: 48, fontSize: 12, color: p.sub }}>
              {point.id}
            </Mono>
            <Body p={p} style={{ flex: 1, fontSize: 14, lineHeight: 18, color: point.on ? p.ink : p.sub }}>
              {point.label}
            </Body>
          </View>
        ))}
      </View>
    </Card>
  );
}

// ------------------------------------------------------------------ settings
export function Settings({ p, snap }: { p: Palette; snap: Snapshot }) {
  const settings = snap.settings ?? [];
  if (settings.length === 0) {
    return null;
  }

  return (
    <Card p={p} title="Program settings" aside={<Label p={p} style={{ fontSize: 11, color: p.faint }}>read from the PLC</Label>}>
      {settings.map((s, i) => (
        <View
          key={s.label}
          style={{ flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 8, borderTopWidth: i === 0 ? 0 : 1, borderTopColor: p.line }}
        >
          <Body p={p} style={{ fontSize: 14 }}>
            {s.label}
          </Body>
          <Mono p={p}>
            {s.value === null
              ? '—'
              : s.unit === 'bool'
                ? s.value !== 0
                  ? 'yes'
                  : 'no'
                : s.unit === 'ms'
                  ? (s.value / 1000).toFixed(s.value % 1000 === 0 ? 0 : 1) + ' s'
                  : s.unit === 'mm'
                    ? s.value + ' mm'
                    : String(s.value)}
          </Mono>
        </View>
      ))}
    </Card>
  );
}

// ----------------------------------------------------------------- event log
const EVENT_MARK: Record<MachineEvent['kind'], string> = { info: '·', warn: '!', step: '›', fault: '×', part: '✓' };

export function EventLog({ p, snap, limit, title = 'Events' }: { p: Palette; snap: Snapshot; limit?: number; title?: string }) {
  const events = (snap.events ?? []).filter((e) => limit === undefined || e.kind !== 'step').slice(0, limit ?? 200);
  return (
    <Card p={p} title={title}>
      {events.length === 0 ? (
        <Body p={p} style={{ color: p.sub, fontSize: 14 }}>
          Nothing has happened since the gateway started.
        </Body>
      ) : (
        events.map((e, i) => (
          <View key={e.ts + i} style={{ flexDirection: 'row', gap: space.sm, paddingVertical: 5 }}>
            <Mono p={p} style={{ fontSize: 12, lineHeight: 20, color: p.faint, width: 62 }}>
              {formatClock(e.ts)}
            </Mono>
            <Mono
              p={p}
              style={{ fontSize: 13, lineHeight: 20, width: 12, color: e.kind === 'fault' ? p.alert : e.kind === 'warn' ? p.warn : e.kind === 'part' ? p.data : p.faint }}
            >
              {EVENT_MARK[e.kind]}
            </Mono>
            <Body
              p={p}
              style={{ flex: 1, fontSize: 14, lineHeight: 20, color: e.kind === 'fault' ? p.alert : e.kind === 'step' ? p.sub : p.ink }}
            >
              {e.text}
            </Body>
          </View>
        ))
      )}
    </Card>
  );
}

// ---------------------------------------------------------------- about card
export function About({ p, snap, sourceLabel, sourceUrl, freshness }: { p: Palette; snap: Snapshot | null; sourceLabel: string; sourceUrl: string; freshness: Freshness }) {
  const rows: [string, string][] = [
    ['Machine', snap?.kit ? `${snap.kit.name} · ${snap.kit.title}` : '—'],
    ['Controller', snap?.link.product ?? '—'],
    ['Controller address', snap?.link.plc ?? '—'],
    ['Read time', snap?.link.readMs !== undefined ? snap.link.readMs + ' ms' : '—'],
    ['Gateway', snap?.gateway ? `v${snap.gateway.version} · ${snap.gateway.mode === 'demo' ? 'simulated data' : 'live PLC'}` : '—'],
    ['Gateway running since', snap?.gateway ? new Date(snap.gateway.startedAt).toLocaleString() : '—'],
    ['Data route', sourceLabel],
    ['Data address', sourceUrl.replace(/^https?:\/\//, '')],
    ['Data state', freshness],
    ['Access', 'Read-only: this app cannot change anything on the machine'],
  ];

  return (
    <Card p={p} title="About this machine">
      {snap?.kit ? (
        <Body p={p} style={{ marginBottom: space.md, color: p.sub }}>
          {snap.kit.description}
        </Body>
      ) : null}
      {rows.map(([k, v], i) => (
        <View key={k} style={{ flexDirection: 'row', gap: space.md, paddingVertical: 8, borderTopWidth: i === 0 ? 0 : 1, borderTopColor: p.line }}>
          <Label p={p} style={{ width: 150, fontSize: 12 }}>
            {k}
          </Label>
          <Body p={p} style={{ flex: 1, fontSize: 14 }}>
            {v}
          </Body>
        </View>
      ))}
    </Card>
  );
}
