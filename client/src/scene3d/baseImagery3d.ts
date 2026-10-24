// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo } from '../api/hooks.ts';
import { isDrawable, wmtsMatrixIds, zoomBand, type DrawableLayer } from '../map/baseLayers.ts';
import { createCogTileSource } from './cogImagery3d.ts';
import type { RenderedSourceStatus } from './renderedTiles3d.ts';
import type { Scene3DImagery, Scene3DImageryOptions, Scene3DServiceCrs } from './scene3dEngine.ts';
import { createVectorTileSource, drawsVectorStyle } from './vectorImagery3d.ts';

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
 * How a self-drawn catalogue layer stands, told to whoever lists the layers, by catalogue id. A
 * GeoTIFF that cannot be read, a style that cannot be loaded, a vector entry drawn by its stand-in:
 * each is said beside the entry rather than left as a layer that is on and shows nothing.
 */
export type CatalogueImageryReport = (catalogId: number, status: RenderedSourceStatus) => void;

/** What the scene needs to know about where it runs and who reads it, to draw a catalogue entry. */
export interface CatalogueImageryContext {
  /** The whole answer of the layer list: a vector entry's stand-in is another row of it. */
  catalog: readonly MapLayerInfo[];
  /** The reader's language code, which a vector style's labels follow. */
  language: string;
  report?: CatalogueImageryReport;
}

/**
 * The row a vector entry names to be draped in its place, when the 3D scene does not draw the
 * style itself. Matched by name within the same answer, as the server publishes it; only a kind
 * the scene drapes as it stands, and only on the same side of the list — which the server already
 * guarantees, and which is checked again here because a wrong stand-in would draw silently.
 */
export function standInFor(entry: MapLayerInfo, catalog: readonly MapLayerInfo[]): MapLayerInfo | undefined {
  const name = entry.vector?.scene3dStandIn;
  if (!name) {
    return undefined;
  }
  return catalog.find(
    (row) =>
      row.name === name &&
      row.isBase === entry.isBase &&
      (row.layerKind === 'xyz' || row.layerKind === 'wms' || row.layerKind === 'wmts') &&
      isDrawable(row),
  );
}

/**
 * Whether the scene can drape an entry, one way or another: every kind the flat map draws as
 * tiles, a GeoTIFF that carries its options, and a vector entry whose style the scene draws or
 * whose stand-in it can drape instead. The layer list offers exactly these, so the two views list
 * the same backgrounds.
 */
export function isDrawableIn3d(entry: MapLayerInfo, catalog: readonly MapLayerInfo[]): boolean {
  switch (entry.layerKind) {
    case 'cog':
      return Boolean(entry.cog);
    case 'vector':
      return Boolean(entry.vector) && (drawsVectorStyle(entry) || standInFor(entry, catalog) !== undefined);
    default:
      return isDrawable(entry);
  }
}

/**
 * Whether this device should be spared drawing vector styles where a stand-in exists: two logical
 * processors or fewer, or two gigabytes of memory or less, where the browser says. A style is drawn
 * on the page's own thread, tile by tile, and on such a machine the scene would stutter through
 * every tile of a background its raster stand-in shows at no cost. A browser that does not say is
 * not spared.
 */
export function vectorStylesTooHeavyHere(): boolean {
  if (typeof navigator === 'undefined') {
    return false;
  }
  const processors = navigator.hardwareConcurrency;
  const memory = (navigator as Navigator & { deviceMemory?: number }).deviceMemory;
  return (processors !== undefined && processors > 0 && processors <= 2) || (memory !== undefined && memory <= 2);
}

/**
 * What the scene is handed for one tile-service entry, by its kind: a z/x/y template, or a map
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
 * The same for any entry of the catalogue, the kinds the scene draws itself included, or
 * undefined when the scene cannot drape it.
 *
 * A GeoTIFF is read tile by tile from its own pyramid. A vector entry is drawn in its style, tile
 * by tile, by the flat map's renderer — or by its stand-in: from the start on a device too weak to
 * draw styles, and as soon as the style turns out not to draw. The stand-in is draped under the
 * vector entry's own scene id, so choosing the entry in either view still chooses it here.
 */
function catalogueImageryFor(
  engine: Scene3DImagery,
  layerId: string,
  entry: MapLayerInfo,
  context: CatalogueImageryContext,
): Scene3DImageryOptions | undefined {
  const catalogId = Number(entry.id);
  const report = (status: RenderedSourceStatus) => context.report?.(catalogId, status);
  const common = { attribution: entry.attribution ?? undefined, visible: false, ...levelsOf(entry) };

  if (entry.layerKind === 'cog') {
    if (!entry.cog) {
      return undefined;
    }
    return { kind: 'rendered', source: createCogTileSource(entry.urlTemplate, entry.cog, report), ...common };
  }

  if (entry.layerKind === 'vector') {
    if (!entry.vector) {
      return undefined;
    }
    const standIn = standInFor(entry, context.catalog);
    const standInOptions = standIn ? { ...imageryOptionsFor(standIn), visible: false } : undefined;
    const drawsStyle = drawsVectorStyle(entry) && !(standInOptions && vectorStylesTooHeavyHere());
    if (!drawsStyle) {
      if (standInOptions) {
        report({ state: 'limited', code: 'vector.stand_in' });
      }
      return standInOptions;
    }
    const source = createVectorTileSource(entry, context.language, (status) => {
      if (status.state === 'unavailable' && standInOptions) {
        // The style did not draw. The stand-in takes the layer's place in the stack, with the
        // visibility and transparency the viewer gave it, and the list says which is drawn.
        engine.replaceImageryLayer(layerId, standInOptions);
        report({ state: 'limited', code: 'vector.stand_in' });
        return;
      }
      report(status);
    });
    return { kind: 'rendered', source, ...common };
  }

  return isDrawable(entry) ? imageryOptionsFor(entry) : undefined;
}

/**
 * Creates any missing base layers from the catalog and leaves `activeId` as the visible one.
 *
 * Every kind the 2D map draws is draped here too — z/x/y tiles, WMS and WMTS, GeoTIFFs and vector
 * styles — and anything else is skipped rather than half-rendered. Every base layer is created up
 * front and only its visibility changes, so switching basemap is instant and each keeps whatever
 * opacity it was given; a hidden layer asks for no tiles, so one the scene draws itself costs
 * nothing until it is chosen.
 */
export function syncBaseImagery(
  engine: Scene3DImagery,
  context: CatalogueImageryContext,
  activeId: number,
): void {
  for (const entry of context.catalog) {
    // int64 ids arrive as number | string from the generated contract — normalize once here.
    const layerId = baseImageryLayerId(Number(entry.id));
    if (!entry.isBase || engine.hasImageryLayer(layerId)) {
      continue;
    }
    const options = catalogueImageryFor(engine, layerId, entry, context);
    if (options) {
      engine.addImageryLayer(layerId, options);
    }
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
  context: CatalogueImageryContext,
  visibleIds: ReadonlySet<number>,
  opacityById: Readonly<Record<number, number>>,
): void {
  for (const entry of context.catalog) {
    const catalogId = Number(entry.id);
    if (entry.isBase) {
      continue;
    }
    const layerId = tileOverlayImageryLayerId(catalogId);
    if (!engine.hasImageryLayer(layerId)) {
      const options = catalogueImageryFor(engine, layerId, entry, context);
      if (!options) {
        continue;
      }
      engine.addImageryLayer(layerId, options);
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
 * A source the scene draws itself states its own ceiling when it opens.
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
