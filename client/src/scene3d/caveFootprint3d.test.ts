// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  cameraFloorFor,
  caveFootprint,
  cutawayPitchLimitDegrees,
  DEFAULT_CAMERA_FLOOR_METERS,
  footprintCenter,
} from './caveFootprint3d.ts';
import { METERS_PER_DEGREE_LATITUDE } from './pseudoZoom.ts';
import type { Scene3DPolyline, Scene3DPosition } from './scene3dEngine.ts';

function line(positions: [number, number, number][]): Scene3DPolyline {
  return {
    positions: positions.map(([longitude, latitude, height]) => ({ longitude, latitude, height })),
    widthPixels: 2,
    color: '#7a1f1f',
    id: 'cave',
  };
}

/**
 * Ray casting in degrees. The ring is a small convex shape a few hundred metres across, so the
 * distinction between a straight line in degrees and one on the ground is far below anything this
 * is asked to decide.
 */
function encloses(ring: Scene3DPosition[], point: { longitude: number; latitude: number }) {
  let inside = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i, i += 1) {
    const a = ring[i];
    const b = ring[j];
    const straddles = a.latitude > point.latitude !== b.latitude > point.latitude;
    if (!straddles) {
      continue;
    }
    const crossing =
      ((b.longitude - a.longitude) * (point.latitude - a.latitude)) / (b.latitude - a.latitude) +
      a.longitude;
    if (point.longitude < crossing) {
      inside = !inside;
    }
  }
  return inside;
}

describe('caveFootprint', () => {
  it('has nothing to cut around when nothing was drawn', () => {
    expect(caveFootprint([])).toBeUndefined();
    expect(caveFootprint([line([])])).toBeUndefined();
  });

  it('has nothing to cut around a survey that lies on the ground', () => {
    // A cave served without depths is drawn as a plan on the surface, and there is nothing under
    // a plan on the surface to reveal. Counting it would be actively harmful rather than merely
    // pointless: its heights are the ground's, so the floor would be put below the ground and the
    // shaft measured from the ground down to it — over real relief a shaft as deep as the hillside
    // is tall, which then demands a near-overhead camera before the mode will engage at all.
    const flat = { ...line([[22.7, 46.5, 0], [22.71, 46.51, 0]]), clampToGround: true };

    expect(caveFootprint([flat])).toBeUndefined();
  });

  it('takes the floor from the passages with depths, ignoring any drawn on the ground', () => {
    // With an elevation model attached a surveyed passage is drawn at its own altitude, which in
    // the karst this is for is several hundred metres up, while a plan on the ground carries the
    // heights of the ground. Letting the second into the sum drags the floor down past the first.
    const surveyed = line([[22.70, 46.50, 700], [22.71, 46.51, 420]]);
    const flat = { ...line([[22.70, 46.50, 0], [22.72, 46.52, 0]]), clampToGround: true };

    expect(caveFootprint([surveyed, flat])!.floorHeight).toBe(220);
  });

  it('draws one smooth ring rather than tracing the passages', () => {
    // Measured against a tight outline traced around the passages: a traced shape shows the whole
    // survey from straight above and loses more than half of it the moment the camera tilts,
    // because every notch in it becomes a wall between the viewer and the cave. A single smooth
    // ring keeps the survey visible from both, and costs nothing more to build.
    const footprint = caveFootprint([
      line([
        [25.44, 45.53, -10],
        [25.46, 45.54, -300],
      ]),
    ])!;

    expect(footprint.ring).toHaveLength(96);
    // A ring, not a path: the first point is not repeated at the end.
    expect(footprint.ring.at(-1)).not.toEqual(footprint.ring[0]);
  });

  it('encloses every part of the survey with room to spare', () => {
    // A passage running into the wall of the shaft is a passage the viewer cannot see the end of.
    const positions: [number, number, number][] = [
      [25.44, 45.53, -10],
      [25.46, 45.53, -120],
      [25.46, 45.545, -300],
      [25.445, 45.545, -80],
    ];
    const footprint = caveFootprint([line(positions)])!;

    for (const [longitude, latitude] of positions) {
      expect(encloses(footprint.ring, { longitude, latitude })).toBe(true);
    }
  });

  it('opens a hole a viewer can look into even for a cave with no extent at all', () => {
    const latitude = 45.6;
    const footprint = caveFootprint([line([[25.4, latitude, -5]])])!;

    const longitudes = footprint.ring.map((position) => position.longitude);
    const latitudes = footprint.ring.map((position) => position.latitude);
    const cosLatitude = Math.cos((latitude * Math.PI) / 180);
    const eastWestMeters =
      ((Math.max(...longitudes) - Math.min(...longitudes)) / 2) *
      METERS_PER_DEGREE_LATITUDE *
      cosLatitude;
    const northSouthMeters =
      ((Math.max(...latitudes) - Math.min(...latitudes)) / 2) * METERS_PER_DEGREE_LATITUDE;

    // Round on the ground rather than round in degrees, which at these latitudes would be an
    // opening half again as wide east-west as it is north-south.
    expect(eastWestMeters).toBeCloseTo(150, 0);
    expect(northSouthMeters).toBeCloseTo(150, 0);
  });

  it('puts the floor of the excavation below the deepest passage', () => {
    const footprint = caveFootprint([
      line([
        [25.44, 45.53, -10],
        [25.45, 45.53, -640],
      ]),
    ])!;

    expect(footprint.floorHeight).toBe(-840);
  });
});

