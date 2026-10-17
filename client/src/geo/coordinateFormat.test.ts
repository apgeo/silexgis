// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  formatApproximateCoordinate,
  formatCoordinate,
  formatCoordinateForCopy,
  fromUtm,
  isCoordinateFormat,
  toUtm,
} from './coordinateFormat.ts';
import { FORMAT_IDS } from './formats/registry.ts';

// Reference points are public, published examples or exact identities of the projection — no
// position from any cave register.

describe('UTM', () => {
  it('puts a point on a central meridian at the equator exactly on the false easting', () => {
    const utm = toUtm(3, 0)!;
    expect(utm.zone).toBe(31);
    expect(utm.band).toBe('N');
    expect(utm.easting).toBeCloseTo(500000, 6);
    expect(utm.northing).toBeCloseTo(0, 6);
  });

  it('matches the published worked example: the CN Tower, Toronto, 17T 630084 4833438', () => {
    // 43°38′33.24″N 79°23′13.7″W, the example the UTM article of the English Wikipedia gives.
    const utm = toUtm(-(79 + 23 / 60 + 13.7 / 3600), 43 + 38 / 60 + 33.24 / 3600)!;
    expect(`${utm.zone}${utm.band}`).toBe('17T');
    expect(Math.abs(utm.easting - 630084)).toBeLessThan(1);
    expect(Math.abs(utm.northing - 4833438)).toBeLessThan(1);
  });

  it('mirrors the southern hemisphere about the false northing', () => {
    const north = toUtm(27.5, 30)!;
    const south = toUtm(27.5, -30)!;
    expect(south.hemisphere).toBe('S');
    expect(south.easting).toBeCloseTo(north.easting, 6);
    expect(south.northing).toBeCloseTo(10000000 - north.northing, 6);
  });

  it.each([
    [25.3, 45.7],
    [21.01, 44.99],
    [29.7, 45.2],
    [-70.6, -33.4],
    [151.2, -33.85],
    [0.1, 51.5],
    [-122.4, 37.8],
  ])('round-trips %f, %f to well under a centimetre', (lon, lat) => {
    const utm = toUtm(lon, lat)!;
    const [backLon, backLat] = fromUtm(utm);
    // A centimetre is about 1e-7 of a degree of latitude.
    expect(Math.abs(backLon - lon)).toBeLessThan(1e-7);
    expect(Math.abs(backLat - lat)).toBeLessThan(1e-7);
  });

  it('follows the grid where it departs from 6° strips: south-west Norway and Svalbard', () => {
    expect(toUtm(5, 60)!.zone).toBe(32);
    expect(toUtm(5, 55)!.zone).toBe(31);
    expect(toUtm(15, 78)!.zone).toBe(33);
    expect(toUtm(25, 78)!.zone).toBe(35);
  });

  it('has no grid reference beyond 84°N or 80°S', () => {
    expect(toUtm(10, 85)).toBeNull();
    expect(toUtm(10, -81)).toBeNull();
  });
});

