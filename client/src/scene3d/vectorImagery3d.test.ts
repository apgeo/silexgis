// SPDX-License-Identifier: AGPL-3.0-or-later
import type BaseLayer from 'ol/layer/Base';
import { afterEach, describe, expect, it } from 'vitest';
import type { MapLayerInfo } from '../api/hooks.ts';
import { forgetDrawnTiles, type RenderedSourceStatus } from './renderedTiles3d.ts';
import type { Scene3DTileImage } from './scene3dEngine.ts';
import { createVectorTileSource, vectorMaximumLevel } from './vectorImagery3d.ts';

// The rules a vector background follows in the scene — when it opens, how much it renders at once,
// when it gives up so its stand-in can take over — with the flat map's layer and the off-screen
// render stood in for: what those draw is the browser specs' to show.

afterEach(() => forgetDrawnTiles());

function entry(overrides: Partial<MapLayerInfo> = {}): MapLayerInfo {
  return {
    id: 42,
    name: 'Liberty',
    layerKind: 'vector',
    urlTemplate: `https://tiles.example/${Math.random()}`,
    wms: null,
    wmts: null,
    cog: null,
    vector: { format: 'mvt', style: 'https://styles.example/liberty.json', styleSource: null, labelField: null, scene3dStandIn: null },
    attribution: null,
    groupName: null,
    minZoom: 0,
    maxZoom: 14,
    isBase: true,
    isDefault: false,
    sortOrder: 0,
    inDocuments: false,
    ...overrides,
  } as MapLayerInfo;
}

/** A renderer whose tiles the test answers, one at a time, in order. */
function stoodInRenderer() {
  const pending: { resolve: (tile: Scene3DTileImage) => void; reject: (error: Error) => void; tile: string }[] = [];
  let disposed = 0;
  const createRenderer = () => ({
    render: (x: number, y: number, level: number) =>
      new Promise<Scene3DTileImage>((resolve, reject) => pending.push({ resolve, reject, tile: `${level}/${x}/${y}` })),
    dispose: () => {
      disposed += 1;
    },
  });
  return { pending, createRenderer, disposed: () => disposed };
}

const layer = {} as BaseLayer;

describe('a vector background in the scene', () => {
  it('opens once the flat map’s layer for it is built, and says it is ready', async () => {
    const reports: RenderedSourceStatus[] = [];
    const built: string[] = [];
    const source = createVectorTileSource(entry(), 'ro', (status) => reports.push(status), {
      build: async (_entry, language) => {
        built.push(language);
        return layer;
      },
      createRenderer: stoodInRenderer().createRenderer,
    });
    const info = await source.open();
    expect(built).toEqual(['ro']);
    expect(info).toEqual({ maximumLevel: 17, tileSize: 256 });
    expect(reports).toEqual([{ state: 'ready' }]);
  });

  it('says a style that cannot be loaded is not drawn, before any tile is asked for', async () => {
    const reports: RenderedSourceStatus[] = [];
    const source = createVectorTileSource(entry(), 'en', (status) => reports.push(status), {
      build: () => Promise.reject(new Error('style 404')),
      createRenderer: stoodInRenderer().createRenderer,
    });
    await expect(source.open()).rejects.toThrow('style 404');
    expect(reports).toEqual([{ state: 'unavailable', code: 'vector.unreadable' }]);
  });

  it('renders one tile at a time, and says "later" to the rest', async () => {
    const renderer = stoodInRenderer();
    const source = createVectorTileSource(entry(), 'en', () => undefined, { build: async () => layer, createRenderer: renderer.createRenderer });
    await source.open();
    const first = source.renderTile(1, 2, 3);
    expect(source.renderTile(2, 2, 3)).toBeUndefined();
    const tile = {} as HTMLCanvasElement;
    await Promise.resolve();
    renderer.pending[0]!.resolve(tile);
    await expect(first).resolves.toBe(tile);
    expect(source.renderTile(2, 2, 3)).toBeDefined();
  });

  it('gives up when its first tiles all fail, so its stand-in can take over', async () => {
    const reports: RenderedSourceStatus[] = [];
    const renderer = stoodInRenderer();
    const source = createVectorTileSource(entry(), 'en', (status) => reports.push(status), {
      build: async () => layer,
      createRenderer: renderer.createRenderer,
    });
    await source.open();
    for (let x = 0; x < 3; x++) {
      const asked = source.renderTile(x, 0, 5)!;
      await Promise.resolve();
      renderer.pending.at(-1)!.reject(new Error('no data'));
      await expect(asked).rejects.toThrow();
    }
    expect(reports).toEqual([{ state: 'ready' }, { state: 'unavailable', code: 'vector.unreadable' }]);
    await expect(source.renderTile(9, 9, 5)).rejects.toThrow();
  });

  it('keeps drawing once it has drawn, whatever single tiles then do', async () => {
    const reports: RenderedSourceStatus[] = [];
    const renderer = stoodInRenderer();
    const source = createVectorTileSource(entry(), 'en', (status) => reports.push(status), {
      build: async () => layer,
      createRenderer: renderer.createRenderer,
    });
    await source.open();
    const drawn = source.renderTile(0, 0, 5)!;
    await Promise.resolve();
    renderer.pending[0]!.resolve({} as HTMLCanvasElement);
    await drawn;
    for (let x = 1; x < 5; x++) {
      const asked = source.renderTile(x, 0, 5)!;
      await Promise.resolve();
      renderer.pending.at(-1)!.reject(new Error('one tile'));
      await expect(asked).rejects.toThrow();
    }
    expect(reports).toEqual([{ state: 'ready' }]);
  });

  it('lets go of its off-screen map when destroyed', async () => {
    const renderer = stoodInRenderer();
    const source = createVectorTileSource(entry(), 'en', () => undefined, { build: async () => layer, createRenderer: renderer.createRenderer });
    await source.open();
    source.destroy();
    expect(renderer.disposed()).toBe(1);
  });

  it('is rendered a few levels past its data, a single file as deep as any background', () => {
    expect(vectorMaximumLevel(entry({ maxZoom: 14 }))).toBe(17);
    expect(vectorMaximumLevel(entry({ maxZoom: 18 }))).toBe(19);
    expect(
      vectorMaximumLevel(entry({ vector: { format: 'geojson', style: null, styleSource: null, labelField: null, scene3dStandIn: null } })),
    ).toBe(19);
  });
});
