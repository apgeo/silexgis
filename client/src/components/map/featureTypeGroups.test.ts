// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { FeatureType } from '../../api/hooks.ts';
import {
  drawShapeForType,
  drawShapesForType,
  geometryGroupOf,
  groupFeatureTypes,
  paletteGroupOf,
} from './featureTypeGroups.ts';

function makeType(id: number, acceptedGeometryClasses: string[]): FeatureType {
  return { id, name: `t${id}`, acceptedGeometryClasses } as FeatureType;
}

describe('groupFeatureTypes', () => {
  it('groups by drawable geometry family in palette order and drops empty groups', () => {
    const groups = groupFeatureTypes([
      makeType(1, ['polygon']),
      makeType(2, ['point']),
      makeType(3, ['lineString', 'polygon']),
      makeType(4, ['point', 'multiPoint']),
    ]);

    expect(groups.map((g) => g.kind)).toEqual(['point', 'line', 'polygon']);
    expect(groups[0].items.map((t) => t.id)).toEqual([2, 4]);
    // The mixed kind is filed under the first family it accepts, not moved out of the palette.
    expect(groups[1].items.map((t) => t.id)).toEqual([3]);
  });

  it('keeps a widened kind in the group readers already look for it in', () => {
    // The doline case. Accepting outlines as well as markers is an addition, and an addition must
    // not take the symbol out of "Points" and put it at the bottom of the palette under "Other" —
    // which is what grouping by "has more than one family" did.
    const doline = makeType(1, ['point', 'multiPoint', 'polygon', 'multiPolygon']);
    expect(paletteGroupOf(doline)).toBe('point');

    const groups = groupFeatureTypes([doline, makeType(2, ['polygon'])]);
    expect(groups.map((g) => g.kind)).toEqual(['point', 'polygon']);
    expect(groups[0].items.map((t) => t.id)).toEqual([1]);

    // And the draw tool still knows it has a choice to offer, which is the other question and
    // must not have been answered by the grouping.
    expect(geometryGroupOf(doline)).toBe('any');
    expect(drawShapesForType(doline)).toEqual(['Point', 'Polygon']);
  });

  it('files a kind that names no drawable geometry under markers', () => {
    expect(paletteGroupOf(makeType(1, []))).toBe('point');
    expect(paletteGroupOf(makeType(2, ['geometryCollection']))).toBe('point');
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
