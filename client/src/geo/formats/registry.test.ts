// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { decodePlusCode } from './plusCodes.ts';
import {
  defaultFormatSettings,
  FORMAT_IDS,
  FORMATS,
  isFormatId,
  readPosition,
  sanitizeFormatSettings,
  writePosition,
  type FormatSettings,
} from './registry.ts';

/** A point inside the Swiss grids' area (near Bern), where they have a reference to write. */
const SWISS_POINT: [number, number] = [7.44, 46.95];

/** The finest settings every format offers — the protected form must ignore all of them. */
function finestSettings(): FormatSettings {
  const settings = defaultFormatSettings();
  settings.dd = { decimals: 8, style: 'letters' };
  settings.ddm = { minutesDecimals: 5 };
  settings.dms = { secondsDecimals: 3 };
  settings.utm = { zone: 'auto', precisionMetres: 1 };
  settings.mgrs = { precisionMetres: 1, spacing: 'spaced' };
  settings.stereo70 = { precisionMetres: 1, axes: 'en' };
  settings.lv95 = { precisionMetres: 1 };
  settings.lv03 = { precisionMetres: 1 };
  settings.pluscode = { length: 13 };
  return settings;
}

describe('the format registry', () => {
  it('knows its ids and nothing else', () => {
    expect(FORMAT_IDS).toEqual(['dd', 'ddm', 'dms', 'utm', 'mgrs', 'stereo70', 'lv95', 'lv03', 'pluscode']);
    expect(isFormatId('utm')).toBe(true);
    expect(isFormatId('toString')).toBe(false);
    expect(isFormatId('stereo-unknown')).toBe(false);
  });

  it('gives every format a default inside its own choices', () => {
    for (const id of FORMAT_IDS) {
      const format = FORMATS[id];
      for (const [key, choices] of Object.entries(format.choices)) {
        expect(choices).toContain((format.defaults as Record<string, unknown>)[key]);
      }
    }
  });

  it('keeps the good members of stored settings and drops the rest', () => {
    const settings = sanitizeFormatSettings({
      dd: { decimals: 3, style: 'sideways' },
      utm: { zone: 35, precisionMetres: 7 },
      pluscode: 'twelve',
      unknownFormat: { anything: 1 },
    });
    expect(settings.dd).toEqual({ decimals: 3, style: 'letters' });
    expect(settings.utm).toEqual({ zone: 35, precisionMetres: 1 });
    expect(settings.pluscode).toEqual({ length: 10 });
    expect(settings).not.toHaveProperty('unknownFormat');
    expect(sanitizeFormatSettings(null)).toEqual(defaultFormatSettings());
  });

  it('writes the default forms the display has always shown', () => {
    expect(writePosition('dd', 25.44721, 45.53127)).toBe('45.53127°N 25.44721°E');
    expect(writePosition('dms', 25.5, 45.25)).toBe('45°15′00.0″N 25°30′00.0″E');
  });
});

