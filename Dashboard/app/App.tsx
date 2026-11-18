import React, { useState } from 'react';
import { Platform, Pressable, ScrollView, Text, useWindowDimensions, View } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { useFonts } from 'expo-font';
import { BarlowCondensed_500Medium } from '@expo-google-fonts/barlow-condensed/500Medium';
import { BarlowCondensed_600SemiBold } from '@expo-google-fonts/barlow-condensed/600SemiBold';
import { Barlow_400Regular } from '@expo-google-fonts/barlow/400Regular';
import { Barlow_500Medium } from '@expo-google-fonts/barlow/500Medium';
import { IBMPlexMono_400Regular } from '@expo-google-fonts/ibm-plex-mono/400Regular';
import { IBMPlexMono_500Medium } from '@expo-google-fonts/ibm-plex-mono/500Medium';
import { DEFAULT_VIEW, Machine, Source, useFactory, View as DataView } from './src/data/useFactory';
import { Twin3D } from './src/components/Twin3D';
import { ArPreset, ArView } from './src/components/ArView';
import { STATIONS } from './src/kits/stations';
import { conditionOf, Floor } from './src/components/Floor';
import { font, Palette, radius, space, usePalette } from './src/theme';
import type { Freshness, Snapshot } from './src/types';
import { About, Actuators, Counters, CycleTime, EventLog, IoPanel, SequenceRail, Settings, StatusHero } from './src/components/panels';
import { Body, Label } from './src/components/ui';

type Tab = 'overview' | 'io' | 'log' | 'about';
const TABS: { id: Tab; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'io', label: 'Signals' },
  { id: 'log', label: 'Log' },
  { id: 'about', label: 'About' },
];

// '?ar' in the address opens the AR view straight away: '?ar=all', or '?ar=kit1' for one station.
function arFromAddress(): ArPreset | null | undefined {
  if (Platform.OS !== 'web' || typeof window === 'undefined') return undefined;
  const value = new URLSearchParams(window.location.search).get('ar');
  if (value === null) return undefined;
  if (value === 'all') return { kits: STATIONS.map((s) => s.id), scale: 0.5 };
  return STATIONS.some((s) => s.id === value) ? { kits: [value], scale: 1 } : null;
}

// '&auto=1' asks the AR view to start the headset session by itself (the installed Quest app).
const arAuto = Platform.OS === 'web' && typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('auto') === '1';

export default function App() {
  const p = usePalette();
  const [fontsLoaded] = useFonts({
    BarlowCondensed_500Medium,
    BarlowCondensed_600SemiBold,
    Barlow_400Regular,
    Barlow_500Medium,
    IBMPlexMono_400Regular,
    IBMPlexMono_500Medium,
  });
  const [view, setView] = useState<DataView>(DEFAULT_VIEW);
  const { machines, feed, source, now } = useFactory(view);
  const [openId, setOpenId] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('overview');
  // undefined: dashboard; null: AR view at its station picker; a preset: AR view with those stations
  const [ar, setAr] = useState<ArPreset | null | undefined>(arFromAddress);
  const { width } = useWindowDimensions();
  const wide = width >= 900;

  if (!fontsLoaded) {
    return <View style={{ flex: 1, backgroundColor: p.bg }} />;
  }

  if (ar !== undefined) {
    return <ArView p={p} initial={ar} auto={arAuto} onClose={() => setAr(undefined)} />;
  }

  const machine = openId ? (machines.find((m) => m.id === openId) ?? null) : null;
  const open = (id: string) => {
    setOpenId(id);
    setTab('overview');
  };

  return (
    <View style={{ flex: 1, backgroundColor: p.bg, paddingTop: Platform.OS === 'web' ? 0 : 44 }}>
      <StatusBar style="auto" />
      <Header
        p={p}
        wide={wide}
        machine={machine}
        count={machines.length}
        feedFreshness={feed.freshness}
        demo={view === 'demo'}
        tab={tab}
        setTab={setTab}
        onBack={() => setOpenId(null)}
      />
      <ScrollView contentContainerStyle={{ padding: wide ? space.xl : space.lg, paddingBottom: space.xxl, alignItems: 'center' }}>
        <View style={{ width: '100%', maxWidth: 1180, gap: space.lg }}>
          <ViewPicker p={p} view={view} setView={setView} onAr={Platform.OS === 'web' ? () => setAr(machine && machine.id !== 'controller' ? { kits: [machine.id], scale: 1 } : null) : undefined} />
          {machine ? (
            <Detail p={p} machine={machine} tab={tab} now={now} wide={wide} source={source} />
          ) : (
            <>
              {view === 'live' && feed.freshness === 'unreachable' ? (
                <Notice p={p} text="The gateway on the rig PC is not sending data. Stations show as not connected until it is running again." />
              ) : null}
              <Floor p={p} machines={machines} now={now} wide={wide} onOpen={open} />
            </>
          )}
        </View>
      </ScrollView>
      {wide || !machine ? null : <BottomTabs p={p} tab={tab} setTab={setTab} />}
    </View>
  );
}

