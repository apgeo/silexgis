// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import { toLonLat } from 'ol/proj';

export interface MapContextMenuTarget {
  /** Pixel position relative to the map viewport, for anchoring the menu. */
  pixel: [number, number];
  /** Click coordinate in EPSG:4326, rounded to ~0.1 m. */
  lonLat: [number, number];
}

/**
 * Binds the browser context-menu gesture on the map viewport (right-click on
 * desktop; touch browsers synthesize the same event from a long-press). The
 * native menu is suppressed and the position is reported both as a viewport
 * pixel (menu anchor) and as lon/lat (what the menu's actions operate on).
 */
export function attachContextMenu(
  map: Map,
  onOpen: (target: MapContextMenuTarget) => void,
): () => void {
  const viewport = map.getViewport();
  const handler = (event: MouseEvent) => {
    event.preventDefault();
    const [x, y] = map.getEventPixel(event);
    const [lon, lat] = toLonLat(map.getEventCoordinate(event));
    onOpen({ pixel: [x, y], lonLat: [Number(lon.toFixed(6)), Number(lat.toFixed(6))] });
  };
  viewport.addEventListener('contextmenu', handler);
  return () => viewport.removeEventListener('contextmenu', handler);
}
