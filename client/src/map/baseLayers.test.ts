// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import Map from 'ol/Map';
import View from 'ol/View';
import type TileLayer from 'ol/layer/Tile';
import TileWMS from 'ol/source/TileWMS';
import WMTS from 'ol/source/WMTS';
import XYZ from 'ol/source/XYZ';
import type { MapLayerInfo } from '../api/hooks.ts';
import {
  documentBackgroundName,
  isDrawable,
  setActiveBaseLayer,
  syncBaseLayers,
  syncTileOverlays,
  tileCrossOrigin,
  tileSourceFor,
  unreadableTileLayers,
  wmtsMatrixIds,
  type DrawableLayer,
} from './baseLayers.ts';

function entry(overrides: Partial<MapLayerInfo>): DrawableLayer {
  return {
    layerKind: 'xyz',
    urlTemplate: 'https://tiles.example.invalid/{z}/{x}/{y}.png',
    wms: null,
    wmts: null,
    attribution: '© Somebody',
    minZoom: 0,
    maxZoom: 18,
    ...overrides,
  };
}

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
    // Named in the vocabulary, drawn by nothing yet: never offered.
    expect(isDrawable(entry({ layerKind: 'vector' }))).toBe(false);
    expect(isDrawable(entry({ layerKind: 'cog' }))).toBe(false);
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
    syncBaseLayers(map, catalog, activeId);
    syncTileOverlays(map, catalog, new Set(catalog.filter((e) => !e.isBase).map((e) => Number(e.id))), {});
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
