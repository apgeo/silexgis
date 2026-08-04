// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { attachViewSync, resetViewSyncMemory, type ViewExtent } from '../workspace/viewSync.ts';
import { cameraHeightForZoom } from './pseudoZoom.ts';
import type { Scene3DBounds, Scene3DCameraState } from './scene3dEngine.ts';
import { attachViewSync3d } from './viewSync3d.ts';

const romania: ViewExtent = [25.2, 45.6, 25.4, 45.8];

/**
 * How long an animated camera move takes to come to rest, in this stand-in.
 *
 * Deliberately longer than the period a following view is kept quiet for, because that is the
 * relationship in the real scene — a flight lasts seconds and the quiet period is a bit over one —
 * and it is the whole reason a followed move has to land at once rather than fly.
 */
const FLIGHT_MILLISECONDS = 3000;

/** A scene whose camera can be read back, which is all the sync touches. */
function fakeScene(camera: Scene3DCameraState) {
  let viewListener: (() => void) | undefined;
  return {
    camera,
    bounds: [25.0, 45.0, 25.1, 45.1] as Scene3DBounds,
    getCamera: () => camera,
    setCamera: (next: Scene3DCameraState, options?: { animate?: boolean }) => {
      camera = next;
      // A real camera reports that it has come to rest, and when it does so depends on whether it
      // was flown or simply placed. Modelled here because that timing is what the sync turns on.
      if (options?.animate) {
        setTimeout(() => viewListener?.(), FLIGHT_MILLISECONDS);
      } else {
        viewListener?.();
      }
    },
    getCameraTarget: () => undefined,
    getProjection: () => 'perspective' as const,
    getOrthoHalfWidth: () => undefined,
    getVisibleBounds() {
      return this.bounds;
    },
    getPseudoZoom: () => 12.4,
    cameraHeightForZoom: (zoom: number, latitude: number) =>
      cameraHeightForZoom(zoom, {
        latitudeDegrees: latitude,
        fieldOfViewRadians: Math.PI / 3,
        viewportHeightPixels: 800,
      }),
    onViewChanged(listener: () => void) {
      viewListener = listener;
      return () => {
        viewListener = undefined;
      };
    },
    settle() {
      viewListener?.();
    },
    get current() {
      return camera;
    },
  };
}

/** A scene's selection, which none of these tests is about: nothing picked, nothing to record. */
function selectionPort() {
  return { current: () => null, set: () => {} };
}

beforeEach(() => {
  vi.useFakeTimers();
  resetViewSyncMemory();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('the scene following the flat map', () => {
  it('goes where the map is looking without flattening the view', () => {
    // The obvious alternative is the engine's own "frame this rectangle", which reorients the
    // camera to look straight down. Panning the map would then repeatedly undo a tilt the viewer
    // had set to look into a cave, and the only way to keep one would be to stop touching the map.
    const scene = fakeScene({
      longitude: 20,
      latitude: 40,
      height: 3000,
      heading: 137,
      pitch: -30,
      roll: 0,
    });
    const sync = attachViewSync3d(scene, selectionPort());
    const map = attachViewSync('map2d', {});

    map.publishExtent(romania, 14);

    expect(scene.current.heading).toBe(137);
    expect(scene.current.pitch).toBe(-30);
    // And it is looking at the middle of the box the map reported, from the north-west of it,
    // because that is the side a camera facing 137° stands on.
    expect(scene.current.latitude).toBeGreaterThan(45.7);
    expect(scene.current.longitude).toBeLessThan(25.3);

    sync.detach();
    map.detach();
  });

  it('does not announce the move it was told to make', () => {
    const scene = fakeScene({
      longitude: 20,
      latitude: 40,
      height: 3000,
      heading: 0,
      pitch: -45,
      roll: 0,
    });
    const heardByMap: unknown[] = [];
    const sync = attachViewSync3d(scene, selectionPort());
    const map = attachViewSync('map2d', { onExtent: (bounds) => heardByMap.push(bounds) });

    map.publishExtent(romania, 14);
    scene.settle();
    vi.advanceTimersByTime(300);

    expect(heardByMap).toHaveLength(0);
    sync.detach();
    map.detach();
  });

  it('does not announce it later either, once the quiet period has lapsed', () => {
    // The failure this is really about. A following view is kept quiet for a fixed period; a
    // camera that FLIES to where it was told comes to rest after that period has already lapsed,
    // and its arrival is then announced as though the viewer had made the move. That echo is not
    // harmless: the box this view reports for a zoom is wider than the one the flat map reports
    // for it, so every echo sends the pair a step further out and a few pans reach the whole globe.
    const scene = fakeScene({
      longitude: 20,
      latitude: 40,
      height: 3000,
      heading: 0,
      pitch: -45,
      roll: 0,
    });
    const heardByMap: unknown[] = [];
    const sync = attachViewSync3d(scene, selectionPort());
    const map = attachViewSync('map2d', { onExtent: (bounds) => heardByMap.push(bounds) });

    map.publishExtent(romania, 14);
    // Well past both the quiet period and any flight the camera could have been given.
    vi.advanceTimersByTime(10_000);

    expect(heardByMap).toHaveLength(0);
    sync.detach();
    map.detach();
  });

  it('announces where it is once the viewer has moved it themselves', () => {
    const scene = fakeScene({
      longitude: 20,
      latitude: 40,
      height: 3000,
      heading: 0,
      pitch: -45,
      roll: 0,
    });
    const heardByMap: unknown[] = [];
    const sync = attachViewSync3d(scene, selectionPort());
    const map = attachViewSync('map2d', { onExtent: (bounds) => heardByMap.push(bounds) });

    scene.settle();
    vi.advanceTimersByTime(300);

    expect(heardByMap).toEqual([[25.0, 45.0, 25.1, 45.1]]);
    sync.detach();
    map.detach();
  });

  it('stops announcing once the scene goes away', () => {
    const scene = fakeScene({
      longitude: 20,
      latitude: 40,
      height: 3000,
      heading: 0,
      pitch: -45,
      roll: 0,
    });
    const heardByMap: unknown[] = [];
    const sync = attachViewSync3d(scene, selectionPort());
    const map = attachViewSync('map2d', { onExtent: (bounds) => heardByMap.push(bounds) });

    scene.settle();
    sync.detach();
    vi.advanceTimersByTime(1000);

    expect(heardByMap).toHaveLength(0);
    map.detach();
  });
});
