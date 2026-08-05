// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { surfaceFeaturePalette } from '../map/markerPalette.ts';
import { entranceMarkers, surfaceFeatureLines, surfaceFeatureMarkers } from './caveMarkers3d.ts';
import { clusterIcon, entranceIcon, surfaceFeatureIcon } from './markerIcons3d.ts';

function collection(features: unknown[]) {
  return { type: 'FeatureCollection', features };
}

function point(lon: number, lat: number, properties: Record<string, unknown>) {
  return { type: 'Feature', geometry: { type: 'Point', coordinates: [lon, lat] }, properties };
}

describe('entranceMarkers', () => {
  it('draws an entrance with everything a click needs to open its cave', () => {
    const markers = entranceMarkers(
      collection([
        point(25.4472, 45.5312, {
          id: 'entrance-1',
          caveId: 'cave-1',
          name: 'Main entrance',
          isMain: true,
          protected: false,
          approximate: false,
        }),
      ]),
      14,
    );

    expect(markers).toHaveLength(1);
    expect(markers[0].id).toMatchObject({
      kind: 'entrance',
      entranceId: 'entrance-1',
      caveId: 'cave-1',
    });
    expect(markers[0].position).toEqual({ longitude: 25.4472, latitude: 45.5312, height: 0 });
    expect(markers[0].image).toBe(entranceIcon(false).image);
    // Entrances are surface things; a survey altitude on the row must not float them off it.
    expect(markers[0].clampToGround).toBe(true);
  });

  it('carries the name and the place chrome over the scene needs, so nothing is looked up on hover', () => {
    // Hover asks its question once per drawn frame. Anything the answer needs has to be in the
    // payload already, because a fetch or a lookup back from a graphics object to a database row
    // at that rate is not affordable — and the loader is the only place that has both.
    const markers = entranceMarkers(
      collection([
        point(25.4472, 45.5312, {
          id: 'entrance-1',
          caveId: 'cave-1',
          name: 'Intrarea Mică',
          caveName: 'Peștera Demo',
        }),
      ]),
      14,
    );

    expect(markers[0].id).toMatchObject({
      label: 'Intrarea Mică — Peștera Demo',
      anchor: { longitude: 25.4472, latitude: 45.5312, height: 0 },
    });
  });

  it('anchors chrome where the marker is drawn, not at the altitude it was surveyed at', () => {
    // Every marker here is dropped onto the ground, so a row's altitude is not where its marker
    // ends up. Anchoring to the surveyed altitude is not a cosmetic error: with the camera nine
    // hundred metres up looking down, an entrance recorded at 952 m is FIFTY METRES BEHIND the
    // camera, the projection correctly answers that it is nowhere on screen, and the label
    // silently never appears — with the marker plainly visible underneath where it should have
    // been. Found exactly that way, against real seeded data.
    const markers = entranceMarkers(
      collection([
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [25.4472, 45.5312, 952] },
          properties: { id: 'e', caveId: 'c', name: 'Main entrance' },
        },
      ]),
      14,
    );

    expect(markers[0].clampToGround).toBe(true);
    expect((markers[0].id as { anchor: { height: number } }).anchor.height).toBe(0);
  });

  it('leaves the name off an entrance that has none rather than carrying an empty one', () => {
    const markers = entranceMarkers(
      collection([point(25, 45, { id: 'e', caveId: 'c' })]),
      14,
    );

    expect(markers[0].id).not.toHaveProperty('label');
  });

  it('gives a cluster the place it stands and no name, because a patch of ground has none', () => {
    const markers = entranceMarkers(collection([point(25, 45.5, { cluster: true, count: 7 })]), 7);

    expect(markers[0].id).not.toHaveProperty('label');
    expect(markers[0].id).toMatchObject({ anchor: { longitude: 25, latitude: 45.5, height: 0 } });
  });

  it('marks an entrance the server would only place approximately', () => {
    const markers = entranceMarkers(
      collection([
        point(25.21, 45.51, { id: 'e', caveId: 'c', protected: true, approximate: true }),
      ]),
      14,
    );

    expect(markers[0].image).toBe(entranceIcon(true).image);
  });

  it('renders the server\'s own clusters as clusters and does not re-cluster anything', () => {
    const markers = entranceMarkers(
      collection([
        point(25.0, 45.5, { cluster: true, count: 7 }),
        point(25.4, 45.5, { cluster: true, count: 3 }),
      ]),
      7,
    );

    expect(markers).toHaveLength(2);
    expect(markers.map((m) => m.image)).toEqual([clusterIcon(7).image, clusterIcon(3).image]);
  });

  it('stamps each cluster with the zoom its count was summed at', () => {
    // The zoom fixed the size of the cell the server added up. Reading it back when the cluster is
    // clicked would ask about whatever the camera had drifted to, which is a different patch of
    // ground and a different set of caves.
    const markers = entranceMarkers(collection([point(25.0, 45.5, { cluster: true, count: 7 })]), 7);

    expect(markers[0].id).toMatchObject({
      kind: 'cluster',
      lon: 25.0,
      lat: 45.5,
      count: 7,
      zoom: 7,
    });
  });

  it('places a cluster exactly where the server put it', () => {
    // The coordinates are the grid cell's, because a protected cave is snapped to that same grid
    // before it is ever aggregated. Moving them here would be undoing the protection.
    const markers = entranceMarkers(
      collection([point(25.3125, 45.5625, { cluster: true, count: 12 })]),
      7,
    );

    expect(markers[0].position).toEqual({ longitude: 25.3125, latitude: 45.5625, height: 0 });
  });

  it('skips a row it could not act on if it were clicked', () => {
    expect(
      entranceMarkers(collection([point(25, 45, { name: 'nameless' })]), 14),
    ).toEqual([]);
  });
});

