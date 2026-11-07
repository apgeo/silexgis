// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  cornersOfPlacement,
  cornersToPlacement,
  cornerTransformationOf,
  formatMetres,
  initialCorners,
  isUsableQuadrilateral,
  minimumPoints,
  pairsToPlacement,
  previewImageOptions,
  sheetAspect,
  type Corners,
} from './placement.ts';

const square: Corners = {
  nw: [24.0, 45.502],
  ne: [24.004, 45.502],
  se: [24.004, 45.5],
  sw: [24.0, 45.5],
};

describe('the first corners of an unplaced sheet', () => {
  it('sits in the middle of the view, 60 % of its width, in the picture’s proportions', () => {
    const corners = initialCorners([0, 0, 1000, 1000], 2);
    expect(corners.nw).toEqual([200, 650]);
    expect(corners.ne).toEqual([800, 650]);
    expect(corners.se).toEqual([800, 350]);
    expect(corners.sw).toEqual([200, 350]);
  });

  it('shrinks a tall sheet to stay inside the view', () => {
    const corners = initialCorners([0, 0, 1000, 500], 0.5);
    const height = corners.nw[1] - corners.sw[1];
    const width = corners.ne[0] - corners.nw[0];
    expect(height).toBeCloseTo(400);
    expect(width).toBeCloseTo(200);
    expect((corners.nw[0] + corners.ne[0]) / 2).toBeCloseTo(500);
  });

  it('takes a sheet whose size is unknown for a square one', () => {
    expect(sheetAspect(null, 100)).toBe(1);
    expect(sheetAspect(2400, 1680)).toBeCloseTo(2400 / 1680);
  });
});

describe('corners as a placement', () => {
  it('sends the four picture corners as fractions, in WGS 84, with the chosen transformation', () => {
    const body = cornersToPlacement(square, 'homography');
    expect(body.version).toBe(1);
    expect(body.crs).toBe('EPSG:4326');
    expect(body.transformation).toBe('homography');
    expect(body.mask).toBeNull();
    expect(body.gcps.map(({ u, v }) => [u, v])).toEqual([
      [0, 0],
      [1, 0],
      [1, 1],
      [0, 1],
    ]);
    expect(body.gcps[2]).toMatchObject({ x: 24.004, y: 45.5 });
    expect(cornersToPlacement(square, 'affine').transformation).toBe('affine');
  });

  it('reads back the corners it wrote, whatever order the points come in', () => {
    const body = cornersToPlacement(square, 'affine');
    body.gcps.reverse();
    expect(cornersOfPlacement(body)).toEqual(square);
    expect(cornerTransformationOf(body)).toBe('affine');
  });

  it('does not read corners into a placement made of other points', () => {
    const body = cornersToPlacement(square, 'homography');
    body.gcps[0] = { ...body.gcps[0], u: 0.5 };
    expect(cornersOfPlacement(body)).toBeNull();
    expect(cornersOfPlacement({ ...cornersToPlacement(square, 'homography'), crs: 'EPSG:3844' })).toBeNull();
    expect(cornersOfPlacement(null)).toBeNull();
    expect(cornerTransformationOf(null)).toBe('homography');
  });

  it('takes a convex quadrilateral and refuses a folded or flattened one', () => {
    expect(isUsableQuadrilateral(square)).toBe(true);
    expect(isUsableQuadrilateral({ ...square, ne: square.se, se: square.ne })).toBe(false);
    expect(isUsableQuadrilateral({ ...square, ne: [24.002, 45.502], nw: [24.002, 45.502] })).toBe(false);
    expect(isUsableQuadrilateral({ ...square, sw: [Number.NaN, 45.5] })).toBe(false);
  });
});

describe('matched point pairs as a placement', () => {
  it('waits for two complete pairs before it makes a similarity', () => {
    const one = [{ u: 0.1, v: 0.2, x: 24.0, y: 45.5 }];
    expect(pairsToPlacement(one)).toBeNull();
    expect(pairsToPlacement([...one, { u: 0.9, v: 0.8, x: null, y: null }])).toBeNull();
    const body = pairsToPlacement([...one, { u: 0.9, v: 0.8, x: 24.004, y: 45.501 }]);
    expect(body).toMatchObject({ version: 1, transformation: 'similarity', crs: 'EPSG:4326' });
    expect(body?.gcps).toEqual([
      { u: 0.1, v: 0.2, x: 24.0, y: 45.5, label: '1' },
      { u: 0.9, v: 0.8, x: 24.004, y: 45.501, label: '2' },
    ]);
  });

  it('knows how many points each transformation needs', () => {
    expect(minimumPoints('similarity')).toBe(2);
    expect(minimumPoints('affine')).toBe(3);
    expect(minimumPoints('homography')).toBe(4);
  });
});

describe('the preview answer', () => {
  it('becomes a static picture in the map projection', () => {
    expect(previewImageOptions({ image: 'data:image/png;base64,AA', extent3857: [1, 2, 3, 4] })).toEqual({
      url: 'data:image/png;base64,AA',
      imageExtent: [1, 2, 3, 4],
      projection: 'EPSG:3857',
    });
    expect(previewImageOptions({ image: 'x', extent3857: [3, 2, 1, 4] })).toBeNull();
    expect(previewImageOptions({ image: 'x', extent3857: [1, 2] })).toBeNull();
  });

  it('prints a close fit finely and a poor one in whole metres', () => {
    expect(formatMetres(0.031)).toBe('0.03');
    expect(formatMetres(12.34)).toBe('12.3');
    expect(formatMetres(1234.5)).toBe('1235');
  });
});