describe('cameraFloorFor', () => {
  it('keeps a viewer out of the centre of the earth when nothing says how deep to go', () => {
    expect(cameraFloorFor(undefined)).toBe(DEFAULT_CAMERA_FLOOR_METERS);
  });

  it('never stops a viewer above the standing limit, however shallow the cave is', () => {
    // A shallow cave must not shrink the space a viewer can move in: the limit exists to catch a
    // runaway descent, not to fence the camera in around whatever happens to be loaded.
    expect(cameraFloorFor({ ring: [], floorHeight: -50 })).toBe(DEFAULT_CAMERA_FLOOR_METERS);
  });

  it('drops below a cave that goes deeper than the standing limit', () => {
    expect(cameraFloorFor({ ring: [], floorHeight: -3000 })).toBe(-3500);
  });
});

describe('cutawayPitchLimitDegrees', () => {
  /** A circular outline of `radiusMeters` around the equator-adjacent test latitude. */
  function ring(radiusMeters: number, points = 96): Scene3DPosition[] {
    const latitudeRadius = radiusMeters / METERS_PER_DEGREE_LATITUDE;
    const longitudeRadius = latitudeRadius / Math.cos((46.5 * Math.PI) / 180);
    return Array.from({ length: points }, (_, index) => {
      const angle = (2 * Math.PI * index) / points;
      return {
        longitude: 22.7 + longitudeRadius * Math.cos(angle),
        latitude: 46.5 + latitudeRadius * Math.sin(angle),
        height: 0,
      };
    });
  }

  it('lets a wide shallow opening be looked into from almost anywhere', () => {
    // 2 km across and 60 m deep: the line of sight over the near rim reaches the floor long before
    // the camera has to climb, so the limit stays at the shallow end.
    const limit = cutawayPitchLimitDegrees({ ring: ring(1000), floorHeight: -60 }, 0);
    expect(limit).toBe(-10);
  });

  it('demands a view from over the top of a shaft as deep as it is wide', () => {
    // Radius 240 m, floor 580 m down: crossing the opening costs 480 m of travel and the sight
    // line has to fall 580 m in it, so it must descend at atan(580/480) — about fifty degrees.
    const limit = cutawayPitchLimitDegrees({ ring: ring(240), floorHeight: -580 }, 0);
    expect(limit).toBeCloseTo(-((Math.atan(580 / 480) * 180) / Math.PI), 1);
    expect(limit).toBeLessThan(-45);
    expect(limit).toBeGreaterThan(-55);
  });

  it('measures the depth from the ground the opening is cut into, not from the ellipsoid', () => {
    // The same excavation under a 1000 m hillside is 1000 m deeper, and needs a steeper look.
    const footprint = { ring: ring(240), floorHeight: -580 };
    expect(cutawayPitchLimitDegrees(footprint, 1000)).toBeLessThan(
      cutawayPitchLimitDegrees(footprint, 0),
    );
  });

  it('never demands more than a mostly overhead view, however narrow the shaft', () => {
    // Past this there is nothing left to gain by taking the mode away, and a viewer who tilts a
    // degree off vertical should not lose the view they came for.
    const limit = cutawayPitchLimitDegrees({ ring: ring(20), floorHeight: -4000 }, 0);
    expect(limit).toBe(-75);
  });

  it('falls back to the shallow limit when there is no opening to look into', () => {
    // A floor above the ground, or an outline with no extent, describes no shaft at all.
    expect(cutawayPitchLimitDegrees({ ring: ring(240), floorHeight: 100 }, 0)).toBe(-10);
    expect(
      cutawayPitchLimitDegrees(
        { ring: [
          { longitude: 22.7, latitude: 46.5, height: 0 },
          { longitude: 22.7, latitude: 46.5, height: 0 },
          { longitude: 22.7, latitude: 46.5, height: 0 },
        ], floorHeight: -580 },
        0,
      ),
    ).toBe(-10);
  });
});

describe('footprintCenter', () => {
  it('is the middle of the outline', () => {
    const footprint = caveFootprint([line([[22.70, 46.50, -10], [22.71, 46.51, -20]])])!;
    const center = footprintCenter(footprint);
    expect(center.longitude).toBeCloseTo(22.705, 6);
    expect(center.latitude).toBeCloseTo(46.505, 6);
  });
});