describe('surfaceFeatureMarkers', () => {
  it('draws a typed feature with the symbol file its type carries', () => {
    const markers = surfaceFeatureMarkers(
      collection([
        point(25.4455, 45.5301, {
          id: 'feature-1',
          name: 'Dolina Demo',
          kind: 'generic',
          typeCode: 'sinkhole',
          symbol: 'sinkhole.png',
          protected: false,
          approximate: false,
        }),
      ]),
    );

    expect(markers).toHaveLength(1);
    expect(markers[0].image).toBe('/feature_symbols/sinkhole.png');
    expect(markers[0].scale).toBe(0.5);
    expect(markers[0].id).toMatchObject({ kind: 'feature', featureId: 'feature-1' });
  });

  it('falls back to a dot for a type with no symbol', () => {
    const markers = surfaceFeatureMarkers(
      collection([point(25, 45, { id: 'f', symbol: null, kind: 'generic' })]),
    );

    expect(markers[0].image).toBe(surfaceFeatureIcon(null).image);
  });

  it('draws a multi-part point feature at each of its points, like the flat map', () => {
    const markers = surfaceFeatureMarkers(
      collection([
        {
          type: 'Feature',
          geometry: {
            type: 'MultiPoint',
            coordinates: [
              [25, 45],
              [25.1, 45.1],
            ],
          },
          properties: { id: 'f', symbol: 'pit.png' },
        },
      ]),
    );

    expect(markers).toHaveLength(2);
    // Selecting either one selects the same feature — that is what a multi-part feature is — but
    // each carries where it stands, so a label about the one that was clicked sits on it rather
    // than jumping to the first of them.
    const [first, second] = markers.map((marker) => marker.id as Record<string, unknown>);
    expect(first.featureId).toBe(second.featureId);
    // On the ground, and saying so: nothing here can know how high the ground is, so the anchor
    // carries the ellipsoid as its fallback and leaves the scene to resolve it against what is
    // actually drawn there.
    expect(first.anchor).toEqual({ longitude: 25, latitude: 45, height: 0, onGround: true });
    expect(second.anchor).toEqual({
      longitude: 25.1,
      latitude: 45.1,
      height: 0,
      onGround: true,
    });
  });

  it('ignores a protected row served without geometry', () => {
    // A feature the viewer may read but not locate arrives as a readable row with nothing to put
    // on the globe. Drawing it anywhere would be inventing a position the server refused to give.
    expect(
      surfaceFeatureMarkers(
        collection([
          { type: 'Feature', geometry: null, properties: { id: 'f', protected: true } },
        ]),
      ),
    ).toEqual([]);
  });
});

describe('surfaceFeatureLines', () => {
  it('draws a line feature as a line, selectable as the same feature', () => {
    const lines = surfaceFeatureLines(
      collection([
        {
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates: [
              [25.442, 45.528],
              [25.449, 45.5335],
            ],
          },
          properties: { id: 'fault-1', kind: 'generic' },
        },
      ]),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0].color).toBe(surfaceFeaturePalette.line);
    expect(lines[0].id).toMatchObject({ kind: 'feature', featureId: 'fault-1' });
  });

  it('draws an area as its outline, with no fill to hide the cave underneath', () => {
    const lines = surfaceFeatureLines(
      collection([
        {
          type: 'Feature',
          geometry: {
            type: 'Polygon',
            coordinates: [
              [
                [25.42, 45.51],
                [25.46, 45.51],
                [25.46, 45.54],
                [25.42, 45.51],
              ],
            ],
          },
          properties: { id: 'plateau-1' },
        },
      ]),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0].positions).toHaveLength(4);
  });

  it('leaves point features to the marker pass', () => {
    expect(surfaceFeatureLines(collection([point(25, 45, { id: 'f' })]))).toEqual([]);
  });

  it('draws on the surface even when the row carries altitudes', () => {
    // Imported geodata can arrive with a third ordinate on some geometries and not on others,
    // and those altitudes are heights above a sea-level datum rather than above the globe. This
    // overlay's points are placed on the surface, so its lines have to be: honouring the ordinate
    // would leave a karst area's outline a kilometre above the symbols marking the same area.
    const lines = surfaceFeatureLines(
      collection([
        {
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates: [
              [25.442, 45.528, 1180],
              [25.449, 45.5335, 1205],
            ],
          },
          properties: { id: 'fault-1' },
        },
      ]),
    );

    expect(lines[0].positions.map((p) => p.height)).toEqual([0, 0]);
  });

  it('lays the line on the ground rather than at a height of zero', () => {
    // Discarding the altitude is only half of it. Zero is the ellipsoid, which is the ground on a
    // smooth globe and a whole hillside below it on a real one — and drawing the cave data over
    // the terrain does not rescue that, because it governs what hides what and not where a point
    // lands on the screen. From any camera not looking straight down the line's own vertical, a
    // vertex eleven hundred metres under the ground it belongs to projects a long way from it, and
    // a karst area's outline is seen adrift from the very imagery it was drawn over.
    const lines = surfaceFeatureLines(
      collection([
        {
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates: [
              [25.442, 45.528],
              [25.449, 45.5335],
            ],
          },
          properties: { id: 'fault-1' },
        },
      ]),
    );

    expect(lines[0].clampToGround).toBe(true);
    // The point symbols of the same overlay are dropped onto the ground, and chrome pinned to
    // either half has to end up in the same place as the half it is about.
    expect(lines[0].id).toMatchObject({ anchor: { height: 0, onGround: true } });
  });
});
