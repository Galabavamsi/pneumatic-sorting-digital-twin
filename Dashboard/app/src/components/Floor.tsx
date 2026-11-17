import React from 'react';
import { Pressable, Text, View } from 'react-native';
import type { Machine } from '../data/useFactory';
import { font, Palette, radius, space } from '../theme';
import type { MachineEvent } from '../types';
import { Body, Card, formatClock, formatDuration, Label, Mono } from './ui';

// The factory-floor view: every station at a glance, what needs attention first.
export type Condition = {
  key: 'running' | 'idle' | 'homing' | 'fault' | 'nodata' | 'detached' | 'unknown';
  word: string;
  detail: string;
  tone: 'normal' | 'quiet' | 'alert' | 'warn';
};

export function conditionOf(m: Machine): Condition {
  const s = m.snapshot;
  if (!m.attached || !s) {
    return { key: 'detached', word: 'Not connected', detail: 'No controller is running this station', tone: 'quiet' };
  }

  if (m.freshness === 'unreachable' || m.freshness === 'connecting') {
    return { key: 'nodata', word: m.freshness === 'connecting' ? 'Connecting' : 'No data', detail: 'Waiting for readings', tone: m.freshness === 'connecting' ? 'quiet' : 'warn' };
  }

  if (!s.link.connected || !s.status) {
    return { key: 'nodata', word: 'PLC offline', detail: s.link.error ?? 'The gateway cannot reach the controller', tone: 'warn' };
  }

  const st = s.status;
  const stale = m.freshness === 'stale' ? ' · last known' : '';
  switch (st.state) {
    case 'fault':
      return { key: 'fault', word: 'Fault', detail: `Fault ${st.faultCode} · ${st.faultText ?? 'Unknown fault'}${stale}`, tone: 'alert' };
    case 'running':
      return { key: 'running', word: 'Running', detail: `Step ${st.step} · ${st.stepName}${stale}`, tone: 'normal' };
    case 'homing':
      return { key: 'homing', word: 'Homing', detail: `Step ${st.step} · ${st.stepName}${stale}`, tone: 'quiet' };
    case 'idle':
      return { key: 'idle', word: 'Idle', detail: 'Waiting for Start' + stale, tone: 'quiet' };
    default:
      return { key: 'unknown', word: 'Monitoring', detail: 'Unknown program: raw inputs and outputs only' + stale, tone: 'quiet' };
  }
}

const toneColor = (p: Palette, tone: Condition['tone']) => (tone === 'alert' ? p.alert : tone === 'warn' ? p.warn : tone === 'normal' ? p.data : p.ink);

export function Floor({ p, machines, now, wide, onOpen }: { p: Palette; machines: Machine[]; now: number; wide: boolean; onOpen: (id: string) => void }) {
  const conditions = machines.map((m) => ({ m, c: conditionOf(m) }));
  const count = (...keys: Condition['key'][]) => conditions.filter((x) => keys.includes(x.c.key)).length;
  const reporting = conditions.filter((x) => x.m.attached && x.m.snapshot?.status);
  const parts = reporting.reduce((sum, x) => sum + (x.m.snapshot?.counters?.find((c) => c.id === 'parts')?.value ?? 0), 0);
  const attention = conditions.filter((x) => x.c.tone === 'alert' || x.c.tone === 'warn');

  const events: (MachineEvent & { machine: string })[] = [];
  for (const m of machines) {
    for (const e of m.snapshot?.events ?? []) {
      if (e.kind !== 'step') {
        events.push({ ...e, machine: m.name });
      }
    }
  }
  events.sort((a, b) => (a.ts < b.ts ? 1 : -1));

  return (
    <>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.md }}>
        <Tile p={p} label="Running" value={count('running')} of={machines.length} color={p.data} />
        <Tile p={p} label="Idle" value={count('idle', 'homing', 'unknown')} of={machines.length} color={p.ink} />
        <Tile p={p} label="Stopped on fault" value={count('fault')} of={machines.length} color={count('fault') > 0 ? p.alert : p.ink} alert={count('fault') > 0} />
        <Tile p={p} label="Not reporting" value={count('nodata', 'detached')} of={machines.length} color={count('nodata') > 0 ? p.warn : p.ink} />
        <Tile p={p} label="Parts sorted" value={parts} color={p.ink} />
      </View>

      {attention.length > 0 ? (
        <View style={{ borderRadius: radius.lg, borderWidth: 1, borderColor: attention.some((x) => x.c.tone === 'alert') ? p.alert : p.warn, backgroundColor: attention.some((x) => x.c.tone === 'alert') ? p.alertSoft : p.warnSoft, padding: space.lg, gap: space.sm }}>
          <Label p={p} style={{ color: p.ink }}>Needs attention</Label>
          {attention.map(({ m, c }) => (
            <Pressable key={m.id} onPress={() => onOpen(m.id)} accessibilityRole="button" style={{ flexDirection: 'row', gap: space.md, alignItems: 'baseline' }}>
              <Text style={{ fontFamily: font.label, fontSize: 17, color: toneColor(p, c.tone), textTransform: 'uppercase', width: 110 }}>{m.name}</Text>
              <Body p={p} style={{ flex: 1 }}>
                {c.word} · {c.detail}
              </Body>
              <Label p={p} style={{ color: p.ink }}>Open ›</Label>
            </Pressable>
          ))}
        </View>
      ) : null}

      <View style={{ flexDirection: wide ? 'row' : 'column', flexWrap: 'wrap', gap: space.lg }}>
        {conditions.map(({ m, c }) => (
          <MachineCard key={m.id} p={p} m={m} c={c} now={now} wide={wide} onOpen={() => onOpen(m.id)} />
        ))}
      </View>

      <Card p={p} title="Floor events">
        {events.length === 0 ? (
          <Body p={p} style={{ color: p.sub, fontSize: 14 }}>
            Nothing to report yet.
          </Body>
        ) : (
          events.slice(0, 12).map((e, i) => (
            <View key={e.ts + e.machine + i} style={{ flexDirection: 'row', gap: space.sm, paddingVertical: 5 }}>
              <Mono p={p} style={{ fontSize: 12, lineHeight: 20, color: p.faint, width: 62 }}>
                {formatClock(e.ts)}
              </Mono>
              <Label p={p} style={{ fontSize: 12, lineHeight: 20, width: 84, color: p.sub }}>
                {e.machine}
              </Label>
              <Body p={p} style={{ flex: 1, fontSize: 14, lineHeight: 20, color: e.kind === 'fault' ? p.alert : e.kind === 'warn' ? p.warn : p.ink }}>
                {e.text}
              </Body>
            </View>
          ))
        )}
      </Card>
    </>
  );
}

