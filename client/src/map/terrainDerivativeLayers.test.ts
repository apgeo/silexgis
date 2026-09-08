// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import WebGLTileLayer from 'ol/layer/WebGLTile';
import type { TerrainDerivativeLayerInfo } from '../api/hooks.ts';
import { getOverlayGroup } from './mapContext.ts';
import { expressionToGlsl } from 'ol/render/webgl/compileUtil.js';
import { newCompilationContext } from 'ol/expr/gpu.js';
import { ColorType } from 'ol/expr/expression.js';
import {
  TERRAIN_DERIVATIVE_LAYER_PREFIX,
  syncTerrainDerivativeLayers,
  terrainDerivativeIdOf,
  terrainDerivativeStyle,
} from './terrainDerivativeLayers.ts';

// The real source opens the file the moment it is built, which a test has no server to answer.
// The stand-in is a data source of the same shape, so the layer is a real layer and only the
// fetching is absent; what it records is the options it was built with, which is the thing under
// test — a layer rebuilt with a fresh address, and a single-band picture not read as a photograph.
vi.mock('ol/source/GeoTIFF', async () => {
  const actual = await vi.importActual<typeof import('ol/source/DataTile')>('ol/source/DataTile');
  return {
    default: class GeoTIFFStub extends actual.default {
      readonly options: Record<string, unknown>;
      constructor(options: Record<string, unknown>) {
        super({ loader: () => new Uint8Array(4), bandCount: 4 });
        this.options = options;
      }
    },
  };
});

function layerInfo(overrides: Partial<TerrainDerivativeLayerInfo> = {}): TerrainDerivativeLayerInfo {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    terrainBuildId: '22222222-2222-2222-2222-222222222222',
    derivative: 'hillshade',
    name: 'Shaded relief',
    settings: '{}',
    status: 'ready',
    errorCode: null,
    message: null,
    version: 1,
    sizeBytes: 1024,
    stale: false,
    computedAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    rasters: [
      {
        id: 7,
        west: 0,
        south: 0,
        east: 1,
        north: 1,
        width: 10,
        height: 10,
        pixelSizeDegrees: 0.1,
        sizeBytes: 1024,
        url: '/api/v1/terrain/derivatives/x/rasters/7/content?token=first',
      },
    ],
    ...overrides,
  };
}

function sourceOptions(layer: WebGLTileLayer): Record<string, unknown> {
  return (layer.getSource() as unknown as { options: Record<string, unknown> }).options;
}

function onlyLayer(): WebGLTileLayer {
  const layers = getOverlayGroup().getLayers().getArray();
  expect(layers).toHaveLength(1);
  return layers[0] as WebGLTileLayer;
}

afterEach(() => {
  getOverlayGroup().getLayers().clear();
});

describe('computed terrain layers', () => {
  it('names the picture a layer belongs to, whatever raster of it the layer draws', () => {
    expect(terrainDerivativeIdOf(`${TERRAIN_DERIVATIVE_LAYER_PREFIX}abc:3:7`)).toBe('abc');
    expect(terrainDerivativeIdOf('raster:abc')).toBeUndefined();
  });

  it('keeps a transparency set by hand when the listing is re-read', () => {
    const info = layerInfo();
    const visible = new Set([info.id]);
    syncTerrainDerivativeLayers([info], visible, new Map([[info.id, 0.4]]));
    expect(onlyLayer().getOpacity()).toBe(0.4);

    // The same picture, re-read with a fresh address: the transparency survives it.
    const refreshed = layerInfo({
      rasters: [{ ...info.rasters[0], url: '/api/v1/terrain/derivatives/x/rasters/7/content?token=second' }],
    });
    syncTerrainDerivativeLayers([refreshed], visible, new Map([[info.id, 0.4]]));
    expect(onlyLayer().getOpacity()).toBe(0.4);
  });

  it('gives an existing layer the refreshed address, because the old one stops being honoured', () => {
    const info = layerInfo();
    const visible = new Set([info.id]);
    syncTerrainDerivativeLayers([info], visible, new Map());
    const first = onlyLayer();
    expect(sourceOptions(first).sources).toEqual([
      { url: '/api/v1/terrain/derivatives/x/rasters/7/content?token=first' },
    ]);

    const refreshed = layerInfo({
      rasters: [{ ...info.rasters[0], url: '/api/v1/terrain/derivatives/x/rasters/7/content?token=second' }],
    });
    syncTerrainDerivativeLayers([refreshed], visible, new Map());

    const after = onlyLayer();
    expect(after).toBe(first); // the same layer, not a replacement
    expect(sourceOptions(after).sources).toEqual([
      { url: '/api/v1/terrain/derivatives/x/rasters/7/content?token=second' },
    ]);
  });

  it('leaves the source alone when the address has not changed', () => {
    const info = layerInfo();
    const visible = new Set([info.id]);
    syncTerrainDerivativeLayers([info], visible, new Map());
    const source = onlyLayer().getSource();
    syncTerrainDerivativeLayers([info], visible, new Map());
    expect(onlyLayer().getSource()).toBe(source);
  });

  it('reads shaded relief as a picture and steepness as measurements', () => {
    const shaded = layerInfo();
    syncTerrainDerivativeLayers([shaded], new Set([shaded.id]), new Map());
    expect(sourceOptions(onlyLayer()).convertToRGB).toBe(true);
    getOverlayGroup().getLayers().clear();

    // A slope raster is one band of degrees. Read as a picture it would be drawn against the range
    // of its number type and come out black everywhere, with nothing raised to say so.
    const steepness = layerInfo({ id: '33333333-3333-3333-3333-333333333333', derivative: 'slope' });
    syncTerrainDerivativeLayers([steepness], new Set([steepness.id]), new Map());
    const options = sourceOptions(onlyLayer());
    expect(options.convertToRGB).toBeUndefined();
    expect(options.sources).toEqual([{ url: steepness.rasters[0].url, min: 0, max: 90 }]);
  });

  it('removes a picture that is no longer wanted', () => {
    const info = layerInfo();
    syncTerrainDerivativeLayers([info], new Set([info.id]), new Map());
    expect(getOverlayGroup().getLayers().getLength()).toBe(1);
    syncTerrainDerivativeLayers([info], new Set(), new Map());
    expect(getOverlayGroup().getLayers().getLength()).toBe(0);
  });

  it('states its colour ramps in a form the drawing hardware accepts', () => {
    // Compiled here because there is nowhere else it happens off a reader's machine: the ramps are
    // turned into a shader at first draw, and a malformed one is an error in the browser rather
    // than anything a build or a type would catch.
    for (const kind of ['slope', 'aspect', 'ruggednessIndex', 'positionIndex', 'roughness'] as const) {
      const style = terrainDerivativeStyle(kind);
      expect(style?.color).toBeDefined();
      const context = { ...newCompilationContext(), bandCount: 2 };
      expect(typeof expressionToGlsl(context, style!.color, ColorType)).toBe('string');
    }

    // Shaded relief and coloured relief arrive as pictures and are drawn as they are.
    expect(terrainDerivativeStyle('hillshade')).toBeUndefined();
    expect(terrainDerivativeStyle('colourRelief')).toBeUndefined();
  });
});
