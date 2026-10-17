// SPDX-License-Identifier: AGPL-3.0-or-later
import { DD, DDM, DMS, parseAngles } from './angles.ts';
import { MGRS } from './militaryGrid.ts';
import { STEREO70, SWISS_LV03, SWISS_LV95 } from './nationalGrids.ts';
import { PLUS_CODE } from './plusCodes.ts';
import { DEFAULT_PROTECTION_GRID_METRES } from './protection.ts';
import { FORMAT_IDS, type FormatId, type FormatSettings } from './settings.ts';
import type { FormatDefinition, ParseError, ParsedPosition } from './types.ts';
import { UTM } from './utm.ts';

export {
  defaultFormatSettings,
  FORMAT_IDS,
  isFormatId,
  sanitizeFormatSettings,
  type FormatId,
  type FormatSettings,
} from './settings.ts';

/**
 * Every way this client writes and reads a position, by id. A format is named by an id rather
 * than being a function so that it can be stored as a preference, and so that adding one — a
 * national grid, say — is one more entry here, with every display site, the settings screen and
 * the converter already asking by id.
 *
 * The ids, their order and each format's settings live in `settings.ts`, which writes no
 * position, so that the preference stores can name formats without loading the grid libraries
 * this module brings in.
 */
export const FORMATS: { readonly [K in FormatId]: FormatDefinition<FormatSettings[K]> } = {
  dd: DD,
  ddm: DDM,
  dms: DMS,
  utm: UTM,
  mgrs: MGRS,
  stereo70: STEREO70,
  lv95: SWISS_LV95,
  lv03: SWISS_LV03,
  pluscode: PLUS_CODE,
};

function definition<K extends FormatId>(id: K): FormatDefinition<FormatSettings[K]> {
  return FORMATS[id] as unknown as FormatDefinition<FormatSettings[K]>;
}

export interface WriteOptions {
  /**
   * The position is one the server snapped to its location-protection grid: it is written no
   * finer than that grid in every format, whatever the settings ask for.
   */
  protectedGridMetres?: number | null;
}

/**
 * A position in one format, with that format's settings (defaults where none are given); null
 * where the format has no reference for the position ("not available here").
 */
export function writePosition<K extends FormatId>(
  id: K,
  lon: number,
  lat: number,
  settings?: Partial<FormatSettings>,
  options: WriteOptions = {},
): string | null {
  const format = definition(id);
  const own = (settings?.[id] ?? format.defaults) as FormatSettings[K];
  if (options.protectedGridMetres !== undefined && options.protectedGridMetres !== null) {
    return format.formatProtected(lon, lat, own, options.protectedGridMetres || DEFAULT_PROTECTION_GRID_METRES);
  }
  return format.format(lon, lat, own);
}

/** The outcome of reading a typed or pasted position. */
export type PositionReading =
  | { kind: 'parsed'; candidates: readonly ParsedPosition[] }
  | { kind: 'invalid'; error: ParseError };

/**
 * Reads a position written in any format this client knows, or — when `only` is given — in that
 * format alone, for a reader who picked the format because the text was ambiguous. Every format is
 * asked; the readings of all that recognise the text are returned together (more than one only for
 * a genuinely ambiguous text), and when none can read it the error is the first format's that
 * recognised the shape and found it malformed.
 *
 * Decimal degrees, degrees-minutes and degrees-minutes-seconds share one reader, which tells them
 * apart by the text's shape.
 */
export function readPosition(text: string, only?: FormatId): PositionReading {
  const angleIds: readonly FormatId[] = ['dd', 'ddm', 'dms'];
  const readers: { id: FormatId; parse: (text: string) => ReturnType<typeof parseAngles> }[] = [];
  if (only === undefined || angleIds.includes(only)) {
    readers.push({ id: 'dd', parse: parseAngles });
  }
  for (const id of FORMAT_IDS) {
    if (angleIds.includes(id) || (only !== undefined && only !== id)) continue;
    readers.push({ id, parse: FORMATS[id].parse });
  }

  const candidates: ParsedPosition[] = [];
  let error: ParseError | null = null;
  for (const reader of readers) {
    const outcome = reader.parse(text);
    if (outcome.kind === 'parsed') {
      candidates.push(...outcome.candidates);
    } else if (outcome.kind === 'invalid' && error === null) {
      error = outcome.error;
    }
  }
  if (candidates.length > 0) {
    return { kind: 'parsed', candidates };
  }
  return { kind: 'invalid', error: error ?? 'unrecognised' };
}
