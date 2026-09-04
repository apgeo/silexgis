// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import type { Camera3DState } from '../scene3d/camera3d.ts';
import { setActiveViewCamera } from '../workspace/viewCamera.ts';
import { applyViewConfig, captureViewConfig, type WorkspaceUiState } from './viewConfig.ts';

const baseUi: Omit<WorkspaceUiState, 'overlayOrder'> = {
  baseLayerId: 3,
  entrancesVisible: true,
  surfaceFeaturesVisible: false,
  centerlinesVisible: true,
  heatmapVisible: true,
  photosVisible: false,
  tripsVisible: true,
  tripsFrom: '2019-01-01',
  tripsTo: '2019-12-31',
  geofileIds: [],
  rasters: [],
  tagFilter: null,
  overlayOpacity: { entrances: 0.5 },
  baseOpacity: { 3: 0.6 },
};

describe('viewConfig heatmap + base opacity', () => {
  it('captures heatmap visibility and per-base opacity into the config', () => {
    const config = captureViewConfig(baseUi);
    expect(config.heatmapVisible).toBe(true);
    expect(config.baseOpacity).toEqual({ 3: 0.6 });
  });

  it('round-trips the new fields back through applyViewConfig', () => {
    const config = captureViewConfig(baseUi);
    const restored = applyViewConfig(config);
    expect(restored?.heatmapVisible).toBe(true);
    expect(restored?.baseOpacity).toEqual({ 3: 0.6 });
  });

  it('defaults the new fields for older saved views that omit them', () => {
    // A pre-B2 config: valid v1, no heatmapVisible / baseOpacity keys.
    const legacy = {
      configVersion: 1,
      center: [25.3, 45.7],
      zoom: 10,
      entrancesVisible: true,
      surfaceFeaturesVisible: true,
      geofileIds: [],
      rasters: [],
      tagFilter: null,
    };
    const restored = applyViewConfig(legacy);
    expect(restored?.heatmapVisible).toBe(false);
    expect(restored?.baseOpacity).toEqual({});
  });

  it('treats a saved view without centerlinesVisible as off', () => {
    // The centerline overlay is opt-in: it is the heaviest one, and a saved view that predates
    // the setting must not switch it on for whoever opens it.
    const legacy = {
      configVersion: 1,
      center: [25.3, 45.7],
      zoom: 10,
      entrancesVisible: true,
      surfaceFeaturesVisible: true,
      geofileIds: [],
      rasters: [],
      tagFilter: null,
    };
    expect(applyViewConfig(legacy)?.centerlinesVisible).toBe(false);
  });

  it('round-trips an explicit centerline choice either way', () => {
    expect(applyViewConfig(captureViewConfig(baseUi))?.centerlinesVisible).toBe(true);
    expect(
      applyViewConfig(captureViewConfig({ ...baseUi, centerlinesVisible: false }))?.centerlinesVisible,
    ).toBe(false);
  });
});

describe('the 3D camera a saved view can carry', () => {
  const camera3d: Camera3DState = {
    eye: { lon: 25.44, lat: 45.53, height: -180 },
    heading: 137.5,
    pitch: -22.4,
    roll: 0,
    target: { lon: 25.45, lat: 45.535, height: 700 },
    projection: 'orthographic',
    orthoHalfWidth: 640,
  };

  const legacy = {
    configVersion: 1,
    center: [25.3, 45.7],
    zoom: 10,
    entrancesVisible: true,
    surfaceFeaturesVisible: true,
    geofileIds: [],
    rasters: [],
    tagFilter: null,
  };

  afterEach(() => {
    // The registry is module state; a test that leaves a camera registered lends it to the next.
    detachCamera?.();
    detachCamera = undefined;
  });

  let detachCamera: (() => void) | undefined;

  function mountSceneReporting(state: Camera3DState | undefined) {
    detachCamera = setActiveViewCamera({
      flyTo: () => {},
      fitGeometry: () => {},
      getCamera3D: () => state,
    });
  }

  it('is read off whichever view is on screen, not passed in by the page assembling the view', () => {
    // The flat map's page has no way of knowing whether a scene is mounted beside it, so a camera
    // it passed in would be a guess — and a stale one saved from a scene closed minutes ago would
    // be restored as confidently as a live one.
    mountSceneReporting(camera3d);
    expect(captureViewConfig(baseUi).camera3d).toEqual(camera3d);
  });

  it('is left out entirely when no 3D view is on screen', () => {
    expect(captureViewConfig(baseUi).camera3d).toBeUndefined();
    expect('camera3d' in captureViewConfig(baseUi)).toBe(false);
  });

  it('survives being written down and read back', () => {
    mountSceneReporting(camera3d);
    const stored: unknown = JSON.parse(JSON.stringify(captureViewConfig(baseUi)));
    expect(applyViewConfig(stored)?.camera3d).toEqual(camera3d);
  });

  it('opens a view saved before the scene existed, without a camera', () => {
    const restored = applyViewConfig(legacy);
    expect(restored).not.toBeNull();
    expect(restored?.camera3d).toBeUndefined();
    // And everything else the older view remembered still arrives.
    expect(restored?.entrancesVisible).toBe(true);
  });

  it('opens a view whose camera block is unusable, and simply says nothing about the scene', () => {
    // The document is client-owned and the server stores it without looking inside, so a
    // hand-edited or older-shaped block has to cost the camera and nothing else.
    const restored = applyViewConfig({ ...legacy, camera3d: { eye: { lon: 25 }, heading: 0 } });
    expect(restored).not.toBeNull();
    expect(restored?.camera3d).toBeUndefined();
    expect(restored?.entrancesVisible).toBe(true);
  });

  it('keeps the document free of anything a renderer owns', () => {
    mountSceneReporting(camera3d);
    const written = JSON.stringify(captureViewConfig(baseUi).camera3d);
    expect(written).toContain('"lon"');
    expect(written).toContain('"height"');
    // No Cartesian triple, and every angle in the range degrees live in rather than radians.
    expect(written).not.toMatch(/"[xyz]":/);
    expect(Math.abs(captureViewConfig(baseUi).camera3d!.heading)).toBeGreaterThan(Math.PI * 2);
  });
});

describe('viewConfig trip overlay', () => {
  it('round-trips the overlay and its day window', () => {
    const restored = applyViewConfig(captureViewConfig(baseUi));
    expect(restored?.tripsVisible).toBe(true);
    expect(restored?.tripsFrom).toBe('2019-01-01');
    expect(restored?.tripsTo).toBe('2019-12-31');
  });

  it('treats a saved view that predates the trip overlay as off, and its window as unbounded', () => {
    // The overlay is opt-in, so a view saved before it existed must not switch it on for whoever
    // opens it. Its window has no bound for the same reason: an absent bound is "every trip",
    // which is what such a view was showing, and defaulting either end to a date would silently
    // narrow a saved view to a period nobody chose.
    const legacy = {
      configVersion: 1,
      center: [25.3, 45.7],
      zoom: 10,
      entrancesVisible: true,
      surfaceFeaturesVisible: true,
      geofileIds: [],
      rasters: [],
      tagFilter: null,
    };
    const restored = applyViewConfig(legacy);
    expect(restored?.tripsVisible).toBe(false);
    expect(restored?.tripsFrom).toBeUndefined();
    expect(restored?.tripsTo).toBeUndefined();
  });

  it('keeps the document at version 1 when the trip fields are written', () => {
    // Never bumped: everything added since the first release is an optional field with a
    // documented default, and raising the number would make every view saved from that moment
    // unopenable by anything already deployed.
    expect(captureViewConfig(baseUi).configVersion).toBe(1);
  });
});
