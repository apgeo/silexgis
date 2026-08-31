// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import WebGLTileLayer from 'ol/layer/WebGLTile';
import GeoTIFF from 'ol/source/GeoTIFF';
import { transformExtent } from 'ol/proj';
import type { RasterMapInfo } from '../api/hooks.ts';
import type { Scene3DBounds, Scene3DImagery } from './scene3dEngine.ts';

/**
 * Drawing this installation's georeferenced maps — scanned survey sheets, old cadastral plans —
 * on the globe, the way the flat map already draws them.
 *
 * <p>The awkward part is the format. These are cloud-optimised GeoTIFFs, and the 2D map reads one
 * directly: OpenLayers has a source that range-requests the pyramid inside the file and hands the
 * tiles to a WebGL layer. A globe engine has nothing equivalent, and the alternatives were both
 * worse than they look. Serving XYZ tiles cut from the raster would mean a new server endpoint
 * over the same bytes — which means a second place deciding who may see a cave's map, when the
 * signed address handed out here has already decided it. Fetching the whole GeoTIFF and decoding
 * it by hand would mean a second, worse implementation of what OpenLayers already does correctly.</p>
 *
 * <p>So the raster is rendered ONCE, by OpenLayers, into a detached map that is never shown to
 * anybody, and the resulting picture is pinned to the ground the raster covers. No new endpoint,
 * no second access decision, no second GeoTIFF reader: the one signed URL the viewer was already
 * entitled to is read by the one component that knows how.</p>
 *
 * <p>What it costs is resolution. The picture is flattened at a fixed size, so zooming right in on
 * the globe shows a softer image than the flat map does at the same place, where the pyramid is
 * still being read level by level. That is a real limitation and it is the reason the flat map's
 * path was not changed to match: for reading a survey sheet closely, the 2D view is still the one
 * that answers.</p>
 */

/** Namespaced so a raster can never collide with a basemap or a tile overlay. */
const RASTER_LAYER_PREFIX = 'raster:';

/**
 * The side, in pixels, the picture is flattened to.
 *
 * Matches what the engine is told to expect. Two thousand and forty-eight is the largest size that
 * is a single texture on every GPU worth targeting — including software rendering, which is what
 * this actually runs on where there is no graphics card — and going higher buys detail that the
 * next limitation up the chain immediately throws away.
 */
const RASTER_SIZE = 2048;

/** Scene layer id for a georeferenced map. */
export function rasterImageryLayerId(rasterId: string): string {
  return `${RASTER_LAYER_PREFIX}${rasterId}`;
}

/** The lon/lat box a raster covers, from the footprint the server computed for it. */
export function rasterBounds(raster: RasterMapInfo): Scene3DBounds | undefined {
  const bbox = raster.bbox as { coordinates?: unknown } | null | undefined;
  const rings = bbox?.coordinates;
  if (!Array.isArray(rings) || rings.length === 0) {
    return undefined;
  }
  const ring = rings[0];
  if (!Array.isArray(ring) || ring.length === 0) {
    return undefined;
  }

  let west = Infinity;
  let south = Infinity;
  let east = -Infinity;
  let north = -Infinity;
  for (const point of ring) {
    if (!Array.isArray(point) || typeof point[0] !== 'number' || typeof point[1] !== 'number') {
      return undefined;
    }
    west = Math.min(west, point[0]);
    east = Math.max(east, point[0]);
    south = Math.min(south, point[1]);
    north = Math.max(north, point[1]);
  }

  // A footprint with no area cannot be draped on anything: the engine would be handed a rectangle
  // of zero width and draw an invisible layer, which is indistinguishable from a raster that
  // failed to load.
  return west < east && south < north ? [west, south, east, north] : undefined;
}

/**
 * Renders a COG to a data URL covering exactly its own footprint.
 *
 * The detached map is sized and positioned so that the rendered canvas IS the bounding box, corner
 * to corner — the picture is pinned to those same corners on the globe, so any disagreement
 * between what was drawn and what was claimed shows up as a survey sheet sitting beside the ground
 * it belongs to. Hence a view fitted to the extent with no padding and no rotation, and a
 * resolution computed from the extent rather than chosen.
 */
