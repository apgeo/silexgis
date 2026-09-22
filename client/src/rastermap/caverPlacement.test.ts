// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TrackedCaver, TrackedCaverPosition } from '../caveview/trackedCavers.ts';
import {
  fanOffsets,
  placedSheetCavers,
  sheetCaverHit,
  stationsWithoutPoint,
} from './caverPlacement.ts';
import type { MapStationMarker } from './mapPoints.ts';

/**
 * The placement rule the sheets draw the party by: a caver reaches a map through a
 * station pin and through no other path. Each positive claim here has its absence twin —
 * the drawn caver beside the not-drawn one — because the whole feature is the difference
 * between the two.
 */

function caver(id: string, position: TrackedCaverPosition, out = false): TrackedCaver {
  return {
    caverId: id,
    name: `Name ${id}`,
    teamId: null,
    teamTitle: null,
    position,
    lastRecordedAt: '2026-09-20T10:00:00Z',
    positionAt: '2026-09-20T10:00:00Z',
    enteredAt: null,
    out,
  };
}

function pin(station: string, x: number, y: number): MapStationMarker {
  return { station, x, y, linkId: `link-${station}`, memberId: `member-${station}`, mayEdit: true };
}

const SHEET_PINS = [pin('p.g.7', 0.25, 0.5), pin('p.g.8', 0.75, 0.25)];

describe('placedSheetCavers', () => {
  it('draws a caver at their station pin, and not a caver whose station has no point here', () => {
    const placed = placedSheetCavers(
      [caver('ana', { kind: 'station', station: 'p.g.7' }), caver('bob', { kind: 'station', station: 'p.g.99' })],
      SHEET_PINS,
      16,
    );

    expect(placed).toHaveLength(1);
    expect(placed[0].caver.caverId).toBe('ana');
    expect(placed[0]).toMatchObject({ station: 'p.g.7', x: 0.25, y: 0.5, offsetPx: [0, 0] });
  });

  it('never invents a place for a position that names no station', () => {
    const placed = placedSheetCavers(
      [
        caver('withheld', { kind: 'withheld', certain: true }),
        caver('depth', { kind: 'depth', depthM: 40 }),
        caver('elsewhere', { kind: 'otherModel' }),
        caver('silent', { kind: 'unreported' }),
        // The positive twin in the same party: the one placeable record still places.
        caver('ana', { kind: 'station', station: 'p.g.8' }),
      ],
      SHEET_PINS,
      16,
    );

    expect(placed.map((m) => m.caver.caverId)).toEqual(['ana']);
  });

  it('fans a shared pin out in watch order: every dot at the pin, each with its own offset', () => {
    const placed = placedSheetCavers(
      [
        caver('ana', { kind: 'station', station: 'p.g.7' }),
        caver('bob', { kind: 'station', station: 'p.g.7' }, true),
      ],
      SHEET_PINS,
      16,
    );

    expect(placed.map((m) => m.caver.caverId)).toEqual(['ana', 'bob']);
    // The geometry stays the pin for both — the fan is pixels, never a position claim.
    expect(placed.every((m) => m.x === 0.25 && m.y === 0.5)).toBe(true);
    expect(placed[0].offsetPx).not.toEqual(placed[1].offsetPx);
  });

  it('gives a caver standing alone the pin itself, with no fan at all', () => {
    expect(fanOffsets(1, 16)).toEqual([[0, 0]]);
    const two = fanOffsets(2, 16);
    expect(two).toHaveLength(2);
    // First of the ring stands at the top; every offset sits on the ring's radius.
    expect(two[0][0]).toBeCloseTo(0);
    expect(two[0][1]).toBeCloseTo(16);
    for (const [dx, dy] of two) {
      expect(Math.hypot(dx, dy)).toBeCloseTo(16);
    }
  });
});

describe('stationsWithoutPoint', () => {
  it('names exactly the reported stations this sheet has no point for', () => {
    const missing = stationsWithoutPoint(
      [
        caver('ana', { kind: 'station', station: 'p.g.7' }),
        caver('bob', { kind: 'station', station: 'p.g.99' }),
        caver('withheld', { kind: 'withheld', certain: true }),
        caver('silent', { kind: 'unreported' }),
      ],
      SHEET_PINS,
    );

    // The pinned station is absent from the answer — its caver is drawn, not messaged —
    // and the stationless positions are absent too: their own wording answers for them.
    expect([...missing]).toEqual(['p.g.99']);
  });

  it('answers empty when everybody placeable is placed', () => {
    expect(
      stationsWithoutPoint([caver('ana', { kind: 'station', station: 'p.g.7' })], SHEET_PINS).size,
    ).toBe(0);
  });
});

describe('sheetCaverHit', () => {
  const SIZE = { width: 4000, height: 1000 };
  // p.g.7's pin at fractions (0.25, 0.5) → map coordinate (1000, 500) on this picture.
  const placed = placedSheetCavers(
    [
      caver('ana', { kind: 'station', station: 'p.g.7' }),
      caver('bob', { kind: 'station', station: 'p.g.7' }),
    ],
    SHEET_PINS,
    16,
  );

  it('answers the dot the press landed on, offsets included', () => {
    // At resolution 2, ana's fan offset (0, 16px) puts her dot 32 map units above the pin.
    const hit = sheetCaverHit(placed, SIZE, [1000, 500 + 32], 2, 8);
    expect(hit?.caver.caverId).toBe('ana');
    // …and the nearest dot wins where the reach would admit both.
    const other = sheetCaverHit(placed, SIZE, [1000, 500 - 30], 2, 24);
    expect(other?.caver.caverId).toBe('bob');
  });

  it('answers nothing outside the reach — a press near a dot is not a press on it', () => {
    expect(sheetCaverHit(placed, SIZE, [1200, 500], 2, 8)).toBeNull();
  });
});
