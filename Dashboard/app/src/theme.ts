import { useColorScheme } from 'react-native';

// "Quiet instrument" style, after high-performance HMI practice (ISA-101):
// a calm, low-contrast surface where normal operation is shown in ink and one
// cool data colour, and saturated colour is kept for things that need attention.
export type Palette = {
  bg: string;
  surface: string;
  raised: string;
  ink: string;
  sub: string;
  faint: string;
  line: string;
  track: string;
  data: string;      // live values
  dataSoft: string;
  alert: string;     // faults only
  alertSoft: string;
  warn: string;      // stale / attention
  warnSoft: string;
  onInk: string;
};

const light: Palette = {
  bg: '#E6E3DA',
  surface: '#F1EFE8',
  raised: '#FAF9F4',
  ink: '#1B1A17',
  sub: '#6B675E',
  faint: '#9A958A',
  line: '#D1CCC0',
  track: '#DCD8CD',
  data: '#0E5E57',
  dataSoft: '#D3E2DD',
  alert: '#C2351B',
  alertSoft: '#F3D9D1',
  warn: '#9A6400',
  warnSoft: '#EFE2C4',
  onInk: '#F6F4EE',
};

const dark: Palette = {
  bg: '#121311',
  surface: '#1A1B18',
  raised: '#22231F',
  ink: '#ECE9DF',
  sub: '#979287',
  faint: '#6A675F',
  line: '#30312C',
  track: '#2A2B26',
  data: '#63C2B1',
  dataSoft: '#1D3531',
  alert: '#F26A45',
  alertSoft: '#3B1D15',
  warn: '#E2AC43',
  warnSoft: '#372C12',
  onInk: '#121311',
};

export function usePalette(): Palette {
  return useColorScheme() === 'dark' ? dark : light;
}

export const font = {
  label: 'BarlowCondensed_600SemiBold',
  labelMedium: 'BarlowCondensed_500Medium',
  body: 'Barlow_400Regular',
  bodyMedium: 'Barlow_500Medium',
  mono: 'IBMPlexMono_400Regular',
  monoMedium: 'IBMPlexMono_500Medium',
};

export const space = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 };
export const radius = { sm: 4, md: 8, lg: 14 };
