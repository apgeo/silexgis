// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { RasterMapInfo } from '../api/hooks.ts';
import { rasterBounds, rasterImageryLayerId, syncRasterImagery } from './rasterOverlay3d.ts';
import type {
  Scene3DBounds,
  Scene3DImageOverlayOptions,
  Scene3DImagery,
  Scene3DImageryOptions,
} from './scene3dEngine.ts';

/**
 * The scene as a plain object. Flattening a raster needs a WebGL context and a real GeoTIFF, so it
 * is substituted — what is under test here is the reconciliation, which is where the expensive
 * mistakes are: rendering something twice, rendering something nobody asked for, or leaving a
 * picture on the globe after its file is gone.
 */
class FakeImagery implements Scene3DImagery {
  readonly layers = new Map<
    string,
    Partial<Scene3DImageryOptions & Scene3DImageOverlayOptions> & { visible: boolean; opacity: number }
  >();

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

/** A square footprint, as the server's polygon envelope arrives. */
function raster(overrides: Partial<RasterMapInfo> & { id: string }): RasterMapInfo {
  return {
    name: `Sheet ${overrides.id}`,
    status: 'ready',
    cogUrl: `https://files.example/${overrides.id}.tif`,
    bbox: { type: 'Polygon', coordinates: [[[25.5, 45.5], [25.6, 45.5], [25.6, 45.6], [25.5, 45.6], [25.5, 45.5]]] },
    defaultOpacity: 1,
    attribution: null,
    ...overrides,
  } as unknown as RasterMapInfo;
}

const stub = () => vi.fn(async (_url: string, _bounds: Scene3DBounds) => 'data:image/png;base64,AAAA');

describe('rasterBounds', () => {
  it('reads the footprint corners out of the server polygon', () => {
    expect(rasterBounds(raster({ id: 'a' }))).toEqual([25.5, 45.5, 25.6, 45.6]);
  });

  it('refuses a footprint with no area', () => {
    // The engine would take a zero-width rectangle and draw an invisible layer, which looks
    // exactly like a raster that failed to load — and sends whoever investigates to the wrong file.
    const degenerate = raster({
      id: 'a',
      bbox: { type: 'Polygon', coordinates: [[[25.5, 45.5], [25.5, 45.5], [25.5, 45.5]]] },
    } as Partial<RasterMapInfo> & { id: string });
    expect(rasterBounds(degenerate)).toBeUndefined();
  });

  it('refuses a missing or malformed footprint rather than guessing one', () => {
    expect(rasterBounds(raster({ id: 'a', bbox: null } as Partial<RasterMapInfo> & { id: string }))).toBeUndefined();
    expect(
      rasterBounds(raster({ id: 'a', bbox: { type: 'Polygon', coordinates: [[['x', 1]]] } } as never)),
    ).toBeUndefined();
  });
});

describe('syncRasterImagery', () => {
  it('renders nothing until somebody asks to see it', async () => {
    // The case that matters for a group with twenty scanned sheets: opening the scene must not
    // spend twenty WebGL renders and twenty megabytes of data URLs on pictures nobody switched on.
    const engine = new FakeImagery();
    const rasterize = stub();
    await syncRasterImagery(engine, [raster({ id: 'a' }), raster({ id: 'b' })], new Set(), {}, rasterize);

    expect(rasterize).not.toHaveBeenCalled();
    expect(engine.getImageryLayerIds()).toEqual([]);
  });

  it('drapes a wanted raster on the ground its footprint names', async () => {
    const engine = new FakeImagery();
    await syncRasterImagery(engine, [raster({ id: 'a' })], new Set(['a']), {}, stub());

    const layer = engine.layers.get(rasterImageryLayerId('a'));
    expect(layer?.visible).toBe(true);
    expect(layer?.bounds).toEqual([25.5, 45.5, 25.6, 45.6]);
  });

  it('does not render the same raster twice when it is switched off and on', async () => {
    // Flattening one takes seconds and the result never changes, so the layer is kept and hidden.
    const engine = new FakeImagery();
    const rasterize = stub();
    const sheets = [raster({ id: 'a' })];
    await syncRasterImagery(engine, sheets, new Set(['a']), {}, rasterize);
    await syncRasterImagery(engine, sheets, new Set(), {}, rasterize);
    await syncRasterImagery(engine, sheets, new Set(['a']), {}, rasterize);

    expect(rasterize).toHaveBeenCalledTimes(1);
    expect(engine.layers.get(rasterImageryLayerId('a'))?.visible).toBe(true);
  });

  it('drops a raster that has left the catalogue', async () => {
    // Deleted, or no longer readable by this viewer. Either way it must come off the globe, not
    // merely stop being listed in the panel.
    const engine = new FakeImagery();
    await syncRasterImagery(engine, [raster({ id: 'a' })], new Set(['a']), {}, stub());
    await syncRasterImagery(engine, [], new Set(), {}, stub());

    expect(engine.getImageryLayerIds()).toEqual([]);
  });

  it('leaves basemaps and tile overlays alone', async () => {
    // Same id space, different prefixes. Without that separation, dropping a deleted raster would
    // take the basemap with it.
    const engine = new FakeImagery();
    engine.addImageryLayer('base:1', { urlTemplate: 'https://tiles.example/{z}/{x}/{y}.png' });
    engine.addImageryLayer('tile-overlay:2', { urlTemplate: 'https://tiles.example/{z}/{x}/{y}.png' });
    await syncRasterImagery(engine, [], new Set(), {}, stub());

    expect(engine.getImageryLayerIds()).toEqual(['base:1', 'tile-overlay:2']);
  });

  it('skips a raster whose conversion has not finished, and one with no footprint', async () => {
    const engine = new FakeImagery();
    const rasterize = stub();
    await syncRasterImagery(
      engine,
      [
        raster({ id: 'pending', cogUrl: null } as Partial<RasterMapInfo> & { id: string }),
        raster({ id: 'nobbox', bbox: null } as Partial<RasterMapInfo> & { id: string }),
      ],
      new Set(['pending', 'nobbox']),
      {},
      rasterize,
    );

    expect(rasterize).not.toHaveBeenCalled();
    expect(engine.getImageryLayerIds()).toEqual([]);
  });

  it('keeps the globe usable when one sheet cannot be rendered', async () => {
    // The flat map draws the same raster by a route that does not go through here, so a failure is
    // a scene missing one overlay rather than a raster nobody can see — and never a rejected
    // promise reaching the effect that called this.
    const engine = new FakeImagery();
    const failing = vi.fn(async () => {
      throw new Error('not a COG');
    });
    await expect(
      syncRasterImagery(engine, [raster({ id: 'a' })], new Set(['a']), {}, failing),
    ).resolves.toBeUndefined();
    expect(engine.getImageryLayerIds()).toEqual([]);
  });

  it("applies the sheet's own default opacity, and a viewer override over it", async () => {
    const engine = new FakeImagery();
    await syncRasterImagery(
      engine,
      [raster({ id: 'a', defaultOpacity: 0.6 } as Partial<RasterMapInfo> & { id: string })],
      new Set(['a']),
      {},
      stub(),
    );
    expect(engine.layers.get(rasterImageryLayerId('a'))?.opacity).toBe(0.6);

    await syncRasterImagery(
      engine,
      [raster({ id: 'a', defaultOpacity: 0.6 } as Partial<RasterMapInfo> & { id: string })],
      new Set(['a']),
      { a: 0.25 },
      stub(),
    );
    expect(engine.layers.get(rasterImageryLayerId('a'))?.opacity).toBe(0.25);
  });
});
