// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals a write to the signed-in account's own profile, address and preferences can meet, in the words
 * of the person who has to act on them.
 *
 * Anything not listed falls back to the sentence the caller passes — the general failure, or the
 * form's own — because a paraphrase of a code nobody has written wording for would be a guess
 * presented as an explanation.
 *
 * Exported so the wording can be checked against both locales: the keys are looked up through a
 * variable, so the scan that catches an untranslated key at a `t('…')` call cannot see them.
 */
export const ME_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  // The club list was read when the form opened; the club chosen from it may have gone since.
  'me.caving_club_unknown': 'settings.problems.cavingClubUnknown',
  // Asking for a link (a change, a resend or a confirmation of the current address) is spaced out.
  'me.email_resend_too_soon': 'settings.emails.tooSoon',
  // An account whose user name is its address cannot take an address the sign-in name rules refuse.
  'me.email_not_usable': 'settings.problems.emailNotUsable',
  // Two saves of the account's preferences met at the same moment (another window or device); the
  // change is still held here and goes with the next attempt.
  'preferences.busy': 'settings.problems.preferencesBusy',
};

/** The sentence for a failed account write: the refusal's own words, or `fallback`. */
export function meProblemMessage(error: unknown, t: TFunction, fallback = 'common.saveFailed'): string {
  const key = error instanceof ApiError && error.code ? ME_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
  return t(key ?? fallback);
}
