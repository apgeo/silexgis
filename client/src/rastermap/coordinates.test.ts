// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { imageExtent, toMapCoordinate } from './coordinates.ts';

// Deliberately not square: on a square image a missing (or doubled) y-flip is invisible
// for exactly the symmetric coordinates a quick test would reach for.
const SIZE = { width: 4000, height: 1000 };

describe('imageExtent', () => {
  it('is the picture at natural size, anchored at the origin', () => {
    expect(imageExtent(SIZE)).toEqual([0, 0, 4000, 1000]);
  });
});

describe('toMapCoordinate', () => {
  it('scales fractions to pixels and flips y from top-left to bottom-left origin', () => {
    // A point a quarter across and a quarter DOWN the picture sits three quarters UP the
    // OL extent: the stored frame hangs from the top, the drawn one stands on the bottom.
    expect(toMapCoordinate({ x: 0.25, y: 0.25 }, SIZE)).toEqual([1000, 750]);
  });

  it('sends the stored origin to the top-left corner of the drawn image', () => {
    // Stored (0,0) is the top-left of the picture, which in the OL frame is (0, height).
    expect(toMapCoordinate({ x: 0, y: 0 }, SIZE)).toEqual([0, 1000]);
    expect(toMapCoordinate({ x: 1, y: 1 }, SIZE)).toEqual([4000, 0]);
  });

  it('leaves the centre where every frame agrees it is', () => {
    expect(toMapCoordinate({ x: 0.5, y: 0.5 }, SIZE)).toEqual([2000, 500]);
  });
});
