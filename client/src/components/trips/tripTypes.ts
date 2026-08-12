// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { TripType } from '../../api/hooks.ts';

/**
 * The trip purposes this client ships wording for. An installation may add its own rows, which
 * are shown exactly as whoever added them wrote them — one list, two sources of wording.
 *
 * The shipped codes are immutable on the server, so a code here always names the row it names.
 */
export const SEEDED_TRIP_TYPE_CODES = [
  'exploration',
  'survey',
  'maintenance',
  'training',
  'tourism',
  'rescue',
  'science',
  'other',
] as const;

export type SeededTripTypeCode = (typeof SEEDED_TRIP_TYPE_CODES)[number];

export function isSeededTripTypeCode(code: string): code is SeededTripTypeCode {
  return (SEEDED_TRIP_TYPE_CODES as readonly string[]).includes(code);
}

/** A shipped purpose reads in the caller's language; a club's own reads as it was written. */
export function tripTypeLabel(type: TripType, t: TFunction): string {
  return isSeededTripTypeCode(type.code) ? t(`trips.typeValues.${type.code}`) : type.name;
}

/**
 * The label for the purpose a trip names, or null when it names none — or names one this caller's
 * copy of the vocabulary does not have yet, which is a list still loading rather than a purpose
 * worth inventing wording for.
 */
export function tripTypeLabelOf(
  tripTypeId: number | null | undefined,
  types: TripType[] | undefined,
  t: TFunction,
): string | null {
  if (tripTypeId == null) {
    return null;
  }

  const type = types?.find((row) => row.id === tripTypeId);
  return type ? tripTypeLabel(type, t) : null;
}
