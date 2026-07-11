// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import { fromLonLat, toLonLat } from 'ol/proj';

// Encodes the map camera in the URL hash as `#<zoom>/<lat>/<lon>` (the OSM/Leaflet
// convention), so a position is bookmarkable and shareable without a saved view.
// The router is path-based (createBrowserRouter), so the hash is ours to own.

export interface MapHash {
  lon: number;
  lat: number;
  zoom: number;
}

/** Parses `#<zoom>/<lat>/<lon>`; returns null when absent or malformed/out of range. */
export function parseMapHash(hash: string = window.location.hash): MapHash | null {
  const match = /^#(-?\d+(?:\.\d+)?)\/(-?\d+(?:\.\d+)?)\/(-?\d+(?:\.\d+)?)$/.exec(hash);
  if (!match) {
    return null;
  }
  const zoom = Number(match[1]);
  const lat = Number(match[2]);
  const lon = Number(match[3]);
  if (![zoom, lat, lon].every(Number.isFinite)) {
    return null;
  }
  if (lat < -90 || lat > 90 || lon < -180 || lon > 180 || zoom < 0 || zoom > 28) {
    return null;
  }
  return { lon, lat, zoom };
}

export function formatMapHash({ lon, lat, zoom }: MapHash): string {
  return `#${zoom.toFixed(2)}/${lat.toFixed(5)}/${lon.toFixed(5)}`;
}

/** True when the current URL carries a shareable map position (used to skip the home view). */
export function hasMapHash(): boolean {
  return parseMapHash() !== null;
}

/**
 * Restores the camera from the URL hash if one is present, then keeps the hash in
 * sync on every `moveend` (debounced, via replaceState so it adds no history entries).
 * Returns a detach function.
 */
export function attachUrlHash(map: Map): () => void {
  const initial = parseMapHash();
  if (initial) {
    const view = map.getView();
    view.setCenter(fromLonLat([initial.lon, initial.lat]));
    view.setZoom(initial.zoom);
  }

  let timer: number | undefined;
  const sync = () => {
    const view = map.getView();
    const center = view.getCenter();
    const zoom = view.getZoom();
    if (!center || zoom === undefined) {
      return;
    }
    const [lon, lat] = toLonLat(center);
    const next = formatMapHash({ lon, lat, zoom });
    if (next !== window.location.hash) {
      window.history.replaceState(
        null,
        '',
        `${window.location.pathname}${window.location.search}${next}`,
      );
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(sync, 300);
  };

  map.on('moveend', onMoveEnd);
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
  };
}
