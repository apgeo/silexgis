// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { Camera3DState } from './camera3d.ts';
import { cameraHeightForZoom, groundSampleDistance } from './pseudoZoom.ts';
import { boundsToBbox, cameraFramingBounds, viewportBounds, type GroundView } from './viewBounds3d.ts';

function view(overrides: Partial<GroundView> = {}): GroundView {
  return {
    centerLongitude: 25.3,
    centerLatitude: 45.7,
    metersPerPixel: 1,
    viewportWidthPixels: 1200,
    viewportHeightPixels: 800,
    ...overrides,
  };
}

describe('viewportBounds', () => {
  it('centres the box on the ground the middle of the screen is showing', () => {
    const bounds = viewportBounds(view())!;
    const [west, south, east, north] = bounds;

    expect((west + east) / 2).toBeCloseTo(25.3, 9);
    expect((south + north) / 2).toBeCloseTo(45.7, 9);
  });

  it('covers the screen from any compass bearing, not just north-up', () => {
    // Half the screen diagonal on both axes, so turning the camera cannot reveal ground the box
    // never asked for. 1200x800 at one metre per pixel is a 1442 m diagonal, 721 m either way.
    const [, south, , north] = viewportBounds(view())!;
    const halfHeightMeters = ((north - south) / 2) * 111319.4907932736;

    expect(halfHeightMeters).toBeCloseTo(Math.hypot(1200, 800) / 2, 3);
  });

  it('asks for more degrees of longitude the further from the equator the view sits', () => {
    const equator = viewportBounds(view({ centerLatitude: 0 }))!;
    const carpathians = viewportBounds(view({ centerLatitude: 45.7 }))!;

    const equatorSpan = equator[2] - equator[0];
    const carpathianSpan = carpathians[2] - carpathians[0];
    expect(carpathianSpan).toBeGreaterThan(equatorSpan);
    expect(carpathianSpan).toBeCloseTo(equatorSpan / Math.cos((45.7 * Math.PI) / 180), 6);
  });

  it('grows with the ground each pixel covers, so a distant camera asks about more ground', () => {
    const close = viewportBounds(view({ metersPerPixel: 1 }))!;
    const far = viewportBounds(view({ metersPerPixel: 10 }))!;

    expect(far[3] - far[1]).toBeCloseTo((close[3] - close[1]) * 10, 9);
  });

  it('produces a box a caver-scale view can actually be served', () => {
    // A camera showing about half a metre of ground per pixel is inside the zoom band where the
    // server sends full survey detail; the box it produces has to be a cave-sized patch, not a
    // county. Anchored to the same ground-sample-distance the zoom is derived from.
    const metersPerPixel = groundSampleDistance(18, 45.7);
    const [west, south, east, north] = viewportBounds(view({ metersPerPixel }))!;

    expect(east - west).toBeLessThan(0.01);
    expect(north - south).toBeLessThan(0.01);
  });

  it('stays inside the world when the view sits against an edge of it', () => {
    const [west, south, east, north] = viewportBounds(
      view({ centerLongitude: 179.999, centerLatitude: 89.9, metersPerPixel: 5000 }),
    )!;

    expect(west).toBeGreaterThanOrEqual(-180);
    expect(east).toBeLessThanOrEqual(180);
    expect(south).toBeGreaterThanOrEqual(-90);
    expect(north).toBeLessThanOrEqual(90);
  });

  it('answers with nothing rather than a nonsense box when the camera cannot produce one', () => {
    expect(viewportBounds(view({ metersPerPixel: Number.NaN }))).toBeUndefined();
    expect(viewportBounds(view({ metersPerPixel: 0 }))).toBeUndefined();
    expect(viewportBounds(view({ viewportHeightPixels: 0 }))).toBeUndefined();
    expect(viewportBounds(view({ viewportWidthPixels: 0 }))).toBeUndefined();
    expect(viewportBounds(view({ centerLatitude: Number.NaN }))).toBeUndefined();
  });
});

describe('boundsToBbox', () => {
  it('sends west,south,east,north at the precision the flat map sends', () => {
    expect(boundsToBbox([25.1234567, 45.1, 25.2, 45.2])).toBe('25.12346,45.10000,25.20000,45.20000');
  });
});

describe('cameraFramingBounds', () => {
  const tilted: Camera3DState = {
    eye: { lon: 20, lat: 40, height: 5000 },
    heading: 90,
    pitch: -30,
    roll: 0,
    projection: 'perspective',
  };
  // Stands in for the one thing that needs the scene: how high to be for a given map zoom.
  const heightForZoom = (zoom: number, latitude: number) =>
    cameraHeightForZoom(zoom, {
      latitudeDegrees: latitude,
      fieldOfViewRadians: Math.PI / 3,
      viewportHeightPixels: 800,
    });

  it('aims at the middle of the box and leaves the camera pointing where it was', () => {
    const framed = cameraFramingBounds([25.2, 45.6, 25.4, 45.8], 14, tilted, heightForZoom)!;

    expect(framed.target!.lon).toBeCloseTo(25.3, 9);
    expect(framed.target!.lat).toBeCloseTo(45.7, 9);
    expect(framed.heading).toBe(90);
    expect(framed.pitch).toBe(-30);
  });

  it('stands the camera the right height above what it is looking at', () => {
    // The height a zoom describes is the camera's height above the ground it is aimed at, which
    // is the vertical leg of the triangle the distance is the hypotenuse of.
    const framed = cameraFramingBounds([25.2, 45.6, 25.4, 45.8], 14, tilted, heightForZoom)!;
    expect(framed.eye.height).toBeCloseTo(heightForZoom(14, 45.7), 0);
  });

  it('closes in as the zoom goes up', () => {
    const wide = cameraFramingBounds([25.2, 45.6, 25.4, 45.8], 10, tilted, heightForZoom)!;
    const close = cameraFramingBounds([25.2, 45.6, 25.4, 45.8], 16, tilted, heightForZoom)!;
    expect(close.eye.height).toBeLessThan(wide.eye.height);
  });

  it('refuses to frame from a camera looking at the horizon by tilting it down first', () => {
    // The distance to what a level camera is aiming at goes to infinity as the tilt goes to zero,
    // so "stand back far enough to see this box" has no answer down there.
    const level: Camera3DState = { ...tilted, pitch: 0 };
    const framed = cameraFramingBounds([25.2, 45.6, 25.4, 45.8], 14, level, heightForZoom)!;
    expect(framed.pitch).toBeLessThan(0);
    expect(Number.isFinite(framed.eye.lat)).toBe(true);
  });

  it('answers with nothing when the scene cannot say how high to be', () => {
    expect(
      cameraFramingBounds([25.2, 45.6, 25.4, 45.8], 14, tilted, () => Number.NaN),
    ).toBeUndefined();
  });
});
