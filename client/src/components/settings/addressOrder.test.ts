// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { nextSortOrder } from './addressOrder.ts';

describe('nextSortOrder', () => {
  it('places the first address first', () => {
    expect(nextSortOrder([])).toBe(0);
  });

  it('places a new address after the last one, also when an earlier one was deleted', () => {
    // 0, 1, 2 with the first deleted: a count would answer 2 and collide with the last.
    expect(nextSortOrder([{ sortOrder: 1 }, { sortOrder: 2 }])).toBe(3);
    expect(nextSortOrder([{ sortOrder: 0 }, { sortOrder: 1 }])).toBe(2);
  });
});
