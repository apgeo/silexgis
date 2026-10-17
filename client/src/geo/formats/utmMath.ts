// SPDX-License-Identifier: AGPL-3.0-or-later

/** WGS84, the datum every position in this application is held in. */
const A = 6378137;
const F = 1 / 298.257223563;
const E2 = F * (2 - F);
const EP2 = E2 / (1 - E2);
const K0 = 0.9996;
export const FALSE_EASTING = 500000;
export const FALSE_NORTHING_SOUTH = 10000000;

/** UTM is defined between these latitudes; the poles have a grid of their own. */
export const UTM_SOUTH_LIMIT = -80;
export const UTM_NORTH_LIMIT = 84;

/** Latitude bands, 8° each from 80°S, the last one (X) 12° tall. I and O are not used. */
export const BANDS = 'CDEFGHJKLMNPQRSTUVWX';

/**
 * How far from its central meridian a position may be written in a zone chosen by the reader
 * rather than by the position: three degrees past either edge of the zone. A member working along
 * a zone boundary keeps one zone across it; beyond this the series below lose accuracy and a grid
 * reference in a far zone helps nobody, so the position's own zone is used instead.
 */
export const FORCED_ZONE_MAX_OFFSET_DEGREES = 9;

export interface UtmCoordinate {
  zone: number;
  band: string;
  hemisphere: 'N' | 'S';
  easting: number;
  northing: number;
}

/**
 * The UTM zone of a position, with the two places the grid departs from plain 6° strips: south-
 * western Norway, where zone 32 is widened over 31, and Svalbard, where four wide zones replace
 * seven.
 */
export function utmZone(lon: number, lat: number): number {
  const plain = Math.floor(((((lon + 180) % 360) + 360) % 360) / 6) + 1;
  if (lat >= 56 && lat < 64 && lon >= 3 && lon < 12) {
    return 32;
  }
  if (lat >= 72 && lat < 84 && lon >= 0 && lon < 42) {
    if (lon < 9) return 31;
    if (lon < 21) return 33;
    if (lon < 33) return 35;
    return 37;
  }
  return plain;
}

export function centralMeridian(zone: number): number {
  return (zone - 1) * 6 - 180 + 3;
}

/** Longitude difference from a zone's central meridian, in degrees, brought into −180…180. */
function offsetFromCentralMeridian(lon: number, zone: number): number {
  return ((((lon - centralMeridian(zone) + 540) % 360) + 360) % 360) - 180;
}

/** The latitude band letter of a latitude inside the UTM limits. */
export function bandOf(lat: number): string {
  return BANDS[Math.min(BANDS.length - 1, Math.max(0, Math.floor((lat - UTM_SOUTH_LIMIT) / 8)))];
}

/** The latitudes a band letter covers, [south, north); null for a letter that is not a band. */
export function bandRange(band: string): [number, number] | null {
  const index = BANDS.indexOf(band);
  if (index < 0 || band.length !== 1) {
    return null;
  }
  const south = UTM_SOUTH_LIMIT + index * 8;
  return [south, band === 'X' ? UTM_NORTH_LIMIT : south + 8];
}

/** Meridian arc length from the equator to a latitude, in metres. */
function meridianArc(phi: number): number {
  const e4 = E2 * E2;
  const e6 = e4 * E2;
  return (
    A *
    ((1 - E2 / 4 - (3 * e4) / 64 - (5 * e6) / 256) * phi -
      ((3 * E2) / 8 + (3 * e4) / 32 + (45 * e6) / 1024) * Math.sin(2 * phi) +
      ((15 * e4) / 256 + (45 * e6) / 1024) * Math.sin(4 * phi) -
      ((35 * e6) / 3072) * Math.sin(6 * phi))
  );
}

/**
 * A WGS84 position as UTM coordinates (the transverse Mercator series in the form USGS publishes
 * them — Snyder, Map Projections: A Working Manual, 1987), or null outside the latitudes UTM
 * covers. Within a zone the series agree with exact computation to well under a centimetre, far
 * below anything this application displays.
 *
 * `forcedZone` writes the position in that zone instead of its own, as long as it lies within
 * `FORCED_ZONE_MAX_OFFSET_DEGREES` of the zone's central meridian; further away the position's own
 * zone is used.
 */
