// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { placementForDrop, placementForStep, reorder } from './albumOrdering.ts';

const order = ['a', 'b', 'c', 'd'];

describe('placementForDrop', () => {
  it('puts a picture dragged rightward where the target was', () => {
    // The naive reading — "insert before the target" — would land it at index 1 and look like
    // the drag missed by one.
    const placement = placementForDrop(order, 'a', 'c');

    expect(placement).toEqual({ afterId: 'c' });
    expect(reorder(order, 'a', placement!)).toEqual(['b', 'c', 'a', 'd']);
  });

  it('puts a picture dragged leftward where the target was', () => {
    const placement = placementForDrop(order, 'c', 'a');

    // Nothing to sit after: it becomes the first picture, which is what the album's cover
    // candidate and its share link both open on.
    expect(placement).toEqual({ afterId: null });
    expect(reorder(order, 'c', placement!)).toEqual(['c', 'a', 'b', 'd']);
  });

  it('swaps neighbours the same way whichever one is dragged', () => {
    expect(reorder(order, 'b', placementForDrop(order, 'b', 'c')!)).toEqual(['a', 'c', 'b', 'd']);
    expect(reorder(order, 'c', placementForDrop(order, 'c', 'b')!)).toEqual(['a', 'c', 'b', 'd']);
  });

  it('is nothing at all when a picture is dropped on itself', () => {
    // A click that moved a few pixels is a drop on self, and it must not spend a request saying
    // so — nor briefly redraw the grid.
    expect(placementForDrop(order, 'b', 'b')).toBeNull();
  });

  it('is nothing when either picture has left the album', () => {
    expect(placementForDrop(order, 'z', 'b')).toBeNull();
    expect(placementForDrop(order, 'b', 'z')).toBeNull();
  });
});

describe('placementForStep', () => {
  it('moves one place left', () => {
    expect(reorder(order, 'c', placementForStep(order, 'c', -1)!)).toEqual(['a', 'c', 'b', 'd']);
  });

  it('moves one place right', () => {
    expect(reorder(order, 'b', placementForStep(order, 'b', 1)!)).toEqual(['a', 'c', 'b', 'd']);
  });

  it('moves the second picture to the front', () => {
    expect(reorder(order, 'b', placementForStep(order, 'b', -1)!)).toEqual(['b', 'a', 'c', 'd']);
  });

  it('refuses to step off either end', () => {
    // The control is disabled at the ends, so this is the belt to that braces — but a step that
    // wrapped would silently move the first picture to last on a stray keypress.
    expect(placementForStep(order, 'a', -1)).toBeNull();
    expect(placementForStep(order, 'd', 1)).toBeNull();
  });
});

describe('reorder', () => {
  it('puts a picture first when nothing is named', () => {
    expect(reorder(order, 'd', { afterId: null })).toEqual(['d', 'a', 'b', 'c']);
  });

  it('puts a picture last', () => {
    expect(reorder(order, 'a', { afterId: 'd' })).toEqual(['b', 'c', 'd', 'a']);
  });

  it('keeps every picture exactly once', () => {
    // The redraw is what the grid shows until the server answers, so a placement that dropped or
    // doubled a picture would show one for as long as the request takes.
    expect([...reorder(order, 'b', { afterId: 'c' })].sort()).toEqual(order);
  });
});
