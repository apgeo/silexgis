// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import {
  createTileQueue,
  DRAWN_TILE_BUDGET,
  forgetDrawnTiles,
  fromMercator,
  levelForResolution,
  levelResolution,
  MERCATOR_HALF_WORLD,
  tileExtent,
  toMercator,
} from './renderedTiles3d.ts';
import type { Scene3DTileImage } from './scene3dEngine.ts';

afterEach(() => forgetDrawnTiles());

describe('where a tile of the pyramid lies', () => {
  it('counts rows from the north, as every z/x/y template does', () => {
    expect(tileExtent(0, 0, 0)).toEqual([-MERCATOR_HALF_WORLD, -MERCATOR_HALF_WORLD, MERCATOR_HALF_WORLD, MERCATOR_HALF_WORLD]);
    expect(tileExtent(1, 0, 1)).toEqual([0, 0, MERCATOR_HALF_WORLD, MERCATOR_HALF_WORLD]);
  });

  it('round-trips a position through web mercator', () => {
    const [lon, lat] = fromMercator(...toMercator(25.3, 45.7));
    expect(lon).toBeCloseTo(25.3, 9);
    expect(lat).toBeCloseTo(45.7, 9);
  });

  it('stops at the level whose pixels are as fine as the source’s', () => {
    expect(levelForResolution(levelResolution(13, 256), 256)).toBe(13);
    expect(levelForResolution(levelResolution(13, 256) * 0.9, 256)).toBe(14);
    expect(levelForResolution(levelResolution(13, 256) * 1.5, 256)).toBe(13);
    expect(levelForResolution(0, 256)).toBe(0);
  });
});

describe('the tiles drawn at once and the ones kept', () => {
  function deferredDraws() {
    const started: { key: string; resolve: (image: Scene3DTileImage) => void; signal: AbortSignal }[] = [];
    const draw = (x: number, y: number, level: number, signal: AbortSignal) =>
      new Promise<Scene3DTileImage>((resolve) => started.push({ key: `${level}/${x}/${y}`, resolve, signal }));
    return { started, draw };
  }

  it('says "later" past its bound, and shares the work of a tile asked for twice', () => {
    const { started, draw } = deferredDraws();
    const queue = createTileQueue({ sourceKey: 'a', concurrency: 2, draw });
    const first = queue.request(0, 0, 1);
    expect(queue.request(1, 0, 1)).toBeDefined();
    expect(queue.request(0, 1, 1)).toBeUndefined();
    expect(queue.request(0, 0, 1)).toBe(first);
    expect(started).toHaveLength(2);
    expect(queue.inFlight()).toBe(2);
  });

  it('keeps a drawn tile for the session, across queues drawing the same thing', async () => {
    const { started, draw } = deferredDraws();
    const tile = {} as HTMLCanvasElement;
    const queue = createTileQueue({ sourceKey: 'same', concurrency: 2, draw });
    const asked = queue.request(3, 4, 5)!;
    started[0]!.resolve(tile);
    await expect(asked).resolves.toBe(tile);
    expect(queue.inFlight()).toBe(0);

    // A scene rebuilt draws the same file into a new queue, and finds the tile already drawn.
    const again = createTileQueue({ sourceKey: 'same', concurrency: 2, draw });
    await expect(again.request(3, 4, 5)).resolves.toBe(tile);
    expect(started).toHaveLength(1);
  });

  it('lets go of the oldest tiles past its budget', async () => {
    const draw = () => Promise.resolve({} as HTMLCanvasElement);
    const queue = createTileQueue({ sourceKey: 'many', concurrency: DRAWN_TILE_BUDGET + 10, draw });
    const firstTile = await queue.request(0, 0, 20)!;
    for (let x = 1; x <= DRAWN_TILE_BUDGET; x++) {
      await queue.request(x, 0, 20);
    }
    // The first has been pushed out; asked again, it is drawn again rather than recalled.
    const redrawn = await queue.request(0, 0, 20)!;
    expect(redrawn).not.toBe(firstTile);
  });

  it('aborts what is in flight when destroyed, and keeps nothing it drew afterwards', async () => {
    const { started, draw } = deferredDraws();
    const queue = createTileQueue({ sourceKey: 'gone', concurrency: 2, draw });
    const asked = queue.request(0, 0, 1)!;
    queue.destroy();
    expect(started[0]!.signal.aborted).toBe(true);
    started[0]!.resolve({} as HTMLCanvasElement);
    await asked;
    await expect(queue.request(0, 0, 1)).rejects.toThrow();
    const fresh = createTileQueue({ sourceKey: 'gone', concurrency: 2, draw });
    fresh.request(0, 0, 1);
    expect(started).toHaveLength(2);
  });
});
