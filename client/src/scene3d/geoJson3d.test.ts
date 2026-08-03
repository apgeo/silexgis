// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  geoJsonBounds,
  lineStrings,
  pointPositions,
  propertiesOf,
  stringProperty,
  toPosition,
} from './geoJson3d.ts';

describe('toPosition', () => {
  it('reads a surveyed altitude as the height it is', () => {
    expect(toPosition([25.44, 45.53, 700])).toEqual({
      longitude: 25.44,
      latitude: 45.53,
      height: 700,
    });
  });

  it('places a position with no altitude on the surface', () => {
    expect(toPosition([25.44, 45.53])).toEqual({ longitude: 25.44, latitude: 45.53, height: 0 });
  });

  it('reads a negative altitude as one, rather than as missing', () => {
    expect(toPosition([25.44, 45.53, -120])!.height).toBe(-120);
  });

  it('refuses anything that is not a position', () => {
    expect(toPosition(null)).toBeUndefined();
    expect(toPosition([25.44])).toBeUndefined();
    expect(toPosition(['25.44', '45.53'])).toBeUndefined();
    expect(toPosition([Number.NaN, 45.53])).toBeUndefined();
  });
});

describe('lineStrings', () => {
  it('reads each ring of every part of a multi-part area', () => {
    const lines = lineStrings({
      geometry: {
        type: 'MultiPolygon',
        coordinates: [
          [
            [
              [25, 45],
              [25.1, 45],
              [25.1, 45.1],
              [25, 45],
            ],
          ],
          [
            [
              [26, 46],
              [26.1, 46],
              [26, 46],
            ],
          ],
        ],
      },
    });

    expect(lines.map((line) => line.length)).toEqual([4, 3]);
  });

  it('drops a component with nowhere to go, which a survey export can genuinely contain', () => {
    const lines = lineStrings({
      geometry: { type: 'MultiLineString', coordinates: [[[25, 45]], [[25, 45], [25.1, 45]]] },
    });

    expect(lines).toHaveLength(1);
  });

  it('reads nothing from a row served without geometry', () => {
    expect(lineStrings({ geometry: null })).toEqual([]);
    expect(lineStrings({})).toEqual([]);
  });

  it('reads nothing from a geometry it has no drawing for', () => {
    expect(lineStrings({ geometry: { type: 'GeometryCollection', coordinates: [] } })).toEqual([]);
  });
});

describe('pointPositions', () => {
  it('reads one point, or every point of a multi-part one', () => {
    expect(pointPositions({ geometry: { type: 'Point', coordinates: [25, 45] } })).toHaveLength(1);
    expect(
      pointPositions({ geometry: { type: 'MultiPoint', coordinates: [[25, 45], [26, 46]] } }),
    ).toHaveLength(2);
    expect(pointPositions({ geometry: { type: 'LineString', coordinates: [] } })).toEqual([]);
  });
});

describe('geoJsonBounds', () => {
  it('covers every position of a shape, whichever shape it is', () => {
    expect(
      geoJsonBounds({
        type: 'Polygon',
        coordinates: [
          [
            [25.42, 45.51],
            [25.46, 45.51],
            [25.46, 45.54],
            [25.42, 45.51],
          ],
        ],
      }),
    ).toEqual([25.42, 45.51, 25.46, 45.54]);
    expect(geoJsonBounds({ type: 'Point', coordinates: [25.3, 45.7, 900] })).toEqual([
      25.3, 45.7, 25.3, 45.7,
    ]);
    expect(
      geoJsonBounds({ type: 'MultiPoint', coordinates: [[25, 45], [26, 44]] }),
    ).toEqual([25, 44, 26, 45]);
  });

  it('reports nothing for a feature there is nothing to frame', () => {
    // A protected feature the viewer may not locate exactly arrives with no geometry at all, and
    // a camera cannot be pointed at it.
    expect(geoJsonBounds(null)).toBeUndefined();
    expect(geoJsonBounds(undefined)).toBeUndefined();
    expect(geoJsonBounds({ type: 'GeometryCollection', geometries: [] })).toBeUndefined();
  });
});

describe('properties', () => {
  it('reads an absent or unusable properties bag as empty', () => {
    expect(propertiesOf({})).toEqual({});
    expect(propertiesOf({ properties: null })).toEqual({});
    expect(propertiesOf({ properties: 'nonsense' })).toEqual({});
  });

  it('treats an empty or non-string identifier as no identifier at all', () => {
    expect(stringProperty({ id: 'abc' }, 'id')).toBe('abc');
    expect(stringProperty({ id: '' }, 'id')).toBeUndefined();
    expect(stringProperty({ id: 42 }, 'id')).toBeUndefined();
    expect(stringProperty({}, 'id')).toBeUndefined();
  });
});
