// SPDX-License-Identifier: AGPL-3.0-or-later

/** Formats WGS84 coordinates for display: "45.53127°N 25.44721°E". */
export function formatLonLat(lon: number, lat: number, decimals = 5): string {
  const ns = lat >= 0 ? 'N' : 'S';
  const ew = lon >= 0 ? 'E' : 'W';
  return `${Math.abs(lat).toFixed(decimals)}°${ns} ${Math.abs(lon).toFixed(decimals)}°${ew}`;
}

/** Clamps a longitude/latitude pair to valid WGS84 ranges. */
export function clampLonLat(lon: number, lat: number): [number, number] {
  return [Math.min(180, Math.max(-180, lon)), Math.min(90, Math.max(-90, lat))];
}

/**
 * A lon/lat extent as the `bbox` query value the map layers accept: inside the globe, west below
 * east. A view zoomed out past one world, or panned onto a copy of it, has an extent beyond ±180°
 * — the server refuses such a window rather than reading it two ways, so it is brought back here:
 * shifted onto the world it shows, and cut at its edges.
 */
export function worldBbox(extent: readonly number[]): string {
  let [west, south, east, north] = extent;
  if (east - west >= 360) {
    west = -180;
    east = 180;
  } else {
    const shift = Math.floor((west + 180) / 360) * 360;
    west -= shift;
    east -= shift;
  }
  const [w, s] = clampLonLat(west, south);
  const [e, n] = clampLonLat(east, north);
  return [w, s, e, n].map((value) => value.toFixed(5)).join(',');
}
