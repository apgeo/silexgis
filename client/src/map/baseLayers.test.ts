// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import TileWMS from 'ol/source/TileWMS';
import WMTS from 'ol/source/WMTS';
import XYZ from 'ol/source/XYZ';
import type { MapLayerInfo } from '../api/hooks.ts';
import { isDrawable, tileCrossOrigin, tileSourceFor, wmtsMatrixIds, type DrawableLayer } from './baseLayers.ts';

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

  it('applies the same rule to every kind of source, so a map service allowed into documents exports too', () => {
    // The attribute is protected on the source; read it the way the tile loader does.
    const crossOriginOf = (source: unknown) => (source as { crossOrigin: string | null }).crossOrigin;
    for (const drawable of [entry({}), entry({ layerKind: 'wms', wms }), entry({ layerKind: 'wmts', wmts })]) {
      expect(crossOriginOf(tileSourceFor({ ...drawable, inDocuments: true }))).toBe('anonymous');
      expect(crossOriginOf(tileSourceFor({ ...drawable, inDocuments: false }))).toBeNull();
    }
  });
});
