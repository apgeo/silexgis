// SPDX-License-Identifier: AGPL-3.0-or-later
import { protectedDecimalPlaces } from './protection.ts';
import {
  DEFAULT_READING_RULES,
  FORMAT_SETTINGS,
  type DdmSettings,
  type DdSettings,
  type DmsSettings,
  type ReadingRules,
} from './settings.ts';
import {
  invalid,
  NOT_THIS_FORMAT,
  type FormatDefinition,
  type ParseAssumption,
  type ParseOutcome,
} from './types.ts';

// ---- writing -------------------------------------------------------------------------------------

function ns(lat: number): string {
  return lat >= 0 ? 'N' : 'S';
}

function ew(lon: number): string {
  return lon >= 0 ? 'E' : 'W';
}

/**
 * One angle as whole degrees and decimal minutes, rounded and carried upwards when rounding makes
 * the minutes 60, so the text never reads 59.9995′ as 60.000′.
 */
function degreesMinutes(angle: number, minutesDecimals: number): string {
  const scale = 10 ** minutesDecimals;
  const totalMinutes = Math.round(Math.abs(angle) * 60 * scale) / scale;
  const degrees = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes - degrees * 60;
  return `${degrees}°${minutes.toFixed(minutesDecimals).padStart(minutesDecimals > 0 ? minutesDecimals + 3 : 2, '0')}′`;
}

