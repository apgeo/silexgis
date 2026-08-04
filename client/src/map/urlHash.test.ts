// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { formatMapHash, parseMapHash } from './urlHash.ts';

describe('parseMapHash', () => {
  it('parses a well-formed zoom/lat/lon hash', () => {
    expect(parseMapHash('#14.00/45.70000/25.30000')).toEqual({ zoom: 14, lat: 45.7, lon: 25.3 });
  });

  it('accepts negative longitudes (western hemisphere)', () => {
    expect(parseMapHash('#12/54.15799/-2.38929')).toEqual({ zoom: 12, lat: 54.15799, lon: -2.38929 });
  });

  it('returns null for an empty or unrelated hash', () => {
    expect(parseMapHash('')).toBeNull();
    expect(parseMapHash('#/caves/42')).toBeNull();
    expect(parseMapHash('#14.0/45.7')).toBeNull();
  });

  it('does not mistake a 3D position for a map one', () => {
    // The two hashes share one address bar and must never be read as each other. This holds
    // because the expression is anchored at both ends and its first group takes digits only, so
    // the literal `3d` fails it twice over — asserted rather than assumed, because that is the
    // kind of property that quietly stops being true when somebody relaxes an anchor.
    expect(parseMapHash('#3d/45.68321/25.30612/-184/137.5/-22.4')).toBeNull();
  });

  it('rejects out-of-range coordinates', () => {
    expect(parseMapHash('#10/91/25')).toBeNull(); // lat > 90
    expect(parseMapHash('#10/45/181')).toBeNull(); // lon > 180
    expect(parseMapHash('#40/45/25')).toBeNull(); // zoom > 28
  });
});

describe('formatMapHash', () => {
  it('round-trips through parseMapHash', () => {
    const original = { zoom: 13.5, lat: 45.883302, lon: 25.306131 };
    const parsed = parseMapHash(formatMapHash(original));
    expect(parsed).toEqual({ zoom: 13.5, lat: 45.8833, lon: 25.30613 });
  });
});
