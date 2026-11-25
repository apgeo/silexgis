// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { GEOLOGICAL_UNIT_TYPE_CODE } from '../../map/lithologyAppearance.ts';
import {
  MEASUREMENT_TYPE_KEY,
  STRUCTURAL_MEASUREMENT_TYPE_CODE,
  measurementTypeLabel,
  structuralFieldLabel,
} from './structural.ts';

export { GEOLOGICAL_UNIT_TYPE_CODE };

/** The property a mapped geological unit's rock class is stored under. */
export const LITHOLOGY_PROPERTY_KEY = 'lithology';

/**
 * The shipped lithology codes, in the order the server offers them. The server's vocabulary is
 * the authority on which are karstifiable and sends that with every figure; this list exists only
 * so a shipped code is shown in the reader's language and any other stored code is shown raw.
 */
export const LITHOLOGY_CODES = [
  'limestone',
  'dolomite',
  'marble',
  'gypsum_anhydrite',
  'salt',
  'conglomerate_calcareous',
  'sandstone',
  'shale_marl',
  'granite',
  'crystalline_schist',
  'volcanic',
  'unconsolidated',
  'other',
] as const;

export type LithologyCode = (typeof LITHOLOGY_CODES)[number];

export function isLithologyCode(code: string | null | undefined): code is LithologyCode {
  return code !== null && code !== undefined && (LITHOLOGY_CODES as readonly string[]).includes(code);
}

/** A stored lithology code in the reader's language; an unshipped code as stored; none as such. */
export function lithologyLabel(t: TFunction, code: string | null | undefined): string {
  if (code === null || code === undefined || code === '') return t('geology.unclassified');
  return isLithologyCode(code) ? t(`geology.lithology.${code}`) : code;
}

/**
 * The words for one value of a typed enum property. Only the geological unit's lithology and a
 * structural reading's plane kind have shipped translations; every other enum value is shown as
 * stored, as before.
 */
export function typedEnumLabel(
  t: TFunction,
  featureTypeCode: string | null | undefined,
  fieldKey: string,
  value: string,
): string {
  if (featureTypeCode === GEOLOGICAL_UNIT_TYPE_CODE && fieldKey === LITHOLOGY_PROPERTY_KEY) {
    return lithologyLabel(t, value);
  }
  if (featureTypeCode === STRUCTURAL_MEASUREMENT_TYPE_CODE && fieldKey === MEASUREMENT_TYPE_KEY) {
    return measurementTypeLabel(t, value);
  }
  return value;
}

/**
 * The label of a typed field: a structural reading's fields in the reader's language, every other
 * field by its schema title, as before.
 */
export function typedFieldLabel(
  t: TFunction,
  featureTypeCode: string | null | undefined,
  fieldKey: string,
  schemaTitle: string,
): string {
  return featureTypeCode === STRUCTURAL_MEASUREMENT_TYPE_CODE
    ? structuralFieldLabel(t, fieldKey, schemaTitle)
    : schemaTitle;
}

/** A stored typed value as a reader sees it: translated where words are shipped, else as stored. */
export function typedValueText(
  t: TFunction,
  featureTypeCode: string | null | undefined,
  fieldKey: string,
  value: unknown,
): string {
  if (typeof value === 'boolean') return value ? '✓' : '✗';
  return typeof value === 'string' ? typedEnumLabel(t, featureTypeCode, fieldKey, value) : String(value);
}
