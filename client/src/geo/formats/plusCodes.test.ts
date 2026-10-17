// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  decodePlusCode,
  encodeIntegers,
  encodePlusCode,
  isFullPlusCode,
  isValidPlusCode,
  locationToIntegers,
  parsePlusCode,
  PLUS_CODE,
  protectedPlusCodeLength,
} from './plusCodes.ts';

// Rows copied from the Open Location Code specification's own test data
// (test_data/encoding.csv, decoding.csv and validityTests.csv in github.com/google/open-location-code,
// Apache-2.0). The whole published set — 302 encodings, 420 decodings, 25 validity cases — passed
// when this was written; these rows keep one of each kind the set exercises.

describe('Plus Codes: the specification test data', () => {
  it.each([
    // latitude, longitude, latitude integer, longitude integer, length, code
    [20.375, 2.775, 2759375000, 1497292800, 6, '7FG49Q00+'],
    [20.3700625, 2.7821875, 2759251562, 1497351680, 10, '7FG49QCJ+2V'],
    [20.3701125, 2.782234375, 2759252812, 1497352064, 11, '7FG49QCJ+2VX'],
    [20.3701135, 2.78223535156, 2759252837, 1497352071, 13, '7FG49QCJ+2VXGJ'],
    [-41.2730625, 174.7859375, 1218173437, 2906406400, 10, '4VCPPQGP+Q9'],
    [-89.9999375, -179.9999375, 1562, 512, 10, '22222222+22'],
    // At and over the pole: clipped into the last cell.
    [90, 1, 4499999999, 1482752000, 10, 'CFX3X2X2+X2'],
    [92, 1, 4499999999, 1482752000, 4, 'CFX30000+'],
    // Longitude far outside ±180: normalised.
    [47.0000625, -711.9999375, 3425001562, 1540096512, 10, '8FVC2222+22'],
    // Values binary floating point cannot hold exactly.
    [40.6, 129.7, 3265000000, 2537062400, 8, '8QGFJP22+'],
    [-34.45, -93.719, 1388750000, 706813952, 6, '46Q8H700+'],
    // More than fifteen digits asked for: fifteen given.
    [37.539669125, -122.375069724, 3188491728, 472063428, 16, '849VGJQF+VX7QR3J'],
  ])('encodes %f, %f to %s at length %i', (lat, lon, latInt, lonInt, length, code) => {
    expect(locationToIntegers(lat, lon)).toEqual([latInt, lonInt]);
    expect(encodeIntegers(latInt, lonInt, length)).toBe(code);
    expect(encodePlusCode(lat, lon, length)).toBe(code);
  });

  it.each([
    // code, length, south, west, north, east
    ['7FG49Q00+', 6, 20.35, 2.75, 20.4, 2.8],
    ['7FG49QCJ+2VX', 11, 20.3701, 2.78221875, 20.370125, 2.78225],
    ['8FVC2222+22', 10, 47.0, 8.0, 47.000125, 8.000125],
    ['4VCPPQGP+Q9', 10, -41.273125, 174.785875, -41.273, 174.786],
    ['6VGX0000+', 4, 0, 179, 1, 180],
  ])('decodes %s', (code, length, south, west, north, east) => {
    const area = decodePlusCode(code)!;
    expect(area.length).toBe(length);
    for (const [got, want] of [
      [area.south, south],
      [area.west, west],
      [area.north, north],
      [area.east, east],
    ]) {
      expect(got).toBeCloseTo(want, 9);
    }
  });

  it.each([
    // code, valid, full
    ['8FWC2345+G6', true, true],
    ['8fwc2345+', true, true],
    ['8FWCX400+', true, true],
    ['WC2345+G6g', true, false],
    ['+G6', true, false],
    ['G+', false, false],
    ['+', false, false],
    ['8FWC2345+G', false, false],
    ['8FWC2_45+G6', false, false],
    ['8FWC2345+G6+', false, false],
    ['8FWC2300+G6', false, false],
    ['84900000+', false, false],
    ['849VGJQF+VX7QR3U', false, false],
  ])('judges %s valid %s, full %s', (code, valid, full) => {
    expect(isValidPlusCode(code)).toBe(valid);
    expect(isFullPlusCode(code)).toBe(full);
  });
});

describe('Plus Codes as a format', () => {
  it('writes the length asked for and refuses a length the specification has no codes of', () => {
    expect(PLUS_CODE.format(2.782234375, 20.3701125, { length: 11 })).toBe('7FG49QCJ+2VX');
    expect(encodePlusCode(20.3701125, 2.782234375, 7)).toBeNull();
    expect(encodePlusCode(Number.NaN, 2)).toBeNull();
  });

  it('reads a full code back to the centre of its cell, with or without spaces and in any case', () => {
    const outcome = parsePlusCode(' 7fg4 9qcj+2v ');
    expect(outcome.kind).toBe('parsed');
    if (outcome.kind !== 'parsed') return;
    expect(outcome.candidates[0].lat).toBeCloseTo(20.3700625, 9);
    expect(outcome.candidates[0].lon).toBeCloseTo(2.7821875, 9);
  });

  it('round-trips at every length it offers, within the cell', () => {
    for (const length of PLUS_CODE.choices.length) {
      const code = PLUS_CODE.format(25.44721, 45.53127, { length })!;
      const area = decodePlusCode(code)!;
      // Decoding adds up floating-point steps, so the cell edges carry a last-digit error.
      const slack = 1e-9;
      expect(45.53127 + slack).toBeGreaterThanOrEqual(area.south);
      expect(45.53127 - slack).toBeLessThan(area.north);
      expect(25.44721 + slack).toBeGreaterThanOrEqual(area.west);
      expect(25.44721 - slack).toBeLessThan(area.east);
    }
  });

  it('refuses a short code, which needs a reference place, and a malformed one', () => {
    expect(parsePlusCode('9QCJ+2V')).toEqual({ kind: 'invalid', error: 'plusCodeShort' });
    expect(parsePlusCode('8FWC2300+G6')).toEqual({ kind: 'invalid', error: 'plusCodeInvalid' });
    expect(parsePlusCode('45.5 25.5').kind).toBe('notThisFormat');
  });

  it('writes a protected position with a cell no smaller than the hundredth of a degree', () => {
    // Six digits — a twentieth of a degree — for the 5 km grid; eight would name a 280 m cell.
    expect(protectedPlusCodeLength(5000)).toBe(6);
    expect(PLUS_CODE.formatProtected(25.44721, 45.53127, { length: 13 }, 5000)).toMatch(/^[2-9CFGHJMPQRVWX]{6}00\+$/);
    // A 500 m grid writes to the thousandth: eight digits (0.0025°) are then coarse enough.
    expect(protectedPlusCodeLength(500)).toBe(8);
  });
});
