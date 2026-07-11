// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import { ScaleLine, defaults as defaultControls } from 'ol/control';
import GeoJSON from 'ol/format/GeoJSON';
import LayerGroup from 'ol/layer/Group';
import { fromLonLat } from 'ol/proj';

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

/** Fits the view to a GeoJSON geometry (EPSG:4326) — points get a sane close-up zoom. */
export function fitGeoJsonGeometry(geometry: object): void {
  const map = getWorkspaceMap();
  const geom = new GeoJSON().readGeometry(geometry, {
    dataProjection: 'EPSG:4326',
    featureProjection: 'EPSG:3857',
  });
  const fit = () =>
    map.getView().fit(geom.getExtent(), {
      padding: [60, 60, 60, 60],
      maxZoom: 17,
      duration: 500,
    });

  if (map.getSize()) {
    fit();
  } else {
    // Called from a table page before the workspace renders: fit as soon as
    // the map gets a size (it acquires one when the map page mounts).
    map.once('change:size', fit);
  }
}
