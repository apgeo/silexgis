// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { WorkArea } from '../api/hooks.ts';
import { childrenOf, colourFor, extentOf, extentOfAll, pathTo, topLevel, workAreaPalette } from './tree.ts';

function area(id: string, parentId: string | null = null, geometry: WorkArea['geometry'] = null): WorkArea {
  return { id, name: id, description: null, parentId, childCount: 0, geometry };
}

const square = (west: number, south: number, size: number): WorkArea['geometry'] => ({
  type: 'Polygon',
  coordinates: [[
    [west, south], [west + size, south], [west + size, south + size], [west, south + size], [west, south],
  ]],
} as WorkArea['geometry']);

describe('levelling work areas', () => {
  it('puts an area with no parent at the top', () => {
    expect(topLevel([area('a'), area('b', 'a')]).map((x) => x.id)).toEqual(['a']);
  });

  it('puts an area whose parent it cannot see at the top rather than nowhere', () => {
    // The server states a parent only when the reader may read that parent too. Treated as a
    // missing level instead, this area would be absent from every level and so from the whole
    // overview — present to the server and invisible to the person it was answered to.
    expect(topLevel([area('b', 'hidden-massif')]).map((x) => x.id)).toEqual(['b']);
  });

  it('offers the level directly inside one area', () => {
    const areas = [area('a'), area('b', 'a'), area('c', 'a'), area('d', 'b')];
    expect(childrenOf(areas, 'a').map((x) => x.id)).toEqual(['b', 'c']);
    expect(childrenOf(areas, 'b').map((x) => x.id)).toEqual(['d']);
  });

  it('reads the chain down to an area, itself last', () => {
    const areas = [area('a'), area('b', 'a'), area('c', 'b')];
    expect(pathTo(areas, 'c').map((x) => x.id)).toEqual(['a', 'b', 'c']);
  });

  it('stops on a cycle instead of walking one for ever', () => {
    // The write service refuses cycles and the verifier re-checks them, so this should never
    // arrive. It is guarded because the alternative failure is a page that never paints.
    const areas = [area('a', 'b'), area('b', 'a')];
    expect(pathTo(areas, 'a').length).toBeLessThanOrEqual(2);
  });
});

describe('framing work areas', () => {
  it('bounds a polygon', () => {
    expect(extentOf(square(25, 45, 1))).toEqual([25, 45, 26, 46]);
  });

  it('bounds a multipolygon by walking the nesting rather than switching on the type', () => {
    // The server accepts multipolygons for this kind, and the recursion has to cover them without
    // a case somebody has to remember to add.
    const multi = {
      type: 'MultiPolygon',
      coordinates: [
        [[[25, 45], [26, 45], [26, 46], [25, 46], [25, 45]]],
        [[[30, 50], [31, 50], [31, 51], [30, 51], [30, 50]]],
      ],
    } as WorkArea['geometry'];
    expect(extentOf(multi)).toEqual([25, 45, 31, 51]);
  });

  it('bounds nothing for an area nobody has outlined yet', () => {
    expect(extentOf(null)).toBeNull();
  });

  it('frames a whole level, skipping the areas with no shape', () => {
    const areas = [area('a', null, square(25, 45, 1)), area('b'), area('c', null, square(30, 50, 1))];
    expect(extentOfAll(areas)).toEqual([25, 45, 31, 51]);
  });

  it('frames nothing when no area on the level has been outlined', () => {
    expect(extentOfAll([area('a'), area('b')])).toBeNull();
  });
});

describe('telling work areas apart', () => {
  it('gives consecutive areas different colours', () => {
    expect(colourFor(0)).not.toBe(colourFor(1));
  });

  it('wraps rather than running out', () => {
    expect(colourFor(workAreaPalette.length)).toBe(colourFor(0));
  });
});
