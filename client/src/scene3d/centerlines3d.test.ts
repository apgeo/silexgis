// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { centerlinePalette } from '../map/markerPalette.ts';
import {
  caveCenterlines,
  CENTERLINE_DEPTH_BANDS,
  centerlineBounds,
  centerlineLoadState,
  centerlinePolylines,
  nearestCaveCenterlines,
} from './centerlines3d.ts';
import type { Scene3DPolyline } from './scene3dEngine.ts';

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

describe('where a survey is drawn once the ground has relief', () => {
  /** A cave whose top is at 700 m and which drops 100 m, plus the survey's own reported top. */
  const cave = () =>
    collection([
      feature(
        {
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 700],
            [25.441, 45.53, 640],
            [25.442, 45.531, 600],
          ],
        },
        { topAltitudeM: 700 },
      ),
    ]);

  it('puts the survey at the altitude it was surveyed at', () => {
    // With a hillside drawn, the honest place for a cave is inside it. Anchoring the top to the
    // ellipsoid — which is the only honest thing to do without relief — would bury the whole cave
    // eleven hundred metres under its own hillside.
    const polylines = centerlinePolylines(cave(), { absolute: true, offsetM: 0 });

    expect(polylines.flatMap((line) => line.positions.map((p) => p.height))).toContain(700);
    expect(polylines.at(-1)!.positions.at(-1)!.height).toBe(600);
  });

  it('raises the survey by the correction the terrain source needs, and by nothing else', () => {
    // A source whose tiles were converted to heights above the ellipsoid when they were baked
    // draws its ground where it really is, so a surveyed altitude — which is measured from sea
    // level — has to travel the geoid undulation before it will meet that ground.
    const polylines = centerlinePolylines(cave(), { absolute: true, offsetM: 43.03 });

    expect(polylines[0].positions[0].height).toBeCloseTo(743.03, 6);
    expect(polylines.at(-1)!.positions.at(-1)!.height).toBeCloseTo(643.03, 6);
  });

  it('colours the same passage the same way wherever the cave is drawn', () => {
    // The bands mean metres below the top of the cave and nothing else. Moving the cave from the
    // ellipsoid onto its hillside must not repaint it, or the same passage would change colour
    // when an elevation model finished loading.
    const anchored = centerlinePolylines(cave());
    const onTerrain = centerlinePolylines(cave(), { absolute: true, offsetM: 43.03 });

    expect(onTerrain.map((line) => line.color)).toEqual(anchored.map((line) => line.color));
    expect(onTerrain).toHaveLength(anchored.length);
    // Every drawn height moved by exactly the same amount: the shape of the cave is untouched.
    const anchoredHeights = anchored.flatMap((line) => line.positions.map((p) => p.height));
    const terrainHeights = onTerrain.flatMap((line) => line.positions.map((p) => p.height));
    expect(terrainHeights.map((h, i) => h - anchoredHeights[i] - 743.03)).toEqual(
      terrainHeights.map(() => 0),
    );
  });

  it('moves the chrome anchor with the cave it is pinned to', () => {
    // The anchor is the survey's highest point, which is where a label about the cave stands. Left
    // behind at the ellipsoid it would name a cave a kilometre above it.
    const [line] = centerlinePolylines(cave(), { absolute: true, offsetM: 43.03 });
    const anchor = (line.id as { anchor?: { height: number } }).anchor;

    expect(anchor?.height).toBeCloseTo(743.03, 6);
  });

  it('hangs the cave from the surface when nothing says the ground has relief', () => {
    // The shipped state, and the one to fall back to: a cave placed at its real altitude over a
    // globe with no hillside on it would float a kilometre above the surface, above the entrance
    // markers standing on it and above a camera flown down to the ground.
    expect(centerlinePolylines(cave())[0].positions[0].height).toBe(0);
  });

  /** The compact representation served for a whole region at once: a plan, and no depths at all. */
  const flatCave = () =>
    collection([
      feature(
        {
          type: 'LineString',
          coordinates: [
            [22.7, 46.5],
            [22.701, 46.5],
            [22.702, 46.501],
          ],
        },
        { hasZ: false, detail: false },
      ),
    ]);

  it('lays a survey that arrived without depths on the ground instead of at sea level', () => {
    // The common case rather than the exception: at any ordinary browsing zoom the server sends
    // most caves as a flat plan. Its coordinates arrive as pairs, which read as height zero — and
    // treating that zero as a surveyed altitude while the ground is a real hillside draws the
    // whole survey an entire hillside below its own entrance markers, which are on the surface.
    const polylines = centerlinePolylines(flatCave(), { absolute: true, offsetM: 0 });

    expect(polylines).toHaveLength(1);
    expect(polylines[0].clampToGround).toBe(true);
    expect(polylines[0].positions.map((p) => p.height)).toEqual([0, 0, 0]);
  });

  it('does not lift a row without depths by the terrain correction either', () => {
    // The correction exists to carry a SURVEYED altitude onto an ellipsoidal model's ground. There
    // is no surveyed altitude here, so applying it would only place the plan forty metres over the
    // hillside instead of on it.
    const [line] = centerlinePolylines(flatCave(), { absolute: true, offsetM: 43.03 });

    expect(line.positions.map((p) => p.height)).toEqual([0, 0, 0]);
    expect(line.clampToGround).toBe(true);
  });

  it('pins the chrome for a row without depths to the ground rather than to the ellipsoid', () => {
    const [line] = centerlinePolylines(flatCave(), { absolute: true, offsetM: 43.03 });
    const anchor = (line.id as { anchor?: { height: number; onGround?: boolean } }).anchor;

    expect(anchor).toMatchObject({ height: 0, onGround: true });
  });

  it('draws a row without depths in the first band, whatever its coordinates say', () => {
    // Nothing here is at a depth, so nothing here can be coloured by one.
    const polylines = centerlinePolylines(flatCave(), { absolute: true, offsetM: 0 });

    expect(polylines.map((line) => line.color)).toEqual([CENTERLINE_DEPTH_BANDS[0].color]);
  });

  it('keeps a row that does carry depths off the ground, so it is drawn where it was surveyed', () => {
    const [line] = centerlinePolylines(cave(), { absolute: true, offsetM: 0 });

    expect(line.clampToGround).toBeUndefined();
  });
});

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

    // The last piece drawn is the deepest one either way; the whole-cave payload also carries the
    // shallower half, which the depth colouring cuts into more pieces than the clipped payload has.
    expect(wholeCave.at(-1)!.positions.map((p) => p.height)).toEqual([-60, -100]);
    expect(lowerHalf.at(-1)!.positions.map((p) => p.height)).toEqual([-60, -100]);
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
    expect(polylines[0].id).toMatchObject({
      kind: 'centerline',
      caveId: 'cave-1',
      centerlineId: 'line-1',
    });
  });

  it('stands the survey it names at its highest point, on the geometry as it is drawn', () => {
    // Chrome about a cave has to be pinned somewhere, and the top is the one place that is on the
    // survey, is the least buried part of it, and does not move as the viewer pans and the server
    // sends a different slice of a long cave. It is stated against the same anchoring the lines
    // are drawn with, so it sits on a line rather than a kilometre above one.
    const polylines = centerlinePolylines(
      collection([
        feature({
          type: 'MultiLineString',
          coordinates: [
            [
              [25.44, 45.53, 640],
              [25.441, 45.53, 620],
            ],
            [
              [25.45, 45.54, 700],
              [25.451, 45.54, 660],
            ],
          ],
        }),
      ]),
    );

    expect(polylines[0].id).toMatchObject({
      anchor: { longitude: 25.45, latitude: 45.54, height: 0 },
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

describe('showing depth in the colour of the line', () => {
  it('darkens a passage through the bands as it descends', () => {
    // The survey is drawn over the ground rather than behind it, which is what keeps a whole cave
    // legible from every angle — and means nothing about where a line sits on screen says how far
    // down it is. Seen from straight above, a passage four hundred metres down and one ten metres
    // down are the same ink in the same place unless the colour says otherwise.
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, 700],
              [25.48, 45.53, 300],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );

    expect(polylines.map((line) => line.color)).toEqual([
      CENTERLINE_DEPTH_BANDS[0].color,
      CENTERLINE_DEPTH_BANDS[1].color,
      CENTERLINE_DEPTH_BANDS[2].color,
      CENTERLINE_DEPTH_BANDS[3].color,
    ]);
    // The first band is the colour the flat map draws the same overlay in, so a cave stays
    // recognisably the same cave in both views.
    expect(CENTERLINE_DEPTH_BANDS[0].color).toBe(centerlinePalette.line);
    // Cutting a passage into pieces must not cut what clicking one of them selects: every piece
    // carries the same payload object, so a click anywhere still answers with the cave.
    expect(new Set(polylines.map((line) => line.id)).size).toBe(1);
  });

  it('changes colour where the depth changes, not at whichever station is nearest', () => {
    // A single long leg dropping through a boundary is cut at the boundary. Drawing it in one
    // colour would put fifty metres of passage in the wrong band, and cutting it at a station
    // would put the change wherever the surveyors happened to stop.
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, 700],
              [25.46, 45.53, 600],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );

    expect(polylines).toHaveLength(2);
    const change = polylines[0].positions[1];
    expect(change.height).toBe(-50);
    // Halfway along the leg, because the boundary is halfway down it.
    expect(change.longitude).toBeCloseTo(25.45, 9);
    expect(change.latitude).toBeCloseTo(45.53, 9);
    // Both pieces share that point, so the line stays unbroken across the change.
    expect(polylines[1].positions[0]).toEqual(change);
  });

  it('leaves a cave that never leaves its first band as the components it arrived as', () => {
    // The cue costs nothing where there is nothing to show: the number of drawn components only
    // grows where a survey actually crosses a boundary.
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
                [25.442, 45.531, 670],
              ],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );

    expect(polylines).toHaveLength(2);
    expect(new Set(polylines.map((line) => line.color))).toEqual(
      new Set([CENTERLINE_DEPTH_BANDS[0].color]),
    );
  });

  it('colours a passage climbing back up by the band it climbs into', () => {
    const polylines = centerlinePolylines(
      collection([
        feature(
          {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, 500],
              [25.46, 45.53, 700],
            ],
          },
          { topAltitudeM: 700 },
        ),
      ]),
    );

    expect(polylines.map((line) => line.color)).toEqual([
      CENTERLINE_DEPTH_BANDS[2].color,
      CENTERLINE_DEPTH_BANDS[1].color,
      CENTERLINE_DEPTH_BANDS[0].color,
    ]);
    expect(polylines[0].positions.at(-1)!.height).toBe(-150);
    expect(polylines[1].positions.at(-1)!.height).toBe(-50);
  });
});

