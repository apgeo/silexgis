// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';

// The flat map's vector layer builder, stood in for: what it draws is the browser specs' to show;
// here only whether its style arrived matters.
const styleArrives = vi.fn(() => Promise.resolve(true));
vi.mock('../map/vectorLayers.ts', () => ({
  createVectorLayer: () => ({ layer: { dispose() {} }, ready: styleArrives() }),
}));
import type { MapLayerInfo } from '../api/hooks.ts';
import {
  baseImageryLayerId,
  isDrawableIn3d,
  setActiveBaseImagery,
  standInFor,
  syncBaseImagery,
  syncTileOverlayImagery,
  tileOverlayImageryLayerId,
  type CatalogueImageryContext,
} from './baseImagery3d.ts';
import type {
  Scene3DImageOverlayOptions,
  Scene3DImagery,
  Scene3DImageryOptions,
} from './scene3dEngine.ts';

// A plain object standing in for the scene — the point of the engine contract is that everything
// around it is testable without a graphics context.
class FakeImagery implements Scene3DImagery {
  readonly layers = new Map<string, Record<string, unknown> & { visible: boolean; opacity: number }>();

  addImageryLayer(id: string, options: Scene3DImageryOptions) {
    if (this.layers.has(id)) return;
    this.layers.set(id, { ...options, visible: options.visible ?? true, opacity: options.opacity ?? 1 });
  }
  addImageOverlayLayer(id: string, options: Scene3DImageOverlayOptions) {
    if (this.layers.has(id)) return;
    this.layers.set(id, { ...options, visible: options.visible ?? true, opacity: options.opacity ?? 1 });
  }
  removeImageryLayer(id: string) {
    this.layers.delete(id);
  }
  replaceImageryLayer(id: string, options: Scene3DImageryOptions) {
    const existing = this.layers.get(id);
    if (!existing) return;
    this.layers.set(id, { ...options, visible: existing.visible, opacity: existing.opacity });
  }
  hasImageryLayer(id: string) {
    return this.layers.has(id);
  }
  getImageryLayerIds() {
    return [...this.layers.keys()];
  }
  setImageryLayerVisible(id: string, visible: boolean) {
    const layer = this.layers.get(id);
    if (layer) layer.visible = visible;
  }
  setImageryLayerOpacity(id: string, opacity: number) {
    const layer = this.layers.get(id);
    if (layer) layer.opacity = opacity;
  }
}

/** The catalogue as the scene is handed it, read in English. */
function inScene(catalog: MapLayerInfo[]): CatalogueImageryContext {
  return { catalog, language: 'en' };
}

function layer(overrides: Partial<MapLayerInfo> & { id: number }): MapLayerInfo {
  return {
    name: `Layer ${overrides.id}`,
    layerKind: 'xyz',
    urlTemplate: `https://tiles.example/${overrides.id}/{z}/{x}/{y}.png`,
    wms: null,
    wmts: null,
    cog: null,
    vector: null,
    attribution: null,
    isBase: true,
    isDefault: false,
    sortOrder: 0,
    ...overrides,
  } as MapLayerInfo;
}

describe('syncBaseImagery', () => {
  it('creates one scene layer per tiled base entry and shows only the active one', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 1 }), layer({ id: 2 })]), 2);

    expect(engine.getImageryLayerIds()).toEqual([baseImageryLayerId(1), baseImageryLayerId(2)]);
    expect(engine.layers.get(baseImageryLayerId(1))!.visible).toBe(false);
    expect(engine.layers.get(baseImageryLayerId(2))!.visible).toBe(true);
  });

  it('carries the catalog URL template and attribution through unchanged', () => {
    const engine = new FakeImagery();
    syncBaseImagery(
      engine,
      inScene([layer({ id: 7, urlTemplate: 'https://t/{z}/{y}/{x}', attribution: '© Someone' })]),
      7,
    );

    const created = engine.layers.get(baseImageryLayerId(7))!;
    expect(created.urlTemplate).toBe('https://t/{z}/{y}/{x}');
    expect(created.attribution).toBe('© Someone');
  });

  it('skips kinds the client has no implementation for, and non-base entries', () => {
    const engine = new FakeImagery();
    syncBaseImagery(
      engine,
      inScene([
        layer({ id: 1, layerKind: 'wms' }),
        layer({ id: 2, layerKind: 'vector' }),
        layer({ id: 3, isBase: false }),
        layer({ id: 4 }),
      ]),
      4,
    );

    expect(engine.getImageryLayerIds()).toEqual([baseImageryLayerId(4)]);
  });

  it('normalizes ids that arrive as strings from the generated contract', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: '11' as unknown as number })]), 11);

    expect(engine.hasImageryLayer(baseImageryLayerId(11))).toBe(true);
    expect(engine.layers.get(baseImageryLayerId(11))!.visible).toBe(true);
  });

  it('is idempotent — a second sync neither duplicates nor recreates layers', () => {
    const engine = new FakeImagery();
    const catalog = [layer({ id: 1 }), layer({ id: 2 })];
    syncBaseImagery(engine, inScene(catalog), 1);
    engine.setImageryLayerOpacity(baseImageryLayerId(1), 0.4);
    syncBaseImagery(engine, inScene(catalog), 1);

    expect(engine.getImageryLayerIds()).toHaveLength(2);
    // The layer was updated in place, not rebuilt: its opacity survived.
    expect(engine.layers.get(baseImageryLayerId(1))!.opacity).toBe(0.4);
  });
});

