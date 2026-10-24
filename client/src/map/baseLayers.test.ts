// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import VectorTileLayer from 'ol/layer/VectorTile';
import WebGLTileLayer from 'ol/layer/WebGLTile';
import VectorTileSource from 'ol/source/VectorTile';
import { Style } from 'ol/style';
import TileWMS from 'ol/source/TileWMS';
import WMTS from 'ol/source/WMTS';
import XYZ from 'ol/source/XYZ';
import type { MapLayerInfo } from '../api/hooks.ts';
import { isHitTestable } from './hitTesting.ts';
import {
  documentBackgroundName,
  getBaseLayers,
  isCatalogueLayer,
  isDrawable,
  isDrawableOnMap,
  mirrorBackground,
  setActiveBaseLayer,
  syncBaseLayers,
  syncTileOverlays,
  tileCrossOrigin,
  tileSourceFor,
  unreadableTileLayers,
  wmtsMatrixIds,
  type DrawableLayer,
} from './baseLayers.ts';

// The real source starts reading its file the moment it is made; this one stays quiet.
vi.mock('ol/source/GeoTIFF', async () => {
  const { default: DataTile } = await import('ol/source/DataTile');
  return {
    default: class extends DataTile {
      constructor() {
        super({});
      }
    },
  };
});

function entry(overrides: Partial<MapLayerInfo>): DrawableLayer {
  return {
    layerKind: 'xyz',
    urlTemplate: 'https://tiles.example.invalid/{z}/{x}/{y}.png',
    wms: null,
    wmts: null,
    cog: null,
    vector: null,
    attribution: '© Somebody',
    minZoom: 0,
    maxZoom: 18,
    ...overrides,
  };
}

const vectorOptions = { format: 'mvt', style: null, styleSource: null, labelField: null, scene3dStandIn: null } as const;
const cogOptions = { bands: null, nodata: null, min: null, max: null, projection: 'EPSG:3857', projectionDefinition: null };

const wms = {
  layers: 'topo,contours',
  styles: '',
  format: 'image/png',
  version: '1.3.0',
  transparent: true,
  crs: 'EPSG:3857',
};

const wmts = {
  layer: 'topo',
  matrixSet: 'google3857',
  format: 'image/png',
  style: 'default',
  crs: 'EPSG:3857',
  matrixIdPrefix: '',
};

describe('which catalogue entries the map can draw', () => {
  it('draws a tile template, and a map service only with the options its kind needs', () => {
    expect(isDrawable(entry({}))).toBe(true);
    expect(isDrawable(entry({ layerKind: 'wms', wms }))).toBe(true);
    expect(isDrawable(entry({ layerKind: 'wmts', wmts }))).toBe(true);
    expect(isDrawable(entry({ layerKind: 'wms' }))).toBe(false);
    expect(isDrawable(entry({ layerKind: 'wmts', wms }))).toBe(false);
    // Drawn by the flat map, not yet draped by the 3D scene, which asks this.
    expect(isDrawable(entry({ layerKind: 'vector', vector: vectorOptions }))).toBe(false);
    expect(isDrawable(entry({ layerKind: 'cog', cog: cogOptions }))).toBe(false);
  });

  it('draws a GeoTIFF and a vector source on the flat map only with the options their kind needs', () => {
    expect(isDrawableOnMap(entry({}))).toBe(true);
    expect(isDrawableOnMap(entry({ layerKind: 'wms', wms }))).toBe(true);
    expect(isDrawableOnMap(entry({ layerKind: 'cog', cog: cogOptions }))).toBe(true);
    expect(isDrawableOnMap(entry({ layerKind: 'vector', vector: vectorOptions }))).toBe(true);
    expect(isDrawableOnMap(entry({ layerKind: 'cog' }))).toBe(false);
    expect(isDrawableOnMap(entry({ layerKind: 'vector', cog: cogOptions }))).toBe(false);
  });
});

