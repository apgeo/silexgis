// SPDX-License-Identifier: AGPL-3.0-or-later
import { protectedDecimalPlaces } from './protection.ts';
import { FORMAT_SETTINGS, type PlusCodeSettings } from './settings.ts';
import { invalid, NOT_THIS_FORMAT, type FormatDefinition, type ParseOutcome } from './types.ts';

/*
 * Open Location Code ("Plus Codes"), written from the published specification
 * (github.com/google/open-location-code, Apache-2.0) and checked against its test data.
 *
 * A code is up to ten digits of base 20 — five pairs, each a latitude digit and a longitude digit,
 * from 20° cells down to 1/8000° — then up to five more digits, each splitting the cell into a
 * 5-row by 4-column grid. A "+" stands after the eighth digit; a code shorter than eight digits is
 * padded with "0" up to it. Only full (global) codes are read and written here: a short code needs a
 * reference locality, and turning a place name into one needs a geocoder this application does not
 * have.
 */

const ALPHABET = '23456789CFGHJMPQRVWX';
const BASE = 20;
const SEPARATOR = '+';
const SEPARATOR_POSITION = 8;
const PADDING = '0';
const PAIR_CODE_LENGTH = 10;
const GRID_CODE_LENGTH = 5;
const MAX_DIGITS = PAIR_CODE_LENGTH + GRID_CODE_LENGTH;
const GRID_COLUMNS = 4;
const GRID_ROWS = 5;
/** Integer units per degree at the full fifteen digits. */
const FINAL_LAT_PRECISION = 8000 * GRID_ROWS ** GRID_CODE_LENGTH;
const FINAL_LNG_PRECISION = 8000 * GRID_COLUMNS ** GRID_CODE_LENGTH;
const LAT_SPAN = 180 * FINAL_LAT_PRECISION;
const LNG_SPAN = 360 * FINAL_LNG_PRECISION;

/**
 * Degrees scaled to integer units and floored — after rounding away the last digits binary
 * floating point gets wrong, so that 40.6 is 1015000000 units and not 1014999999.9999999 floored to
 * the cell below. The specification's test data include exactly such values.
 */
function scaledFloor(degrees: number, precision: number): number {
  return Math.floor(Math.round(degrees * precision * 1e6) / 1e6);
}

/** A position as the two non-negative integers every code length is cut from. */
export function locationToIntegers(lat: number, lon: number): [number, number] {
  let latValue = scaledFloor(lat, FINAL_LAT_PRECISION) + 90 * FINAL_LAT_PRECISION;
  latValue = Math.min(Math.max(latValue, 0), LAT_SPAN - 1);
  let lngValue = scaledFloor(lon, FINAL_LNG_PRECISION) + 180 * FINAL_LNG_PRECISION;
  lngValue = ((lngValue % LNG_SPAN) + LNG_SPAN) % LNG_SPAN;
  return [latValue, lngValue];
}

function validLength(length: number): boolean {
  return (
    Number.isInteger(length) && length >= 2 && (length >= PAIR_CODE_LENGTH || length % 2 === 0)
  );
}

/** A code of `length` digits (2, 4, 6, 8, or 10 to 15) from the integer form of a position. */
export function encodeIntegers(latInt: number, lngInt: number, length: number): string {
  const digits = new Array<string>(MAX_DIGITS);
  let lat = latInt;
  let lng = lngInt;
  for (let i = MAX_DIGITS - 1; i >= PAIR_CODE_LENGTH; i--) {
    digits[i] = ALPHABET[(lat % GRID_ROWS) * GRID_COLUMNS + (lng % GRID_COLUMNS)];
    lat = Math.floor(lat / GRID_ROWS);
    lng = Math.floor(lng / GRID_COLUMNS);
  }
  for (let i = PAIR_CODE_LENGTH - 2; i >= 0; i -= 2) {
    digits[i] = ALPHABET[lat % BASE];
    digits[i + 1] = ALPHABET[lng % BASE];
    lat = Math.floor(lat / BASE);
    lng = Math.floor(lng / BASE);
  }
  const kept = digits.slice(0, Math.min(length, MAX_DIGITS)).join('');
  if (kept.length >= SEPARATOR_POSITION) {
    return `${kept.slice(0, SEPARATOR_POSITION)}${SEPARATOR}${kept.slice(SEPARATOR_POSITION)}`;
  }
  return `${kept.padEnd(SEPARATOR_POSITION, PADDING)}${SEPARATOR}`;
}

/** A position as a full Plus Code of `length` digits; null for a length the specification has no codes of. */
export function encodePlusCode(lat: number, lon: number, length = PAIR_CODE_LENGTH): string | null {
  if (!Number.isFinite(lat) || !Number.isFinite(lon) || !validLength(length)) {
    return null;
  }
  const [latInt, lngInt] = locationToIntegers(lat, lon);
  return encodeIntegers(latInt, lngInt, length);
}

