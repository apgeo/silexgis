// SPDX-License-Identifier: AGPL-3.0-or-later
import OlMap from 'ol/Map';
import View from 'ol/View';
import type BaseLayer from 'ol/layer/Base';
import type { MapLayerInfo } from '../api/hooks.ts';
import { createVectorLayer } from '../map/vectorLayers.ts';
import {
  createTileCanvas,
  createTileQueue,
  levelResolution,
  tileExtent,
  type RenderedSourceReport,
} from './renderedTiles3d.ts';
import type { Scene3DTileImage, Scene3DTileSource, Scene3DTileSourceInfo } from './scene3dEngine.ts';

// A vector background from the layer catalogue, drawn on the globe in its own style.
//
// The globe engine draws neither vector tiles in a MapLibre style nor labels, and a second style
// renderer would be a second home for every rule the flat map applies to a style — which source
// layer stands for the entry's data, which field a label reads in the reader's language. So the
// flat map's own layer for the entry is used, as it stands: each tile the globe asks for is
// rendered by OpenLayers into an off-screen map exactly covering that tile, and the picture is
// handed over — the georeferenced-map overlay's idea, applied tile by tile.
//
// Labels are drawn into the tiles, so they lie on the ground and turn with it. Each tile is
// rendered with a margin around it and cut out of the middle, so a label near an edge is drawn
// whole in both tiles it straddles and meets itself at the seam. What the margin cannot do is make
// two renders agree on which of two crowded labels to leave out: where they disagree, one tile
// shows a label its neighbour dropped, cut at the seam. Tiles of different levels side by side —
// the globe's ordinary state while it loads, and permanently at the horizon of a tilted view —
// show labels at the sizes of their own levels.

/** Side of a tile handed to the globe. */
const TILE_SIZE = 256;

/** The margin each tile is rendered with, so a label near its edge is drawn whole. */
const LABEL_MARGIN = 64;

/**
 * Tiles rendered at once, per background: one, by one off-screen map. Rendering is work on the
 * page's own thread, so a second map would not render faster; it would only fetch every vector
 * tile a second time into a cache of its own. The data tiles one render needs are fetched in
 * parallel within it, and kept for the next.
 */
const CONCURRENT_RENDERS = 1;

/** A tile that has not finished rendering in this long has failed: its data never came. */
const RENDER_TIMEOUT_MILLISECONDS = 20_000;

/** Rendered tiles that failed in a row, with none succeeding, before the background gives up. */
const FAILURES_BEFORE_GIVING_UP = 3;

/**
 * How far past the last level its tiles hold a background is rendered for the globe. A vector
 * tile enlarged stays sharp, which a picture enlarged by the globe does not, so the globe is
 * given a few levels more than the data has before it enlarges what it was given.
 */
const SHARP_LEVELS_PAST_THE_DATA = 3;

/** The deepest level any background is rendered at; past it the globe enlarges. */
const DEEPEST_LEVEL = 19;

/**
 * The flat map's OpenLayers layer for a vector entry, in the reader's language, resolved once its
 * data and style are in place. Rejects when either could not be loaded.
 */
export type VectorLayerBuilder = (entry: MapLayerInfo, language: string) => Promise<BaseLayer>;

/**
 * The flat map's own builder, shown. Where the flat map would still draw the shapes in its plain
 * default look because the style did not arrive, the scene does not: it drapes the entry's
 * stand-in, or says the background is not drawn — a background that looks nothing like the one
 * chosen is not the same background.
 */
async function flatMapLayer(entry: MapLayerInfo, language: string): Promise<BaseLayer> {
  if (!entry.vector) {
    throw new Error('the entry carries no vector options');
  }
  const { layer, ready } = createVectorLayer({ ...entry, vector: entry.vector }, { language, visible: true });
  if (!(await ready)) {
    layer.dispose();
    throw new Error('the style or the data of the background could not be loaded');
  }
  return layer;
}

/** Whether the scene draws this vector entry's style itself, rather than leaving it to a stand-in. */
export function drawsVectorStyle(entry: MapLayerInfo): boolean {
  return entry.layerKind === 'vector' && Boolean(entry.vector);
}

/** The deepest level a background is rendered at for the globe. */
export function vectorMaximumLevel(entry: Pick<MapLayerInfo, 'maxZoom'> & { vector: MapLayerInfo['vector'] }): number {
  if (entry.vector?.format === 'geojson') {
    return DEEPEST_LEVEL;
  }
  const last = entry.maxZoom && entry.maxZoom > 0 ? entry.maxZoom : 14;
  return Math.min(DEEPEST_LEVEL, last + SHARP_LEVELS_PAST_THE_DATA);
}

/** One off-screen map, rendering one tile at a time. */
interface TileRenderer {
  render(x: number, y: number, level: number, signal: AbortSignal): Promise<Scene3DTileImage>;
  dispose(): void;
}

/**
 * An off-screen map the size of a tile and its margin. Off-screen rather than hidden: a map with
 * no layout box renders nothing, and one far outside the viewport is laid out and never seen.
 */
