// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import { unByKey } from 'ol/Observable';
import type { EventsKey } from 'ol/events';
import { getTopLeft, getWidth, type Extent } from 'ol/extent';
import TileLayer from 'ol/layer/Tile';
import { get as getProjection } from 'ol/proj';
import type TileSource from 'ol/source/Tile';
import TileWMS from 'ol/source/TileWMS';
import WMTS from 'ol/source/WMTS';
import XYZ from 'ol/source/XYZ';
import WMTSTileGrid from 'ol/tilegrid/WMTS';
import type { MapLayerInfo } from '../api/hooks.ts';

/**
 * What a source needs in order to be drawn: a catalogue entry, or the background a shared view
 * was saved with — the same fields, so both are drawn by the same code. Only a catalogue entry
 * says whether it is allowed into documents; a shared view's background never ends up in one.
 */
export type DrawableLayer = Pick<
  MapLayerInfo,
  'layerKind' | 'urlTemplate' | 'wms' | 'wmts' | 'attribution' | 'minZoom' | 'maxZoom'
> &
  Partial<Pick<MapLayerInfo, 'inDocuments'>>;

/**
 * Whether this client can draw the entry: tiles on a z/x/y template, or a map service carrying
 * the options its kind needs. The server publishes nothing else, and this keeps a list from
 * offering a radio button or a switch that would draw nothing if it ever did.
 */
export function isDrawable(entry: DrawableLayer): boolean {
  switch (entry.layerKind) {
    case 'xyz':
      return true;
    case 'wms':
      return Boolean(entry.wms);
    case 'wmts':
      return Boolean(entry.wmts);
    default:
      return false;
  }
}

/**
 * The tile matrix identifiers of a WMTS grid from level 0 to `maxZoom`, as the service spells
 * them: the level number, after whatever the service puts before it.
 */
export function wmtsMatrixIds(prefix: string, maxZoom: number): string[] {
  return Array.from({ length: maxZoom + 1 }, (_, level) => `${prefix}${level}`);
}

/**
 * The tile grid of a WMTS matrix set on one of the two well-known grids. Web mercator starts with
 * one tile covering the world; plain longitude and latitude with two, side by side.
 */
function wmtsTileGrid(crs: string, maxZoom: number, matrixIdPrefix: string): WMTSTileGrid {
  const geographic = crs === 'EPSG:4326';
  const extent: Extent = geographic ? [-180, -90, 180, 90] : getProjection('EPSG:3857')!.getExtent();
  const levelZero = geographic ? 180 / 256 : getWidth(extent) / 256;
  return new WMTSTileGrid({
    extent,
    origin: getTopLeft(extent),
    resolutions: Array.from({ length: maxZoom + 1 }, (_, level) => levelZero / 2 ** level),
    matrixIds: wmtsMatrixIds(matrixIdPrefix, maxZoom),
  });
}

/**
 * How a catalogue source's tiles are asked for. A source the catalogue lets into the pictures this
 * application makes is asked for with leave to be read back out of the browser (CORS) — which is
 * what lets "export map image" put it into a picture at all: a tile drawn without that leave makes
 * the whole map's canvas unreadable. Those are the sources documents already ask for the same way,
 * so they are expected to answer it. Any other source is asked the ordinary way, so that a server
 * that does not answer cross-origin requests still draws on screen; an export over it is refused,
 * naming it, instead. (A source allowed into documents that turns out not to answer them is asked
 * the ordinary way from then on — see `tileLayerFor`.) The flag alone decides, whatever the kind of
 * source; the catalogue only ever allows a tile template into documents.
 */
export function tileCrossOrigin(entry: Pick<DrawableLayer, 'inDocuments'>): 'anonymous' | undefined {
  return entry.inDocuments ? 'anonymous' : undefined;
}

/**
 * The OpenLayers source for an entry, by its kind.
 *
 * A Web Map Service is asked for one picture per tile, so it has no last zoom to respect and is
 * drawn at whatever resolution the view needs. A Web Map Tile Service has ready-cut levels; its
 * grid is built from the coordinate system the catalogue names, up to the entry's last zoom, past
 * which the last level is stretched — the same blur-rather-than-blank an XYZ source gets.
 */