describe('the source each kind is drawn from', () => {
  it('asks a tile template for z/x/y tiles up to its last zoom', () => {
    const source = tileSourceFor(entry({ maxZoom: 17 }));
    expect(source).toBeInstanceOf(XYZ);
    expect(source.getTileGrid()!.getMaxZoom()).toBe(17);
  });

  it('asks a WMS for one picture per tile with the catalogue’s parameters', () => {
    const source = tileSourceFor(entry({ layerKind: 'wms', urlTemplate: 'https://maps.example.invalid/wms', wms }));
    expect(source).toBeInstanceOf(TileWMS);
    const params = (source as TileWMS).getParams();
    expect(params).toMatchObject({
      LAYERS: 'topo,contours',
      STYLES: '',
      FORMAT: 'image/png',
      VERSION: '1.3.0',
      TRANSPARENT: 'TRUE',
    });
    expect((source as TileWMS).getUrls()).toEqual(['https://maps.example.invalid/wms']);
    expect(source.getProjection()!.getCode()).toBe('EPSG:3857');
  });

  it('asks a WMS that offers only longitude and latitude in that, and lets the map reproject it', () => {
    const source = tileSourceFor(entry({ layerKind: 'wms', wms: { ...wms, crs: 'EPSG:4326', transparent: false } }));
    expect(source.getProjection()!.getCode()).toBe('EPSG:4326');
    expect((source as TileWMS).getParams().TRANSPARENT).toBe('FALSE');
  });

  it('builds a web-mercator WMTS grid from the matrix set and the last zoom, by its REST template', () => {
    const source = tileSourceFor(
      entry({
        layerKind: 'wmts',
        urlTemplate: 'https://tiles.example.invalid/wmts/{TileMatrix}/{TileRow}/{TileCol}.png',
        wmts,
        maxZoom: 16,
      }),
    ) as WMTS;
    expect(source).toBeInstanceOf(WMTS);
    expect(source.getRequestEncoding()).toBe('REST');
    expect(source.getLayer()).toBe('topo');
    expect(source.getMatrixSet()).toBe('google3857');
    const grid = source.getTileGrid()!;
    // Level 0 is one 256-pixel tile over the whole web-mercator world.
    expect(grid.getResolution(0)).toBeCloseTo(40075016.68557849 / 256, 3);
    expect(grid.getMaxZoom()).toBe(16);
    expect((grid as unknown as { getMatrixIds(): string[] }).getMatrixIds()).toEqual(wmtsMatrixIds('', 16));
  });

  it('builds a geographic WMTS grid of two level-zero tiles, by its key-value address', () => {
    const source = tileSourceFor(
      entry({
        layerKind: 'wmts',
        urlTemplate: 'https://tiles.example.invalid/wmts',
        wmts: { ...wmts, matrixSet: 'WorldCRS84Quad', crs: 'EPSG:4326', matrixIdPrefix: 'EPSG:4326:' },
        maxZoom: 3,
      }),
    ) as WMTS;
    expect(source.getRequestEncoding()).toBe('KVP');
    expect(source.getProjection()!.getCode()).toBe('EPSG:4326');
    const grid = source.getTileGrid()!;
    expect(grid.getResolution(0)).toBeCloseTo(180 / 256, 9);
    expect(grid.getOrigin(0)).toEqual([-180, 90]);
    expect((grid as unknown as { getMatrixIds(): string[] }).getMatrixIds()).toEqual([
      'EPSG:4326:0',
      'EPSG:4326:1',
      'EPSG:4326:2',
      'EPSG:4326:3',
    ]);
  });
});

describe('tileCrossOrigin', () => {
  it('asks a source allowed into documents for its tiles with leave to read them back', () => {
    // Without it, one of its tiles on the map makes the whole canvas unreadable, and exporting the
    // map as an image fails with the browser's security error.
    expect(tileCrossOrigin({ inDocuments: true })).toBe('anonymous');
  });

  it('asks any other source the ordinary way, so a server without cross-origin answers still draws', () => {
    expect(tileCrossOrigin({ inDocuments: false })).toBeUndefined();
    // A shared view's background carries no such flag, and is asked the ordinary way too.
    expect(tileCrossOrigin({})).toBeUndefined();
  });

  it('lets the flag alone decide, whatever kind of source it is', () => {
    // The attribute is protected on the source; read it the way the tile loader does.
    const crossOriginOf = (source: unknown) => (source as { crossOrigin: string | null }).crossOrigin;
    for (const drawable of [entry({}), entry({ layerKind: 'wms', wms }), entry({ layerKind: 'wmts', wmts })]) {
      expect(crossOriginOf(tileSourceFor({ ...drawable, inDocuments: true }))).toBe('anonymous');
      expect(crossOriginOf(tileSourceFor({ ...drawable, inDocuments: false }))).toBeNull();
    }
  });
});