export async function rasterizeCog(cogUrl: string, bounds: Scene3DBounds): Promise<string> {
  const [west, south, east, north] = bounds;
  const extent = transformExtent([west, south, east, north], 'EPSG:4326', 'EPSG:3857');
  const width = extent[2] - extent[0];
  const height = extent[3] - extent[1];
  // The longer side gets the full resolution and the shorter side is scaled to match, so the
  // picture keeps the footprint's aspect ratio rather than being stretched into a square.
  const scale = RASTER_SIZE / Math.max(width, height);
  const pixelWidth = Math.max(1, Math.round(width * scale));
  const pixelHeight = Math.max(1, Math.round(height * scale));

  const container = document.createElement('div');
  container.style.width = `${pixelWidth}px`;
  container.style.height = `${pixelHeight}px`;
  // Off-screen rather than hidden: `display: none` gives the canvas no size at all and OpenLayers
  // renders nothing into it, while a positioned element far outside the viewport is laid out
  // normally and simply never seen.
  container.style.position = 'absolute';
  container.style.left = '-10000px';
  container.style.top = '0';
  document.body.appendChild(container);

  const source = new GeoTIFF({ sources: [{ url: cogUrl }], convertToRGB: true, interpolate: false });
  const map = new Map({
    target: container,
    controls: [],
    interactions: [],
    layers: [new WebGLTileLayer({ source })],
    view: new View({ center: [(extent[0] + extent[2]) / 2, (extent[1] + extent[3]) / 2], zoom: 0 }),
  });
  map.getView().fit(extent, { size: [pixelWidth, pixelHeight] });

  try {
    const canvas = await new Promise<HTMLCanvasElement>((resolve, reject) => {
      // Bounded, because `rendercomplete` never fires for a source that fails to load — a bad
      // range request, an expired signature, a file that is not a COG — and an unbounded promise
      // would leave the detached map and its WebGL context alive for the rest of the session.
      const timer = window.setTimeout(() => reject(new Error('timed out rendering the raster')), 30_000);
      map.once('rendercomplete', () => {
        window.clearTimeout(timer);
        const element = map.getViewport().querySelector('canvas');
        if (element instanceof HTMLCanvasElement) {
          resolve(element);
        } else {
          reject(new Error('the raster rendered no canvas'));
        }
      });
      map.renderSync();
    });
    return canvas.toDataURL('image/png');
  } finally {
    // Always, including on the timeout path. A browser allows only a handful of live WebGL
    // contexts, and leaking one per raster is how the 3D scene itself stops being able to start.
    map.setTarget(undefined);
    map.dispose();
    container.remove();
  }
}

/**
 * Brings the scene's georeferenced maps into line with the wanted set.
 *
 * Rendering one is slow and the result never changes, so a raster that has been drawn once is kept
 * and only hidden — turning one off and on again must not pay for it twice. Rasters the viewer can
 * no longer see at all are dropped, because those are gone from the catalogue rather than merely
 * unticked.
 */
export async function syncRasterImagery(
  engine: Scene3DImagery,
  rasters: readonly RasterMapInfo[],
  visibleIds: ReadonlySet<string>,
  opacityById: Readonly<Record<string, number>>,
  // A parameter so the reconciliation rules — what is kept, what is dropped, what is never
  // rendered at all — can be tested without a WebGL context and without a GeoTIFF. Callers pass
  // nothing; only the tests substitute it.
  rasterize: (cogUrl: string, bounds: Scene3DBounds) => Promise<string> = rasterizeCog,
): Promise<void> {
  const known = new Set(rasters.map((r) => r.id));
  for (const layerId of engine.getImageryLayerIds()) {
    if (layerId.startsWith(RASTER_LAYER_PREFIX) && !known.has(layerId.slice(RASTER_LAYER_PREFIX.length))) {
      engine.removeImageryLayer(layerId);
    }
  }

  for (const raster of rasters) {
    const layerId = rasterImageryLayerId(raster.id);
    const wanted = visibleIds.has(raster.id);

    if (engine.hasImageryLayer(layerId)) {
      engine.setImageryLayerVisible(layerId, wanted);
      engine.setImageryLayerOpacity(layerId, opacityById[raster.id] ?? Number(raster.defaultOpacity ?? 1));
      continue;
    }

    // Nothing is rendered until somebody asks to see it. A group with twenty scanned sheets would
    // otherwise spend twenty WebGL renders and twenty megabytes of data URLs on opening the scene,
    // for pictures nobody switched on.
    if (!wanted || !raster.cogUrl) {
      continue;
    }

    const bounds = rasterBounds(raster);
    if (!bounds) {
      continue;
    }

    try {
      const imageUrl = await rasterize(raster.cogUrl, bounds);
      // Checked again after the await: the viewer may have unticked it, or left the scene, during
      // the seconds this took, and adding it now would put a picture on the globe that nothing on
      // screen asked for.
      if (!engine.hasImageryLayer(layerId)) {
        engine.addImageOverlayLayer(layerId, {
          imageUrl,
          bounds,
          attribution: raster.attribution ?? undefined,
          visible: true,
          opacity: opacityById[raster.id] ?? Number(raster.defaultOpacity ?? 1),
        });
      }
    } catch {
      // Left off the globe. The flat map draws the same raster by a route that does not go through
      // here, so a failure is a scene missing one overlay rather than a raster nobody can see.
    }
  }
}
