// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { OverburdenHighlight } from '../workspace/overburdenHighlight.ts';
import { ANCHORED_TO_SURFACE } from './altitude3d.ts';
import { overburdenHighlightPolylines } from './overburdenHighlight3d.ts';

/** A reading taken on a passage 180 m below the top of its own survey. */
const reading: OverburdenHighlight = {
  caveId: 'cave-a',
  longitude: 24,
  latitude: 46,
  altitudeM: 820,
  label: '48.3 m of rock overhead',
};

const tops: Record<string, number> = { 'cave-a': 1000 };
const topOf = (caveId: string) => tops[caveId];

describe('overburdenHighlightPolylines', () => {
  it('draws nothing when no reading has been pressed', () => {
    expect(overburdenHighlightPolylines(null, ANCHORED_TO_SURFACE, topOf)).toEqual([]);
  });

  it('stands the mark on the passage, hung from the survey top of its own cave', () => {
    // On the bare ellipsoid a cave is drawn with its top on the surface and the rest below, so a
    // mark placed at the raw surveyed altitude would float 820 m up, nowhere near the passage it
    // belongs to.
    const [line] = overburdenHighlightPolylines(reading, ANCHORED_TO_SURFACE, topOf);
    expect(line.positions.map((p) => p.height)).toEqual([-180, -160]);
    expect(line.positions.map((p) => p.longitude)).toEqual([24, 24]);
    expect(line.clampToGround).toBeUndefined();
  });

  it('stands it where the passage was surveyed once the ground has relief', () => {
    // With an elevation model the anchoring is what would be wrong: the cave is inside its own
    // hillside, and the offset applied is the model's rather than the cave's.
    const [line] = overburdenHighlightPolylines(reading, { absolute: true, offsetM: 30 }, topOf);
    expect(line.positions.map((p) => p.height)).toEqual([850, 870]);
  });

  it('lays the mark on the ground when the scene has not been told where the cave is drawn', () => {
    // A cave whose survey layer is off, or which the camera is nowhere near, has no top to hang
    // from. Laying the mark on the ground is honest; inventing a height for it is not.
    const [line] = overburdenHighlightPolylines(reading, ANCHORED_TO_SURFACE, () => undefined);
    expect(line.clampToGround).toBe(true);
    expect(line.positions.map((p) => p.height)).toEqual([0, 20]);
  });

  it('is not a thing that can be selected', () => {
    // It is an answer drawn over the data rather than a thing in it, so a click on it should
    // select whatever survey lies underneath.
    const [line] = overburdenHighlightPolylines(reading, ANCHORED_TO_SURFACE, topOf);
    expect(line.id).toBeNull();
  });
});
