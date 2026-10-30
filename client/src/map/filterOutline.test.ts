// SPDX-License-Identifier: AGPL-3.0-or-later
import { fromLonLat } from 'ol/proj';
import { describe, expect, it } from 'vitest';
import { MAX_POLYGON_POSITIONS, parsePolygon, polygonText } from '../filters/spatial.ts';
import { ringsOfDrawn, viewOutline } from './filterOutline.ts';

describe('viewOutline', () => {
  it('is the box on screen as one closed ring that the editor reads back as a polygon', () => {
    const rings = viewOutline([24, 45, 25, 46])!;
    expect(rings).toEqual([[[24, 45], [25, 45], [25, 46], [24, 46], [24, 45]]]);
    expect(parsePolygon(polygonText(rings))).toEqual({ coordinates: rings });
  });

  it('keeps a view zoomed out past the world on the globe', () => {
    expect(viewOutline([-400, -95, 400, 95])).toEqual([[[-180, -90], [180, -90], [180, 90], [-180, 90], [-180, -90]]]);
  });

  it('is nothing for a box with no area or no box', () => {
    expect(viewOutline(undefined)).toBeNull();
    expect(viewOutline([24, 45, 24, 46])).toBeNull();
    expect(viewOutline([24, 46, 25, 45])).toBeNull();
    expect(viewOutline([Number.NaN, 45, 25, 46])).toBeNull();
  });
});

describe('ringsOfDrawn', () => {
  const ring = [[24, 45], [25, 45], [25, 46], [24, 45]].map(([lon, lat]) => fromLonLat([lon, lat]));

  it('turns the drawn ground coordinates into degrees', () => {
    const rings = ringsOfDrawn([ring])!;
    expect(rings[0]).toHaveLength(4);
    expect(rings[0][1][0]).toBeCloseTo(25, 5);
    expect(rings[0][1][1]).toBeCloseTo(45, 5);
    expect(parsePolygon(polygonText(rings))).toEqual({ coordinates: rings });
  });

  it('refuses a drawing with more positions than one outline may hold', () => {
    const big = Array.from({ length: MAX_POLYGON_POSITIONS + 1 }, () => ring[0]);
    expect(ringsOfDrawn([big])).toBeNull();
  });

  it('refuses a drawing with no ring', () => {
    expect(ringsOfDrawn([])).toBeNull();
  });
});
