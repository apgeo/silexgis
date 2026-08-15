// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';

/**
 * What somebody was at a camp as, for the codes this client ships wording for. An installation
 * may add its own rows, which are shown exactly as whoever added them wrote them — one list, two
 * sources of wording.
 *
 * The shipped codes are immutable on the server, so a code here always names the row it names.
 *
 * Its own list rather than the jobs people are recorded under on a trip: cooking and keeping the
 * base camp are what a camp's presence record is for, and neither is a job underground.
 */
export const SEEDED_EXPEDITION_ROSTER_ROLE_CODES = [
  'member',
  'organiser',
  'cook',
  'base_camp',
  'driver',
  'medic',
  'equipment',
  'guest',
] as const;

export type SeededExpeditionRosterRoleCode = (typeof SEEDED_EXPEDITION_ROSTER_ROLE_CODES)[number];

export function isSeededExpeditionRosterRoleCode(
  code: string,
): code is SeededExpeditionRosterRoleCode {
  return (SEEDED_EXPEDITION_ROSTER_ROLE_CODES as readonly string[]).includes(code);
}

/** A shipped role reads in the caller's language; a club's own reads as it was written. */
export function expeditionRosterRoleLabel(
  role: { code: string; name: string },
  t: TFunction,
): string {
  return isSeededExpeditionRosterRoleCode(role.code)
    ? t(`expeditions.rosterRoleValues.${role.code}`)
    : role.name;
}
