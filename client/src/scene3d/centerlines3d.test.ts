// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { centerlinePalette } from '../map/markerPalette.ts';
import { centerlineLoadState, centerlinePolylines } from './centerlines3d.ts';

/** A response shaped exactly like the centerline overlay's, altitudes included. */
function collection(features: unknown[], extras: Record<string, unknown> = {}) {
  return {
    type: 'FeatureCollection',
    features,
    withheldCount: 0,
    detail: true,
    flatCount: 0,
    ...extras,
  };
}

function feature(geometry: unknown, properties: Record<string, unknown> = {}) {
  return {
    type: 'Feature',
    geometry,
    properties: {
      id: 'line-1',
      caveId: 'cave-1',
      name: 'Main survey',
      lengthM: 1234.5,
      paths: 2,
      detail: true,
      hasZ: true,
      ...properties,
    },
  };
}

describe('centerlinePolylines', () => {
  it('draws every component of a survey as its own line, hanging beneath the surface', () => {
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'MultiLineString',
            coordinates: [
              [
                [25.44, 45.53, 700],
                [25.441, 45.53, 690],
              ],
              [
                [25.441, 45.53, 690],
                [25.442, 45.531, 660],
              ],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );

    expect(polylines).toHaveLength(2);
    // Surveyed altitudes are heights above a sea-level datum; the globe has no hillside on it for
    // the cave to be inside, so the cave's top is put on the surface and the rest hangs below it
    // at the depths a caver reads.
    expect(polylines[0].positions).toEqual([
      { longitude: 25.44, latitude: 45.53, height: 0 },
      { longitude: 25.441, latitude: 45.53, height: -10 },
    ]);
    expect(polylines[1].positions.map((p) => p.height)).toEqual([-10, -40]);
  });

  it('keeps a cave at the same depths as the viewer pans across it', () => {
    // At close zooms the server cuts the survey to the viewport, so the deepest — and the
    // highest — point in the payload changes with every pan. The response reports the top of the
    // whole cave for exactly that reason: anchoring to what happened to arrive would slide the
    // survey up and down as the viewer moved, and the cave would appear to breathe.
    const wholeCave = centerlinePolylines(
      collection([
        feature(
          {
            type: 'MultiLineString',
            coordinates: [
              [
                [25.44, 45.53, 700],
                [25.441, 45.53, 640],
              ],
              [
                [25.441, 45.53, 640],
                [25.442, 45.531, 600],
              ],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );
    // The same cave with only its lower half in view: nothing in the payload reaches 700 m.
    const lowerHalf = centerlinePolylines(
      collection([
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.441, 45.53, 640],
              [25.442, 45.531, 600],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );

    expect(wholeCave[1].positions.map((p) => p.height)).toEqual([-60, -100]);
    expect(lowerHalf[0].positions.map((p) => p.height)).toEqual([-60, -100]);
  });

  it('anchors to the highest point it was sent when the response reports no top', () => {
    // Less stable than the reported figure, and deliberately so: it still puts the cave on the
    // surface the viewer is looking at rather than a kilometre over their head.
    const polylines = centerlinePolylines(
      collection([
        feature({
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 690],
            [25.441, 45.53, 660],
          ],
        }),
      ]),
    );

    expect(polylines[0].positions.map((p) => p.height)).toEqual([0, -30]);
  });

  it('anchors a cave surveyed below sea level by its own top, not by sea level', () => {
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, -20],
              [25.441, 45.53, -45],
            ],
          },
          { topAltitudeM: -20 },
        ),
      ]),
    );

    expect(polylines[0].positions.map((p) => p.height)).toEqual([0, -25]);
  });

  it('handles the single-component shape a clipped survey comes back as', () => {
    // Clipping a cave to the viewport can leave one component, and the database answers with a
    // plain line rather than a collection of one. A loader that only knew the plural form would
    // silently drop those caves at the edge of the view.
    const polylines = centerlinePolylines(
      collection([
        feature({
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 700],
            [25.441, 45.53, 690],
          ],
        }),
      ]),
    );

    expect(polylines).toHaveLength(1);
    expect(polylines[0].positions).toHaveLength(2);
  });

  it('places a row that could only be served flat on the surface rather than dropping it', () => {
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'MultiLineString',
            coordinates: [
              [
                [25.44, 45.53],
                [25.441, 45.53],
              ],
            ],
          },
          { hasZ: false, detail: false },
        ),
      ]),
    );

    expect(polylines).toHaveLength(1);
    expect(polylines[0].positions.map((p) => p.height)).toEqual([0, 0]);
  });

  it('reads depths and flat rows out of one response, because one response can carry both', () => {
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, 700],
              [25.441, 45.53, 690],
            ],
          },
          { id: 'line-1', caveId: 'cave-1', topAltitudeM: 700 },
        ),
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.5, 45.6],
              [25.501, 45.6],
            ],
          },
          { id: 'line-2', caveId: 'cave-2', hasZ: false },
        ),
      ]),
    );

    // Both start on the surface: the surveyed one because that is where its top is, the flat one
    // because a row without altitudes has nothing to hang below it.
    expect(polylines.map((p) => p.positions[0].height)).toEqual([0, 0]);
    expect(polylines[0].positions[1].height).toBe(-10);
    expect(polylines[1].positions[1].height).toBe(0);
  });

  it('gives every component of one cave the same payload object, by reference', () => {
    // What the renderer hands back on a click is the object it was given, not a copy, so one
    // object per cave means a click anywhere on a survey answers with that cave and no lookup
    // table has to be kept alongside the geometry.
    const polylines = centerlinePolylines(
      collection([
        feature({
          type: 'MultiLineString',
          coordinates: [
            [
              [25.44, 45.53, 700],
              [25.441, 45.53, 690],
            ],
            [
              [25.45, 45.54, 640],
              [25.451, 45.54, 630],
            ],
          ],
        }),
      ]),
    );

    expect(polylines[0].id).toBe(polylines[1].id);
    expect(polylines[0].id).toEqual({
      kind: 'centerline',
      caveId: 'cave-1',
      centerlineId: 'line-1',
    });
  });

  it('strokes in the same colour the flat map uses, once — no casing pass', () => {
    const polylines = centerlinePolylines(
      collection([
        feature({
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 700],
            [25.441, 45.53, 690],
          ],
        }),
      ]),
    );

    expect(polylines).toHaveLength(1);
    expect(polylines[0].color).toBe(centerlinePalette.line);
    expect(polylines[0].widthPixels).toBe(2);
  });

  it('drops what cannot be drawn or selected instead of drawing something unclickable', () => {
    const polylines = centerlinePolylines(
      collection([
        // A single-position component: a shot whose two stations coincide.
        feature({ type: 'LineString', coordinates: [[25.44, 45.53, 700]] }),
        // No cave to select if it were clicked.
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, 700],
              [25.441, 45.53, 690],
            ],
          },
          { caveId: null },
        ),
        // Nothing to draw at all.
        feature(null),
      ]),
    );

    expect(polylines).toEqual([]);
  });

  it('is unbothered by a response that is not one', () => {
    expect(centerlinePolylines(undefined)).toEqual([]);
    expect(centerlinePolylines({})).toEqual([]);
  });
});

describe('centerlineLoadState', () => {
  it('carries the counters the response reports', () => {
    expect(
      centerlineLoadState(collection([], { withheldCount: 3, detail: true, flatCount: 2 })),
    ).toEqual({ withheldCount: 3, detail: true, flatCount: 2 });
  });

  it('reads absent counters as nothing withheld and nothing flat', () => {
    expect(centerlineLoadState({})).toEqual({ withheldCount: 0, detail: false, flatCount: 0 });
  });
});