describe('map services in the scene', () => {
  const wms = {
    layers: 'topo',
    styles: '',
    format: 'image/png',
    version: '1.3.0',
    transparent: false,
    crs: 'EPSG:3857',
  };
  const wmts = {
    layer: 'topo',
    matrixSet: 'WorldCRS84Quad',
    format: 'image/jpeg',
    style: 'default',
    crs: 'EPSG:4326',
    matrixIdPrefix: 'EPSG:4326:',
  };

  it('drapes a WMS basemap with the catalogue’s request parameters', () => {
    const engine = new FakeImagery();
    syncBaseImagery(
      engine,
      inScene([layer({ id: 5, layerKind: 'wms', urlTemplate: 'https://maps.example/wms', wms, attribution: '© Agency' })]),
      5,
    );

    expect(engine.layers.get(baseImageryLayerId(5))).toMatchObject({
      kind: 'wms',
      url: 'https://maps.example/wms',
      layers: 'topo',
      version: '1.3.0',
      transparent: false,
      crs: 'EPSG:3857',
      attribution: '© Agency',
      visible: true,
    });
  });

  it('drapes a WMTS overlay with one matrix id per level up to its last zoom', () => {
    const engine = new FakeImagery();
    const catalog = [layer({ id: 6, isBase: false, layerKind: 'wmts', urlTemplate: 'https://tiles.example/wmts', wmts, maxZoom: 2 })];
    syncTileOverlayImagery(engine, inScene(catalog), new Set([6]), {});

    expect(engine.layers.get(tileOverlayImageryLayerId(6))).toMatchObject({
      kind: 'wmts',
      url: 'https://tiles.example/wmts',
      layer: 'topo',
      matrixSet: 'WorldCRS84Quad',
      crs: 'EPSG:4326',
      matrixIds: ['EPSG:4326:0', 'EPSG:4326:1', 'EPSG:4326:2'],
      maximumLevel: 2,
      visible: true,
    });
  });

  it('skips a map service that arrives without the options its kind needs', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 8, layerKind: 'wmts' }), layer({ id: 9 })]), 9);

    expect(engine.getImageryLayerIds()).toEqual([baseImageryLayerId(9)]);
  });
});

describe('the zoom band a catalogue entry declares', () => {
  it('reaches the scene for basemaps and overlays alike', () => {
    const engine = new FakeImagery();
    const catalog = [
      layer({ id: 1, maxZoom: 17 }),
      layer({ id: 2, isBase: false, minZoom: 10, maxZoom: 16 }),
    ];
    syncBaseImagery(engine, inScene(catalog), 1);
    syncTileOverlayImagery(engine, inScene(catalog), new Set([2]), {});

    // A close camera must not ask a source for zoom levels it never published: past 17 the first
    // went blank, and every refused tile was an error in the console.
    expect(engine.layers.get(baseImageryLayerId(1))).toMatchObject({ minimumLevel: 0, maximumLevel: 17 });
    expect(engine.layers.get(tileOverlayImageryLayerId(2))).toMatchObject({
      minimumLevel: 10,
      maximumLevel: 16,
    });
  });

  it('falls back as the 2D map does when an entry declares no usable ceiling', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 3, maxZoom: 0 })]), 3);

    expect(engine.layers.get(baseImageryLayerId(3))!.maximumLevel).toBe(19);
  });
});

describe('setActiveBaseImagery', () => {
  it('leaves layers that did not come from the catalog alone', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 1 })]), 1);
    engine.addImageryLayer('overlay:hillshade', { urlTemplate: 'https://t/{z}/{x}/{y}' });

    setActiveBaseImagery(engine, 1);

    expect(engine.layers.get('overlay:hillshade')!.visible).toBe(true);
  });

  it('hides every basemap when the active id names none of them', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 1 }), layer({ id: 2 })]), 1);

    setActiveBaseImagery(engine, 99);

    expect([...engine.layers.values()].every((l) => !l.visible)).toBe(true);
  });
});