/** Whether a text is a well-formed code, full or short, by the specification's rules. */
export function isValidPlusCode(raw: string): boolean {
  const code = raw.toUpperCase();
  const separator = code.indexOf(SEPARATOR);
  if (code.length < 2 || separator < 0 || separator !== code.lastIndexOf(SEPARATOR) || separator > SEPARATOR_POSITION || separator % 2 === 1) {
    return false;
  }
  const padding = code.indexOf(PADDING);
  if (padding >= 0) {
    if (padding === 0 || separator < SEPARATOR_POSITION) return false;
    const pad = code.slice(padding, separator);
    if (!/^0+$/.test(pad) || pad.length % 2 === 1 || padding % 2 === 1) return false;
    if (code.length > separator + 1) return false;
  }
  if (code.length - separator - 1 === 1) {
    return false;
  }
  for (const char of code.replace(SEPARATOR, '').replace(/0+$/, '')) {
    if (!ALPHABET.includes(char)) return false;
  }
  return true;
}

/** Whether a valid code is a full one — eight digits before its separator, inside the globe. */
export function isFullPlusCode(raw: string): boolean {
  const code = raw.toUpperCase();
  if (!isValidPlusCode(code) || code.indexOf(SEPARATOR) !== SEPARATOR_POSITION) {
    return false;
  }
  return ALPHABET.indexOf(code[0]) * BASE < 180 && ALPHABET.indexOf(code[1]) * BASE < 360;
}

export interface PlusCodeArea {
  south: number;
  west: number;
  north: number;
  east: number;
  length: number;
}

/** The cell a full code names, or null for anything else. */
export function decodePlusCode(raw: string): PlusCodeArea | null {
  if (!isFullPlusCode(raw)) {
    return null;
  }
  const digits = raw.toUpperCase().replace(SEPARATOR, '').replace(/0+$/, '').slice(0, MAX_DIGITS);
  let south = 0;
  let west = 0;
  let latStep = BASE ** 2;
  let lngStep = BASE ** 2;
  for (let i = 0; i < Math.min(digits.length, PAIR_CODE_LENGTH); i += 2) {
    latStep /= BASE;
    lngStep /= BASE;
    south += ALPHABET.indexOf(digits[i]) * latStep;
    west += ALPHABET.indexOf(digits[i + 1]) * lngStep;
  }
  for (let i = PAIR_CODE_LENGTH; i < digits.length; i++) {
    latStep /= GRID_ROWS;
    lngStep /= GRID_COLUMNS;
    const index = ALPHABET.indexOf(digits[i]);
    south += Math.floor(index / GRID_COLUMNS) * latStep;
    west += (index % GRID_COLUMNS) * lngStep;
  }
  return { south: south - 90, west: west - 180, north: south - 90 + latStep, east: west - 180 + lngStep, length: digits.length };
}

/** The height of a code's cell in degrees of latitude. */
export function plusCodeCellDegrees(length: number): number {
  return length <= PAIR_CODE_LENGTH ? BASE ** (2 - length / 2) : BASE ** -3 / GRID_ROWS ** (length - PAIR_CODE_LENGTH);
}

/**
 * The code length a protected position is written to: the longest whose cell is no smaller than
 * the step decimal degrees would write it to (six digits, a twentieth of a degree, for a 5 km
 * grid) — the lengths below ten jump twenty-fold, so this is the closest a code can come to the
 * grid without naming a square finer than any other format would.
 */
export function protectedPlusCodeLength(gridMetres: number): number {
  const step = 10 ** -protectedDecimalPlaces(gridMetres);
  for (const length of [8, 6, 4, 2]) {
    if (plusCodeCellDegrees(length) >= step - 1e-12) return length;
  }
  return 2;
}

export function parsePlusCode(raw: string): ParseOutcome {
  const code = raw.replace(/\s+/g, '').toUpperCase();
  if (!code.includes(SEPARATOR) || !/^[0-9A-Z+]+$/.test(code)) {
    return NOT_THIS_FORMAT;
  }
  if (!isValidPlusCode(code)) {
    return /^[+]?[-]?\d+(?:\.\d+)?$/.test(code) ? NOT_THIS_FORMAT : invalid('plusCodeInvalid');
  }
  const area = decodePlusCode(code);
  if (!area) {
    return invalid('plusCodeShort');
  }
  return {
    kind: 'parsed',
    candidates: [
      {
        lat: Math.min((area.south + area.north) / 2, 90),
        lon: (area.west + area.east) / 2,
        format: 'pluscode',
        assumptions: [],
      },
    ],
  };
}

export type { PlusCodeSettings } from './settings.ts';

/** Ten digits is about 14 m by 14 m at the equator, eleven about 3.5 m, twelve under a metre. */
export const PLUS_CODE: FormatDefinition<PlusCodeSettings> = {
  id: 'pluscode',
  ...FORMAT_SETTINGS.pluscode,
  format: (lon, lat, s) => encodePlusCode(lat, lon, s.length),
  formatProtected: (lon, lat, s, gridMetres) =>
    encodePlusCode(lat, lon, Math.min(s.length, protectedPlusCodeLength(gridMetres))),
  parse: parsePlusCode,
};
