// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { fromMapCoordinate, imageExtent, toMapCoordinate } from './coordinates.ts';

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

describe('fromMapCoordinate', () => {
  it('is the exact inverse of toMapCoordinate, so a pin placed by clicking reads back at the click', () => {
    // The asymmetric size again: a doubled or missing flip in either direction cannot
    // survive a round trip through a picture four times wider than it is tall.
    expect(fromMapCoordinate(toMapCoordinate({ x: 0.25, y: 0.25 }, SIZE), SIZE)).toEqual({
      x: 0.25,
      y: 0.25,
    });
    expect(fromMapCoordinate([1000, 750], SIZE)).toEqual({ x: 0.25, y: 0.25 });
  });

  it('sends the drawn top-left corner back to the stored origin', () => {
    expect(fromMapCoordinate([0, 1000], SIZE)).toEqual({ x: 0, y: 0 });
    expect(fromMapCoordinate([4000, 0], SIZE)).toEqual({ x: 1, y: 1 });
  });

  it('refuses a click outside the picture rather than clamping it to an edge nobody pointed at', () => {
    expect(fromMapCoordinate([-1, 500], SIZE)).toBeNull();
    expect(fromMapCoordinate([4001, 500], SIZE)).toBeNull();
    expect(fromMapCoordinate([1000, -1], SIZE)).toBeNull();
    expect(fromMapCoordinate([1000, 1001], SIZE)).toBeNull();
    // The positive twin: the same coordinates one pixel inside are places on the picture.
    expect(fromMapCoordinate([0, 500], SIZE)).not.toBeNull();
    expect(fromMapCoordinate([4000, 500], SIZE)).not.toBeNull();
  });
});