function Tile({ p, label, value, of, color, alert }: { p: Palette; label: string; value: number; of?: number; color: string; alert?: boolean }) {
  return (
    <View
      style={{
        flexGrow: 1,
        flexBasis: 150,
        backgroundColor: alert ? p.alertSoft : p.surface,
        borderColor: alert ? p.alert : p.line,
        borderWidth: 1,
        borderRadius: radius.lg,
        paddingHorizontal: space.lg,
        paddingVertical: space.md,
      }}
    >
      <Label p={p} style={{ fontSize: 12 }}>
        {label}
      </Label>
      <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: 6 }}>
        <Text style={{ fontFamily: font.monoMedium, fontSize: 34, lineHeight: 40, color }}>{value}</Text>
        {of !== undefined ? (
          <Mono p={p} style={{ color: p.faint, fontSize: 14 }}>
            / {of}
          </Mono>
        ) : null}
      </View>
    </View>
  );
}

function MachineCard({ p, m, c, now, wide, onOpen }: { p: Palette; m: Machine; c: Condition; now: number; wide: boolean; onOpen: () => void }) {
  const s = m.attached ? m.snapshot : null;
  const st = s?.status;
  const seq = s?.kit?.sequence ?? [];
  const index = st ? seq.indexOf(st.step) : -1;
  const partsCount = s?.counters?.find((x) => x.id === 'parts')?.value;
  const split = (s?.counters ?? []).filter((x) => x.id !== 'parts');
  const dim = c.key === 'detached' || m.freshness === 'stale';
  const inStep = st && c.key !== 'detached' && c.key !== 'nodata' ? now - new Date(st.stepSince).getTime() : null;

  return (
    <Pressable
      onPress={onOpen}
      accessibilityRole="button"
      accessibilityLabel={`${m.name}, ${c.word}. Open`}
      style={{
        flexGrow: 1,
        flexBasis: wide ? 320 : undefined,
        backgroundColor: c.tone === 'alert' ? p.alertSoft : p.surface,
        borderColor: c.tone === 'alert' ? p.alert : c.tone === 'warn' ? p.warn : p.line,
        borderWidth: 1,
        borderRadius: radius.lg,
        padding: space.lg,
        opacity: dim ? 0.62 : 1,
      }}
    >
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontFamily: font.label, fontSize: 22, lineHeight: 24, color: p.ink, textTransform: 'uppercase' }}>{m.name}</Text>
          <Body p={p} style={{ fontSize: 13, lineHeight: 17, color: p.sub }}>
            {m.title}
          </Body>
        </View>
        <Label p={p} style={{ color: p.sub }}>Open ›</Label>
      </View>

      <Text style={{ fontFamily: font.label, fontSize: 40, lineHeight: 44, color: toneColor(p, c.tone), textTransform: 'uppercase', marginTop: space.md }}>{c.word}</Text>
      <Body p={p} style={{ fontSize: 14, lineHeight: 19, minHeight: 38 }}>
        {c.detail}
      </Body>

      <View style={{ flexDirection: 'row', gap: 2, marginTop: space.sm, height: 10, alignItems: 'center' }}>
        {(seq.length > 0 ? seq : [0, 1, 2, 3, 4, 5, 6, 7]).map((id, i) => (
          <View
            key={i}
            style={{
              flex: 1,
              height: seq.length > 0 && i === index ? 10 : 5,
              borderRadius: 1.5,
              backgroundColor: seq.length > 0 && index >= 0 && i < index ? p.ink : seq.length > 0 && i === index ? p.data : p.track,
            }}
          />
        ))}
      </View>

      <View style={{ flexDirection: 'row', gap: space.lg, marginTop: space.md, flexWrap: 'wrap' }}>
        <Stat p={p} name="Parts" value={partsCount ?? '—'} />
        {split.map((x) => (
          <Stat key={x.id} p={p} name={x.label.split(' · ')[0]} value={x.value ?? '—'} />
        ))}
        <Stat p={p} name="Last cycle" value={s?.cycle?.lastSeconds != null ? s.cycle.lastSeconds.toFixed(1) + ' s' : '—'} />
        <Stat p={p} name="In step" value={inStep !== null && c.key === 'running' ? formatDuration(inStep) : '—'} />
      </View>
    </Pressable>
  );
}

function Stat({ p, name, value }: { p: Palette; name: string; value: string | number }) {
  return (
    <View>
      <Label p={p} style={{ fontSize: 10 }}>
        {name}
      </Label>
      <Mono p={p} style={{ fontSize: 16 }}>
        {value}
      </Mono>
    </View>
  );
}
