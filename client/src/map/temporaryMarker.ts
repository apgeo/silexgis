// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type Map from 'ol/Map';
import { Point } from 'ol/geom';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { temporaryMarkerPalette as palette } from './markerPalette.ts';

/** The query parameter that asks the map for a temporary marker: `?mark=<lat>,<lon>`. */
export const MARK_PARAM = 'mark';

export interface MarkPosition {
  lat: number;
  lon: number;
}

/**
 * The position an address asks to be marked, or null when it names none or names something that
 * is not a position on the globe. Latitude comes first, as it does in the `#zoom/lat/lon` hash and
 * in everything a person copies a coordinate pair out of.
 */
export function parseMarkParam(value: string | null): MarkPosition | null {
  if (!value) {
    return null;
  }
  const parts = value.split(',');
  if (parts.length !== 2 || parts.some((part) => part.trim() === '')) {
    return null;
  }
  const lat = Number(parts[0]);
  const lon = Number(parts[1]);
  if (!Number.isFinite(lat) || !Number.isFinite(lon) || Math.abs(lat) > 90 || Math.abs(lon) > 180) {
    return null;
  }
  return { lat, lon };
}

export function formatMarkParam(position: MarkPosition): string {
  return `${position.lat.toFixed(6)},${position.lon.toFixed(6)}`;
}

function markerStyle(): Style {
  return new Style({
    image: new CircleStyle({
      radius: 8,
      fill: new Fill({ color: palette.fill }),
      stroke: new Stroke({ color: palette.casing, width: 2 }),
    }),
  });
}

/**
 * Puts the marker an address asked for on the map, read once when the map is built.
 *
 * It is a throwaway layer of its own rather than a member of the overlay group: it is not data the
 * map holds, so the layer panel has nothing to offer for it and no saved view or filter ever
 * mentions it. Nothing is written back to the address and nothing is stored; the marker lasts as
 * long as the page does. Returns a detach function that removes the layer.
 */
export function attachTemporaryMarker(
  map: Map,
  search: string = window.location.search,
): () => void {
  const position = parseMarkParam(new URLSearchParams(search).get(MARK_PARAM));
  if (!position) {
    return () => undefined;
  }
  const layer = new VectorLayer({
    source: new VectorSource({
      features: [new Feature(new Point(fromLonLat([position.lon, position.lat])))],
    }),
    style: markerStyle(),
    zIndex: 1000,
  });
  layer.set('id', 'temporary-marker');
  map.addLayer(layer);
  return () => {
    map.removeLayer(layer);
  };
}
