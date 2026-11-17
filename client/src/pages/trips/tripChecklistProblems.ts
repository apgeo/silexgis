// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';
import { TRACKING_PROBLEM_MESSAGE_KEYS } from '../../components/trips/trackingProblems.ts';

/**
 * The refusals confirming or clearing a line of a trip's checklist can meet, in the words of the
 * person who has to act on them. Exported so the wording can be checked against both locales: the
 * keys are looked up through a variable, so the scan that catches an untranslated key at a
 * `t('…')` call cannot see them.
 */
export const TRIP_CHECKLIST_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'trip_checklist.tick_forbidden': 'trips.checklistProblems.tickForbidden',
  // The list was changed since this tab drew it; the line is gone, not refused.
  'trip_checklist.item_not_found': 'trips.checklistProblems.itemGone',
  // The trip itself went, or stopped being readable, while the tab was open. Worded by the trip
  // area's own sentence for that code, so the checklist and the tracking tab say it the same way.
  'trip_log.not_found': TRACKING_PROBLEM_MESSAGE_KEYS['trip_log.not_found']!,
};

/**
 * Why a line could not be confirmed or cleared — or nothing, for a stale version, which the
 * application's own conflict warning already words for every write.
 *
 * The server's sentence is never shown in place of these: it is not translated, and a failure that
 * carries no sentence at all would otherwise show an empty message.
 */
export function tripChecklistTickProblem(error: unknown, t: TFunction): string | null {
  if (isConcurrencyConflict(error)) {
    return null;
  }

  const key =
    error instanceof ApiError && error.code
      ? TRIP_CHECKLIST_PROBLEM_MESSAGE_KEYS[error.code]
      : undefined;
  return t(key ?? 'trips.checklistTickFailed');
}
