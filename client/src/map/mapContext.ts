// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import { ScaleLine, defaults as defaultControls } from 'ol/control';
import GeoJSON from 'ol/format/GeoJSON';
import { fromLonLat } from 'ol/proj';

// The workspace map is a module-level singleton living OUTSIDE React state;
// components attach/detach the DOM target and subscribe to events.
let workspaceMap: Map | undefined;

export function getWorkspaceMap(): Map {
  workspaceMap ??= new Map({
    controls: defaultControls().extend([new ScaleLine()]),
    view: new View({
      center: fromLonLat([25.3, 45.7]), // Southern Carpathians default; saved views come later
      zoom: 8,
    }),
  });
  return workspaceMap;
}

export function flyTo(lon: number, lat: number, zoom = 15): void {
  getWorkspaceMap().getView().animate({ center: fromLonLat([lon, lat]), zoom, duration: 500 });
}

/** Sets the opacity (0..1) of a map layer identified by its `id` property, if present. */
export function setLayerOpacity(layerId: string, opacity: number): void {
  getWorkspaceMap()
    .getLayers()
    .getArray()
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
