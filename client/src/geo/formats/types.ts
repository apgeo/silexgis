// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * A format's settings: each one a choice from a short, fixed list, so that a stored value is
 * checked by membership alone and the settings screen can offer every choice without knowing the
 * format.
 */
export type FormatSettingsShape = Record<string, string | number>;

export type SettingChoices<S extends FormatSettingsShape> = { readonly [K in keyof S]: readonly S[K][] };

/**
 * Something the reader of a parsed position should be told was assumed rather than read: the
 * order of two bare numbers, the hemisphere of a grid reference typed without one, how a
 * letter that is both a UTM band and a hemisphere was taken, or which of two unlabelled national
 * grid numbers was taken as the easting.
 */
export type ParseAssumption =
  | 'latLonOrder'
  | 'northernHemisphere'
  | 'utmLetterAsBand'
  | 'utmLetterAsHemisphere'
  | 'gridAxisOrder';

/** Why a text that looked like a format could not be read as a position in it. */
export type ParseError =
  | 'unrecognised'
  | 'latitudeRange'
  | 'longitudeRange'
  | 'minutesRange'
  | 'secondsRange'
  | 'fractionNotLast'
  | 'unitOrder'
  | 'hemisphereConflict'
  | 'componentCount'
  | 'utmZoneRange'
  | 'utmBandUnknown'
  | 'utmBandMismatch'
  | 'utmEastingRange'
  | 'utmNorthingRange'
  | 'plusCodeShort'
  | 'plusCodeInvalid'
  | 'mgrsInvalid'
  | 'gridOutsideArea';

export interface ParsedPosition {
  lon: number;
  lat: number;
  /** The format the text was written in, as far as its shape tells. */
  format: string;
  assumptions: readonly ParseAssumption[];
}

/**
 * What one format makes of a text: one or more readings (more than one only when the text is
 * genuinely ambiguous, such as a UTM reference whose letter may be a band or a hemisphere), a
 * reason it is malformed, or that it is not written in this format at all.
 */
export type ParseOutcome =
  | { kind: 'parsed'; candidates: readonly ParsedPosition[] }
  | { kind: 'invalid'; error: ParseError }
  | { kind: 'notThisFormat' };

export interface FormatDefinition<S extends FormatSettingsShape> {
  /** The id a preference stores and every display site asks by. */
  id: string;
  choices: SettingChoices<S>;
  defaults: S;
  /** The position as text, or null when the format has no reference for it ("not available here"). */
  format(lon: number, lat: number, settings: S): string | null;
  /**
   * A position the server has snapped to its location-protection grid, written no finer than the
   * grid allows — printed finer, a point moved by up to half a grid square reads as a surveyed one.
   */
  formatProtected(lon: number, lat: number, settings: S, gridMetres: number): string | null;
  parse(text: string): ParseOutcome;
}

export const NOT_THIS_FORMAT: ParseOutcome = { kind: 'notThisFormat' };

export function invalid(error: ParseError): ParseOutcome {
  return { kind: 'invalid', error };
}

/**
 * A stored settings object made safe member by member: a value outside its choices (or of the
 * wrong type, or missing) falls back to the default, so one bad member never discards the rest.
 */
export function sanitizeSettings<S extends FormatSettingsShape>(
  definition: Pick<FormatDefinition<S>, 'choices' | 'defaults'>,
  raw: unknown,
): S {
  const out = { ...definition.defaults };
  if (raw === null || typeof raw !== 'object') {
    return out;
  }
  const record = raw as Record<string, unknown>;
  for (const key of Object.keys(definition.choices) as (keyof S & string)[]) {
    const value = record[key];
    if ((definition.choices[key] as readonly unknown[]).includes(value)) {
      out[key] = value as S[typeof key];
    }
  }
  return out;
}