describe('nearestCaveCenterlines', () => {
  const line = (caveId: string, longitude: number): Scene3DPolyline => ({
    positions: [
      { longitude, latitude: 45.53, height: 0 },
      { longitude: longitude + 0.001, latitude: 45.53, height: -20 },
    ],
    widthPixels: 2,
    color: '#7a1f1f',
    id: { kind: 'centerline', caveId, centerlineId: `${caveId}-1` },
  });

  it('picks the cave the middle of the view is on', () => {
    const near = line('cave-near', 25.44);
    const far = line('cave-far', 26.44);

    expect(nearestCaveCenterlines([far, near], { longitude: 25.45, latitude: 45.53 })).toEqual([
      near,
    ]);
    expect(nearestCaveCenterlines([far, near], { longitude: 26.45, latitude: 45.53 })).toEqual([
      far,
    ]);
  });

  it('keeps every component of the cave it picks', () => {
    const first = line('cave-near', 25.44);
    const second = line('cave-near', 25.45);
    const far = line('cave-far', 26.44);

    expect(nearestCaveCenterlines([first, far, second], { longitude: 25.44, latitude: 45.53 })).toEqual(
      [first, second],
    );
  });

  it('measures nearest on the ground rather than in degrees', () => {
    // At Carpathian latitudes a degree of longitude is about two thirds of a degree of latitude,
    // so a cave 0.5 degrees east is nearer than one 0.5 degrees north — and comparing the two raw
    // would say they were the same distance away.
    const east = line('cave-east', 25.94);
    const north: Scene3DPolyline = {
      ...line('cave-north', 25.44),
      positions: [
        { longitude: 25.44, latitude: 46.03, height: 0 },
        { longitude: 25.441, latitude: 46.03, height: -20 },
      ],
      id: { kind: 'centerline', caveId: 'cave-north', centerlineId: 'cave-north-1' },
    };

    expect(nearestCaveCenterlines([north, east], { longitude: 25.44, latitude: 45.53 })).toEqual([
      east,
    ]);
  });

  it('has nothing to offer when nothing was drawn', () => {
    expect(nearestCaveCenterlines([], { longitude: 25.44, latitude: 45.53 })).toEqual([]);
  });
});