describe('protected positions in every format', () => {
  // A position as the server hands out a snapped one: the centre of a 5 km cell is no more than
  // that, and must not read as a surveyed point in any format.
  const [lon, lat] = [25.4472, 45.5313];
  const grid = { protectedGridMetres: 5000 };

  it.each([
    ['dd', /^45\.53°N 25\.45°E$/],
    ['ddm', /^45°32′N 25°27′E$/],
    ['dms', /^45°32′N 25°27′E$/],
    ['utm', /^35T \d+000 E \d+000 N$/],
    ['mgrs', /^35T [A-Z]{2} \d \d$/],
    ['stereo70', /^E \d{3}000 N \d{3}000$/],
    ['pluscode', /^[2-9CFGHJMPQRVWX]{6}00\+$/],
  ] as const)('writes %s no finer than the grid', (id, pattern) => {
    expect(writePosition(id, lon, lat, finestSettings(), grid)).toMatch(pattern);
    // The same settings without the protection mark write the full precision.
    expect(writePosition(id, lon, lat, finestSettings())).not.toMatch(pattern);
  });

  it.each([
    ['lv95', /^E \d{4}000 N \d{4}000$/],
    ['lv03', /^E \d+000 N \d+000$/],
  ] as const)('writes %s no finer than the grid inside Switzerland', (id, pattern) => {
    expect(writePosition(id, ...SWISS_POINT, finestSettings(), grid)).toMatch(pattern);
    expect(writePosition(id, ...SWISS_POINT, finestSettings())).not.toMatch(pattern);
  });

  it('covers every registered format, so a new one cannot arrive without its protected form', () => {
    for (const id of FORMAT_IDS) {
      const [atLon, atLat] = id === 'lv95' || id === 'lv03' ? SWISS_POINT : [lon, lat];
      const exact = writePosition(id, atLon, atLat, finestSettings());
      const approximate = writePosition(id, atLon, atLat, finestSettings(), grid);
      expect(approximate).not.toBeNull();
      expect(approximate).not.toBe(exact);
    }
  });

  it('names a Plus Code cell that still contains the snapped point', () => {
    const area = decodePlusCode(writePosition('pluscode', lon, lat, finestSettings(), grid)!)!;
    expect(lat).toBeGreaterThanOrEqual(area.south);
    expect(lat).toBeLessThan(area.north);
  });
});

describe('reading a position in any format', () => {
  it.each([
    ['45.53127°N 25.44721°E', 'dd'],
    ['45°31.876′N 25°26.833′E', 'ddm'],
    ['45°31′52.6″N 25°26′50.0″E', 'dms'],
    ['35T 378752 5043143', 'utm'],
    ['35T LL 78753 43143', 'mgrs'],
    ['Stereo 70: X 448017 Y 535073', 'stereo70'],
    ['8GQ7GCJW+GV', 'pluscode'],
  ])('detects %s as %s', (text, format) => {
    const reading = readPosition(text);
    expect(reading.kind).toBe('parsed');
    if (reading.kind !== 'parsed') return;
    expect(reading.candidates.map((c) => c.format)).toEqual([format]);
    expect(reading.candidates[0].lat).toBeCloseTo(45.53, 1);
    expect(reading.candidates[0].lon).toBeCloseTo(25.45, 1);
  });

  it('round-trips every format at its default settings', () => {
    for (const id of FORMAT_IDS) {
      const [atLon, atLat] = id === 'lv95' || id === 'lv03' ? SWISS_POINT : [25.44721, 45.53127];
      const text = writePosition(id, atLon, atLat)!;
      const reading = readPosition(text);
      expect(reading.kind).toBe('parsed');
      if (reading.kind !== 'parsed') continue;
      // UTM to the metre and a ten-digit code (14 m) are the coarsest defaults.
      const own = reading.candidates.find((candidate) => candidate.format === id)!;
      expect(Math.abs(own.lat - atLat)).toBeLessThan(1e-4);
      expect(Math.abs(own.lon - atLon)).toBeLessThan(1e-4);
    }
  });

  it('returns every reading of an ambiguous text, and one format alone when it is picked', () => {
    const both = readPosition('35S 500000 3800000');
    expect(both.kind === 'parsed' && both.candidates).toHaveLength(2);
    const picked = readPosition('45.5 25.5', 'utm');
    expect(picked).toEqual({ kind: 'invalid', error: 'unrecognised' });
    const angles = readPosition('45 30 25 15', 'dms');
    expect(angles.kind === 'parsed' && angles.candidates[0].format).toBe('ddm');
  });

  it('reports why a recognisable text cannot be read, and that an unrecognisable one is not a position', () => {
    expect(readPosition('95.5 125.5')).toEqual({ kind: 'invalid', error: 'latitudeRange' });
    expect(readPosition('9QCJ+2V')).toEqual({ kind: 'invalid', error: 'plusCodeShort' });
    expect(readPosition('the cave by the road')).toEqual({ kind: 'invalid', error: 'unrecognised' });
  });
});