describe('formatCoordinate', () => {
  it('keeps the decimal degrees every display printed until now', () => {
    expect(formatCoordinate(25.44721, 45.53127, 'dd')).toBe('45.53127°N 25.44721°E');
    expect(formatCoordinate(-79.38714, -43.64257, 'dd', { dd: { decimals: 3, style: 'letters' } })).toBe('43.643°S 79.387°W');
  });

  it('writes degrees, minutes and seconds to the places the settings ask for', () => {
    expect(formatCoordinate(-(79 + 23 / 60 + 13.7 / 3600), 43 + 38 / 60 + 33.24 / 3600, 'dms')).toBe(
      '43°38′33.2″N 79°23′13.7″W',
    );
    expect(formatCoordinate(25.5, 45.25, 'dms', { dms: { secondsDecimals: 2 } })).toBe('45°15′00.00″N 25°30′00.00″E');
  });

  it('carries a rounded 60″ up rather than printing it', () => {
    // 45.9999999° is 45°59′59.99964″, which rounds to a whole minute.
    expect(formatCoordinate(25, 45.9999999, 'dms')).toBe('46°00′00.0″N 25°00′00.0″E');
  });

  it('writes UTM with its zone and band, in whole metres by default, truncated as grid references are', () => {
    expect(formatCoordinate(-(79 + 23 / 60 + 13.7 / 3600), 43 + 38 / 60 + 33.24 / 3600, 'utm')).toBe(
      '17T 630084 E 4833438 N',
    );
  });

  it('cuts UTM to the coarser square the settings ask for', () => {
    expect(formatCoordinate(-(79 + 23 / 60 + 13.7 / 3600), 43 + 38 / 60 + 33.24 / 3600, 'utm', {
      utm: { zone: 'auto', precisionMetres: 100 },
    })).toBe(
      '17T 630000 E 4833400 N',
    );
  });

  it('falls back to decimal degrees where UTM is not defined', () => {
    expect(formatCoordinate(10, 85, 'utm')).toBe('85.00000°N 10.00000°E');
  });

  it('tells a known stored format from anything else', () => {
    expect(isCoordinateFormat('utm')).toBe(true);
    expect(isCoordinateFormat('stereo70')).toBe(true);
    expect(isCoordinateFormat('gauss-kruger')).toBe(false);
    expect(isCoordinateFormat(undefined)).toBe(false);
  });
});

describe('formatApproximateCoordinate', () => {
  // The summit of Moldoveanu, a public trig point, standing in for a position snapped to the grid.
  const [lon, lat] = [24.73617, 45.59944];

  it('writes decimal degrees no finer than the 5 km grid: to the hundredth, about a kilometre', () => {
    expect(formatApproximateCoordinate(lon, lat, 'dd')).toBe('45.60°N 24.74°E');
  });

  it('writes degrees and whole minutes, with no seconds', () => {
    expect(formatApproximateCoordinate(lon, lat, 'dms')).toBe('45°36′N 24°44′E');
  });

  it('writes UTM to the kilometre, never to the metre', () => {
    const text = formatApproximateCoordinate(lon, lat, 'utm');
    expect(text).toMatch(/^35T \d+000 E \d+000 N$/);
    expect(text).not.toBe(formatCoordinate(lon, lat, 'utm'));
  });

  it('follows a coarser or finer grid when told one', () => {
    expect(formatApproximateCoordinate(lon, lat, 'dd', undefined, 20_000)).toBe('45.6°N 24.7°E');
    expect(formatApproximateCoordinate(lon, lat, 'utm', undefined, 10_000)).toMatch(/^35T \d+0000 E \d+0000 N$/);
  });
});

describe('every format against the protection grid', () => {
  const [lon, lat] = [24.73617, 45.59944];

  it.each(FORMAT_IDS)('never writes a snapped position as finely as an exact one in %s', (format) => {
    const exact = formatCoordinate(lon, lat, format);
    const approximate = formatApproximateCoordinate(lon, lat, format);
    expect(approximate.length).toBeGreaterThan(0);
    expect(approximate).not.toBe(exact);
  });
});

describe('formatCoordinateForCopy', () => {
  it('copies decimal degrees as signed latitude, longitude to six places', () => {
    expect(formatCoordinateForCopy(25.44721, 45.53127, 'dd')).toBe('45.531270, 25.447210');
  });

  it('copies a protected position no finer than it is shown', () => {
    expect(formatCoordinateForCopy(24.73617, 45.59944, 'dd', undefined, true)).toBe('45.60, 24.74');
    expect(formatCoordinateForCopy(24.73617, 45.59944, 'utm', undefined, true)).toMatch(/^35T \d+000 E \d+000 N$/);
  });

  it('falls back to decimal degrees where the format has no reference', () => {
    expect(formatCoordinateForCopy(10, 85, 'utm')).toBe('85.000000, 10.000000');
  });
});
