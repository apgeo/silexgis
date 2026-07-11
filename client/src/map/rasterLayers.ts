// SPDX-License-Identifier: AGPL-3.0-or-later
import WebGLTileLayer from 'ol/layer/WebGLTile';
import GeoTIFF from 'ol/source/GeoTIFF';
import type { RasterMapInfo } from '../api/hooks.ts';
import { getOverlayGroup } from './mapContext.ts';

// One WebGL tile layer per visible georeferenced map, keyed by map id, living in
// the shared overlay group. The COG is streamed straight from the signed delivery
// URL via HTTP range requests.

export const RASTER_LAYER_PREFIX = 'raster:';

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

    const layer = new WebGLTileLayer({
      source: new GeoTIFF({
        sources: [{ url: raster.cogUrl! }],
        convertToRGB: true,
      }),
      opacity,
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