describe('a picture of the map and the layers that cannot go into one', () => {
  const catalogEntry = (id: number, over: Partial<MapLayerInfo>): MapLayerInfo => ({
    id,
    name: `Layer ${id}`,
    layerKind: 'xyz',
    urlTemplate: `https://tiles.example.org/${id}/{z}/{x}/{y}.png`,
    wms: null,
    wmts: null,
    cog: null,
    vector: null,
    attribution: null,
    groupName: null,
    minZoom: 0,
    maxZoom: 19,
    isBase: true,
    isDefault: false,
    sortOrder: id,
    inDocuments: true,
    ...over,
  });

  const mapWith = (catalog: MapLayerInfo[], activeId: number) => {
    const map = new Map({ view: new View({ center: [0, 0], zoom: 3 }) });
    syncBaseLayers(map, catalog, activeId, 'ro');
    syncTileOverlays(map, catalog, new Set(catalog.filter((e) => !e.isBase).map((e) => Number(e.id))), {}, 'ro');
    return map;
  };

  const layerNamed = (map: Map, name: string) =>
    map.getLayers().getArray().find((layer) => layer.get('name') === name) as TileLayer<XYZ>;

  it('names the layers on screen a picture cannot read — the background or an overlay', () => {
    const map = mapWith(
      [
        catalogEntry(1, { name: 'Topographic', inDocuments: false }),
        catalogEntry(2, { name: 'Street map' }),
        catalogEntry(3, { name: 'Hiking routes', isBase: false, inDocuments: false }),
      ],
      1,
    );
    expect(unreadableTileLayers(map)).toEqual(['Topographic', 'Hiking routes']);

    setActiveBaseLayer(map, 2);
    expect(unreadableTileLayers(map)).toEqual(['Hiking routes']);
  });

  it('draws a background on screen whose server turns out not to allow cross-origin reads', () => {
    const map = mapWith([catalogEntry(1, { name: 'Street map' })], 1);
    const layer = layerNamed(map, 'Street map');
    const asked = layer.getSource()!;

    asked.dispatchEvent('tileloaderror');

    // Asked plainly from then on, so it draws; and named if an export is attempted over it.
    expect(layer.getSource()).not.toBe(asked);
    expect(unreadableTileLayers(map)).toEqual(['Street map']);
  });

  it('takes a failed tile after one has loaded as a missing tile, not a refusal', () => {
    const map = mapWith([catalogEntry(1, { name: 'Street map' })], 1);
    const layer = layerNamed(map, 'Street map');
    const asked = layer.getSource()!;

    asked.dispatchEvent('tileloadend');
    asked.dispatchEvent('tileloaderror');

    expect(layer.getSource()).toBe(asked);
    expect(unreadableTileLayers(map)).toEqual([]);
  });

  it('points to the installation’s own first background allowed in documents, or to none', () => {
    expect(
      documentBackgroundName([
        catalogEntry(1, { name: 'Topographic', inDocuments: false }),
        catalogEntry(2, { name: 'Orthophoto' }),
        catalogEntry(3, { name: 'Street map' }),
      ]),
    ).toBe('Orthophoto');
    expect(documentBackgroundName([catalogEntry(1, { inDocuments: false }), catalogEntry(2, { isBase: false })])).toBeUndefined();
  });
});