export function toUtm(lon: number, lat: number, forcedZone?: number): UtmCoordinate | null {
  if (!Number.isFinite(lon) || !Number.isFinite(lat) || lat < UTM_SOUTH_LIMIT || lat > UTM_NORTH_LIMIT) {
    return null;
  }
  const zone =
    forcedZone !== undefined &&
    Number.isInteger(forcedZone) &&
    forcedZone >= 1 &&
    forcedZone <= 60 &&
    Math.abs(offsetFromCentralMeridian(lon, forcedZone)) <= FORCED_ZONE_MAX_OFFSET_DEGREES
      ? forcedZone
      : utmZone(lon, lat);
  const phi = (lat * Math.PI) / 180;
  const dLambda = offsetFromCentralMeridian(lon, zone) * (Math.PI / 180);

  const sin = Math.sin(phi);
  const cos = Math.cos(phi);
  const tan = Math.tan(phi);
  const n = A / Math.sqrt(1 - E2 * sin * sin);
  const t = tan * tan;
  const c = EP2 * cos * cos;
  const a = cos * dLambda;

  const easting =
    K0 * n * (a + ((1 - t + c) * a ** 3) / 6 + ((5 - 18 * t + t * t + 72 * c - 58 * EP2) * a ** 5) / 120) +
    FALSE_EASTING;
  let northing =
    K0 *
    (meridianArc(phi) +
      n *
        tan *
        ((a * a) / 2 +
          ((5 - t + 9 * c + 4 * c * c) * a ** 4) / 24 +
          ((61 - 58 * t + t * t + 600 * c - 330 * EP2) * a ** 6) / 720));
  if (lat < 0) {
    northing += FALSE_NORTHING_SOUTH;
  }

  return { zone, band: bandOf(lat), hemisphere: lat < 0 ? 'S' : 'N', easting, northing };
}

/** The inverse of `toUtm`, as [lon, lat] — what a typed grid reference is read back with. */
export function fromUtm({ zone, hemisphere, easting, northing }: Omit<UtmCoordinate, 'band'>): [number, number] {
  const x = easting - FALSE_EASTING;
  const y = hemisphere === 'S' ? northing - FALSE_NORTHING_SOUTH : northing;
  const e4 = E2 * E2;
  const e6 = e4 * E2;
  const mu = y / K0 / (A * (1 - E2 / 4 - (3 * e4) / 64 - (5 * e6) / 256));
  const e1 = (1 - Math.sqrt(1 - E2)) / (1 + Math.sqrt(1 - E2));
  const phi1 =
    mu +
    ((3 * e1) / 2 - (27 * e1 ** 3) / 32) * Math.sin(2 * mu) +
    ((21 * e1 * e1) / 16 - (55 * e1 ** 4) / 32) * Math.sin(4 * mu) +
    ((151 * e1 ** 3) / 96) * Math.sin(6 * mu) +
    ((1097 * e1 ** 4) / 512) * Math.sin(8 * mu);

  const sin = Math.sin(phi1);
  const cos = Math.cos(phi1);
  const tan = Math.tan(phi1);
  const n1 = A / Math.sqrt(1 - E2 * sin * sin);
  const t1 = tan * tan;
  const c1 = EP2 * cos * cos;
  const r1 = (A * (1 - E2)) / (1 - E2 * sin * sin) ** 1.5;
  const d = x / (n1 * K0);

  const phi =
    phi1 -
    ((n1 * tan) / r1) *
      ((d * d) / 2 -
        ((5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * EP2) * d ** 4) / 24 +
        ((61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * EP2 - 3 * c1 * c1) * d ** 6) / 720);
  const lambda =
    (d -
      ((1 + 2 * t1 + c1) * d ** 3) / 6 +
      ((5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * EP2 + 24 * t1 * t1) * d ** 5) / 120) /
    cos;

  const lon = centralMeridian(zone) + (lambda * 180) / Math.PI;
  return [((((lon + 180) % 360) + 360) % 360) - 180, (phi * 180) / Math.PI];
}
