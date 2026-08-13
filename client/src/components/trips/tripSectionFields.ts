// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { SchemaField } from '../typedProperties/propertiesSchema.ts';

/**
 * The section field codes this client ships wording for.
 *
 * A trip purpose carries three JSON schemas, and a schema's field titles are one string each,
 * written in whatever language whoever wrote the schema was working in. The shipped schemas are
 * written in English, so rendering their titles raw would put English labels on a Romanian
 * screen. These codes are therefore translated the way every other shipped vocabulary is —
 * by code — and anything else falls back to the title the schema itself carries, which is the
 * only wording an installation's own field has.
 *
 * A club that edits a shipped schema's title for one of these codes will still see the
 * translated wording: the code is what identifies the field, and a client that preferred the
 * stored title would show English again for every installation that never touched it. Renaming
 * a shipped field is done by giving it a new code.
 */
export const SEEDED_TRIP_SECTION_FIELD_CODES = [
  // Field data — what the trip found underground.
  'conditions',
  'water_level',
  'objective_reached',
  'leads_found',
  'leads_remaining',
  'survey_grade',
  'instrument',
  'loops_closed',
  'sketch_completed',
  'new_passage_note',
  // Logistics — what it took to get in.
  'permit_reference',
  'permit_holder_caver_id',
  'permit_holder_note',
  'key_holder_caver_id',
  'key_holder_note',
  'landowner_caver_id',
  'landowner_note',
  'access_notes',
  'cost_amount',
  'cost_currency',
  'cost_note',
  // Safety — what went wrong, and what was learned.
  'incident_summary',
  'incident_severity',
  'equipment_failure',
  'lessons_learned',
  'reported_to',
] as const;

export type SeededTripSectionFieldCode = (typeof SEEDED_TRIP_SECTION_FIELD_CODES)[number];

export function isSeededTripSectionFieldCode(code: string): code is SeededTripSectionFieldCode {
  return (SEEDED_TRIP_SECTION_FIELD_CODES as readonly string[]).includes(code);
}

/** A shipped field reads in the caller's language; a club's own reads as its schema wrote it. */
export function tripSectionFieldLabel(field: SchemaField, t: TFunction): string {
  return isSeededTripSectionFieldCode(field.key)
    ? t(`trips.sectionFields.${field.key}`)
    : field.label;
}

/**
 * The values a shipped field's choice offers, by field code.
 *
 * A translated label above a list of English tokens is only half a translation: the reader is
 * still being asked to pick "near_miss". The values the product ships are therefore worded the
 * same way their field codes are, and an installation's own choice — a value this does not list —
 * shows exactly as its schema wrote it, which is the only wording it has.
 */
export const SEEDED_TRIP_SECTION_ENUM_VALUES: Record<string, readonly string[]> = {
  water_level: ['low', 'normal', 'high', 'flood'],
  survey_grade: ['1', '2', '3', '4', '5', '6', 'X'],
  incident_severity: ['near_miss', 'minor', 'serious', 'rescue'],
};

/** A shipped choice reads in the caller's language; anything else reads as it is stored. */
export function tripSectionEnumLabel(fieldKey: string, value: string, t: TFunction): string {
  return SEEDED_TRIP_SECTION_ENUM_VALUES[fieldKey]?.includes(value) === true
    ? t(`trips.sectionValues.${fieldKey}.${value}`)
    : value;
}

/**
 * How a stored section value reads to somebody who is not editing it.
 *
 * One home, because a section is shown read-only in two places — the trip's own page and the
 * report made from it — and a value written one way there and another way here would have two
 * surfaces disagreeing about what the same record says. An unset field reads as a dash, which
 * the report takes as its cue to leave the row out entirely.
 */
export function tripSectionValueText(field: SchemaField, value: unknown, t: TFunction): string {
  if (value === undefined || value === null || value === '') {
    return '—';
  }
  if (typeof value === 'boolean') {
    return t(value ? 'trips.sections.yes' : 'trips.sections.no');
  }
  if (field.kind === 'enum' && typeof value === 'string') {
    return tripSectionEnumLabel(field.key, value, t);
  }
  return String(value);
}

/**
 * Whether a field holds a person from the roster rather than a line of text.
 *
 * A permit holder, a key holder or a landowner contact is a roster identity, with a free-text
 * field beside it for somebody the roster does not know. The identity fields are named
 * `*_caver_id` and carry the identifier's own shape in their schema, which is what makes the
 * difference real; this is the reading of that convention the screen needs, so the field is
 * offered as a person to pick and shown as that person's name — not as an identifier nobody can
 * type and nobody can read.
 */
export function isCaverReferenceField(field: SchemaField): boolean {
  return field.kind === 'string' && field.key.endsWith('_caver_id');
}