describe('syncTileOverlayImagery', () => {
  const overlay = (id: number) => layer({ id, isBase: false });

  it('shows every wanted overlay at once, unlike the basemaps', () => {
    // The whole reason overlays are not a second radio group: hiking routes and ski routes over
    // one topographic map are three answers about one place.
    const engine = new FakeImagery();
    syncTileOverlayImagery(engine, inScene([overlay(7), overlay(8), overlay(9)]), new Set([7, 9]), {});

    expect(engine.layers.get(tileOverlayImageryLayerId(7))!.visible).toBe(true);
    expect(engine.layers.get(tileOverlayImageryLayerId(8))!.visible).toBe(false);
    expect(engine.layers.get(tileOverlayImageryLayerId(9))!.visible).toBe(true);
  });

  it('keeps overlay ids clear of basemap ids', () => {
    // A shared prefix would leave "hide every catalogue layer that is not the chosen basemap"
    // unable to tell the two apart, and it would switch the overlays off with them.
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 1 })]), 1);
    syncTileOverlayImagery(engine, inScene([overlay(1)]), new Set([1]), {});

    expect(engine.getImageryLayerIds()).toHaveLength(2);
    expect(baseImageryLayerId(1)).not.toEqual(tileOverlayImageryLayerId(1));
  });

  it('leaves the overlays alone when the basemap changes', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 1 }), layer({ id: 2 })]), 1);
    syncTileOverlayImagery(engine, inScene([overlay(7)]), new Set([7]), {});

    setActiveBaseImagery(engine, 2);

    expect(engine.layers.get(tileOverlayImageryLayerId(7))!.visible).toBe(true);
  });

  it('applies per-overlay opacity, defaulting to opaque', () => {
    const engine = new FakeImagery();
    syncTileOverlayImagery(engine, inScene([overlay(7), overlay(8)]), new Set([7, 8]), { 7: 0.4 });

    expect(engine.layers.get(tileOverlayImageryLayerId(7))!.opacity).toBe(0.4);
    expect(engine.layers.get(tileOverlayImageryLayerId(8))!.opacity).toBe(1);
  });

  it('does not rebuild a layer it already made', () => {
    // Rebuilding would restart every tile request in the viewport on each re-render.
    const engine = new FakeImagery();
    syncTileOverlayImagery(engine, inScene([overlay(7)]), new Set([7]), {});
    const first = engine.layers.get(tileOverlayImageryLayerId(7));
    syncTileOverlayImagery(engine, inScene([overlay(7)]), new Set([7]), {});

    expect(engine.layers.get(tileOverlayImageryLayerId(7))).toBe(first);
  });
});