function createTileRenderer(layer: BaseLayer): TileRenderer {
  const side = TILE_SIZE + 2 * LABEL_MARGIN;
  const container = document.createElement('div');
  container.style.width = `${side}px`;
  container.style.height = `${side}px`;
  container.style.position = 'absolute';
  container.style.left = '-10000px';
  container.style.top = '0';
  container.setAttribute('aria-hidden', 'true');
  document.body.appendChild(container);
  const map = new OlMap({
    target: container,
    controls: [],
    interactions: [],
    // One canvas pixel per tile pixel, whatever the screen: the globe asks for a tile of a size.
    pixelRatio: 1,
    layers: [layer],
    view: new View({ projection: 'EPSG:3857', center: [0, 0], resolution: levelResolution(0, TILE_SIZE) }),
  });

  return {
    render(x, y, level, signal) {
      const [west, south, east, north] = tileExtent(x, y, level);
      map.getView().setCenter([(west + east) / 2, (south + north) / 2]);
      map.getView().setResolution(levelResolution(level, TILE_SIZE));
      return new Promise<Scene3DTileImage>((resolve, reject) => {
        const timer = window.setTimeout(() => {
          map.un('rendercomplete', done);
          reject(new Error('the tile did not finish rendering'));
        }, RENDER_TIMEOUT_MILLISECONDS);
        const abort = () => {
          window.clearTimeout(timer);
          map.un('rendercomplete', done);
          reject(new Error('the source was closed'));
        };
        signal.addEventListener('abort', abort, { once: true });
        function done() {
          window.clearTimeout(timer);
          signal.removeEventListener('abort', abort);
          resolve(cutOut(container));
        }
        map.once('rendercomplete', done);
        map.renderSync();
      });
    },
    dispose() {
      map.setTarget(undefined);
      map.dispose();
      container.remove();
    },
  };
}

/** The tile out of the middle of a rendered map: every layer canvas, as the map composed it. */
function cutOut(container: HTMLElement): HTMLCanvasElement {
  const tile = createTileCanvas(TILE_SIZE);
  const context = tile.getContext('2d');
  if (!context) {
    return tile;
  }
  container.querySelectorAll<HTMLCanvasElement>('.ol-layer canvas, canvas.ol-layer').forEach((canvas) => {
    if (canvas.width === 0) {
      return;
    }
    const opacity = (canvas.parentNode as HTMLElement | null)?.style.opacity || canvas.style.opacity;
    context.globalAlpha = opacity === '' ? 1 : Number(opacity);
    const matrix = /^matrix\(([^(]*)\)$/.exec(canvas.style.transform)?.[1]?.split(',').map(Number);
    const [a, b, c, d, e, f] = matrix && matrix.length === 6 ? matrix : [1, 0, 0, 1, 0, 0];
    context.setTransform(a!, b!, c!, d!, e! - LABEL_MARGIN, f! - LABEL_MARGIN);
    context.drawImage(canvas, 0, 0);
  });
  context.globalAlpha = 1;
  context.setTransform(1, 0, 0, 1, 0, 0);
  return tile;
}

export interface VectorTileSourceDependencies {
  /** The flat map's layer for the entry; the flat map's own builder unless a test passes one. */
  build?: VectorLayerBuilder;
  /** One off-screen map per concurrent render; a test passes a stand-in that needs no canvas. */
  createRenderer?: (layer: BaseLayer) => TileRenderer;
}

/**
 * The scene's tile source for one vector background. It says when it cannot draw — the style or
 * the data cannot be loaded, or its tiles keep failing — so the scene can drape the entry's
 * stand-in in its place or the layer list can say why nothing is drawn.
 */
export function createVectorTileSource(
  entry: MapLayerInfo,
  language: string,
  report: RenderedSourceReport,
  dependencies: VectorTileSourceDependencies = {},
): Scene3DTileSource {
  const build = dependencies.build ?? flatMapLayer;
  const createRenderer = dependencies.createRenderer ?? createTileRenderer;
  let renderer: TileRenderer | undefined;
  let failuresInARow = 0;
  let succeeded = false;
  let gaveUp = false;
  let closed = false;

  const fail = () => {
    if (!gaveUp && !closed) {
      gaveUp = true;
      report({ state: 'unavailable', code: 'vector.unreadable' });
    }
  };

  const queue = createTileQueue({
    sourceKey: `vector:${entry.urlTemplate}:${JSON.stringify(entry.vector)}:${language}`,
    concurrency: CONCURRENT_RENDERS,
    async draw(x, y, level, signal) {
      if (!renderer) {
        throw new Error('the background has not opened');
      }
      try {
        const tile = await renderer.render(x, y, level, signal);
        failuresInARow = 0;
        succeeded = true;
        return tile;
      } catch (error) {
        failuresInARow += 1;
        // Only a background that never drew gives up: once tiles have come, one that fails is a
        // tile, and the engine shows its parent there.
        if (!succeeded && failuresInARow >= FAILURES_BEFORE_GIVING_UP) {
          fail();
        }
        throw error;
      }
    },
  });

  return {
    async open(): Promise<Scene3DTileSourceInfo> {
      // The first layer is built here, so a style that cannot be loaded is found before the globe
      // asks for a tile, and the stand-in can take over without a tile having been tried.
      let layer: BaseLayer;
      try {
        layer = await build(entry, language);
      } catch (error) {
        fail();
        throw error;
      }
      if (closed) {
        throw new Error('the source was closed');
      }
      renderer = createRenderer(layer);
      report({ state: 'ready' });
      return { maximumLevel: vectorMaximumLevel(entry), tileSize: TILE_SIZE };
    },
    renderTile(x, y, level) {
      return gaveUp ? Promise.reject(new Error('the background does not draw')) : queue.request(x, y, level);
    },
    destroy() {
      closed = true;
      queue.destroy();
      renderer?.dispose();
      renderer = undefined;
    },
  };
}
