// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import WebGLTileLayer from 'ol/layer/WebGLTile';
import { expressionToGlsl } from 'ol/render/webgl/compileUtil.js';
import { newCompilationContext } from 'ol/expr/gpu.js';
import { ColorType } from 'ol/expr/expression.js';
import type { CavePotentialResultInfo } from '../api/hooks/cavePotential.ts';
import { getOverlayGroup } from './mapContext.ts';
import {
  CAVE_POTENTIAL_LAYER_PREFIX,
  cavePotentialLayerId,
  cavePotentialStyle,
  syncCavePotentialLayers,
} from './cavePotentialLayers.ts';

// The real source opens the file the moment it is built, which a test has no server to answer.
// The stand-in records the options it was built with, which is the thing under test.
vi.mock('ol/source/GeoTIFF', async () => {
  const actual = await vi.importActual<typeof import('ol/source/DataTile')>('ol/source/DataTile');
  return {
    default: class GeoTIFFStub extends actual.default {
      readonly options: Record<string, unknown>;
      constructor(options: Record<string, unknown>) {
        super({ loader: () => new Float32Array(1), bandCount: 1 });
        this.options = options;
      }
    },
  };
});

function resultInfo(overrides: Partial<CavePotentialResultInfo> = {}): CavePotentialResultInfo {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    modelId: '22222222-2222-2222-2222-222222222222',
    modelName: 'Invented model',
    method: 'directWeights',
    definition: null,
    weighting: null,
    areaFeatureId: '33333333-3333-3333-3333-333333333333',
    areaName: 'Invented area',
    cellMeters: 50,
    status: 'ready',
    errorCode: null,
    message: null,
    usesPositions: false,
    evidenceFeatures: 0,
    protectedFeatures: 0,
    summary: null,
    west: 0,
    south: 0,
    east: 1,
    north: 1,
    columns: 10,
    rows: 10,
    sizeBytes: 1024,
    noData: -9999,
    mine: true,
    requestedByName: null,
    ranAsFullAdministrator: false,
    terrainBuildId: null,
    terrainBuildActive: null,
    terrainInputs: [],
    mayDelete: true,
    url: '/api/v1/cave-potential/results/x/content?token=first',
    computedAt: '2026-01-01T00:00:00Z',
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function sourceOptions(layer: WebGLTileLayer): Record<string, unknown> {
  return (layer.getSource() as unknown as { options: Record<string, unknown> }).options;
}

function layers(): WebGLTileLayer[] {
  return getOverlayGroup().getLayers().getArray() as WebGLTileLayer[];
}

afterEach(() => {
  getOverlayGroup().getLayers().clear();
});

describe('cave-potential map layers', () => {
  it('draws a ready result that is switched on, read as stored values at the nearest cell', () => {
    const info = resultInfo();
    syncCavePotentialLayers([info], new Set([info.id]), new Map());

    expect(layers()).toHaveLength(1);
    expect(layers()[0].get('id')).toBe(`${CAVE_POTENTIAL_LAYER_PREFIX}${info.id}`);
    expect(sourceOptions(layers()[0])).toEqual({
      sources: [{ url: info.url }],
      normalize: false,
      interpolate: false,
    });
  });

  it('draws nothing for a run that is not finished, or that nobody switched on', () => {
    const queued = resultInfo({ status: 'queued', url: null });
    const ready = resultInfo({ id: '44444444-4444-4444-4444-444444444444' });
    syncCavePotentialLayers([queued, ready], new Set([queued.id]), new Map());
    expect(layers()).toHaveLength(0);
  });

  it('takes a map away when it is switched off or no longer listed, and leaves other overlays alone', () => {
    const info = resultInfo();
    const other = new WebGLTileLayer({});
    other.set('id', 'terrain-derivative:abc:1:7');
    getOverlayGroup().getLayers().push(other);

    syncCavePotentialLayers([info], new Set([info.id]), new Map());
    expect(layers()).toHaveLength(2);

    // No longer listed: deleted, or no longer the reader's to read.
    syncCavePotentialLayers([], new Set([info.id]), new Map());
    expect(layers().map((l) => l.get('id'))).toEqual(['terrain-derivative:abc:1:7']);
  });

  it('gives an existing layer the refreshed address and keeps a transparency set by hand', () => {
    const info = resultInfo();
    const opacity = new Map([[cavePotentialLayerId(info.id), 0.4]]);
    syncCavePotentialLayers([info], new Set([info.id]), opacity);
    const first = layers()[0];
    expect(first.getOpacity()).toBe(0.4);

    const refreshed = resultInfo({ url: '/api/v1/cave-potential/results/x/content?token=second' });
    syncCavePotentialLayers([refreshed], new Set([info.id]), opacity);

    expect(layers()).toHaveLength(1);
    expect(layers()[0]).toBe(first);
    expect(first.getOpacity()).toBe(0.4);
    expect(sourceOptions(first).sources).toEqual([{ url: refreshed.url }]);
  });

  it('colours with an expression the drawing hardware can compile', () => {
    // Read by the shader compiler and nowhere else, so a mistake in it is a blank layer on a
    // reader's machine rather than a failure anywhere a check would see it.
    const style = cavePotentialStyle();
    expect(style.color).toBeDefined();
    const context = { ...newCompilationContext(), bandCount: 2 };
    expect(typeof expressionToGlsl(context, style.color!, ColorType)).toBe('string');
  });
});