/** One angle as degrees, minutes and seconds, the seconds to `secondsDecimals` places, carried likewise. */
function degreesMinutesSeconds(angle: number, secondsDecimals: number): string {
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

/** Whole degrees and minutes — a minute of latitude is about 1.85 km, the protected form of both. */
function wholeMinutes(lon: number, lat: number): string {
  return `${degreesMinutes(lat, 0)}${ns(lat)} ${degreesMinutes(lon, 0)}${ew(lon)}`;
}

function decimalDegrees(lon: number, lat: number, decimals: number, style: 'letters' | 'signed'): string {
  if (style === 'signed') {
    // Latitude first and a comma between: the form a GPS app, a search box or a spreadsheet takes.
    // A value that rounds to zero is written without its sign: "-0.00" names no hemisphere.
    const signed = (value: number) => value.toFixed(decimals).replace(/^-(?=[0.]*$)/, '');
    return `${signed(lat)}, ${signed(lon)}`;
  }
  return `${Math.abs(lat).toFixed(decimals)}°${ns(lat)} ${Math.abs(lon).toFixed(decimals)}°${ew(lon)}`;
}

function finite(lon: number, lat: number): boolean {
  return Number.isFinite(lon) && Number.isFinite(lat);
}

export type { DdmSettings, DdSettings, DmsSettings } from './settings.ts';

// ---- reading -------------------------------------------------------------------------------------

/**
 * The text as the parser reads it: every degree, minute and second mark people type or paste (the
 * masculine ordinal and ring for degrees, apostrophes, acute accents and primes for minutes,
 * quotation marks, double primes and two apostrophes for seconds), every dash used as a minus, and
 * upper case.
 */
export function normaliseCoordinateText(text: string): string {
  return text
    .replace(/[   ]/g, ' ')
    .replace(/[°º˚ᵒ]/g, '°')
    .replace(/''|′′|’’|‘‘|´´/g, '″')
    .replace(/[″"“”]/g, '″')
    .replace(/['’‘´`′]/g, '′')
    .replace(/[−–—]/g, '-')
    .toUpperCase()
    .trim();
}

type Token =
  | { kind: 'letter'; letter: 'N' | 'S' | 'E' | 'W' }
  | { kind: 'number'; text: string; value: number; negative: boolean; unit: 0 | 1 | 2 | null };

/**
 * The tokens of one angle text, or null when something in it is not part of an angle — a letter
 * other than a hemisphere, say, which is how a grid reference or a Plus Code is told apart. West
 * is also read from V, the Romanian "vest".
 */
function tokenise(text: string): Token[] | null {
  const tokens: Token[] = [];
  const pattern = /\s*(?:([NSEWV])|([-+]?)(\d+(?:\.\d+)?|\.\d+)\s*([°′″])?)/y;
  let index = 0;
  while (index < text.length) {
    pattern.lastIndex = index;
    const match = pattern.exec(text);
    if (!match || match[0].length === 0) {
      return text.slice(index).trim() === '' ? tokens : null;
    }
    index = pattern.lastIndex;
    if (match[1]) {
      const letter = match[1] === 'V' ? 'W' : (match[1] as 'N' | 'S' | 'E' | 'W');
      tokens.push({ kind: 'letter', letter });
    } else {
      const unit = match[4] === '°' ? 0 : match[4] === '′' ? 1 : match[4] === '″' ? 2 : null;
      tokens.push({ kind: 'number', text: match[3], value: Number(match[3]), negative: match[2] === '-', unit });
    }
  }
  return tokens;
}

type NumberToken = Extract<Token, { kind: 'number' }>;
interface Group {
  numbers: NumberToken[];
  letter: 'N' | 'S' | 'E' | 'W' | null;
}

type Read<T> = { ok: true; value: T } | { ok: false; outcome: ParseOutcome };

/** Splits the tokens of a whole text into the two angles it names. */
function groups(tokens: Token[]): Read<Group[]> {
  const letters = tokens.filter((token) => token.kind === 'letter').length;
  const result: Group[] = [];
  if (letters > 0) {
    const prefix = tokens[0].kind === 'letter';
    let current: Group = { numbers: [], letter: null };
    for (const token of tokens) {
      if (token.kind === 'letter') {
        if (prefix) {
          if (current.letter !== null || current.numbers.length > 0) result.push(current);
          current = { numbers: [], letter: token.letter };
        } else {
          current.letter = token.letter;
          result.push(current);
          current = { numbers: [], letter: null };
        }
      } else {
        current.numbers.push(token);
      }
    }
    if (current.letter !== null || current.numbers.length > 0) result.push(current);
    return { ok: true, value: result };
  }
  const numbers = tokens as NumberToken[];
  if (numbers.some((token) => token.unit !== null)) {
    let current: Group = { numbers: [], letter: null };
    for (const token of numbers) {
      if (token.unit === 0 && current.numbers.length > 0) {
        result.push(current);
        current = { numbers: [], letter: null };
      }
      current.numbers.push(token);
    }
    result.push(current);
    return { ok: true, value: result };
  }
  if (numbers.length % 2 !== 0 || numbers.length < 2 || numbers.length > 6) {
    return { ok: false, outcome: invalid('componentCount') };
  }
  const half = numbers.length / 2;
  return {
    ok: true,
    value: [
      { numbers: numbers.slice(0, half), letter: null },
      { numbers: numbers.slice(half), letter: null },
    ],
  };
}

/** One angle's signed value in degrees and how many components (1, 2 or 3) it was written with. */
function angle(group: Group): Read<{ degrees: number; components: number }> {
  const { numbers } = group;
  if (numbers.length < 1 || numbers.length > 3) {
    return { ok: false, outcome: invalid('componentCount') };
  }
  const values = [0, 0, 0];
  let position = -1;
  for (const [index, token] of numbers.entries()) {
    const at = token.unit ?? position + 1;
    if (at <= position || at > 2 || (index === 0 && at !== 0)) {
      return { ok: false, outcome: invalid('unitOrder') };
    }
    if (index > 0 && token.negative) {
      return { ok: false, outcome: invalid('unitOrder') };
    }
    if (index < numbers.length - 1 && token.text.includes('.')) {
      return { ok: false, outcome: invalid('fractionNotLast') };
    }
    values[at] = token.value;
    position = at;
  }
  if (values[1] >= 60) return { ok: false, outcome: invalid('minutesRange') };
  if (values[2] >= 60) return { ok: false, outcome: invalid('secondsRange') };
  const negative = numbers[0].negative;
  if (negative && (group.letter === 'S' || group.letter === 'W' || group.letter === 'N' || group.letter === 'E')) {
    return { ok: false, outcome: invalid('hemisphereConflict') };
  }
  const magnitude = values[0] + values[1] / 60 + values[2] / 3600;
  const signed = negative || group.letter === 'S' || group.letter === 'W' ? -magnitude : magnitude;
  return { ok: true, value: { degrees: signed, components: position + 1 } };
}

/**
 * The text split where it names two angles, with commas resolved: a comma followed by a space, or
 * any comma in a text that uses decimal points, separates; commas between digits in a text without
 * decimal points are decimal commas — unless there is exactly one and no space anywhere ("45,25"),
 * which separates two whole numbers.
 */
function splitAngles(text: string): string[] {
  const decimal = (part: string) => part.replace(/(\d),(\d)/g, '$1.$2');
  if (/[;\t|]/.test(text)) {
    return text.split(/[;\t|]/).map((part) => (text.includes('.') ? part : decimal(part)));
  }
  if (!text.includes(',')) {
    return [text];
  }
  if (text.includes('.')) {
    return text.split(',');
  }
  if (/,\s/.test(text)) {
    return text.split(/,\s+/).map(decimal);
  }
  const commas = (text.match(/,/g) ?? []).length;
  if (commas === 1 && !/\s/.test(text)) {
    return text.split(',');
  }
  return [decimal(text)];
}

const ANGLE_FORMAT_BY_COMPONENTS = ['dd', 'ddm', 'dms'] as const;

/**
 * Reads a position written as two angles, in any of decimal degrees, degrees and decimal minutes,
 * or degrees, minutes and seconds — with hemisphere letters before or after each angle or a sign
 * instead, with or without degree, minute and second marks, with decimal points or decimal
 * commas. Without letters the order is latitude, longitude unless the first number can only be a
 * longitude; the reading then says the order was assumed.
 */
export function parseAngles(raw: string, rules: ReadingRules = DEFAULT_READING_RULES): ParseOutcome {
  const text = normaliseCoordinateText(raw);
  if (text === '') {
    return NOT_THIS_FORMAT;
  }
  const parts = splitAngles(text);
  if (parts.length > 2) {
    return invalid('componentCount');
  }
  const tokenLists = parts.map(tokenise);
  if (tokenLists.some((tokens) => tokens === null || tokens.length === 0)) {
    return NOT_THIS_FORMAT;
  }
  let angleGroups: Group[];
  if (tokenLists.length === 2) {
    angleGroups = [];
    // Either side of a separator is one angle, its letter before or after its numbers.
    for (const tokens of tokenLists as Token[][]) {
      const letters = tokens.filter((token) => token.kind === 'letter');
      if (letters.length > 1) {
        return invalid('componentCount');
      }
      angleGroups.push({
        numbers: tokens.filter((token): token is NumberToken => token.kind === 'number'),
        letter: letters[0]?.letter ?? null,
      });
    }
  } else {
    const split = groups(tokenLists[0] as Token[]);
    if (!split.ok) {
      return split.outcome;
    }
    angleGroups = split.value;
  }
  if (angleGroups.length !== 2) {
    return invalid('componentCount');
  }

  const read = angleGroups.map(angle);
  for (const one of read) {
    if (!one.ok) return one.outcome;
  }
  const [first, second] = read.map((one) => (one as { ok: true; value: { degrees: number; components: number } }).value);
  const [firstAxis, secondAxis] = angleGroups.map((group) =>
    group.letter === 'N' || group.letter === 'S' ? 'lat' : group.letter === 'E' || group.letter === 'W' ? 'lon' : null,
  );

  const assumptions: ParseAssumption[] = [];
  let firstIsLat: boolean;
  if (firstAxis && secondAxis) {
    if (firstAxis === secondAxis) return invalid('hemisphereConflict');
    firstIsLat = firstAxis === 'lat';
  } else if (firstAxis) {
    firstIsLat = firstAxis === 'lat';
  } else if (secondAxis) {
    firstIsLat = secondAxis === 'lon';
  } else if (Math.abs(first.degrees) > 90 && Math.abs(second.degrees) <= 90) {
    firstIsLat = false;
  } else if (rules.pairOrder === 'lonLat' && !(Math.abs(second.degrees) > 90 && Math.abs(first.degrees) <= 90)) {
    // Longitude first, as the person chose — unless the second number cannot be a latitude.
    firstIsLat = false;
    assumptions.push('lonLatOrder');
  } else {
    firstIsLat = true;
    assumptions.push('latLonOrder');
  }
  const lat = firstIsLat ? first.degrees : second.degrees;
  const lon = firstIsLat ? second.degrees : first.degrees;
  if (Math.abs(lat) > 90) return invalid('latitudeRange');
  if (Math.abs(lon) > 180) return invalid('longitudeRange');

  const components = Math.max(first.components, second.components);
  return {
    kind: 'parsed',
    candidates: [{ lon, lat, format: ANGLE_FORMAT_BY_COMPONENTS[components - 1], assumptions }],
  };
}

// ---- the three definitions ---------------------------------------------------------------------

/** Decimal degrees: places from whole degrees to eight (about a millimetre), hemisphere letters or signs. */
export const DD: FormatDefinition<DdSettings> = {
  id: 'dd',
  ...FORMAT_SETTINGS.dd,
  format: (lon, lat, s) => (finite(lon, lat) ? decimalDegrees(lon, lat, s.decimals, s.style) : null),
  formatProtected: (lon, lat, s, gridMetres) =>
    finite(lon, lat) ? decimalDegrees(lon, lat, Math.min(s.decimals, protectedDecimalPlaces(gridMetres)), s.style) : null,
  parse: parseAngles,
};

/** Degrees and decimal minutes — the form most GPS receivers and marine charts use. Three places ≈ 2 m. */
export const DDM: FormatDefinition<DdmSettings> = {
  id: 'ddm',
  ...FORMAT_SETTINGS.ddm,
  format: (lon, lat, s) =>
    finite(lon, lat)
      ? `${degreesMinutes(lat, s.minutesDecimals)}${ns(lat)} ${degreesMinutes(lon, s.minutesDecimals)}${ew(lon)}`
      : null,
  formatProtected: (lon, lat) => (finite(lon, lat) ? wholeMinutes(lon, lat) : null),
  parse: parseAngles,
};

/** Degrees, minutes and seconds; one place of seconds is about three metres. */
export const DMS: FormatDefinition<DmsSettings> = {
  id: 'dms',
  ...FORMAT_SETTINGS.dms,
  format: (lon, lat, s) =>
    finite(lon, lat)
      ? `${degreesMinutesSeconds(lat, s.secondsDecimals)}${ns(lat)} ${degreesMinutesSeconds(lon, s.secondsDecimals)}${ew(lon)}`
      : null,
  formatProtected: (lon, lat) => (finite(lon, lat) ? wholeMinutes(lon, lat) : null),
  parse: parseAngles,
};
