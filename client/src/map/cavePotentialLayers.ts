// SPDX-License-Identifier: AGPL-3.0-or-later
import WebGLTileLayer, { type Style as WebGLTileStyle } from 'ol/layer/WebGLTile';
import GeoTIFF from 'ol/source/GeoTIFF';
import type { CavePotentialResultInfo } from '../api/hooks/cavePotential.ts';
import { getOverlayGroup } from './mapContext.ts';

/**
 * The favourability maps of cave-potential runs, drawn over the map.
 *
 * A prefix of their own: every family of overlay is reconciled by a pass that removes each layer
 * carrying its prefix that it does not want, so sharing one with the pictures of the ground would
 * have each pass delete the other's layers on every render.
 */
export const CAVE_POTENTIAL_LAYER_PREFIX = 'cave-potential:';

/** The id of the one map layer a result is drawn as. Also the key its transparency is kept under. */
export function cavePotentialLayerId(resultId: string): string {
  return `${CAVE_POTENTIAL_LAYER_PREFIX}${resultId}`;
}

/**
 * The colours favourability is drawn in, from the least favourable ground to the most, as the
 * stops of one ramp. The legend is painted from the same list, so the key cannot drift from the
 * map it explains.
 *
 * Pale to dark along a single hue family, so the order reads without the key and survives being
 * printed in grey; it is deliberately not a green-to-red ramp, which says "safe" and "danger"
 * about something that is neither.
 */
export const CAVE_POTENTIAL_STOPS: readonly (readonly [number, string])[] = [
  [0, '#ffffcc'],
  [0.25, '#a1dab4'],
  [0.5, '#41b6c4'],
  [0.75, '#2c7fb8'],
  [1, '#253494'],
];

/**
 * How a favourability map is coloured. The band is read as the stored numbers, nought to one, and
 * a cell holding the no-answer value is left clear: outside the area, and wherever a layer of the
 * model had nothing to say, the map beneath shows through instead of a colour meaning "unlikely".
 */
export function cavePotentialStyle(): WebGLTileStyle {
  return {
    color: [
      'case',
      ['<', ['band', 1], 0],
      [0, 0, 0, 0],
      ['interpolate', ['linear'], ['band', 1], ...CAVE_POTENTIAL_STOPS.flatMap(([at, colour]) => [at, colour])],
    ],
  };
}

/**
 * Read as the stored values rather than stretched to a display range, and at the nearest cell: a
 * blend between a scored cell and the large negative no-answer value beside it would be a number
 * that is neither, and the edge of every area would be drawn as a rim of the wrong colour.
 */
function sourceFor(url: string): GeoTIFF {
  return new GeoTIFF({ sources: [{ url }], normalize: false, interpolate: false });
}

/** Reconciles the favourability maps on the map with the wanted set and applies their opacity. */
export function syncCavePotentialLayers(
  results: CavePotentialResultInfo[],
  visibleIds: ReadonlySet<string>,
  opacityByLayerId: ReadonlyMap<string, number>,
): void {
  const wanted = new globalThis.Map<string, { result: CavePotentialResultInfo; url: string }>();
  for (const result of results) {
    if (result.status !== 'ready' || !result.url || !visibleIds.has(result.id)) continue;
    wanted.set(cavePotentialLayerId(result.id), { result, url: result.url });
  }

  const overlays = getOverlayGroup().getLayers();
  for (const layer of [...overlays.getArray()]) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(CAVE_POTENTIAL_LAYER_PREFIX) && !wanted.has(id)) {
      overlays.remove(layer);
    }
  }

  for (const [layerId, { result, url }] of wanted) {
    const opacity = opacityByLayerId.get(layerId) ?? 0.7;
    const existing = overlays.getArray().find((l) => l.get('id') === layerId);
    if (existing) {
      existing.setOpacity(opacity);

      // The signature in the address expires, which is why the list is re-read while the page is
      // open; a layer that kept the address it was built with would stop filling in as somebody
      // panned, with nothing on screen saying why. Rebuilt only when the address has changed.
      if (existing.get('url') !== url) {
        existing.set('url', url);
        (existing as WebGLTileLayer).setSource(sourceFor(url));
      }
      continue;
    }

    const layer = new WebGLTileLayer({ source: sourceFor(url), style: cavePotentialStyle(), opacity });
    layer.set('id', layerId);
    layer.set('name', result.modelName ?? '');
    layer.set('url', url);

    // Beneath every vector overlay, like the pictures of the ground: caves, entrances and outlines
    // are read against the map, not hidden by it.
    overlays.insertAt(0, layer);
  }
}
