// SPDX-License-Identifier: AGPL-3.0-or-later
import WebGLTileLayer from 'ol/layer/WebGLTile';
import GeoTIFF from 'ol/source/GeoTIFF';
import type { RasterMapInfo } from '../api/hooks.ts';
import { getOverlayGroup } from './mapContext.ts';
import { resolveProjection } from './projections.ts';

// One WebGL tile layer per visible georeferenced map, keyed by map id, living in
// the shared overlay group. The COG is streamed straight from the signed delivery
// URL via HTTP range requests.

export const RASTER_LAYER_PREFIX = 'raster:';

/** Set on a raster layer whose coordinate system the map could not place: it is never shown. */
export const RASTER_UNPLACEABLE_PROP = 'silexgis:unplaceable';

/**
 * Whether the map can place an uploaded raster: it reads the file's coordinate system and, when it
 * is one the map does not know yet, makes it known from this installation's registry. False for a
 * file whose system has no code or no definition there, or that cannot be read. Never rejects.
 *
 * An uploaded map keeps the coordinate system it came in, so a Romanian scan is usually in
 * Stereo 70, which OpenLayers knows nothing of by itself. Shown before its system is known, such a
 * layer cannot be turned into the map's coordinates at all; shown with a guessed one, it would be
 * drawn somewhere else.
 */
export async function placeRaster(
  source: GeoTIFF,
  resolve: (code: string) => Promise<boolean> = resolveProjection,
): Promise<boolean> {
  try {
    const { projection } = await source.getView();
    const code = typeof projection === 'string' ? projection : projection?.getCode();
    return code ? await resolve(code) : false;
  } catch {
    return false;
  }
}

/**
 * Reconciles raster overlays with the wanted set and applies per-map opacity.
 * The source URL embeds a rotating token, so an existing layer is kept as long as the
 * map id matches — sources are only created when the layer first appears.
 */
export function syncRasterLayers(
  rasters: RasterMapInfo[],
  visibleIds: ReadonlySet<string>,
  opacityById: ReadonlyMap<string, number>,
): void {
  const wanted = new globalThis.Map(
    rasters.filter((r) => visibleIds.has(r.id) && r.status === 'ready' && r.cogUrl).map((r) => [r.id, r]),
  );
  const overlays = getOverlayGroup().getLayers();

  for (const layer of [...overlays.getArray()]) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(RASTER_LAYER_PREFIX) && !wanted.has(id.slice(RASTER_LAYER_PREFIX.length))) {
      overlays.remove(layer);
    }
  }

  for (const [rasterId, raster] of wanted) {
    const layerId = RASTER_LAYER_PREFIX + rasterId;
    const opacity = opacityById.get(rasterId) ?? Number(raster.defaultOpacity);
    const existing = overlays.getArray().find((l) => l.get('id') === layerId);
    if (existing) {
      existing.setOpacity(opacity);
      continue;
    }

    const source = new GeoTIFF({
      sources: [{ url: raster.cogUrl! }],
      convertToRGB: true,
    });
    // Hidden until the map knows where the file goes.
    const layer = new WebGLTileLayer({ source, opacity, visible: false });
    void placeRaster(source).then((placed) => {
      layer.set(RASTER_UNPLACEABLE_PROP, !placed);
      layer.setVisible(placed);
    });
    // Nothing may show it while it cannot be placed — the layer tree's checkbox included.
    layer.on('change:visible', () => {
      if (layer.getVisible() && layer.get(RASTER_UNPLACEABLE_PROP) !== false) {
        layer.setVisible(false);
      }
    });
    layer.set('id', layerId);
    layer.set('name', raster.name);
    // Default stacking slot: bottom of the overlay stack (above base maps only),
    // beneath every vector overlay (bottom→top collection order).
    const insertAt = overlays.getArray().filter((l) =>
      (l.get('id') as string | undefined)?.startsWith(RASTER_LAYER_PREFIX),
    ).length;
    overlays.insertAt(insertAt, layer);
  }
}
