// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { parseUtm, UTM } from './utm.ts';
import { fromUtm, toUtm } from './utmMath.ts';

// Reference points are published worked examples or exact identities of the projection.

/** The CN Tower, Toronto: 43°38′33.24″N 79°23′13.7″W = 17T 630084 4833438 (the UTM article of the English Wikipedia). */
const CN_TOWER: [number, number] = [-(79 + 23 / 60 + 13.7 / 3600), 43 + 38 / 60 + 33.24 / 3600];

describe('UTM writing', () => {
  it('writes the published example at each precision, truncating to the square', () => {
    const [lon, lat] = CN_TOWER;
    expect(UTM.format(lon, lat, UTM.defaults)).toBe('17T 630084 E 4833438 N');
    expect(UTM.format(lon, lat, { zone: 'auto', precisionMetres: 10 })).toBe('17T 630080 E 4833430 N');
    expect(UTM.format(lon, lat, { zone: 'auto', precisionMetres: 100 })).toBe('17T 630000 E 4833400 N');
  });

  it('keeps a forced zone across the zone edge, and gives it up far from that zone', () => {
    // 24°E lies on the edge of zones 34 and 35; a point at 23.9°E is in 34 by itself.
    expect(toUtm(23.9, 45.5)!.zone).toBe(34);
    expect(toUtm(23.9, 45.5, 35)!.zone).toBe(35);
    expect(UTM.format(23.9, 45.5, { zone: 35, precisionMetres: 1 })).toMatch(/^35T /);
    // 9° beyond the central meridian of 35 (27°E) is as far as a forced zone reaches.
    expect(toUtm(17.5, 45.5, 35)!.zone).toBe(33);
  });

  it('reads a forced-zone reference back to the same point', () => {
    const forced = toUtm(23.2, 45.5, 35)!;
    const [lon, lat] = fromUtm(forced);
    expect(Math.abs(lon - 23.2)).toBeLessThan(1e-6);
    expect(Math.abs(lat - 45.5)).toBeLessThan(1e-6);
  });

  it('has nothing to write beyond 84°N or 80°S', () => {
    expect(UTM.format(10, 85, UTM.defaults)).toBeNull();
    expect(UTM.format(10, -81, UTM.defaults)).toBeNull();
  });

  it('writes a protected position to the kilometre, whatever the precision asked for', () => {
    const [lon, lat] = CN_TOWER;
    expect(UTM.formatProtected(lon, lat, UTM.defaults, 5000)).toBe('17T 630000 E 4833000 N');
    expect(UTM.formatProtected(lon, lat, { zone: 'auto', precisionMetres: 100 }, 5000)).toBe('17T 630000 E 4833000 N');
  });
});

describe('UTM reading', () => {
  it.each([
    '17T 630084 4833438',
    '17T 630084 E 4833438 N',
    '17 T 630084mE 4833438mN',
    '17t, 630084, 4833438',
    '17T E 630084 N 4833438',
    '17T N 4833438 E 630084',
  ])('reads %s', (text) => {
    const outcome = parseUtm(text);
    expect(outcome.kind).toBe('parsed');
    if (outcome.kind !== 'parsed') return;
    expect(outcome.candidates).toHaveLength(1);
    const [{ lon, lat, assumptions }] = outcome.candidates;
    expect(Math.abs(lon - CN_TOWER[0])).toBeLessThan(2e-5);
    expect(Math.abs(lat - CN_TOWER[1])).toBeLessThan(2e-5);
    expect(assumptions).toEqual([]);
  });

  it('takes a reference without a letter as northern, and says so', () => {
    const outcome = parseUtm('17 630084 4833438');
    expect(outcome.kind === 'parsed' && outcome.candidates[0].assumptions).toEqual(['northernHemisphere']);
  });

  it('offers both readings of S when the reference could be band S or the southern hemisphere', () => {
    // 35S 500000 3800000 is 34.3°N as band S, and 55.9°S as the southern hemisphere.
    const outcome = parseUtm('35S 500000 3800000');
    expect(outcome.kind).toBe('parsed');
    if (outcome.kind !== 'parsed') return;
    expect(outcome.candidates.map((c) => [Math.round(c.lat), c.assumptions[0]])).toEqual([
      [34, 'utmLetterAsBand'],
      [-56, 'utmLetterAsHemisphere'],
    ]);
    // Far outside band S only the hemisphere is left.
    const south = parseUtm('19S 346000 6303000');
    expect(south.kind === 'parsed' && south.candidates.map((c) => Math.round(c.lat))).toEqual([-33]);
  });

  it('round-trips what it writes, in the southern hemisphere too', () => {
    for (const [lon, lat] of [
      [25.44721, 45.53127],
      [-70.6, -33.4],
      [151.2, -33.85],
      [5, 60],
    ]) {
      const outcome = parseUtm(UTM.format(lon, lat, UTM.defaults)!);
      expect(outcome.kind).toBe('parsed');
      if (outcome.kind !== 'parsed') continue;
      const reading = outcome.candidates.find((c) => Math.abs(c.lat - lat) < 1e-4)!;
      expect(Math.abs(reading.lon - lon)).toBeLessThan(2e-5);
    }
  });

  it.each([
    ['61T 630084 4833438', 'utmZoneRange'],
    ['17T 63008 4833438', 'utmEastingRange'],
    ['17T 630084 12000000', 'utmNorthingRange'],
    ['17A 630084 4833438', 'utmBandUnknown'],
    ['17U 630084 4833438', 'utmBandMismatch'],
  ])('refuses %s with %s', (text, error) => {
    expect(parseUtm(text)).toEqual({ kind: 'invalid', error });
  });

  it('leaves angles and codes to their readers', () => {
    expect(parseUtm('45.5 25.5').kind).toBe('notThisFormat');
    expect(parseUtm('45 30 25 15').kind).toBe('notThisFormat');
    expect(parseUtm('8GQ7GCJW+GV').kind).toBe('notThisFormat');
  });
});
