// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';

/** The kind a structural reading carries, and the property keys its typed data is stored under. */
export const STRUCTURAL_MEASUREMENT_TYPE_CODE = 'structural_measurement';
export const MEASUREMENT_TYPE_KEY = 'measurement_type';
export const DIP_DIRECTION_KEY = 'dip_direction';
export const DIP_KEY = 'dip';
export const NOTE_KEY = 'note';
export const MEASURED_ON_KEY = 'measured_on';

/**
 * Accepted by the server on entry only: a strike by the right-hand rule. The server converts it to
 * the dip direction it stores, so the form sends what was typed under this key and never does the
 * arithmetic itself — one home for the convention.
 */
export const STRIKE_KEY = 'strike';

/** The shipped plane kinds, in the order the server offers them. */
export const MEASUREMENT_TYPES = ['bedding', 'joint', 'fault', 'cleavage', 'fracture', 'other'] as const;

export type MeasurementType = (typeof MEASUREMENT_TYPES)[number];

/** How a reading's orientation is typed into the form. */
export type OrientationEntry = 'dipDirection' | 'strike';

const FIELD_KEYS = [MEASUREMENT_TYPE_KEY, DIP_DIRECTION_KEY, DIP_KEY, NOTE_KEY, MEASURED_ON_KEY] as const;

export function isMeasurementType(value: string | null | undefined): value is MeasurementType {
  return value !== null && value !== undefined && (MEASUREMENT_TYPES as readonly string[]).includes(value);
}

/** A stored plane kind in the reader's language; an unshipped code as stored. */
export function measurementTypeLabel(t: TFunction, value: string): string {
  return isMeasurementType(value) ? t(`structural.types.${value}`) : value;
}

/**
 * The label of one of a reading's typed fields in the reader's language, or the schema's own
 * (English) title for a field this build does not ship words for — an administrator may add one.
 */
export function structuralFieldLabel(t: TFunction, key: string, schemaTitle: string): string {
  return (FIELD_KEYS as readonly string[]).includes(key) ? t(`structural.fields.${key}`) : schemaTitle;
}

/**
 * The property document a form sends. Entered by strike, the typed value goes under the strike key
 * and the dip direction is dropped, so the one the reading had before cannot contradict the new
 * strike; the server turns the strike into the dip direction it stores.
 */
export function withOrientationEntry(
  properties: Record<string, unknown>,
  entry: OrientationEntry,
): Record<string, unknown> {
  if (entry !== 'strike') return properties;
  const next = { ...properties };
  const typed = next[DIP_DIRECTION_KEY];
  delete next[DIP_DIRECTION_KEY];
  if (typed !== undefined) next[STRIKE_KEY] = typed;
  return next;
}
