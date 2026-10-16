// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import type { Camera3DState } from '../scene3d/camera3d.ts';
import { setActiveViewCamera } from '../workspace/viewCamera.ts';
import { emptyDocument, withWhere } from '../filters/document.ts';
import { tagChipFilter, type MapFilter } from './mapFilters.ts';
import { applyViewConfig, captureViewConfig, type WorkspaceUiState } from './viewConfig.ts';

const baseUi: Omit<WorkspaceUiState, 'overlayOrder'> = {
  baseLayerId: 3,
  entrancesVisible: true,
  surfaceFeaturesVisible: false,
  centerlinesVisible: true,
  heatmapVisible: true,
  karstDensityVisible: true,
  karstDensityMode: 'hotSpot',
  karstIndexVisible: true,
  photosVisible: false,
  tripsVisible: true,
  tripsFrom: '2019-01-01',
  tripsTo: '2019-12-31',
  libraryPhotoSources: [],
  geofileIds: [],
  rasters: [],
  mapFilter: null,
  filterLayers: [],
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

  it('round-trips which photo libraries were switched on, and treats an older view as none', () => {
    // These overlays read a photo library this installation does not own, and they are opt-in: a
    // saved view that predates them must not switch one on for whoever opens it, and a view that
    // names one this installation no longer has is ignored the same way a deleted imported file
    // is — by there being no layer with that id to show.
    const config = captureViewConfig({ ...baseUi, libraryPhotoSources: ['photoprism'] });
    expect(config.libraryPhotoSources).toEqual(['photoprism']);
    expect(applyViewConfig(config)?.libraryPhotoSources).toEqual(['photoprism']);

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
    expect(applyViewConfig(legacy)?.libraryPhotoSources).toEqual([]);
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

describe('viewConfig cave-density grid', () => {
  it('round-trips the grid and the reading its cells are filled by', () => {
    const config = captureViewConfig(baseUi);
    expect(config.karstDensityVisible).toBe(true);
    expect(config.karstDensityMode).toBe('hotSpot');
    expect(config.configVersion).toBe(1);
    const restored = applyViewConfig(config);
    expect(restored?.karstDensityVisible).toBe(true);
    expect(restored?.karstDensityMode).toBe('hotSpot');
  });

  it('round-trips the karstification-index choropleth beside the grid, at version 1', () => {
    const config = captureViewConfig(baseUi);
    expect(config.karstIndexVisible).toBe(true);
    expect(config.configVersion).toBe(1);
    expect(applyViewConfig(config)?.karstIndexVisible).toBe(true);
    expect(applyViewConfig(captureViewConfig({ ...baseUi, karstIndexVisible: false }))?.karstIndexVisible).toBe(false);
  });

  it('treats an older view as off with the plain reading, and an unknown reading as the plain one', () => {
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
    expect(restored?.karstIndexVisible).toBe(false);
    expect(restored?.karstDensityVisible).toBe(false);
    expect(restored?.karstDensityMode).toBe('density');
    expect(applyViewConfig({ ...legacy, karstDensityMode: 'contours' })?.karstDensityMode).toBe('density');
  });
});

describe('the filter a saved view carries', () => {
  const savedFilterId = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
  const lengthDocument = withWhere(emptyDocument(['feature']), 'feature', {
    node: 'condition',
    field: 'surveyedLength',
    op: 'greaterThan',
    values: [{ type: 'number', value: 300 }],
  });

  const roundTrip = (mapFilter: MapFilter | null) => {
    const config = captureViewConfig({ ...baseUi, mapFilter });
    return { config, restored: applyViewConfig(JSON.parse(JSON.stringify(config)))?.mapFilter };
  };

  it('stores a saved filter by its id alone, and restores it', () => {
    const { config, restored } = roundTrip({ kind: 'saved', savedFilterId });
    expect(config.savedFilterId).toBe(savedFilterId);
    expect(config.filterDocument).toBeUndefined();
    // Null, because a shared link reads this key and no other: left with a tag in it, the link
    // would draw that tag's caves for a view that showed something else.
    expect(config.tagFilter).toBeNull();
    expect(restored).toEqual({ kind: 'saved', savedFilterId });
  });

  it('stores a document whole when the filter is not a saved one, and restores it', () => {
    const { config, restored } = roundTrip({ kind: 'document', document: lengthDocument });
    expect(config.savedFilterId).toBeUndefined();
    expect(config.filterDocument).toEqual(lengthDocument);
    expect(config.tagFilter).toBeNull();
    expect(restored).toEqual({ kind: 'document', document: lengthDocument });
  });

  it('stores the tag chooser in the old key, so a shared link keeps drawing it', () => {
    const { config, restored } = roundTrip(tagChipFilter('winter-2026'));
    expect(config.tagFilter).toBe('winter-2026');
    expect(config.savedFilterId).toBeUndefined();
    expect(config.filterDocument).toBeUndefined();
    expect(restored).toEqual(tagChipFilter('winter-2026'));
  });

  it('stores no filter as none', () => {
    const { config, restored } = roundTrip(null);
    expect(config.tagFilter).toBeNull();
    expect(config.savedFilterId).toBeUndefined();
    expect(config.filterDocument).toBeUndefined();
    expect(restored).toBeNull();
  });

  it('reads a view saved before filters had a document as its tag', () => {
    const legacy = {
      configVersion: 1,
      center: [25.3, 45.7],
      zoom: 10,
      entrancesVisible: true,
      surfaceFeaturesVisible: true,
      geofileIds: [],
      rasters: [],
      tagFilter: 'leads',
    };
    expect(applyViewConfig(legacy)?.mapFilter).toEqual(tagChipFilter('leads'));
  });

  it('restores no filter from forms it cannot read rather than guessing', () => {
    const view = {
      configVersion: 1,
      center: [25.3, 45.7],
      zoom: 10,
      entrancesVisible: true,
      surfaceFeaturesVisible: true,
      geofileIds: [],
      rasters: [],
      tagFilter: null,
      savedFilterId: 'not-an-id',
      filterDocument: { version: 99, scope: [], sort: 'updated', descending: true },
    };
    expect(applyViewConfig(view)?.mapFilter).toBeNull();
  });
});

describe('the filter layers a saved view carries', () => {
  const first = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
  const second = '7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d';
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

  it('round-trips each layer with its colour, symbol and visibility, in order', () => {
    const filterLayers = [
      { savedFilterId: first, color: 'magenta' as const, symbol: 'triangle' as const, visible: true },
      { savedFilterId: second, color: 'violet' as const, symbol: 'circle' as const, visible: false },
    ];
    const overlayOpacity = { [`filter:${first}`]: 0.4 };
    const config = captureViewConfig({ ...baseUi, filterLayers, overlayOpacity });
    const restored = applyViewConfig(JSON.parse(JSON.stringify(config)));
    expect(restored?.filterLayers).toEqual(filterLayers);
    // Transparency travels with every other overlay's, under the layer's own id.
    expect(restored?.overlayOpacity).toEqual(overlayOpacity);
  });

  it('writes nothing for a view with no filter layers, and reads an older view as none', () => {
    expect('filterLayers' in captureViewConfig(baseUi)).toBe(false);
    expect(applyViewConfig(legacy)?.filterLayers).toEqual([]);
  });

  it('keeps the filter layers out of what the main filter is stored as', () => {
    // A shared link reads `tagFilter`, `savedFilterId` and `filterDocument` to decide what it may
    // draw; a filter layer written into any of them would change what the link shows.
    const config = captureViewConfig({
      ...baseUi,
      filterLayers: [{ savedFilterId: first, color: 'violet', symbol: 'circle', visible: true }],
    });
    expect(config.tagFilter).toBeNull();
    expect(config.savedFilterId).toBeUndefined();
    expect(config.filterDocument).toBeUndefined();
  });

  it('drops entries it cannot read and repairs a style it does not know', () => {
    const restored = applyViewConfig({
      ...legacy,
      filterLayers: [
        { savedFilterId: 'not-an-id', color: 'violet', symbol: 'circle', visible: true },
        { savedFilterId: first, color: 'chartreuse', symbol: 'star' },
        { savedFilterId: first.toUpperCase(), color: 'gold', symbol: 'square', visible: true },
        'nonsense',
      ],
    });
    expect(restored?.filterLayers).toEqual([
      { savedFilterId: first, color: 'violet', symbol: 'circle', visible: true },
    ]);
  });
});
