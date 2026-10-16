// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo } from '../api/hooks.ts';
import { isDrawable, wmtsMatrixIds, zoomBand, type DrawableLayer } from '../map/baseLayers.ts';
import type { Scene3DImagery, Scene3DImageryOptions, Scene3DServiceCrs } from './scene3dEngine.ts';

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
 * What the scene is handed for one catalogue entry, by its kind: a z/x/y template, or a map
 * service with the options the catalogue gave it. The flat map draws the same entry from the same
 * fields, so the two views ask a service for the same pictures.
 */
export function imageryOptionsFor(entry: DrawableLayer): Scene3DImageryOptions {
  const common = { attribution: entry.attribution ?? undefined, visible: false, ...levelsOf(entry) };
  if (entry.layerKind === 'wms' && entry.wms) {
    const wms = entry.wms;
    return {
      kind: 'wms',
      url: entry.urlTemplate,
      layers: wms.layers,
      styles: wms.styles,
      format: wms.format,
      version: wms.version,
      transparent: wms.transparent,
      crs: wms.crs as Scene3DServiceCrs,
      ...common,
    };
  }
  if (entry.layerKind === 'wmts' && entry.wmts) {
    const wmts = entry.wmts;
    return {
      kind: 'wmts',
      url: entry.urlTemplate,
      layer: wmts.layer,
      matrixSet: wmts.matrixSet,
      format: wmts.format,
      style: wmts.style,
      crs: wmts.crs as Scene3DServiceCrs,
      matrixIds: wmtsMatrixIds(wmts.matrixIdPrefix, zoomBand(entry).max),
      ...common,
    };
  }
  return { urlTemplate: entry.urlTemplate, ...common };
}

/**
 * Creates any missing base layers from the catalog and leaves `activeId` as the visible one.
 *
 * Every kind the 2D map draws is draped here too — z/x/y tiles, WMS and WMTS — and anything else
 * is skipped rather than half-rendered. Every base layer is created up front and only its
 * visibility changes, so switching basemap is instant and each keeps whatever opacity it was
 * given.
 */
export function syncBaseImagery(
  engine: Scene3DImagery,
  catalog: readonly MapLayerInfo[],
  activeId: number,
): void {
  for (const entry of catalog) {
    // int64 ids arrive as number | string from the generated contract — normalize once here.
    const layerId = baseImageryLayerId(Number(entry.id));
    if (!isDrawable(entry) || !entry.isBase || engine.hasImageryLayer(layerId)) {
      continue;
    }
    engine.addImageryLayer(layerId, imageryOptionsFor(entry));
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
    if (!isDrawable(entry) || entry.isBase) {
      continue;
    }
    const layerId = tileOverlayImageryLayerId(catalogId);
    if (!engine.hasImageryLayer(layerId)) {
      engine.addImageryLayer(layerId, imageryOptionsFor(entry));
    }
    engine.setImageryLayerVisible(layerId, visibleIds.has(catalogId));
    engine.setImageryLayerOpacity(layerId, opacityById[catalogId] ?? 1);
  }
}

/**
 * The zoom band a catalogue entry declares, as the scene takes it — the same band the 2D map
 * applies. Without the ceiling a close camera asks a source for zoom levels it never published:
 * two shipped imagery sources stop at 16 and 17 and went blank in 3D past them, and a street map
 * stopping at 19 answered every closer request with a refusal the console recorded by the hundred.
 */
function levelsOf(entry: DrawableLayer): { minimumLevel: number; maximumLevel: number } {
  const { min, max } = zoomBand(entry);
  return { minimumLevel: min, maximumLevel: max };
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
