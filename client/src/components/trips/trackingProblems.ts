// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';

/**
 * Every refusal the tracking routes answer with, in the words of somebody who has to act on it
 * while a party is underground.
 *
 * The code is what the server keeps stable; the sentence is this application's. Anything not
 * listed falls back to the general failure wording, because a paraphrase of a code nobody has
 * written words for would be a guess presented as an explanation — and on this surface a guess is
 * read as news about where people are.
 *
 * Exported so the wording can be checked against both locales. Every key here is looked up through
 * a variable rather than written out at the call, so the scan that catches an untranslated key
 * cannot see a single one of them — and a refusal rendered as its own key would land at the exact
 * moment somebody is trying to record a position.
 */
export const TRACKING_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'tracking.not_armed': 'trips.tracking.problems.notArmed',
  'tracking.state_invalid': 'trips.tracking.problems.stateInvalid',
  'tracking.model_missing': 'trips.tracking.problems.modelMissing',
  'tracking.model_unavailable': 'trips.tracking.problems.modelUnavailable',
  'tracking.reference_unknown': 'trips.tracking.problems.referenceUnknown',
  'tracking.station_unknown': 'trips.tracking.problems.stationUnknown',
  'tracking.no_station_at_depth': 'trips.tracking.problems.noStationAtDepth',
  'tracking.caver_not_participant': 'trips.tracking.problems.caverNotParticipant',
  'tracking.recorded_in_future': 'trips.tracking.problems.recordedInFuture',
  'tracking.team_not_found': 'trips.tracking.problems.teamNotFound',
  'tracking.event_not_found': 'trips.tracking.problems.eventNotFound',
  'tracking.concurrent_write': 'trips.tracking.problems.concurrentWrite',
  'trip_log.not_found': 'trips.tracking.problems.tripNotFound',
  'concurrency.if_match_required': 'trips.tracking.problems.ifMatchRequired',
  'concurrency.version_mismatch': 'trips.tracking.problems.versionMismatch',
};

/** The sentence for a refusal, or the general one when the server named nothing this file knows. */
export function trackingProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  if (!(error instanceof ApiError) || !error.code) {
    return t('common.saveFailed');
  }
  const key = TRACKING_PROBLEM_MESSAGE_KEYS[error.code];
  return key ? t(key) : t('common.saveFailed');
}