describe('GeoTIFF and vector entries among the flat map\u2019s layers', () => {
  const catalogEntry = (id: number, over: Partial<MapLayerInfo>): MapLayerInfo => ({
    id,
    name: `Layer ${id}`,
    layerKind: 'xyz',
    urlTemplate: `https://tiles.example.org/${id}/{z}/{x}/{y}.png`,
    wms: null,
    wmts: null,
    cog: null,
    vector: null,
    attribution: '© Somebody',
    groupName: null,
    minZoom: 0,
    maxZoom: 18,
    isBase: true,
    isDefault: false,
    sortOrder: id,
    inDocuments: false,
    ...over,
  });
  const catalog = [
    catalogEntry(1, { name: 'Tiles' }),
    catalogEntry(2, { name: 'Ortho', layerKind: 'cog', urlTemplate: 'https://data.example.org/ortho.tif', cog: cogOptions }),
    catalogEntry(3, { name: 'Vector', layerKind: 'vector', urlTemplate: 'https://v.example.org/{z}/{x}/{y}.pbf', vector: vectorOptions }),
    catalogEntry(4, {
      name: 'Boundary',
      isBase: false,
      layerKind: 'vector',
      urlTemplate: 'https://v.example.org/natura.geojson',
      vector: { ...vectorOptions, format: 'geojson' },
    }),
  ];
  const layerNamed = (map: Map, name: string) => map.getLayers().getArray().find((layer) => layer.get('name') === name)!;

  it('builds each as the layer its kind needs, and none makes a picture of the map unreadable', () => {
    const map = new Map({ view: new View({ center: [0, 0], zoom: 3 }) });
    syncBaseLayers(map, catalog, 2, 'ro');
    syncTileOverlays(map, catalog, new Set([4]), {}, 'ro');

    expect(layerNamed(map, 'Tiles')).toBeInstanceOf(TileLayer);
    expect(layerNamed(map, 'Ortho')).toBeInstanceOf(WebGLTileLayer);
    expect(layerNamed(map, 'Vector')).toBeInstanceOf(VectorTileLayer);
    expect(layerNamed(map, 'Boundary')).toBeInstanceOf(VectorLayer);
    expect(getBaseLayers(map).map((layer) => layer.get('name'))).toEqual(['Tiles', 'Ortho', 'Vector']);
    // Their bytes are fetched with the server's cross-origin leave or not at all.
    expect(unreadableTileLayers(map)).toEqual([]);
    setActiveBaseLayer(map, 3);
    expect(unreadableTileLayers(map)).toEqual([]);
  });

  it('builds a vector layer again, in its place, when the reader changes language — and nothing else', () => {
    const map = new Map({ view: new View({ center: [0, 0], zoom: 3 }) });
    syncBaseLayers(map, catalog, 1, 'ro');
    syncTileOverlays(map, catalog, new Set([4]), { 4: 0.4 }, 'ro');
    const tiles = layerNamed(map, 'Tiles');
    const vector = layerNamed(map, 'Vector');
    const boundary = layerNamed(map, 'Boundary');

    syncBaseLayers(map, catalog, 1, 'en');
    syncTileOverlays(map, catalog, new Set([4]), { 4: 0.4 }, 'en');

    expect(layerNamed(map, 'Tiles')).toBe(tiles);
    expect(layerNamed(map, 'Vector')).not.toBe(vector);
    expect(layerNamed(map, 'Boundary')).not.toBe(boundary);
    expect(layerNamed(map, 'Boundary').getOpacity()).toBe(0.4);
    expect(getBaseLayers(map).map((layer) => layer.get('name'))).toEqual(['Tiles', 'Ortho', 'Vector']);
    expect(map.getLayers().getLength()).toBe(4);
  });

  it('keeps every catalogue layer out of hit tests, so a background\u2019s roads are never picked', () => {
    const map = new Map({ view: new View({ center: [0, 0], zoom: 3 }) });
    syncBaseLayers(map, catalog, 3, 'ro');
    syncTileOverlays(map, catalog, new Set([4]), {}, 'ro');
    const data = new VectorLayer({});

    for (const name of ['Tiles', 'Ortho', 'Vector', 'Boundary']) {
      expect(isCatalogueLayer(layerNamed(map, name))).toBe(true);
      expect(isHitTestable(layerNamed(map, name))).toBe(false);
    }
    expect(isCatalogueLayer(data)).toBe(false);
    expect(isHitTestable(data)).toBe(true);
  });

  it('mirrors each kind for the overview inset from the same source, a vector one following its style as it arrives', () => {
    const webgl = new WebGLTileLayer({ visible: true });
    expect(mirrorBackground(webgl).mirror).toBeInstanceOf(WebGLTileLayer);

    const vector = new VectorTileLayer({ visible: false });
    const { mirror, keys } = mirrorBackground(vector);
    expect(mirror).toBeInstanceOf(VectorTileLayer);
    expect(mirror.getVisible()).toBe(false);
    expect(keys.length).toBeGreaterThan(0);

    const source = new VectorTileSource({ url: 'https://v.example.org/{z}/{x}/{y}.pbf' });
    const style = new Style({});
    vector.setSource(source);
    vector.setStyle(style);
    vector.setBackground('#f8f4f0');
    expect((mirror as VectorTileLayer).getSource()).toBe(source);
    expect((mirror as VectorTileLayer).getStyle()).toBe(style);
    expect((mirror as VectorTileLayer).getBackground()).toBe('#f8f4f0');
  });
});
