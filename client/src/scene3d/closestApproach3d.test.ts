// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ClosestApproachLine } from '../workspace/closestApproachLine.ts';
import { ANCHORED_TO_SURFACE } from './altitude3d.ts';
import { closestApproachPolylines } from './closestApproach3d.ts';

/**
 * Two caves whose nearest ends are a hundred and fifty metres apart vertically, in caves whose
 * surveys top out at different altitudes — which is the case the anchoring has to get right.
 */
const line: ClosestApproachLine = {
  caveAId: 'cave-a',
  caveBId: 'cave-b',
  from: { longitude: 24, latitude: 46, altitudeM: 900 },
  to: { longitude: 24.003, latitude: 46, altitudeM: 750 },
  label: '268.2 m',
};

const tops: Record<string, number> = { 'cave-a': 1000, 'cave-b': 800 };
const topOf = (caveId: string) => tops[caveId];

describe('closestApproachPolylines', () => {
  it('draws nothing when nothing has been measured', () => {
    expect(closestApproachPolylines(null, ANCHORED_TO_SURFACE, topOf)).toEqual([]);
  });

  it('hangs each end from the top of the cave that end belongs to', () => {
    // The bare ellipsoid has no hillside, so each cave is drawn with its own top on the surface
    // and the rest of it below. An end drawn against the other cave's top — or against a single
    // shared one — would sit two hundred metres off the passage it was measured from, which is
    // the whole of what this line is for.
    const [polyline] = closestApproachPolylines(line, ANCHORED_TO_SURFACE, topOf);
    expect(polyline.positions.map((p) => p.height)).toEqual([-100, -50]);
    expect(polyline.clampToGround).toBeUndefined();
    expect(polyline.positions.map((p) => p.longitude)).toEqual([24, 24.003]);
  });

  it('draws both ends where they were surveyed once the ground has relief', () => {
    // With an elevation model the anchoring is what would be wrong: the caves are inside their
    // own hillside, and so is the line between them. The offset is the model's, not the cave's.
    const [polyline] = closestApproachPolylines(line, { absolute: true, offsetM: 30 }, topOf);
    expect(polyline.positions.map((p) => p.height)).toEqual([930, 780]);
  });

  it('lays the line on the ground when a cave it joins is not drawn', () => {
    // A cave whose top the scene was never told — its survey layer is off, or the camera is
    // nowhere near it — has no anchor, and inventing one would put the line at a depth nothing
    // measured. On the ground it is at least the honest plan of the measurement.
    const [polyline] = closestApproachPolylines(line, ANCHORED_TO_SURFACE, (id) =>
      id === 'cave-a' ? 1000 : undefined,
    );
    expect(polyline.clampToGround).toBe(true);
    expect(polyline.positions.map((p) => p.height)).toEqual([0, 0]);
  });

  it('is not a thing that can be selected', () => {
    // It is an answer drawn over the data rather than a row in it; a click should reach whatever
    // survey lies underneath.
    const [polyline] = closestApproachPolylines(line, ANCHORED_TO_SURFACE, topOf);
    expect(polyline.id).toBeNull();
  });
});
