// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import { ScaleLine, defaults as defaultControls } from 'ol/control';
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
