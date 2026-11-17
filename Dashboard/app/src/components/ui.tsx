import React from 'react';
import { StyleProp, Text, TextStyle, View, ViewStyle } from 'react-native';
import { font, Palette, radius, space } from '../theme';

export function Card({
  p,
  title,
  aside,
  children,
  style,
}: {
  p: Palette;
  title?: string;
  aside?: React.ReactNode;
  children: React.ReactNode;
  style?: StyleProp<ViewStyle>;
}) {
  return (
    <View
      style={[
        {
          backgroundColor: p.surface,
          borderColor: p.line,
          borderWidth: 1,
          borderRadius: radius.lg,
          padding: space.lg,
        },
        style,
      ]}
    >
      {title ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: space.md }}>
          <Label p={p}>{title}</Label>
          {aside}
        </View>
      ) : null}
      {children}
    </View>
  );
}

// Small uppercase caption used for every section and field name.
export function Label({ p, children, style }: { p: Palette; children: React.ReactNode; style?: StyleProp<TextStyle> }) {
  return (
    <Text
      style={[{ fontFamily: font.label, fontSize: 13, letterSpacing: 1.3, color: p.sub, textTransform: 'uppercase' }, style]}
    >
      {children}
    </Text>
  );
}

export function Body({ p, children, style }: { p: Palette; children: React.ReactNode; style?: StyleProp<TextStyle> }) {
  return <Text style={[{ fontFamily: font.body, fontSize: 15, lineHeight: 21, color: p.ink }, style]}>{children}</Text>;
}

export function Mono({ p, children, style }: { p: Palette; children: React.ReactNode; style?: StyleProp<TextStyle> }) {
  return <Text style={[{ fontFamily: font.mono, fontSize: 14, color: p.ink }, style]}>{children}</Text>;
}

// Square lamp: filled when on, outlined when off, hatched dash when unknown.
// Shape and fill carry the state; colour is not needed to read it.
export function Lamp({ p, on, size = 12 }: { p: Palette; on: boolean | null; size?: number }) {
  return (
    <View
      style={{
        width: size,
        height: size,
        borderRadius: 2,
        borderWidth: 1.5,
        borderColor: on ? p.ink : p.faint,
        backgroundColor: on ? p.ink : 'transparent',
        opacity: on === null ? 0.4 : 1,
      }}
    />
  );
}

export function formatClock(iso: string | undefined): string {
  if (!iso) {
    return '--:--:--';
  }

  const d = new Date(iso);
  const two = (n: number) => String(n).padStart(2, '0');
  return `${two(d.getHours())}:${two(d.getMinutes())}:${two(d.getSeconds())}`;
}

export function formatDuration(ms: number): string {
  const s = Math.max(0, ms) / 1000;
  if (s < 60) {
    return s.toFixed(1) + ' s';
  }

  if (s < 3600) {
    return Math.floor(s / 60) + ' min ' + Math.floor(s % 60) + ' s';
  }

  return Math.floor(s / 3600) + ' h ' + Math.floor((s % 3600) / 60) + ' min';
}
