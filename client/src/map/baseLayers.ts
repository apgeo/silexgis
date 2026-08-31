// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import type { MapLayerInfo } from '../api/hooks.ts';

const BASE_ID_PROP = 'silexgis:baseLayerId';
const TILE_OVERLAY_ID_PROP = 'silexgis:tileOverlayId';

/**
 * Where tile overlays sit in the stack: above the basemap (0), below every data layer (1 upwards,
 * renumbered by the map context as layers come and go).
 *
 * A fraction rather than an integer because both neighbours are already spoken for, and because
 * the alternative — relying on these being added to the collection after the basemaps, which is
 * what decides the order between two layers of equal index — is a rule that holds today and breaks
 * silently the first time the catalogue arrives in two passes. What it would look like is a hiking
 * overlay underneath an opaque basemap: not drawn, no error, and no reason to suspect ordering.
 */
const TILE_OVERLAY_Z = 0.5;

/**
 * The zoom band a catalogue entry declares, in the form OpenLayers wants it.
 *
 * `maxZoom` on the SOURCE rather than the layer, and the difference is the whole point. A layer's
 * `maxZoom` hides it past that zoom, leaving a viewer who zooms one step in with nothing there. A
 * source's tells the tile grid to keep drawing the last level it has, scaled up — so zooming past
 * a source's limit gives blurry tiles instead of a blank screen, which is what every mapping
 * application does and what nobody has to be told about.
 *
 * The lower end is the opposite case and belongs on the layer: an overlay that starts at zoom 10
 * genuinely has nothing to show at zoom 3, and stretching one tile across a continent would be a
 * lie rather than a blur.
 */
function zoomBand(entry: MapLayerInfo): { min: number; max: number } {
  const min = entry.minZoom ?? 0;
  // A catalogue row that predates the zoom columns, or one an operator left at zero, would
  // otherwise be a source that draws at zoom 0 and nowhere else — invisible, with the reason
  // sitting in a database column nobody is looking at.
  const max = entry.maxZoom && entry.maxZoom > min ? entry.maxZoom : 19;
  return { min, max };
}

function tileLayerFor(entry: MapLayerInfo, zIndex: number): TileLayer {
  const { min, max } = zoomBand(entry);
  return new TileLayer({
    source: new XYZ({
      url: entry.urlTemplate,
      attributions: entry.attribution ?? undefined,
      maxZoom: max,
    }),
    minZoom: min,
    visible: false,
    zIndex,
  });
}

/** Creates/refreshes base tile layers from the server catalog (only XYZ for now). */
export function syncBaseLayers(map: Map, catalog: MapLayerInfo[], activeId: number): void {
  const existing = new Set(
    map
      .getLayers()
      .getArray()
      .map((layer) => layer.get(BASE_ID_PROP) as number | undefined)
      .filter((id) => id !== undefined),
  );

  for (const entry of catalog) {
    // int64 ids arrive as number | string from the generated contract — normalize once here.
    const entryId = Number(entry.id);
    if (entry.layerKind !== 'xyz' || !entry.isBase || existing.has(entryId)) {
      continue;
    }
    const layer = tileLayerFor(entry, 0);
    layer.set(BASE_ID_PROP, entryId);
    layer.set('name', entry.name); // shown by the on-canvas background-layer chooser
    map.addLayer(layer);
  }

  setActiveBaseLayer(map, activeId);
}

/**
 * Creates/refreshes the tile layers drawn ON TOP of whichever basemap is chosen, and shows the
 * ones asked for.
 *
 * Several at once, unlike the basemaps, because that is what they are for: hiking routes and ski
 * routes over a topographic map are three sources answering three different questions about one
 * place, and picking between them defeats the point.
 *
 * They sit above the basemap and below every data layer. That ordering is not cosmetic — an
 * entrance marker hidden under a route overlay is a cave nobody can click.
 */
export function syncTileOverlays(
  map: Map,
  catalog: MapLayerInfo[],
  visibleIds: ReadonlySet<number>,
  opacityById: Readonly<Record<number, number>>,
): void {
  const existing: Record<number, TileLayer> = {};
  for (const layer of map.getLayers().getArray()) {
    const id = layer.get(TILE_OVERLAY_ID_PROP) as number | undefined;
    if (id !== undefined) {
      existing[id] = layer as TileLayer;
    }
  }

  for (const entry of catalog) {
    const entryId = Number(entry.id);
    if (entry.layerKind !== 'xyz' || entry.isBase) {
      continue;
    }

    let layer = existing[entryId];
    if (!layer) {
      layer = tileLayerFor(entry, TILE_OVERLAY_Z);
      layer.set(TILE_OVERLAY_ID_PROP, entryId);
      layer.set('name', entry.name);
      map.addLayer(layer);
    }

    layer.setVisible(visibleIds.has(entryId));
    layer.setOpacity(opacityById[entryId] ?? 1);
  }
}

/** The catalog id of a tile overlay created by `syncTileOverlays`. */
export function getTileOverlayId(layer: TileLayer): number | undefined {
  return layer.get(TILE_OVERLAY_ID_PROP) as number | undefined;
}

/** The base tile layers currently on the map (creation order = catalog order). */
export function getBaseLayers(map: Map): TileLayer[] {
  return map
    .getLayers()
    .getArray()
    .filter((layer): layer is TileLayer => layer.get(BASE_ID_PROP) !== undefined);
}

/** The catalog id of a base layer created by `syncBaseLayers`. */
export function getBaseLayerId(layer: TileLayer): number | undefined {
  return layer.get(BASE_ID_PROP) as number | undefined;
}

export function setActiveBaseLayer(map: Map, id: number): void {
  for (const layer of map.getLayers().getArray()) {
    const baseId = layer.get(BASE_ID_PROP) as number | undefined;
    if (baseId !== undefined) {
      layer.setVisible(baseId === id);
    }
  }
}
