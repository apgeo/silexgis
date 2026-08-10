// SPDX-License-Identifier: AGPL-3.0-or-later
import LineString from 'ol/geom/LineString';
import Point from 'ol/geom/Point';
import Polygon from 'ol/geom/Polygon';
import { fromLonLat } from 'ol/proj';
import { describe, expect, it } from 'vitest';
import { readTripGeometry, writeTripGeometry, type TripGeometry } from './tripGeometry.ts';

/** The API's shape, built loosely the way the generated client types it. */
const geometry = (type: string, coordinates: unknown): TripGeometry =>
  ({ type, coordinates } as unknown as TripGeometry);

describe('trip geometry conversion', () => {
  it('reads a stored point into map coordinates', () => {
    const read = readTripGeometry(geometry('Point', [25.6, 45.65]));

    expect(read).toBeInstanceOf(Point);
    const [x, y] = (read as Point).getCoordinates();
    const [expectedX, expectedY] = fromLonLat([25.6, 45.65]);
    expect(x).toBeCloseTo(expectedX, 3);
    expect(y).toBeCloseTo(expectedY, 3);
  });

  it('writes a drawn shape back as longitude and latitude', () => {
    const drawn = new LineString([fromLonLat([25.6, 45.65]), fromLonLat([25.7, 45.7])]);

    const written = writeTripGeometry(drawn);

    expect(written.type).toBe('LineString');
    const coordinates = written.coordinates as unknown as number[][];
    expect(coordinates[0][0]).toBeCloseTo(25.6, 5);
    expect(coordinates[0][1]).toBeCloseTo(45.65, 5);
    expect(coordinates[1][0]).toBeCloseTo(25.7, 5);
    expect(coordinates[1][1]).toBeCloseTo(45.7, 5);
  });

  it('round-trips an area without moving it', () => {
    const ring = [
      [25.6, 45.6],
      [25.7, 45.6],
      [25.7, 45.7],
      [25.6, 45.6],
    ];

    const written = writeTripGeometry(readTripGeometry(geometry('Polygon', [ring]))!);

    expect(written.type).toBe('Polygon');
    const rings = written.coordinates as unknown as number[][][];
    expect(rings[0]).toHaveLength(4);
    rings[0].forEach(([lon, lat], index) => {
      expect(lon).toBeCloseTo(ring[index][0], 5);
      expect(lat).toBeCloseTo(ring[index][1], 5);
    });
  });

  it('reads an unusable shape as no shape rather than throwing', () => {
    // A trip whose sketch cannot be parsed must still open its page.
    expect(readTripGeometry(null)).toBeNull();
    expect(readTripGeometry(geometry('Nonsense', [1, 2]))).toBeNull();
  });

  it('keeps the drawable shapes drawable', () => {
    // The three shapes the editor arms, each surviving the trip to the API and back.
    const shapes = [
      new Point(fromLonLat([25.6, 45.65])),
      new LineString([fromLonLat([25.6, 45.65]), fromLonLat([25.61, 45.66])]),
      new Polygon([
        [fromLonLat([25.6, 45.6]), fromLonLat([25.7, 45.6]), fromLonLat([25.7, 45.7]), fromLonLat([25.6, 45.6])],
      ]),
    ];

    for (const shape of shapes) {
      expect(readTripGeometry(writeTripGeometry(shape))?.getType()).toBe(shape.getType());
    }
  });
});
