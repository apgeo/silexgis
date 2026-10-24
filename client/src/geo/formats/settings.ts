// SPDX-License-Identifier: AGPL-3.0-or-later
import { sanitizeSettings, type ReadingRules, type SettingChoices, type FormatSettingsShape } from './types.ts';

export type { ReadingRules };

/**
 * The formats a position can be written in, by id, with each one's settings: the choices a
 * setting may take and its default. This is everything a stored preference needs — the ids it may
 * name and the values it may hold — and deliberately nothing that writes or reads a position.
 *
 * It is kept apart from the writers because the preference stores load with the application
 * itself, on every page including sign-in and public shares, while the writers of the national
 * grids and of MGRS bring a projection library and a grid library with them. Importing a writer
 * from here, or from a store, would move both libraries into the bundle every visitor downloads.
 *
 * The order is the order formats are offered and listed in.
 */

export type DdSettings = { decimals: number; style: 'letters' | 'signed' };
export type DdmSettings = { minutesDecimals: number };
export type DmsSettings = { secondsDecimals: number };

export type UtmSettings = {
  /** `'auto'`: each position in its own zone; a number: that zone wherever it still applies. */
  zone: 'auto' | number;
  /** The square a reference names: 1 m, 10 m or 100 m. */
  precisionMetres: number;
};

export type MgrsSettings = {
  /** The square a reference names, 1 m to 10 km: five to one digits per axis. */
  precisionMetres: number;
  /** `35T LM 78752 43143` or `35TLM7875243143`. */
  spacing: 'spaced' | 'compact';
};

export type Stereo70Settings = {
  /** The square a reference names: 1 m, 10 m or 100 m (the transformation is good to a few metres). */
  precisionMetres: number;
  /** `E 512345 N 465123`, or the Romanian surveyor's `X 465123 Y 512345`. */
  axes: 'en' | 'xy';
};

export type SwissGridSettings = {
  precisionMetres: number;
};

export type PlusCodeSettings = { length: number };

/** One format's settings: what each may be, and what it is until the person says otherwise. */
export interface SettingsSchema<S extends FormatSettingsShape> {
  choices: SettingChoices<S>;
  defaults: S;
}

function schema<S extends FormatSettingsShape>(definition: SettingsSchema<S>): SettingsSchema<S> {
  return definition;
}

const ZONES = Array.from({ length: 60 }, (_, index) => index + 1);
const GRID_PRECISIONS = [1, 10, 100];

export const FORMAT_SETTINGS = {
  dd: schema<DdSettings>({
    choices: { decimals: [0, 1, 2, 3, 4, 5, 6, 7, 8], style: ['letters', 'signed'] },
    defaults: { decimals: 5, style: 'letters' },
  }),
  ddm: schema<DdmSettings>({
    choices: { minutesDecimals: [0, 1, 2, 3, 4, 5] },
    defaults: { minutesDecimals: 3 },
  }),
  dms: schema<DmsSettings>({
    choices: { secondsDecimals: [0, 1, 2, 3] },
    defaults: { secondsDecimals: 1 },
  }),
  utm: schema<UtmSettings>({
    choices: { zone: ['auto', ...ZONES], precisionMetres: [1, 10, 100] },
    defaults: { zone: 'auto', precisionMetres: 1 },
  }),
  mgrs: schema<MgrsSettings>({
    choices: { precisionMetres: [1, 10, 100, 1000, 10000], spacing: ['spaced', 'compact'] },
    defaults: { precisionMetres: 1, spacing: 'spaced' },
  }),
  stereo70: schema<Stereo70Settings>({
    choices: { precisionMetres: GRID_PRECISIONS, axes: ['en', 'xy'] },
    defaults: { precisionMetres: 1, axes: 'en' },
  }),
  lv95: schema<SwissGridSettings>({
    choices: { precisionMetres: GRID_PRECISIONS },
    defaults: { precisionMetres: 1 },
  }),
  lv03: schema<SwissGridSettings>({
    choices: { precisionMetres: GRID_PRECISIONS },
    defaults: { precisionMetres: 1 },
  }),
  pluscode: schema<PlusCodeSettings>({
    choices: { length: [8, 10, 11, 12, 13] },
    defaults: { length: 10 },
  }),
};

export type FormatId = keyof typeof FORMAT_SETTINGS;

export const FORMAT_IDS = Object.keys(FORMAT_SETTINGS) as FormatId[];

type SettingsOf<D> = D extends SettingsSchema<infer S> ? S : never;

/** Each format's settings, keyed by format id. */
export type FormatSettings = { [K in FormatId]: SettingsOf<(typeof FORMAT_SETTINGS)[K]> };

export function isFormatId(value: unknown): value is FormatId {
  return typeof value === 'string' && Object.hasOwn(FORMAT_SETTINGS, value);
}

export function defaultFormatSettings(): FormatSettings {
  const out = {} as Record<FormatId, FormatSettingsShape>;
  for (const id of FORMAT_IDS) {
    out[id] = { ...FORMAT_SETTINGS[id].defaults };
  }
  return out as FormatSettings;
}

/**
 * Stored settings made safe: every format present, every member one of its choices, anything
 * unknown or malformed back at its default — so a value written by a newer or older client never
 * breaks a display, and never discards the members that are still good.
 */
export function sanitizeFormatSettings(raw: unknown): FormatSettings {
  const record = raw !== null && typeof raw === 'object' ? (raw as Record<string, unknown>) : {};
  const out = {} as Record<FormatId, FormatSettingsShape>;
  for (const id of FORMAT_IDS) {
    out[id] = sanitizeSettings(FORMAT_SETTINGS[id] as SettingsSchema<FormatSettingsShape>, record[id]);
  }
  return out as FormatSettings;
}

/** The format a person reads positions in. */
export type CoordinateFormat = FormatId;

export const DEFAULT_COORDINATE_FORMAT: CoordinateFormat = 'dd';

/** Whether a stored value is a format this client knows, so an unknown one falls back. */
export function isCoordinateFormat(value: unknown): value is CoordinateFormat {
  return isFormatId(value);
}

/**
 * The reading rules a person may choose, kept with their account. The defaults are the readings
 * this application has always made — latitude first, and the northern hemisphere — because every
 * cave it was written for is north of the equator, while a GIS export that writes longitude first
 * is common enough for a member who pastes from one to want it the other way round.
 */
export const READING_RULE_CHOICES: SettingChoices<ReadingRules> = {
  pairOrder: ['latLon', 'lonLat'],
  bareUtmHemisphere: ['north', 'south'],
};

export const DEFAULT_READING_RULES: ReadingRules = { pairOrder: 'latLon', bareUtmHemisphere: 'north' };

/** Stored reading rules made safe member by member, like a format's settings. */
export function sanitizeReadingRules(raw: unknown): ReadingRules {
  return sanitizeSettings({ choices: READING_RULE_CHOICES, defaults: DEFAULT_READING_RULES }, raw);
}
