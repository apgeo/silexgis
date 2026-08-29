// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { FeatureType } from '../../api/hooks.ts';
import {
  drawShapeForType,
  drawShapesForType,
  geometryGroupOf,
  groupFeatureTypes,
} from './featureTypeGroups.ts';

function makeType(id: number, acceptedGeometryClasses: string[]): FeatureType {
  return { id, name: `t${id}`, acceptedGeometryClasses } as FeatureType;
}

describe('groupFeatureTypes', () => {
  it('groups by drawable geometry family in palette order and drops empty groups', () => {
    const groups = groupFeatureTypes([
      makeType(1, ['polygon']),
      makeType(2, ['point']),
      makeType(3, ['point', 'lineString', 'polygon']),
      makeType(4, ['point', 'multiPoint']),
    ]);

    expect(groups.map((g) => g.kind)).toEqual(['point', 'polygon', 'any']); // no 'line'
    expect(groups[0].items.map((t) => t.id)).toEqual([2, 4]);
  });

  it('treats missing geometry classes as point', () => {
    const groups = groupFeatureTypes([makeType(1, [])]);
    expect(groups).toHaveLength(1);
    expect(groups[0].kind).toBe('point');
  });

  it('collapses Multi* classes into their base family', () => {
    expect(geometryGroupOf(makeType(1, ['lineString', 'multiLineString']))).toBe('line');
    expect(geometryGroupOf(makeType(2, ['multiPolygon']))).toBe('polygon');
  });
});

describe('drawShapeForType', () => {
  it('arms the single-family shape and leaves mixed types to the caller', () => {
    expect(drawShapeForType(makeType(1, ['point']))).toBe('Point');
    expect(drawShapeForType(makeType(2, ['multiLineString']))).toBe('LineString');
    expect(drawShapeForType(makeType(3, ['polygon', 'multiPolygon']))).toBe('Polygon');
    expect(drawShapeForType(makeType(4, ['point', 'polygon']))).toBeNull();
    expect(drawShapeForType(undefined)).toBe('Point');
  });
});

describe('drawShapesForType', () => {
  it('offers one shape for a kind that accepts one family, so nothing is asked', () => {
    expect(drawShapesForType(makeType(1, ['point', 'multiPoint']))).toEqual(['Point']);
    expect(drawShapesForType(makeType(2, ['polygon', 'multiPolygon']))).toEqual(['Polygon']);
  });

  it('offers every family a widened kind accepts, in palette order', () => {
    // The doline case: a marker on a small depression and a drawn outline on a large one. Both
    // have to be offered or the shape the kind was widened for can never be drawn.
    expect(drawShapesForType(makeType(3, ['point', 'multiPoint', 'polygon', 'multiPolygon'])))
      .toEqual(['Point', 'Polygon']);
    expect(drawShapesForType(makeType(4, ['polygon', 'lineString', 'point'])))
      .toEqual(['Point', 'LineString', 'Polygon']);
  });

  it('falls back to a marker for a kind that names no geometry at all', () => {
    expect(drawShapesForType(makeType(5, []))).toEqual(['Point']);
    expect(drawShapesForType(undefined)).toEqual(['Point']);
  });
});
