// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import {
  bearingPoint,
  foldAxisDegrees,
  rosePetals,
  roseScale,
  roseSummary,
  wedgePath,
  type RoseBin,
} from './roseGeometry.ts';

/** A sector of the folded half-circle carrying the counts and lengths given. */
function bin(fromDegrees: number, count: number, lengthM: number): RoseBin {
  return { fromDegrees, toDegrees: fromDegrees + 10, count, lengthM, countFraction: 0, lengthFraction: 0 };
}

/** The fractions a caller would have been handed alongside the raw figures. */
function withFractions(bins: RoseBin[]): RoseBin[] {
  const totalCount = bins.reduce((sum, b) => sum + b.count, 0);
  const totalLength = bins.reduce((sum, b) => sum + b.lengthM, 0);
  return bins.map((b) => ({
    ...b,
    countFraction: totalCount > 0 ? b.count / totalCount : 0,
    lengthFraction: totalLength > 0 ? b.lengthM / totalLength : 0,
  }));
}

describe('the axial rule', () => {
  it('reads a passage and the same passage surveyed backwards as one trend', () => {
    // This is the whole reason the rose is written by hand. A directional rose would put these two
    // in opposite sectors and look entirely plausible while doing it.
    expect(foldAxisDegrees(10)).toBe(foldAxisDegrees(190));
    expect(foldAxisDegrees(10)).toBe(10);
  });

  it('folds the whole circle, including the turns either side of it', () => {
    expect(foldAxisDegrees(0)).toBe(0);
    expect(foldAxisDegrees(180)).toBe(0);
    expect(foldAxisDegrees(360)).toBe(0);
    expect(foldAxisDegrees(-10)).toBe(170);
    expect(foldAxisDegrees(370)).toBe(10);
  });
});

describe('the petals', () => {
  it('draws every measured sector twice, half a turn apart', () => {
    const petals = rosePetals(withFractions([bin(40, 5, 100)]), 'count');

    expect(petals).toHaveLength(2);
    expect(petals[0]).toMatchObject({ fromDegrees: 40, toDegrees: 50, mirrored: false });
    expect(petals[1]).toMatchObject({ fromDegrees: 220, toDegrees: 230, mirrored: true });
    expect(petals[0].fraction).toBe(petals[1].fraction);
  });

  it('wraps the mirrored sector past north rather than off the end of the circle', () => {
    const petals = rosePetals(withFractions([bin(170, 3, 30)]), 'count');
    expect(petals[1].fromDegrees).toBe(350);
    expect(petals[1].toDegrees).toBe(360);
  });

  it('leaves an empty sector empty instead of drawing a sliver in it', () => {
    const petals = rosePetals(withFractions([bin(0, 0, 0), bin(90, 4, 80)]), 'count');
    expect(petals).toHaveLength(2);
    expect(petals.every((p) => p.fromDegrees % 180 === 90)).toBe(true);
  });

  it('draws nothing at all when nothing was measured', () => {
    expect(rosePetals(withFractions([bin(0, 0, 0), bin(90, 0, 0)]), 'count')).toEqual([]);
    expect(rosePetals([], 'length')).toEqual([]);
  });
});

describe('the two weightings', () => {
  // A chamber surveyed in many short legs against one long straight gallery: counting legs and
  // measuring metres do not merely differ in scale here, they name a different dominant trend. A
  // rose that quietly picked one of the two would be answering a question nobody asked.
  const chamberAndGallery = withFractions([bin(0, 40, 120), bin(90, 4, 900)]);

  it('name different dominant trends on a cave where they must', () => {
    expect(roseSummary(chamberAndGallery, 'count')?.dominantFromDegrees).toBe(0);
    expect(roseSummary(chamberAndGallery, 'length')?.dominantFromDegrees).toBe(90);
  });

  it('measure their petals against their own quantity', () => {
    const byCount = rosePetals(chamberAndGallery, 'count');
    const byLength = rosePetals(chamberAndGallery, 'length');

    expect(byCount[0].value).toBe(40);
    expect(byLength[0].value).toBe(120);
    expect(byCount[0].fraction).toBeGreaterThan(byLength[0].fraction);
  });

  it('says nothing rather than something when no direction was measured', () => {
    expect(roseSummary(withFractions([bin(0, 0, 0)]), 'count')).toBeNull();
    expect(roseSummary([], 'length')).toBeNull();
  });
});

describe('the rings', () => {
  it('reaches a round number at or above the largest sector', () => {
    const scale = roseScale(withFractions([bin(0, 1, 10), bin(90, 3, 30)]), 'count');

    expect(scale.rings.length).toBeGreaterThan(0);
    expect(scale.max).toBe(scale.rings[scale.rings.length - 1]);
    expect(scale.max).toBeGreaterThanOrEqual(0.75);
    expect([...scale.rings].sort((a, b) => a - b)).toEqual(scale.rings);
  });

  it('has no rings when there is nothing to measure against them', () => {
    expect(roseScale(withFractions([bin(0, 0, 0)]), 'count')).toEqual({ max: 0, rings: [] });
  });
});

describe('the drawing surface', () => {
  const centre = { x: 100, y: 100 };

  it('puts north up and lets bearings run clockwise', () => {
    const north = bearingPoint(0, 50, centre);
    expect(north.x).toBeCloseTo(100);
    expect(north.y).toBeCloseTo(50);

    const east = bearingPoint(90, 50, centre);
    expect(east.x).toBeCloseTo(150);
    expect(east.y).toBeCloseTo(100);

    const south = bearingPoint(180, 50, centre);
    expect(south.y).toBeCloseTo(150);
  });

  it('draws a wedge from the centre out and back', () => {
    const path = wedgePath(0, 10, 50, centre);
    expect(path.startsWith('M 100.00 100.00')).toBe(true);
    expect(path.endsWith('Z')).toBe(true);
    // Sweep 1: bearings increase clockwise, and the drawing surface grows downwards.
    expect(path).toContain('A 50.00 50.00 0 0 1');
  });
});