// ------------------------------------------------------------ station detail
function Detail({ p, machine, tab, now, wide, source }: { p: Palette; machine: Machine; tab: Tab; now: number; wide: boolean; source: Source }) {
  if (!machine.attached) {
    return (
      <Notice
        p={p}
        text={`No controller is running the ${machine.name} program right now, so there is nothing live to show for this station. Switch to Simulation to see it running.`}
      />
    );
  }

  const snapshot = machine.snapshot;
  const freshness = machine.freshness;
  // While the data is not fresh, machine values are "last known", never "now".
  const usable = snapshot && snapshot.link.connected && (freshness === 'live' || freshness === 'stale') ? snapshot : null;

  if (tab === 'overview') {
    return <Overview p={p} snap={snapshot} usable={usable} freshness={freshness} now={now} wide={wide} />;
  }

  if (tab === 'io') {
    return usable ? (
      <View style={{ flexDirection: wide ? 'row' : 'column', gap: space.lg, alignItems: 'flex-start' }}>
        <View style={{ flex: 1, width: '100%', gap: space.lg }}>
          <IoPanel p={p} title="Inputs" points={usable.inputs ?? []} />
        </View>
        <View style={{ flex: 1, width: '100%', gap: space.lg }}>
          <IoPanel p={p} title="Outputs" points={usable.outputs ?? []} />
          <Settings p={p} snap={usable} />
        </View>
      </View>
    ) : (
      <Notice p={p} text="No live data from this station right now. Values appear here as soon as the controller can be read again." />
    );
  }

  if (tab === 'log') {
    return snapshot ? <EventLog p={p} snap={snapshot} title="Event log" /> : <Notice p={p} text="No events yet." />;
  }

  return <About p={p} snap={snapshot} sourceLabel={source.label} sourceUrl={source.url} freshness={freshness} />;
}

