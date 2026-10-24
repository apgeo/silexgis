// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DD, DDM, DMS, parseAngles } from './angles.ts';
import type { ParseOutcome } from './types.ts';

// Positions are exact identities of the notation or public landmarks — none from a cave register.

function only(outcome: ParseOutcome) {
  expect(outcome.kind).toBe('parsed');
  if (outcome.kind !== 'parsed') throw new Error('not parsed');
  expect(outcome.candidates).toHaveLength(1);
  return outcome.candidates[0];
}

describe('writing angles', () => {
  it('writes decimal degrees with letters by default, as the display always has', () => {
    expect(DD.format(25.44721, 45.53127, DD.defaults)).toBe('45.53127°N 25.44721°E');
    expect(DD.format(-79.387, -43.643, { decimals: 3, style: 'letters' })).toBe('43.643°S 79.387°W');
  });

  it('writes signed decimal degrees latitude first, the form search boxes take', () => {
    expect(DD.format(-70.1, -33.5, { decimals: 4, style: 'signed' })).toBe('-33.5000, -70.1000');
    expect(DD.format(-0.001, -0.001, { decimals: 2, style: 'signed' })).toBe('0.00, 0.00');
  });

  it('writes degrees and decimal minutes, carrying a rounded 60 upwards', () => {
    expect(DDM.format(25.5, 45.25, { minutesDecimals: 3 })).toBe('45°15.000′N 25°30.000′E');
    expect(DDM.format(24.9999999, 45.9999999, { minutesDecimals: 2 })).toBe('46°00.00′N 25°00.00′E');
  });

  it('writes degrees, minutes and seconds as before (one place of seconds by default)', () => {
    expect(DMS.format(25.5, 45.25, DMS.defaults)).toBe('45°15′00.0″N 25°30′00.0″E');
    expect(DMS.format(25.5, 45.25, { secondsDecimals: 0 })).toBe('45°15′00″N 25°30′00″E');
  });

  it('writes no finer than the protection grid, whatever the settings ask for', () => {
    // 5 km grid: decimal degrees to the hundredth, both minute forms to the whole minute.
    expect(DD.formatProtected(25.44721, 45.53127, { decimals: 8, style: 'letters' }, 5000)).toBe('45.53°N 25.45°E');
    expect(DD.formatProtected(25.44721, 45.53127, { decimals: 8, style: 'signed' }, 5000)).toBe('45.53, 25.45');
    expect(DD.formatProtected(25.44721, 45.53127, { decimals: 1, style: 'letters' }, 5000)).toBe('45.5°N 25.4°E');
    expect(DDM.formatProtected(25.44721, 45.53127, { minutesDecimals: 5 }, 5000)).toBe('45°32′N 25°27′E');
    expect(DMS.formatProtected(25.44721, 45.53127, { secondsDecimals: 3 }, 5000)).toBe('45°32′N 25°27′E');
  });
});

