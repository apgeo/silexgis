// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ANCHORED_TO_SURFACE, drawnAltitude, drawnTopHeight, samePlacement } from './altitude3d.ts';

// An invented cave whose top is at 1100 m and whose deepest point is 180 m below it.
const TOP = 1100;
const DEEPEST = 920;

describe('where a surveyed altitude is drawn', () => {
  it('hangs a cave from the surface on the bare ellipsoid, keeping its true depths', () => {
    expect(drawnTopHeight(TOP, ANCHORED_TO_SURFACE)).toBe(0);
    expect(drawnAltitude(DEEPEST, TOP, ANCHORED_TO_SURFACE)).toBe(-180);
    // The anchoring offset of a placement plays no part when nothing is absolute.
    expect(drawnAltitude(DEEPEST, TOP, { absolute: false, offsetM: 37 })).toBe(-180);
  });

  it('puts a cave where it was surveyed against relief, raised by the elevation model’s offset', () => {
    const relief = { absolute: true, offsetM: 37.5 };
    expect(drawnTopHeight(TOP, relief)).toBe(1137.5);
    expect(drawnAltitude(DEEPEST, TOP, relief)).toBe(957.5);
    // A model speaking heights above sea level needs no offset at all.
    expect(drawnAltitude(DEEPEST, TOP, { absolute: true, offsetM: 0 })).toBe(DEEPEST);
  });

  it('keeps the depth between two points of a cave the same whichever way it is placed', () => {
    for (const placement of [ANCHORED_TO_SURFACE, { absolute: true, offsetM: 37.5 }]) {
      expect(drawnTopHeight(TOP, placement) - drawnAltitude(DEEPEST, TOP, placement)).toBe(TOP - DEEPEST);
    }
  });

  it('tells two placements apart by both what they are and their offset', () => {
    expect(samePlacement(ANCHORED_TO_SURFACE, { absolute: false, offsetM: 0 })).toBe(true);
    expect(samePlacement({ absolute: true, offsetM: 37.5 }, { absolute: true, offsetM: 37.5 })).toBe(true);
    expect(samePlacement(ANCHORED_TO_SURFACE, { absolute: true, offsetM: 0 })).toBe(false);
    expect(samePlacement({ absolute: true, offsetM: 0 }, { absolute: true, offsetM: 37.5 })).toBe(false);
  });
});