function Overview({ p, snap, usable, freshness, now, wide }: { p: Palette; snap: Snapshot | null; usable: Snapshot | null; freshness: Freshness; now: number; wide: boolean }) {
  const hero = <StatusHero p={p} snap={snap} freshness={freshness} now={now} />;
  if (!usable) {
    return (
      <>
        {hero}
        {snap ? <EventLog p={p} snap={snap} limit={8} title="Recent events" /> : null}
      </>
    );
  }

  const raw = usable.status?.state === 'unknown';
  const model = <Twin3D key={usable.kit?.id} p={p} snap={usable} height={wide ? 360 : 280} />;
  if (!wide) {
    return (
      <>
        {hero}
        {model}
        <SequenceRail p={p} snap={usable} />
        <Counters p={p} snap={usable} />
        <Actuators p={p} snap={usable} />
        {raw ? <IoPanel p={p} title="Inputs" points={usable.inputs ?? []} /> : null}
        {raw ? <IoPanel p={p} title="Outputs" points={usable.outputs ?? []} /> : null}
        <CycleTime p={p} snap={usable} now={now} />
        <EventLog p={p} snap={usable} limit={6} title="Recent events" />
      </>
    );
  }

  return (
    <View style={{ flexDirection: 'row', gap: space.lg, alignItems: 'flex-start' }}>
      <View style={{ flex: 1.55, gap: space.lg }}>
        {hero}
        {model}
        <SequenceRail p={p} snap={usable} />
        <Actuators p={p} snap={usable} />
        {raw ? <IoPanel p={p} title="Inputs" points={usable.inputs ?? []} /> : null}
      </View>
      <View style={{ flex: 1, gap: space.lg }}>
        <Counters p={p} snap={usable} />
        <CycleTime p={p} snap={usable} now={now} />
        {raw ? <IoPanel p={p} title="Outputs" points={usable.outputs ?? []} /> : null}
        <EventLog p={p} snap={usable} limit={8} title="Recent events" />
      </View>
    </View>
  );
}

// ------------------------------------------------------------------- chrome
function ViewPicker({ p, view, setView, onAr }: { p: Palette; view: DataView; setView: (v: DataView) => void; onAr?: () => void }) {
  const options: { id: DataView; label: string }[] = [
    { id: 'live', label: 'Live rig' },
    { id: 'demo', label: 'Simulation' },
  ];
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, flexWrap: 'wrap' }}>
      <Label p={p} style={{ fontSize: 11 }}>
        Showing
      </Label>
      <View style={{ flexDirection: 'row', gap: 2, backgroundColor: p.track, padding: 3, borderRadius: radius.md }}>
        {options.map((o) => (
          <Pressable
            key={o.id}
            onPress={() => setView(o.id)}
            accessibilityRole="button"
            accessibilityState={{ selected: view === o.id }}
            style={{ paddingHorizontal: space.md, paddingVertical: 5, borderRadius: radius.sm + 2, backgroundColor: view === o.id ? p.raised : 'transparent' }}
          >
            <Label p={p} style={{ fontSize: 12, color: view === o.id ? p.ink : p.sub }}>
              {o.label}
            </Label>
          </Pressable>
        ))}
      </View>
      {view === 'demo' ? (
        <Body p={p} style={{ fontSize: 13, color: p.warn }}>
          Simulated data, not the real machines
        </Body>
      ) : null}
      {onAr ? (
        <Pressable
          onPress={onAr}
          accessibilityRole="button"
          accessibilityLabel="Open the AR view"
          style={{ marginLeft: 'auto', paddingHorizontal: space.md, paddingVertical: 7, borderRadius: radius.md, borderWidth: 1, borderColor: p.line, backgroundColor: p.surface }}
        >
          <Label p={p} style={{ fontSize: 12, color: p.ink }}>
            AR view
          </Label>
        </Pressable>
      ) : null}
    </View>
  );
}

function Notice({ p, text }: { p: Palette; text: string }) {
  return (
    <View style={{ padding: space.xl, borderRadius: radius.lg, borderWidth: 1, borderColor: p.line, borderStyle: 'dashed' }}>
      <Body p={p} style={{ color: p.sub }}>
        {text}
      </Body>
    </View>
  );
}

