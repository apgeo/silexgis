// SPDX-License-Identifier: AGPL-3.0-or-later
import { formatLonLat } from './coords.ts';

/**
 * How a person has chosen to read positions: decimal degrees, degrees-minutes-seconds, or UTM
 * grid coordinates with their zone.
 *
 * A format is named by an id rather than being a function so that it can be stored as a
 * preference and so that a format added later — a national grid such as Stereo 70, which needs a
 * projection library this client does not carry — is one more id here and one more branch in
 * `formatCoordinate`, with every display site already asking by id.
 */
export type CoordinateFormat = 'dd' | 'dms' | 'utm';

export const COORDINATE_FORMATS: readonly CoordinateFormat[] = ['dd', 'dms', 'utm'];

export const DEFAULT_COORDINATE_FORMAT: CoordinateFormat = 'dd';

/** Whether a stored value is a format this client knows, so an unknown one falls back. */
export function isCoordinateFormat(value: unknown): value is CoordinateFormat {
  return typeof value === 'string' && (COORDINATE_FORMATS as readonly string[]).includes(value);
}

// ---- degrees, minutes, seconds -----------------------------------------------------------------

/**
 * One angle as degrees, minutes and seconds, the seconds to `secondsDecimals` places — carried
 * upwards when rounding makes them 60, so the text never reads 59′60″.
 */
function dms(angle: number, secondsDecimals: number): string {
  const scale = 10 ** secondsDecimals;
  let totalSeconds = Math.round(Math.abs(angle) * 3600 * scale) / scale;
  const degrees = Math.floor(totalSeconds / 3600);
  totalSeconds -= degrees * 3600;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds - minutes * 60;
  return `${degrees}°${String(minutes).padStart(2, '0')}′${seconds
    .toFixed(secondsDecimals)
    .padStart(secondsDecimals > 0 ? secondsDecimals + 3 : 2, '0')}″`;
}

// ---- UTM ---------------------------------------------------------------------------------------

/** WGS84, the datum every position in this application is held in. */
const A = 6378137;
const F = 1 / 298.257223563;
const E2 = F * (2 - F);
const EP2 = E2 / (1 - E2);
const K0 = 0.9996;
const FALSE_EASTING = 500000;
const FALSE_NORTHING_SOUTH = 10000000;

/** UTM is defined between these latitudes; the poles have a grid of their own. */
const UTM_SOUTH_LIMIT = -80;
const UTM_NORTH_LIMIT = 84;

/** Latitude bands, 8° each from 80°S, the last one (X) 12° tall. I and O are not used. */
const BANDS = 'CDEFGHJKLMNPQRSTUVWX';

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
function utmZone(lon: number, lat: number): number {
  const plain = Math.floor((((lon + 180) % 360) + 360) % 360 / 6) + 1;
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

function centralMeridian(zone: number): number {
  return (zone - 1) * 6 - 180 + 3;
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
 */
export function toUtm(lon: number, lat: number): UtmCoordinate | null {
  if (!Number.isFinite(lon) || !Number.isFinite(lat) || lat < UTM_SOUTH_LIMIT || lat > UTM_NORTH_LIMIT) {
    return null;
  }
  const zone = utmZone(lon, lat);
  const phi = (lat * Math.PI) / 180;
  const dLambda = (((lon - centralMeridian(zone) + 540) % 360) - 180) * (Math.PI / 180);

  const sin = Math.sin(phi);
  const cos = Math.cos(phi);
  const tan = Math.tan(phi);
  const n = A / Math.sqrt(1 - E2 * sin * sin);
  const t = tan * tan;
  const c = EP2 * cos * cos;
  const a = cos * dLambda;

  const easting =
    K0 *
      n *
      (a + ((1 - t + c) * a ** 3) / 6 + ((5 - 18 * t + t * t + 72 * c - 58 * EP2) * a ** 5) / 120) +
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

  const band = BANDS[Math.min(BANDS.length - 1, Math.floor((lat - UTM_SOUTH_LIMIT) / 8))];
  return { zone, band, hemisphere: lat < 0 ? 'S' : 'N', easting, northing };
}

/** The inverse of `toUtm`, as [lon, lat]. Used by the tests; display needs only the forward way. */
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
    (d - ((1 + 2 * t1 + c1) * d ** 3) / 6 + ((5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * EP2 + 24 * t1 * t1) * d ** 5) / 120) /
    cos;

  return [centralMeridian(zone) + (lambda * 180) / Math.PI, (phi * 180) / Math.PI];
}

/**
 * Metres at the precision `decimals` places of a degree carry (5 places ≈ 1 m) — truncated, not
 * rounded, as grid references are by convention: a printed reference names the square the point
 * lies in, so the same point printed coarser still names a square that contains it.
 */
function metres(value: number, decimals: number): string {
  if (decimals >= 5) {
    const scale = 10 ** (decimals - 5);
    return (Math.floor(value * scale) / scale).toFixed(decimals - 5);
  }
  const step = 10 ** (5 - decimals);
  return String(Math.floor(value / step) * step);
}

// ---- the one formatter ---------------------------------------------------------------------------

/**
 * A position as text in the chosen format. `decimals` is the precision the caller would print in
 * decimal degrees — five places, about a metre, unless it says otherwise — and the other formats
 * keep to roughly the same ground precision rather than inventing a finer one: seconds get
 * `decimals − 4` places (one place, about three metres, at five), UTM gets whole metres at five.
 *
 * Outside the latitudes UTM is defined for, UTM falls back to decimal degrees rather than print a
 * grid reference that does not exist.
 */
export function formatCoordinate(lon: number, lat: number, format: CoordinateFormat, decimals = 5): string {
  switch (format) {
    case 'dms': {
      const secondsDecimals = Math.max(0, decimals - 4);
      return `${dms(lat, secondsDecimals)}${lat >= 0 ? 'N' : 'S'} ${dms(lon, secondsDecimals)}${lon >= 0 ? 'E' : 'W'}`;
    }
    case 'utm': {
      const utm = toUtm(lon, lat);
      if (!utm) {
        return formatLonLat(lon, lat, decimals);
      }
      return `${utm.zone}${utm.band} ${metres(utm.easting, decimals)} E ${metres(utm.northing, decimals)} N`;
    }
    case 'dd':
    default:
      return formatLonLat(lon, lat, decimals);
  }
}
