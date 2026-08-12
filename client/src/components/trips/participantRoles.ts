// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';

/**
 * The jobs on a trip this client ships wording for. An installation may add its own rows, which
 * are shown exactly as whoever added them wrote them — one list, two sources of wording.
 *
 * The shipped codes are immutable on the server, so a code here always names the row it names.
 */
export const SEEDED_PARTICIPANT_ROLE_CODES = [
  'participant',
  'proposer',
  'leader',
  'driver',
  'surveyor',
  'photographer',
  'trainee',
  'instructor',
  'callout_contact',
] as const;

export type SeededParticipantRoleCode = (typeof SEEDED_PARTICIPANT_ROLE_CODES)[number];

export function isSeededParticipantRoleCode(code: string): code is SeededParticipantRoleCode {
  return (SEEDED_PARTICIPANT_ROLE_CODES as readonly string[]).includes(code);
}

/** A shipped role reads in the caller's language; a club's own reads as it was written. */
export function participantRoleLabel(role: { code: string; name: string }, t: TFunction): string {
  return isSeededParticipantRoleCode(role.code) ? t(`trips.participantRoleValues.${role.code}`) : role.name;
}