function Header({
  p,
  wide,
  machine,
  count,
  feedFreshness,
  demo,
  tab,
  setTab,
  onBack,
}: {
  p: Palette;
  wide: boolean;
  machine: Machine | null;
  count: number;
  feedFreshness: Freshness;
  demo: boolean;
  tab: Tab;
  setTab: (t: Tab) => void;
  onBack: () => void;
}) {
  let text: string;
  let good: boolean;
  if (machine) {
    const c = conditionOf(machine);
    good = c.key !== 'detached' && c.key !== 'nodata' && machine.freshness === 'live';
    text = c.key === 'detached' ? 'Not connected' : c.key === 'nodata' ? c.word : machine.freshness === 'stale' ? 'Stale' : 'Live';
  } else {
    good = feedFreshness === 'live';
    text = feedFreshness === 'live' ? 'Live' : feedFreshness === 'stale' ? 'Stale' : feedFreshness === 'connecting' ? 'Connecting' : 'No data';
  }

  return (
    <View
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: space.lg,
        paddingHorizontal: wide ? space.xl : space.lg,
        paddingVertical: space.md,
        borderBottomWidth: 1,
        borderBottomColor: p.line,
        backgroundColor: p.bg,
      }}
    >
      <View style={{ flex: 1, flexDirection: 'row', alignItems: 'center', gap: space.md }}>
        {machine ? (
          <Pressable
            onPress={onBack}
            accessibilityRole="button"
            accessibilityLabel="Back to the floor"
            style={{ paddingHorizontal: space.md, paddingVertical: 8, borderRadius: radius.md, borderWidth: 1, borderColor: p.line, backgroundColor: p.surface }}
          >
            <Label p={p} style={{ color: p.ink }}>
              ‹ Floor
            </Label>
          </Pressable>
        ) : null}
        <View style={{ flexShrink: 1 }}>
          <Text style={{ fontFamily: font.label, fontSize: 22, lineHeight: 24, letterSpacing: 0.4, color: p.ink, textTransform: 'uppercase' }}>
            {machine ? machine.name : 'Pneumatics lab'}
          </Text>
          <Body p={p} style={{ fontSize: 13, lineHeight: 16, color: p.sub }}>
            {machine ? machine.title : `${count} stations`} · read-only
          </Body>
        </View>
      </View>
      {wide && machine ? (
        <View style={{ flexDirection: 'row', gap: 2, backgroundColor: p.track, padding: 3, borderRadius: radius.md }}>
          {TABS.map((t) => (
            <Pressable
              key={t.id}
              onPress={() => setTab(t.id)}
              accessibilityRole="tab"
              accessibilityState={{ selected: tab === t.id }}
              style={{ paddingHorizontal: space.lg, paddingVertical: 7, borderRadius: radius.sm + 2, backgroundColor: tab === t.id ? p.raised : 'transparent' }}
            >
              <Label p={p} style={{ color: tab === t.id ? p.ink : p.sub }}>
                {t.label}
              </Label>
            </Pressable>
          ))}
        </View>
      ) : null}
      <View style={{ alignItems: 'flex-end', flex: wide ? 1 : undefined }}>
        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            gap: 7,
            paddingHorizontal: space.md,
            paddingVertical: 6,
            borderRadius: 99,
            borderWidth: 1,
            borderColor: good ? p.line : p.warn,
            backgroundColor: good ? p.surface : p.warnSoft,
          }}
        >
          <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: good ? p.data : p.warn }} />
          <Label p={p} style={{ color: good ? p.ink : p.warn }}>
            {text}
            {demo ? ' · simulated' : ''}
          </Label>
        </View>
      </View>
    </View>
  );
}

function BottomTabs({ p, tab, setTab }: { p: Palette; tab: Tab; setTab: (t: Tab) => void }) {
  return (
    <View style={{ flexDirection: 'row', borderTopWidth: 1, borderTopColor: p.line, backgroundColor: p.surface, paddingBottom: Platform.OS === 'ios' ? 20 : 0 }}>
      {TABS.map((t) => (
        <Pressable
          key={t.id}
          onPress={() => setTab(t.id)}
          accessibilityRole="tab"
          accessibilityState={{ selected: tab === t.id }}
          style={{ flex: 1, alignItems: 'center', paddingVertical: space.md }}
        >
          <View style={{ height: 3, width: 22, borderRadius: 2, marginBottom: 6, backgroundColor: tab === t.id ? p.ink : 'transparent' }} />
          <Label p={p} style={{ color: tab === t.id ? p.ink : p.sub }}>
            {t.label}
          </Label>
        </Pressable>
      ))}
    </View>
  );
}