describe('framing one cave out of what was drawn', () => {
  const line = (caveId: string, longitude: number): Scene3DPolyline => ({
    positions: [
      { longitude, latitude: 45.53, height: 0 },
      { longitude: longitude + 0.01, latitude: 45.54, height: -820 },
    ],
    widthPixels: 2,
    color: '#7a1f1f',
    id: { kind: 'centerline', caveId, centerlineId: `${caveId}-1` },
  });

  it('takes only the lines of the cave asked for', () => {
    const wanted = line('cave-1', 25.44);
    expect(caveCenterlines([wanted, line('cave-2', 26.44)], 'cave-1')).toEqual([wanted]);
  });

  it('measures the ground a survey covers and leaves its depth out of it', () => {
    // A camera is framed by the ground it has to cover. A cave eight hundred metres deep and a
    // hundred metres wide would, if its depth were counted, be framed from far enough away to be
    // a dot.
    const [west, south, east, north] = centerlineBounds([line('cave-1', 25.44)])!;
    expect(west).toBeCloseTo(25.44, 9);
    expect(south).toBeCloseTo(45.53, 9);
    expect(east).toBeCloseTo(25.45, 9);
    expect(north).toBeCloseTo(45.54, 9);
  });

  it('has no box to offer when nothing of that cave is drawn', () => {
    expect(centerlineBounds([])).toBeUndefined();
    expect(centerlineBounds(caveCenterlines([line('cave-1', 25.44)], 'cave-9'))).toBeUndefined();
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
