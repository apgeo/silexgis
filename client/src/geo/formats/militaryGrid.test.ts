// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { MGRS } from './militaryGrid.ts';
import { toUtm } from './utmMath.ts';

describe('MGRS', () => {
  const [lon, lat] = [25.44721, 45.53127];

  it('names the same square as the UTM reference of the point, at every precision', () => {
    const utm = toUtm(lon, lat)!;
    const easting = String(Math.round(utm.easting) % 100000).padStart(5, '0');
    const northing = String(Math.round(utm.northing) % 100000).padStart(5, '0');
    const spaced = MGRS.format(lon, lat, MGRS.defaults)!;
    expect(spaced).toMatch(new RegExp(`^35T [A-Z]{2} ${easting} ${northing}$`));
    expect(MGRS.format(lon, lat, { precisionMetres: 100, spacing: 'compact' })).toBe(
      spaced.replace(/ /g, '').replace(/^(.{5})(\d{3})\d\d(\d{3})\d\d$/, '$1$2$3'),
    );
  });

  it('meets the published reference point', () => {
    // The example the MGRS literature uses: the Washington Monument, 38.8895°N 77.0352°W, is
    // 18S UJ 23487 06483.
    expect(MGRS.format(-77.0352, 38.8895, MGRS.defaults)).toBe('18S UJ 23487 06483');
  });

  it('reads it back with or without spaces, as the centre of the square it names', () => {
    for (const text of ['18S UJ 23487 06483', '18SUJ2348706483', 'mgrs: 18suj 23487 06483']) {
      const outcome = MGRS.parse(text);
      expect(outcome.kind).toBe('parsed');
      if (outcome.kind !== 'parsed') continue;
      expect(outcome.candidates[0].lat).toBeCloseTo(38.8895, 4);
      expect(outcome.candidates[0].lon).toBeCloseTo(-77.0352, 4);
    }
    const coarse = MGRS.parse('18SUJ2306');
    expect(coarse.kind === 'parsed' && coarse.candidates[0].lat).toBeCloseTo(38.89, 1);
  });

  it('tells a malformed reference from text that is not MGRS at all', () => {
    expect(MGRS.parse('18SUJ234870648')).toEqual({ kind: 'invalid', error: 'mgrsInvalid' });
    expect(MGRS.parse('61SUJ2348706483')).toEqual({ kind: 'invalid', error: 'utmZoneRange' });
    expect(MGRS.parse('35T 378752 5043143')).toEqual({ kind: 'notThisFormat' });
    expect(MGRS.parse('45.5 25.5')).toEqual({ kind: 'notThisFormat' });
  });

  it('has no reference over the poles', () => {
    expect(MGRS.format(10, 85, MGRS.defaults)).toBeNull();
    expect(MGRS.format(10, -81, MGRS.defaults)).toBeNull();
  });

  it('names a protected position by its 10 km square whatever the precision setting', () => {
    expect(MGRS.formatProtected(25.4472, 45.5313, MGRS.defaults, 5000)).toMatch(/^35T [A-Z]{2} \d \d$/);
  });
});
