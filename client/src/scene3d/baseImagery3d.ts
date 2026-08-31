// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo } from '../api/hooks.ts';
import type { Scene3DImagery } from './scene3dEngine.ts';

// Basemaps for the 3D scene come from the same server-side layer catalog the 2D map reads, so an
// administrator configures one list and both views follow it. Takes the scene as an argument
// rather than reaching for the shared one, which keeps it testable with a plain object.

/** Namespaced so a scene layer created from the catalog can never collide with a data overlay. */
const BASE_LAYER_PREFIX = 'base:';

/**
 * Namespaced separately from the basemaps, and that separation is what makes the two rules
 * coexist: exactly one basemap is shown, while any number of overlays are, and a single prefix
 * would leave "hide every catalogue layer that is not the chosen one" unable to tell them apart.
 */
const TILE_OVERLAY_PREFIX = 'tile-overlay:';

/** Scene layer id for a catalog entry. */
export function baseImageryLayerId(catalogId: number): string {
  return `${BASE_LAYER_PREFIX}${catalogId}`;
}

/** Scene layer id for a catalogue entry drawn over the basemap. */
export function tileOverlayImageryLayerId(catalogId: number): string {
  return `${TILE_OVERLAY_PREFIX}${catalogId}`;
}

/**
 * Creates any missing base layers from the catalog and leaves `activeId` as the visible one.
 *
 * Only tiled `xyz` entries are used, matching what the 2D map implements; the other kinds the
 * catalog can describe have no client-side implementation anywhere yet and are skipped rather
 * than half-rendered. Every base layer is created up front and only its visibility changes, so
 * switching basemap is instant and each keeps whatever opacity it was given.
 */
export function syncBaseImagery(
  engine: Scene3DImagery,
  catalog: readonly MapLayerInfo[],
  activeId: number,
): void {
  for (const entry of catalog) {
    // int64 ids arrive as number | string from the generated contract — normalize once here.
    const layerId = baseImageryLayerId(Number(entry.id));
    if (entry.layerKind !== 'xyz' || !entry.isBase || engine.hasImageryLayer(layerId)) {
      continue;
    }
    engine.addImageryLayer(layerId, {
      urlTemplate: entry.urlTemplate,
      attribution: entry.attribution ?? undefined,
      visible: false,
    });
  }
  setActiveBaseImagery(engine, activeId);
}

/**
 * Creates any missing tile overlays from the catalogue and leaves exactly the wanted ones showing.
 *
 * Called after {@link syncBaseImagery} and never before it, because a globe composites its imagery
 * in the order the layers were added: an overlay created before the basemap would be drawn
 * underneath an opaque picture of the ground, which is not an error anything reports and not a
 * state anything on screen explains.
 */
export function syncTileOverlayImagery(
  engine: Scene3DImagery,
  catalog: readonly MapLayerInfo[],
  visibleIds: ReadonlySet<number>,
  opacityById: Readonly<Record<number, number>>,
): void {
  for (const entry of catalog) {
    const catalogId = Number(entry.id);
    if (entry.layerKind !== 'xyz' || entry.isBase) {
      continue;
    }
    const layerId = tileOverlayImageryLayerId(catalogId);
    if (!engine.hasImageryLayer(layerId)) {
      engine.addImageryLayer(layerId, {
        urlTemplate: entry.urlTemplate,
        attribution: entry.attribution ?? undefined,
        visible: false,
      });
    }
    engine.setImageryLayerVisible(layerId, visibleIds.has(catalogId));
    engine.setImageryLayerOpacity(layerId, opacityById[catalogId] ?? 1);
  }
}

/** Shows exactly one catalog basemap and hides the rest. */
export function setActiveBaseImagery(engine: Scene3DImagery, activeId: number): void {
  const wanted = baseImageryLayerId(activeId);
  for (const layerId of engine.getImageryLayerIds()) {
    if (layerId.startsWith(BASE_LAYER_PREFIX)) {
      engine.setImageryLayerVisible(layerId, layerId === wanted);
    }
  }
}
