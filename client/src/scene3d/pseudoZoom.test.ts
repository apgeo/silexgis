// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  cameraGroundSampleDistance,
  cameraHeightForZoom,
  groundSampleDistance,
  mapZoomFor,
  pseudoZoom,
  zoomForGroundSampleDistance,
  type CameraView,
} from './pseudoZoom.ts';

// A plausible desktop scene: Cesium's default vertical field of view is ~pi/3 over a viewport
// roughly 800 css pixels tall, looking at the Carpathians.
const view = (heightMeters: number): CameraView => ({
  heightMeters,
  latitudeDegrees: 45.7,
  fieldOfViewRadians: Math.PI / 3,
  viewportHeightPixels: 800,
});

describe('ground sample distance (the tile pyramid the 2D map stands on)', () => {
  it('halves with every zoom step', () => {
    expect(groundSampleDistance(9, 45.7)).toBeCloseTo(groundSampleDistance(8, 45.7) / 2, 9);
  });

  it('is the equatorial figure at the equator', () => {
    expect(groundSampleDistance(0, 0)).toBeCloseTo(156543.03392804097, 6);
    // 360 degrees of longitude over 256 pixels at zoom 0, in metres of equatorial arc.
    expect(groundSampleDistance(8, 0)).toBeCloseTo(156543.03392804097 / 256, 6);
  });

  it('agrees with the server threshold the detail zoom was chosen from', () => {
    // The centerline detail zoom defaults to 18 because survey splays stop carrying information
    // above roughly 0.4 ground metres per pixel. That only comes out right with the cos term.
    expect(groundSampleDistance(18, 45.7)).toBeCloseTo(0.417, 3);
  });

  it('shrinks towards the poles, where a Mercator pixel covers less ground', () => {
    expect(groundSampleDistance(12, 60)).toBeLessThan(groundSampleDistance(12, 0));
    // Symmetric about the equator.
    expect(groundSampleDistance(12, -45.7)).toBeCloseTo(groundSampleDistance(12, 45.7), 9);
  });
});

describe('zoomForGroundSampleDistance', () => {
  it('inverts groundSampleDistance exactly', () => {
    for (const zoom of [0, 5, 11.5, 18, 24]) {
      expect(zoomForGroundSampleDistance(groundSampleDistance(zoom, 45.7), 45.7)).toBeCloseTo(zoom, 9);
    }
  });

  it('rejects a nonsensical resolution rather than returning a plausible zoom', () => {
    expect(zoomForGroundSampleDistance(0, 45.7)).toBeNaN();
    expect(zoomForGroundSampleDistance(-10, 45.7)).toBeNaN();
  });
});

describe('camera height → pseudo-zoom', () => {
  it('derives metres per pixel from the frustum a camera actually has', () => {
    // 2 · 1000 m · tan(30 deg) / 800 px.
    expect(cameraGroundSampleDistance(view(1000))).toBeCloseTo(
      (2 * 1000 * Math.tan(Math.PI / 6)) / 800,
      9,
    );
  });

  it('gains a zoom level for every halving of the camera height', () => {
    expect(pseudoZoom(view(500))).toBeCloseTo(pseudoZoom(view(1000)) + 1, 9);
  });

  it('round-trips through cameraHeightForZoom', () => {
    for (const zoom of [8, 12, 18, 21]) {
      const height = cameraHeightForZoom(zoom, {
        latitudeDegrees: 45.7,
        fieldOfViewRadians: Math.PI / 3,
        viewportHeightPixels: 800,
      });
      expect(height).toBeGreaterThan(0);
      expect(pseudoZoom(view(height))).toBeCloseTo(zoom, 9);
    }
  });

  it('puts a caver-scale view of a cave entrance at the zooms the server serves detail at', () => {
    // Standing 60 m off the hillside on an 800 px viewport is ~0.087 m/px — well past the
    // detail-zoom threshold of 18, and short of the endpoints' ceiling of 24.
    const zoom = pseudoZoom(view(60));
    expect(zoom).toBeGreaterThan(18);
    expect(zoom).toBeLessThan(24);
  });

  it('puts a whole-country view below the cluster threshold', () => {
    // 500 km up: the entrance layer clusters below zoom 11, which is what that view wants.
    expect(pseudoZoom(view(500_000))).toBeLessThan(11);
  });

  it('is not fooled by a taller viewport showing the same ground', () => {
    // Twice the pixels over the same frustum is half the ground per pixel: one zoom level in.
    const tall: CameraView = { ...view(1000), viewportHeightPixels: 1600 };
    expect(pseudoZoom(tall)).toBeCloseTo(pseudoZoom(view(1000)) + 1, 9);
  });

  it('refuses to answer for a camera on the ground or a viewport with no pixels', () => {
    expect(cameraGroundSampleDistance(view(0))).toBeNaN();
    expect(pseudoZoom(view(-5))).toBeNaN();
    expect(pseudoZoom({ ...view(1000), viewportHeightPixels: 0 })).toBeNaN();
    expect(pseudoZoom({ ...view(1000), fieldOfViewRadians: 0 })).toBeNaN();
  });
});

describe('mapZoomFor', () => {
  it('rounds the way the 2D layers round the map view', () => {
    expect(mapZoomFor(13.4)).toBe(13);
    expect(mapZoomFor(13.5)).toBe(14);
  });

  it('clamps to the range the map endpoints accept', () => {
    expect(mapZoomFor(-3)).toBe(0);
    expect(mapZoomFor(31)).toBe(24);
    // A camera at ground level produces NaN upstream; the request still has to name a zoom.
    expect(mapZoomFor(Number.NaN)).toBe(0);
  });
});
