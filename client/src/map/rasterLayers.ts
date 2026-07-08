// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import WebGLTileLayer from 'ol/layer/WebGLTile';
import GeoTIFF from 'ol/source/GeoTIFF';
import type { RasterMapInfo } from '../api/hooks.ts';

// One WebGL tile layer per visible georeferenced map, keyed by map id. The COG is
// streamed straight from the signed delivery URL via HTTP range requests.

const RASTER_LAYER_PREFIX = 'raster:';

/**
 * Reconciles raster overlays with the wanted set and applies per-map opacity.
 * The source URL embeds a rotating token, so an existing layer is kept as long as the
 * map id matches — sources are only created when the layer first appears.
 */
export function syncRasterLayers(
  map: Map,
  rasters: RasterMapInfo[],
  visibleIds: ReadonlySet<string>,
  opacityById: ReadonlyMap<string, number>,
): void {
  const wanted = new globalThis.Map(
    rasters.filter((r) => visibleIds.has(r.id) && r.status === 'ready' && r.cogUrl).map((r) => [r.id, r]),
  );

  for (const layer of [...map.getLayers().getArray()]) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(RASTER_LAYER_PREFIX) && !wanted.has(id.slice(RASTER_LAYER_PREFIX.length))) {
      map.removeLayer(layer);
    }
  }

  for (const [rasterId, raster] of wanted) {
    const layerId = RASTER_LAYER_PREFIX + rasterId;
    const opacity = opacityById.get(rasterId) ?? Number(raster.defaultOpacity);
    const existing = map.getLayers().getArray().find((l) => l.get('id') === layerId);
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
      zIndex: 5, // above base maps, under all vector overlays
    });
    layer.set('id', layerId);
    map.addLayer(layer);
  }
}
