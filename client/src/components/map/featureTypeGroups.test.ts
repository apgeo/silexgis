// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { FeatureType } from '../../api/hooks.ts';
import { groupFeatureTypes } from './featureTypeGroups.ts';

function makeType(id: number, geometryKind: string | null): FeatureType {
  return { id, name: `t${id}`, geometryKind } as FeatureType;
}

describe('groupFeatureTypes', () => {
  it('groups by geometry kind in palette order and drops empty groups', () => {
    const groups = groupFeatureTypes([
      makeType(1, 'polygon'),
      makeType(2, 'point'),
      makeType(3, 'any'),
      makeType(4, 'point'),
    ]);

    expect(groups.map((g) => g.kind)).toEqual(['point', 'polygon', 'any']); // no 'line'
    expect(groups[0].items.map((t) => t.id)).toEqual([2, 4]);
  });

  it('treats a missing geometry kind as point', () => {
    const groups = groupFeatureTypes([makeType(1, null)]);
    expect(groups).toHaveLength(1);
    expect(groups[0].kind).toBe('point');
  });
});