describe('reading angles', () => {
  it.each([
    ['45.53127°N 25.44721°E', 'dd'],
    ['N 45.53127 E 25.44721', 'dd'],
    ['45.53127, 25.44721', 'dd'],
    ['45,53127 25,44721', 'dd'],
    ['45,53127; 25,44721', 'dd'],
    ['45°31.8762′N 25°26.8326′E', 'ddm'],
    ["N45 31.8762' E25 26.8326'", 'ddm'],
    ['45°31′52.572″N 25°26′49.956″E', 'dms'],
    ['45º31\'52.572"N, 25º26′49.956″E', 'dms'],
    ["45 31 52.572 N 25 26 49.956 E", 'dms'],
    ["45°31'52.572'' N 25°26’49.956” E", 'dms'],
  ])('reads %s as %s', (text, format) => {
    const position = only(parseAngles(text));
    expect(position.format).toBe(format);
    expect(position.lat).toBeCloseTo(45.53127, 5);
    expect(position.lon).toBeCloseTo(25.44721, 5);
  });

  it('reads hemisphere letters and signs south and west, V as the Romanian west', () => {
    const position = only(parseAngles('33°30′S 70°06′V'));
    expect(position.lat).toBeCloseTo(-33.5, 9);
    expect(position.lon).toBeCloseTo(-70.1, 9);
    expect(only(parseAngles('−33.5 −70.1')).lat).toBe(-33.5);
  });

  it('round-trips every angle format it writes', () => {
    for (const [lon, lat] of [
      [25.44721, 45.53127],
      [-70.1, -33.5],
      [151.2093, -33.8688],
      [-0.1276, 51.5072],
    ]) {
      for (const text of [
        DD.format(lon, lat, { decimals: 6, style: 'letters' })!,
        DD.format(lon, lat, { decimals: 6, style: 'signed' })!,
        DDM.format(lon, lat, { minutesDecimals: 4 })!,
        DMS.format(lon, lat, { secondsDecimals: 2 })!,
      ]) {
        const position = only(parseAngles(text));
        expect(Math.abs(position.lat - lat)).toBeLessThan(1e-5);
        expect(Math.abs(position.lon - lon)).toBeLessThan(1e-5);
      }
    }
  });

  it('says when the order of two bare numbers was assumed, and swaps a first number only a longitude can be', () => {
    expect(only(parseAngles('45.5 25.5')).assumptions).toEqual(['latLonOrder']);
    const swapped = only(parseAngles('120.5 45.5'));
    expect([swapped.lon, swapped.lat, swapped.assumptions]).toEqual([120.5, 45.5, []]);
    // One letter fixes both axes.
    const lettered = only(parseAngles('25.5E 45.5'));
    expect([lettered.lon, lettered.lat]).toEqual([25.5, 45.5]);
  });

  it('reads two bare numbers longitude first when the person chose that, and says so', () => {
    const lonLat = { pairOrder: 'lonLat', bareUtmHemisphere: 'north' } as const;
    const read = only(parseAngles('25.5 45.5', lonLat));
    expect([read.lon, read.lat, read.assumptions]).toEqual([25.5, 45.5, ['lonLatOrder']]);
    // What the text settles is read as written, whatever the rule: a letter, or a second number
    // only a longitude can be.
    const lettered = only(parseAngles('45.5N 25.5', lonLat));
    expect([lettered.lon, lettered.lat, lettered.assumptions]).toEqual([25.5, 45.5, []]);
    const forced = only(parseAngles('45.5 120.5', lonLat));
    expect([forced.lon, forced.lat]).toEqual([120.5, 45.5]);
    // The default is latitude first, as before.
    const byDefault = only(parseAngles('25.5 45.5', { pairOrder: 'latLon', bareUtmHemisphere: 'north' }));
    expect([byDefault.lon, byDefault.lat, byDefault.assumptions]).toEqual([45.5, 25.5, ['latLonOrder']]);
  });

  it.each([
    ['91N 25E', 'latitudeRange'],
    ['45 181', 'longitudeRange'],
    ['45 61 25 10', 'minutesRange'],
    ['45 30 61 25 10 0', 'secondsRange'],
    ['45.5 30 25 10', 'fractionNotLast'],
    ['45N 25S', 'hemisphereConflict'],
    ['-45S 25E', 'hemisphereConflict'],
    ['45 25 10', 'componentCount'],
    ['45′ 25°', 'unitOrder'],
  ])('refuses %s with %s', (text, error) => {
    expect(parseAngles(text)).toEqual({ kind: 'invalid', error });
  });

  it('leaves texts in other formats to their readers', () => {
    expect(parseAngles('35T 412345 5043210').kind).toBe('notThisFormat');
    expect(parseAngles('8GQ7GCJW+GV').kind).toBe('notThisFormat');
    expect(parseAngles('').kind).toBe('notThisFormat');
  });
});
