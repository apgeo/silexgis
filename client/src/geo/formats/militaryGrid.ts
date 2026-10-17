// SPDX-License-Identifier: AGPL-3.0-or-later
import { forward, toPoint } from 'mgrs';
import { normaliseCoordinateText } from './angles.ts';
import { FORMAT_SETTINGS, type MgrsSettings } from './settings.ts';
import { invalid, NOT_THIS_FORMAT, type FormatDefinition, type ParseOutcome } from './types.ts';
import { UTM_NORTH_LIMIT, UTM_SOUTH_LIMIT } from './utmMath.ts';

export type { MgrsSettings } from './settings.ts';

/**
 * The square a protected position is named by. A 1 km square is finer than the 5 km grid a
 * protected position is snapped to, and MGRS has nothing between 1 km and 10 km, so a protected
 * position is always named by its 10 km square — coarser than the grid, never finer.
 */
const PROTECTED_PRECISION_METRES = 10_000;

function digitsFor(precisionMetres: number): number {
  return Math.max(1, Math.min(5, 5 - Math.round(Math.log10(precisionMetres))));
}

function write(lon: number, lat: number, precisionMetres: number, spacing: MgrsSettings['spacing']): string | null {
  // MGRS proper covers UTM's latitudes; the polar caps are UPS, which this library does not write.
  if (!Number.isFinite(lon) || !Number.isFinite(lat) || lat < UTM_SOUTH_LIMIT || lat >= UTM_NORTH_LIMIT) {
    return null;
  }
  const digits = digitsFor(precisionMetres);
  let compact: string;
  try {
    compact = forward([lon, lat], digits);
  } catch {
    return null;
  }
  if (spacing === 'compact') {
    return compact;
  }
  const match = /^(\d{1,2}[A-Z])([A-Z]{2})(\d*)$/.exec(compact);
  if (!match) {
    return compact;
  }
  const numbers = match[3];
  const half = numbers.length / 2;
  return `${match[1]} ${match[2]} ${numbers.slice(0, half)} ${numbers.slice(half)}`;
}

/**
 * Reads an MGRS reference with or without spaces — `35T LM 78752 43143`, `35TLM7875243143`,
 * `35TLM 787 431` — as the centre of the square it names. The library throws plain strings, not
 * Errors, on a reference it cannot decode, so anything thrown is a malformed reference.
 */
export function parseMgrs(raw: string): ParseOutcome {
  const text = normaliseCoordinateText(raw).replace(/^MGRS\s*:?\s*/, '').replace(/\s+/g, '');
  const match = /^(\d{1,2})([C-HJ-NP-X])([A-HJ-NP-Z]{2})(\d{0,10})$/.exec(text);
  if (!match) {
    return NOT_THIS_FORMAT;
  }
  const zone = Number(match[1]);
  if (zone < 1 || zone > 60) {
    return invalid('utmZoneRange');
  }
  if (match[4].length % 2 === 1) {
    return invalid('mgrsInvalid');
  }
  let point: [number, number];
  try {
    point = toPoint(text);
  } catch {
    return invalid('mgrsInvalid');
  }
  const [lon, lat] = point;
  if (!Number.isFinite(lon) || !Number.isFinite(lat)) {
    return invalid('mgrsInvalid');
  }
  return { kind: 'parsed', candidates: [{ lon, lat, format: 'mgrs', assumptions: [] }] };
}

/**
 * The Military Grid Reference System: a UTM zone and band, a 100 km square's two letters, and an
 * easting and a northing inside it, truncated to the square the precision names.
 */
export const MGRS: FormatDefinition<MgrsSettings> = {
  id: 'mgrs',
  ...FORMAT_SETTINGS.mgrs,
  format: (lon, lat, s) => write(lon, lat, s.precisionMetres, s.spacing),
  formatProtected: (lon, lat, s) =>
    write(lon, lat, Math.max(s.precisionMetres, PROTECTED_PRECISION_METRES), s.spacing),
  parse: parseMgrs,
};
