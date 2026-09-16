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
  // Moving an *armed* watch to a survey of another cave, which is the one model change the server
  // refuses. Worded as its own refusal rather than folded into the one above: a survey that cannot
  // be used and a survey that belongs to a different cave lead whoever is reading to two different
  // acts, and this one has to say which cave the watch is anchored to the party being in.
  'tracking.model_other_cave': 'trips.tracking.problems.modelOtherCave',
  'tracking.reference_unknown': 'trips.tracking.problems.referenceUnknown',
  'tracking.station_unknown': 'trips.tracking.problems.stationUnknown',
  'tracking.no_station_at_depth': 'trips.tracking.problems.noStationAtDepth',
  'tracking.caver_not_participant': 'trips.tracking.problems.caverNotParticipant',
  'tracking.recorded_in_future': 'trips.tracking.problems.recordedInFuture',
  'tracking.team_not_found': 'trips.tracking.problems.teamNotFound',
  'tracking.event_not_found': 'trips.tracking.problems.eventNotFound',
  'tracking.concurrent_write': 'trips.tracking.problems.concurrentWrite',
  // Publishing. The two refusals are deliberately different things — one is about this caller's
  // rights over the cave, the other about the cave itself — and are worded as two, because the
  // first is fixed by asking somebody and the second by nobody at all.
  'tracking.share_not_found': 'trips.tracking.problems.shareNotFound',
  'tracking.publication_refused_cave': 'trips.tracking.problems.publicationRefusedCave',
  'tracking.publication_refused_protected': 'trips.tracking.problems.publicationRefusedProtected',
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

/**
 * Why a watch will not be read again, for a refusal the server has settled.
 *
 * <b>Separate from the sentence above because a read is not a write, and the fallback is the whole
 * difference.</b> When a write fails and nothing here has words for the code, "that could not be
 * saved" is true and useful. When a *read* fails it is neither: nobody was saving anything, and a
 * coordinator watching a party underground would be told the wrong thing about the wrong act at
 * the moment they most need the right one. So the fallbacks here are read-shaped, and the one
 * refusal a signed-in surface hits without any code at all — a session that lapsed behind the
 * reader while a poll was in flight — is named rather than lumped in, because it is the only one
 * of them the reader can do something about immediately.
 *
 * <b>The two fallbacks are worded outside the table above, and that is not tidying.</b> That table
 * holds one sentence per code the server answers with, and the check that keeps it honest asserts
 * the two sets are equal — wording kept there for a code nothing answers with would sit unread with
 * nothing ever saying so. Neither of these is a code: both are read off a status, for refusals that
 * arrive carrying no code at all.
 */
export function trackingReadRefusalMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  if (error instanceof ApiError) {
    const key = error.code ? TRACKING_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
    if (key) {
      return t(key);
    }
    if (error.status === 401) {
      return t('trips.tracking.refusedSignedOut');
    }
  }
  return t('trips.tracking.refusedUnknown');
}