describe('GeoTIFFs and vector entries in the scene', () => {
  const cog = {
    bands: [1],
    nodata: 0,
    min: 1,
    max: 255,
    projection: 'EPSG:3844',
    projectionDefinition: '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass +units=m +no_defs',
  };
  const vector = (scene3dStandIn: string | null) => ({
    format: 'mvt' as const,
    style: 'https://styles.example/liberty.json',
    styleSource: 'openmaptiles',
    labelField: 'name:{lang}',
    scene3dStandIn,
  });

  it('drapes a catalogue GeoTIFF as tiles the scene reads from the file itself', () => {
    const engine = new FakeImagery();
    syncTileOverlayImagery(
      engine,
      inScene([
        layer({ id: 41, layerKind: 'cog', urlTemplate: 'https://data.example/lidar.tif', cog, isBase: false, minZoom: 8 }),
      ]),
      new Set([41]),
      {},
    );

    const created = engine.layers.get(tileOverlayImageryLayerId(41))!;
    expect(created.kind).toBe('rendered');
    expect(created.minimumLevel).toBe(8);
    expect(created.visible).toBe(true);
    const source = created.source as { open: unknown; renderTile: unknown; destroy: unknown };
    expect(typeof source.open).toBe('function');
    expect(typeof source.renderTile).toBe('function');
  });

  it('skips a GeoTIFF that arrives without its options', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, inScene([layer({ id: 41, layerKind: 'cog' }), layer({ id: 1 })]), 1);
    expect(engine.getImageryLayerIds()).toEqual([baseImageryLayerId(1)]);
  });

  const streetMap = () =>
    layer({ id: 1, name: 'OpenStreetMap', urlTemplate: 'https://osm/{z}/{x}/{y}.png', attribution: '© OSM' });

  it('draws a vector entry’s style itself, as tiles it renders', () => {
    const engine = new FakeImagery();
    const catalog = [streetMap(), layer({ id: 42, layerKind: 'vector', vector: vector('OpenStreetMap'), attribution: '© OpenMapTiles' })];
    syncBaseImagery(engine, inScene(catalog), 42);

    expect(engine.layers.get(baseImageryLayerId(42))).toMatchObject({
      kind: 'rendered',
      attribution: '© OpenMapTiles',
      visible: true,
    });
  });

  it('puts the stand-in in the vector entry’s place when the style does not draw, and says so', async () => {
    styleArrives.mockResolvedValueOnce(false);
    const engine = new FakeImagery();
    const reports: [number, unknown][] = [];
    const catalog = [streetMap(), layer({ id: 42, layerKind: 'vector', vector: vector('OpenStreetMap') })];
    syncBaseImagery(engine, { catalog, language: 'en', report: (id, status) => reports.push([id, status]) }, 42);
    engine.setImageryLayerOpacity(baseImageryLayerId(42), 0.5);

    const rendered = engine.layers.get(baseImageryLayerId(42))!.source as { open(): Promise<unknown> };
    await expect(rendered.open()).rejects.toThrow();

    // Chosen as the vector entry, drawn as the stand-in's tiles with the stand-in's credit, and
    // with what the viewer had set.
    expect(engine.layers.get(baseImageryLayerId(42))).toMatchObject({
      urlTemplate: 'https://osm/{z}/{x}/{y}.png',
      attribution: '© OSM',
      visible: true,
      opacity: 0.5,
    });
    expect(reports).toEqual([[42, { state: 'limited', code: 'vector.stand_in' }]]);
  });

  it('says a vector entry with no stand-in is not drawn when its style does not draw', async () => {
    styleArrives.mockResolvedValueOnce(false);
    const engine = new FakeImagery();
    const reports: [number, unknown][] = [];
    const catalog = [layer({ id: 42, layerKind: 'vector', vector: vector(null) })];
    syncBaseImagery(engine, { catalog, language: 'en', report: (id, status) => reports.push([id, status]) }, 42);

    const rendered = engine.layers.get(baseImageryLayerId(42))!.source as { open(): Promise<unknown> };
    await expect(rendered.open()).rejects.toThrow();
    expect(engine.layers.get(baseImageryLayerId(42))!.kind).toBe('rendered');
    expect(reports).toEqual([[42, { state: 'unavailable', code: 'vector.unreadable' }]]);
  });

  it('drapes the stand-in from the start on a device too weak to draw styles', () => {
    vi.stubGlobal('navigator', { ...navigator, hardwareConcurrency: 2 });
    try {
      const engine = new FakeImagery();
      const reports: [number, unknown][] = [];
      const catalog = [streetMap(), layer({ id: 42, layerKind: 'vector', vector: vector('OpenStreetMap') })];
      syncBaseImagery(engine, { catalog, language: 'en', report: (id, status) => reports.push([id, status]) }, 42);

      expect(engine.layers.get(baseImageryLayerId(42))).toMatchObject({ urlTemplate: 'https://osm/{z}/{x}/{y}.png' });
      expect(reports).toEqual([[42, { state: 'limited', code: 'vector.stand_in' }]]);
      // Without a stand-in there is nothing lighter to draw, so the style is drawn anyway.
      syncBaseImagery(engine, inScene([layer({ id: 43, layerKind: 'vector', vector: vector(null) })]), 43);
      expect(engine.layers.get(baseImageryLayerId(43))!.kind).toBe('rendered');
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('takes a stand-in only of a kind the scene drapes as it stands, on the same side of the list', () => {
    const catalog = [
      layer({ id: 1, name: 'Overlay', isBase: false }),
      layer({ id: 2, name: 'Other vector', layerKind: 'vector', vector: vector(null) }),
      layer({ id: 42, layerKind: 'vector', vector: vector('Overlay') }),
      layer({ id: 43, layerKind: 'vector', vector: vector('Other vector') }),
      layer({ id: 44, layerKind: 'vector', vector: vector('Gone') }),
    ];
    for (const id of [42, 43, 44]) {
      expect(standInFor(catalog.find((entry) => entry.id === id)!, catalog)).toBeUndefined();
    }
    // A vector entry without its options is draped neither way.
    expect(isDrawableIn3d(layer({ id: 45, layerKind: 'vector', vector: null }), catalog)).toBe(false);
  });

  it('lists the same kinds the scene drapes', () => {
    const catalog = [
      layer({ id: 1, name: 'OpenStreetMap' }),
      layer({ id: 41, layerKind: 'cog', cog }),
      layer({ id: 42, layerKind: 'vector', vector: vector('OpenStreetMap') }),
      layer({ id: 43, layerKind: 'wms' }),
    ];
    expect(catalog.map((entry) => isDrawableIn3d(entry, catalog))).toEqual([true, true, true, false]);
    expect(standInFor(catalog[2]!, catalog)?.id).toBe(1);
  });
});
