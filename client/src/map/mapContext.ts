// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import { ScaleLine, defaults as defaultControls } from 'ol/control';
import GeoJSON from 'ol/format/GeoJSON';
import LayerGroup from 'ol/layer/Group';
import { fromLonLat, transformExtent } from 'ol/proj';

// The workspace map is a module-level singleton living OUTSIDE React state;
// components attach/detach the DOM target and subscribe to events.
let workspaceMap: Map | undefined;

export function getWorkspaceMap(): Map {
  workspaceMap ??= new Map({
    controls: defaultControls().extend([new ScaleLine()]),
    layers: [getOverlayGroup()],
    view: new View({
      center: fromLonLat([25.3, 45.7]), // Southern Carpathians default; saved views come later
      zoom: 8,
    }),
  });
  return workspaceMap;
}

// ---- overlay group ----------------------------------------------------------
// All data overlays (built-in vector layers, geofiles, rasters) live in one
// LayerGroup so the layer tree can toggle/reorder them as a unit. Stacking is
// made explicit: children get sequential zIndex 1..n from their collection
// position (bottom→top), which keeps them above the base tile layers (zIndex 0)
// regardless of when those are added to the map, and makes drag-reorder in the
// tree take visual effect deterministically.

export const OVERLAY_GROUP_ID = 'overlays';

let overlayGroup: LayerGroup | undefined;

export function getOverlayGroup(): LayerGroup {
  if (!overlayGroup) {
    const group = new LayerGroup();
    group.set('id', OVERLAY_GROUP_ID);
    const renumber = () =>
      group.getLayers().forEach((layer, index) => layer.setZIndex(1 + index));
    group.getLayers().on(['add', 'remove'], renumber);
    overlayGroup = group;
  }
  return overlayGroup;
}

/** Finds an overlay-group layer by its `id` property. */
export function findOverlayLayer(id: string) {
  return getOverlayGroup()
    .getLayers()
    .getArray()
    .find((layer) => layer.get('id') === id);
}

/** Current overlay stacking, bottom→top, as the layers' `id` properties. */
export function getOverlayOrder(): string[] {
  return getOverlayGroup()
    .getLayers()
    .getArray()
    .map((layer) => layer.get('id') as string | undefined)
    .filter((id): id is string => id !== undefined);
}

/**
 * Reorders the overlay layers so the ids listed appear in the given bottom→top
 * order. Layers not mentioned keep their relative position; listed ids that
 * don't exist (yet) are ignored.
 */
export function applyOverlayOrder(ids: string[]): void {
  const collection = getOverlayGroup().getLayers();
  const current = collection.getArray().slice();
  const wanted = ids.filter((id) => current.some((layer) => layer.get('id') === id));
  if (wanted.length < 2) {
    return; // nothing to reorder
  }
  const queue = wanted.map((id) => current.find((layer) => layer.get('id') === id)!);
  const next = current.map((layer) =>
    wanted.includes(layer.get('id') as string) ? queue.shift()! : layer,
  );
  if (next.some((layer, index) => layer !== current[index])) {
    collection.clear();
    collection.extend(next);
  }
}

// A saved view can list layers that are only created moments later (geofile and
// raster layers appear asynchronously once their sync effect runs). The desired
// order is kept briefly and re-applied after each sync until every listed layer
// exists — bounded by a TTL so a layer that never materializes (deleted geofile)
// cannot resurrect a stale order after the user has re-dragged layers.
const PENDING_ORDER_TTL_MS = 5000;
let pendingOrder: { ids: string[]; expiresAt: number } | null = null;

export function setDesiredOverlayOrder(ids: string[]): void {
  pendingOrder = { ids, expiresAt: Date.now() + PENDING_ORDER_TTL_MS };
  applyPendingOverlayOrder();
}

export function applyPendingOverlayOrder(): void {
  if (!pendingOrder) {
    return;
  }
  if (Date.now() > pendingOrder.expiresAt) {
    pendingOrder = null;
    return;
  }
  applyOverlayOrder(pendingOrder.ids);
  const present = new Set(getOverlayOrder());
  if (pendingOrder.ids.every((id) => present.has(id))) {
    pendingOrder = null; // fully applied
  }
}