export function tileSourceFor(
  entry: DrawableLayer,
  crossOrigin: 'anonymous' | undefined = tileCrossOrigin(entry),
): TileSource {
  const { max } = zoomBand(entry);
  const attributions = entry.attribution ?? undefined;
  if (entry.layerKind === 'wms' && entry.wms) {
    const wms = entry.wms;
    return new TileWMS({
      url: entry.urlTemplate,
      params: {
        LAYERS: wms.layers,
        STYLES: wms.styles,
        FORMAT: wms.format,
        VERSION: wms.version,
        TRANSPARENT: wms.transparent ? 'TRUE' : 'FALSE',
      },
      projection: wms.crs,
      attributions,
      crossOrigin,
    });
  }
  if (entry.layerKind === 'wmts' && entry.wmts) {
    const wmts = entry.wmts;
    return new WMTS({
      url: entry.urlTemplate,
      layer: wmts.layer,
      matrixSet: wmts.matrixSet,
      format: wmts.format,
      style: wmts.style,
      projection: wmts.crs,
      tileGrid: wmtsTileGrid(wmts.crs, max, wmts.matrixIdPrefix),
      // A url naming the tile matrix is the service's REST template; anything else is its
      // key-value endpoint.
      requestEncoding: entry.urlTemplate.includes('{TileMatrix}') ? 'REST' : 'KVP',
      attributions,
      crossOrigin,
      wrapX: true,
    });
  }
  return new XYZ({ url: entry.urlTemplate, attributions, maxZoom: max, crossOrigin });
}

const BASE_ID_PROP = 'silexgis:baseLayerId';
const TILE_OVERLAY_ID_PROP = 'silexgis:tileOverlayId';
/** Whether a tile layer's tiles can be read back out of the map's canvas, into a picture of it. */
const READABLE_PROP = 'silexgis:readable';

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
 *
 * The 3D scene reads the same band from here, so the two views can never disagree about where a
 * source stops.
 */
export function zoomBand(entry: Pick<DrawableLayer, 'minZoom' | 'maxZoom'>): { min: number; max: number } {
  const min = entry.minZoom ?? 0;
  // A catalogue row that predates the zoom columns, or one an operator left at zero, would
  // otherwise be a source that draws at zoom 0 and nowhere else — invisible, with the reason
  // sitting in a database column nobody is looking at.
  const max = entry.maxZoom && entry.maxZoom > min ? entry.maxZoom : 19;
  return { min, max };
}

export function tileLayerFor(entry: DrawableLayer, zIndex: number): TileLayer {
  const crossOrigin = tileCrossOrigin(entry);
  const layer = new TileLayer({
    source: tileSourceFor(entry, crossOrigin),
    minZoom: zoomBand(entry).min,
    visible: false,
    zIndex,
  });
  layer.set(READABLE_PROP, crossOrigin !== undefined);
  if (crossOrigin !== undefined) {
    askPlainlyIfRefused(layer, entry);
  }
  return layer;
}

/**
 * Switches a layer asked for with cross-origin leave to ordinary requests when its server turns
 * out not to give that leave: its first tile failing before any has loaded. Asked with leave, such
 * a server's every tile fails, and the background would go blank on screen because of an export
 * nobody may ever make; asked plainly it draws, and an export over it is refused by name.
 *
 * Only before any tile has loaded: once one has, the server does answer cross-origin requests, and
 * a later failure is a missing tile, not a refusal.
 */
export function askPlainlyIfRefused(layer: TileLayer, entry: DrawableLayer): void {
  const source = layer.getSource();
  if (!source) {
    return;
  }
  const keys: EventsKey[] = [];
  keys.push(
    source.once('tileloadend', () => unByKey(keys)),
    source.once('tileloaderror', () => {
      unByKey(keys);
      layer.set(READABLE_PROP, false);
      layer.setSource(tileSourceFor(entry, undefined));
    }),
  );
}

/**
 * The names of the tile layers on screen — the background, and any tile overlay shown — whose
 * tiles a picture of the map cannot read back, so that refusing an export can say which.
 */
export function unreadableTileLayers(map: Map): string[] {
  return map
    .getLayers()
    .getArray()
    .filter(
      (layer) =>
        (layer.get(BASE_ID_PROP) !== undefined || layer.get(TILE_OVERLAY_ID_PROP) !== undefined) &&
        layer.getVisible() &&
        layer.get(READABLE_PROP) === false,
    )
    .map((layer) => String(layer.get('name') ?? ''));
}

/**
 * The background a refused export can point to instead: the first of this installation's
 * backgrounds allowed into documents, or none when it has none.
 */
export function documentBackgroundName(catalog: readonly MapLayerInfo[]): string | undefined {
  return catalog.find((entry) => entry.layerKind === 'xyz' && entry.isBase && entry.inDocuments)?.name;
}

/** Creates/refreshes base tile layers from the server catalog: every kind this client can draw. */
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
    if (!isDrawable(entry) || !entry.isBase || existing.has(entryId)) {
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
    if (!isDrawable(entry) || entry.isBase) {
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
