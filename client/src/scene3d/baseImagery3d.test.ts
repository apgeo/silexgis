// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { MapLayerInfo } from '../api/hooks.ts';
import { baseImageryLayerId, setActiveBaseImagery, syncBaseImagery } from './baseImagery3d.ts';
import type { Scene3DImagery, Scene3DImageryOptions } from './scene3dEngine.ts';

// A plain object standing in for the scene — the point of the engine contract is that everything
// around it is testable without a graphics context.
class FakeImagery implements Scene3DImagery {
  readonly layers = new Map<string, Scene3DImageryOptions & { visible: boolean; opacity: number }>();

  addImageryLayer(id: string, options: Scene3DImageryOptions) {
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

function layer(overrides: Partial<MapLayerInfo> & { id: number }): MapLayerInfo {
  return {
    name: `Layer ${overrides.id}`,
    layerKind: 'xyz',
    urlTemplate: `https://tiles.example/${overrides.id}/{z}/{x}/{y}.png`,
    options: null,
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
    syncBaseImagery(engine, [layer({ id: 1 }), layer({ id: 2 })], 2);

    expect(engine.getImageryLayerIds()).toEqual([baseImageryLayerId(1), baseImageryLayerId(2)]);
    expect(engine.layers.get(baseImageryLayerId(1))!.visible).toBe(false);
    expect(engine.layers.get(baseImageryLayerId(2))!.visible).toBe(true);
  });

  it('carries the catalog URL template and attribution through unchanged', () => {
    const engine = new FakeImagery();
    syncBaseImagery(
      engine,
      [layer({ id: 7, urlTemplate: 'https://t/{z}/{y}/{x}', attribution: '© Someone' })],
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
      [
        layer({ id: 1, layerKind: 'wms' }),
        layer({ id: 2, layerKind: 'vector' }),
        layer({ id: 3, isBase: false }),
        layer({ id: 4 }),
      ],
      4,
    );

    expect(engine.getImageryLayerIds()).toEqual([baseImageryLayerId(4)]);
  });

  it('normalizes ids that arrive as strings from the generated contract', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, [layer({ id: '11' as unknown as number })], 11);

    expect(engine.hasImageryLayer(baseImageryLayerId(11))).toBe(true);
    expect(engine.layers.get(baseImageryLayerId(11))!.visible).toBe(true);
  });

  it('is idempotent — a second sync neither duplicates nor recreates layers', () => {
    const engine = new FakeImagery();
    const catalog = [layer({ id: 1 }), layer({ id: 2 })];
    syncBaseImagery(engine, catalog, 1);
    engine.setImageryLayerOpacity(baseImageryLayerId(1), 0.4);
    syncBaseImagery(engine, catalog, 1);

    expect(engine.getImageryLayerIds()).toHaveLength(2);
    // The layer was updated in place, not rebuilt: its opacity survived.
    expect(engine.layers.get(baseImageryLayerId(1))!.opacity).toBe(0.4);
  });
});

describe('setActiveBaseImagery', () => {
  it('leaves layers that did not come from the catalog alone', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, [layer({ id: 1 })], 1);
    engine.addImageryLayer('overlay:hillshade', { urlTemplate: 'https://t/{z}/{x}/{y}' });

    setActiveBaseImagery(engine, 1);

    expect(engine.layers.get('overlay:hillshade')!.visible).toBe(true);
  });

  it('hides every basemap when the active id names none of them', () => {
    const engine = new FakeImagery();
    syncBaseImagery(engine, [layer({ id: 1 }), layer({ id: 2 })], 1);

    setActiveBaseImagery(engine, 99);

    expect([...engine.layers.values()].every((l) => !l.visible)).toBe(true);
  });
});
