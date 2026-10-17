// SPDX-License-Identifier: AGPL-3.0-or-later
import { DEFAULT_PROTECTION_GRID_METRES } from './formats/protection.ts';
import { writePosition } from './formats/registry.ts';
import { FORMAT_IDS, type CoordinateFormat, type FormatSettings } from './formats/settings.ts';

/**
 * How a person has chosen to read positions — one of the formats in `formats/registry.ts`, each
 * with its own settings and a reader for typed positions. This module is the seam every display
 * site writes through: the chosen format, the person's settings for it, and the rule for a
 * position the server snapped to its location-protection grid.
 */
export { DEFAULT_COORDINATE_FORMAT, isCoordinateFormat, type CoordinateFormat } from './formats/settings.ts';

export const COORDINATE_FORMATS: readonly CoordinateFormat[] = FORMAT_IDS;

export { fromUtm, toUtm, type UtmCoordinate } from './formats/utmMath.ts';
export { DEFAULT_PROTECTION_GRID_METRES } from './formats/protection.ts';

/**
 * A position as text in the chosen format, with the person's settings for it (each format's
 * defaults where none are given).
 *
 * Where the chosen format has no reference for the position — UTM beyond its latitudes, a national
 * grid outside its country — the position falls back to decimal degrees rather than print a
 * reference that does not exist, so a display is never empty.
 */
export function formatCoordinate(
  lon: number,
  lat: number,
  format: CoordinateFormat,
  settings?: Partial<FormatSettings>,
): string {
  return writePosition(format, lon, lat, settings) ?? writePosition('dd', lon, lat, settings) ?? '';
}

/**
 * A position the server has snapped to its location-protection grid, written no finer than that
 * grid in whatever format and with whatever settings: printed to the metre, a point moved by up to
 * half a grid square reads as a surveyed one.
 */
export function formatApproximateCoordinate(
  lon: number,
  lat: number,
  format: CoordinateFormat,
  settings?: Partial<FormatSettings>,
  gridMetres: number = DEFAULT_PROTECTION_GRID_METRES,
): string {
  const options = { protectedGridMetres: gridMetres };
  return writePosition(format, lon, lat, settings, options) ?? writePosition('dd', lon, lat, settings, options) ?? '';
}

/** The places decimal degrees go on the clipboard with: six (about 10 cm) unless the person asked for more. */
const COPY_DECIMALS = 6;

/**
 * A position as it is put on the clipboard. In decimal degrees that is `lat, lon` with signs — the
 * form a GPS app, a search box or a spreadsheet takes as it is — rather than the display form with
 * its hemisphere letters; in the other formats it is the display form, which is what those formats
 * are typed as elsewhere. A protected position is copied exactly as coarse as it is shown.
 */
export function formatCoordinateForCopy(
  lon: number,
  lat: number,
  format: CoordinateFormat,
  settings?: Partial<FormatSettings>,
  approximate = false,
): string {
  const options = approximate ? { protectedGridMetres: DEFAULT_PROTECTION_GRID_METRES } : {};
  if (format !== 'dd') {
    const text = writePosition(format, lon, lat, settings, options);
    if (text !== null) {
      return text;
    }
  }
  const ownDecimals = settings?.dd?.decimals;
  const copyDd = {
    ...settings,
    dd: {
      style: 'signed' as const,
      // A protected position's places are cut to the grid by the writer whatever is asked here.
      decimals: Math.max(COPY_DECIMALS, ownDecimals ?? 0),
    },
  };
  return writePosition('dd', lon, lat, copyDd, options) ?? '';
}