export function flyTo(lon: number, lat: number, zoom = 15): void {
  getWorkspaceMap().getView().animate({ center: fromLonLat([lon, lat]), zoom, duration: 500 });
}

/** Sets the opacity (0..1) of a map layer identified by its `id` property, if present. */
export function setLayerOpacity(layerId: string, opacity: number): void {
  // getAllLayers flattens layer groups, so overlays inside the group are found too.
  getWorkspaceMap()
    .getAllLayers()
    .find((layer) => layer.get('id') === layerId)
    ?.setOpacity(opacity);
}

/**
 * Fits the view to a map-projection extent — degenerate (point) extents get maxZoom.
 *
 * How close to go is a parameter because it is sometimes a statement about how well a position
 * is known rather than a matter of taste. A point that reached this client snapped to a grid
 * should be framed loosely, so the screen does not present a hillside kilometres away as the
 * entrance; see `APPROXIMATE_MAX_ZOOM`.
 */
export function fitExtent(extent: [number, number, number, number], maxZoom = 17): void {
  const map = getWorkspaceMap();
  const fit = () =>
    map.getView().fit(extent, {
      padding: [60, 60, 60, 60],
      maxZoom,
      duration: 500,
    });

  if (map.getSize()) {
    fit();
  } else {
    // Called before the workspace renders (table pages, tests): fit as soon
    // as the map gets a size (it acquires one when the map page mounts).
    map.once('change:size', fit);
  }
}

/**
 * The ground box the map is showing, in degrees, west/south/east/north — or undefined before it
 * has been laid out, when it is showing nothing at all.
 */
export function mapLonLatExtent(): [number, number, number, number] | undefined {
  const map = getWorkspaceMap();
  const size = map.getSize();
  if (!size) {
    return undefined;
  }
  return transformExtent(map.getView().calculateExtent(size), 'EPSG:3857', 'EPSG:4326') as [
    number,
    number,
    number,
    number,
  ];
}

/**
 * Frames a box given in degrees, but only while the map is actually on screen.
 *
 * Deliberately not `fitExtent`, which waits for the map to be laid out and fits as soon as it is.
 * That is right for "show me this feature", which is a thing the viewer asked for and should still
 * happen when they arrive at the map. It is wrong for keeping two views in step: the flat map is a
 * module-level object that exists whether or not it is mounted, so an extent that arrived while
 * the viewer was in the 3D view would be held and then applied, minutes later, over wherever they
 * had since navigated to.
 */
export function fitLonLatExtent(bounds: [number, number, number, number]): void {
  const map = getWorkspaceMap();
  if (!map.getSize()) {
    return;
  }
  map.getView().fit(transformExtent(bounds, 'EPSG:4326', 'EPSG:3857'), {
    padding: [60, 60, 60, 60],
    maxZoom: 17,
    duration: 500,
  });
}

/**
 * How close to frame a position this reader is only allowed to know approximately.
 *
 * The server snaps such a point to a grid — 5 km by default — so the true place can be anywhere in
 * that cell. Framing it at the ordinary close-up zoom would draw a screen a couple of hundred
 * metres across and centre it on a spot that is confidently, precisely wrong. At this zoom the
 * screen is wide enough that the cell is a fair share of it, which is an honest picture of what is
 * actually known. It is a fixed number rather than the real cell size because the grid is a server
 * setting the API does not publish; if it ever does, frame the cell itself instead.
 */
export const APPROXIMATE_MAX_ZOOM = 12;

/** Fits the view to a GeoJSON geometry (EPSG:4326) — points get a sane close-up zoom. */
export function fitGeoJsonGeometry(geometry: object, maxZoom?: number): void {
  const geom = new GeoJSON().readGeometry(geometry, {
    dataProjection: 'EPSG:4326',
    featureProjection: 'EPSG:3857',
  });
  fitExtent(geom.getExtent() as [number, number, number, number], maxZoom);
}
