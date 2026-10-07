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
  'tracking.not_writable': 'trips.tracking.problems.notWritable',
  // A sheet of reports offered to a trip that has no watch at all. Distinct from the line above:
  // that one is a log that exists and may not be written to, this is a log with nowhere to put a
  // report, and the way out is setting the watch up rather than starting or reopening it.
  'tracking.not_configured': 'trips.tracking.problems.notConfigured',
  // A sheet read in a zone this server does not carry. Reached by a browser whose zone list is
  // newer or older than the server's; the way out is another name for the same clock.
  'tracking_csv.zone_unknown': 'trips.tracking.problems.csvZoneUnknown',
  // A sheet committed after the trip changed under its preview: the server read it again for the
  // write and would no longer have written what the reviewer was shown, so it wrote nothing.
  'tracking_csv.plan_changed': 'trips.tracking.problems.csvPlanChanged',
  // A sheet with no readable row at all for a reason about the file — times of day and no day to
  // put them on, most often. The preview shows the same thing as a finding above the table.
  'tracking_csv.sheet_unreadable': 'trips.tracking.problems.csvSheetUnreadable',
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
  // A third, and worded apart from the general `tracking.not_writable` above on purpose: that one is
  // refused to somebody trying to record where a party is, and this one to somebody trying to
  // publish. The act to take is the same and the sentence is not — one says a report cannot land,
  // the other says an address would be handed out that opens nothing.
  'tracking.publication_refused_not_armed': 'trips.tracking.problems.publicationNotArmed',
  // Replacing a link somebody has already taken back. Its own sentence rather than the one for a
  // link that is not there: this link is there, and what the reader has to do next is different —
  // there is nothing left to exchange, so the trip is published afresh.
  'tracking.share_revoked': 'trips.tracking.problems.shareRevoked',
  // Photographs hung on a trip's moments. The first and the last two refuse a whole request; the
  // three between them arrive per photograph, inside an answer that attached the others, and the
  // roster code above is answered there as well. Each has its own sentence because each has its own
  // remedy — start the watch, correct the camera's clock, choose a different file, or nothing at
  // all because the photograph is already where it was being put.
  'tracking.not_tracked': 'trips.tracking.problems.notTracked',
  'tracking.picture_in_future': 'trips.tracking.problems.pictureInFuture',
  'tracking.picture_not_image': 'trips.tracking.problems.pictureNotImage',
  'tracking.picture_already_attached': 'trips.tracking.problems.pictureAlreadyAttached',
  'tracking.picture_relation_missing': 'trips.tracking.problems.pictureRelationMissing',
  'tracking.picture_not_found': 'trips.tracking.problems.pictureNotFound',
  'trip_log.not_found': 'trips.tracking.problems.tripNotFound',
  'concurrency.if_match_required': 'trips.tracking.problems.ifMatchRequired',
  'concurrency.version_mismatch': 'trips.tracking.problems.versionMismatch',
};

/**
 * The sentence for a refusal, or a general one when the server named nothing this file knows.
 *
 * The general one is the caller's to choose, already worded: a surface that is not saving anything
 * — hanging photographs on a moment, taking one off — has a truer thing to say than "that could not
 * be saved", and passing the sentence rather than its key keeps the key written out at the call,
 * where the scan for untranslated keys can see it.
 */
export function trackingProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
  fallback: string = t('common.saveFailed'),
): string {
  return trackingProblemCodeMessage(error instanceof ApiError ? error.code : undefined, t) ?? fallback;
}

/**
 * The sentence for a bare code, or nothing when this file has no words for it.
 *
 * For the refusals that do not arrive as a failed request: a write that takes several things at
 * once answers successfully and names, per thing, the code it was left out for. Nothing rather than
 * a general sentence, because the caller there has already said how many were left out, and a
 * second line that explains none of them would only look like an explanation.
 */
export function trackingProblemCodeMessage(
  code: string | null | undefined,
  t: ReturnType<typeof useTranslation>['t'],
): string | undefined {
  // `Object.hasOwn`, because the code is the server's text and the table is a plain object: a code
  // that happened to be called `constructor` would otherwise find a function and be handed to `t`.
  if (!code || !Object.hasOwn(TRACKING_PROBLEM_MESSAGE_KEYS, code)) {
    return undefined;
  }
  return t(TRACKING_PROBLEM_MESSAGE_KEYS[code]);
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
 * holds one sentence per code the server answers with, and two checks keep it honest: the script
 * test `scripts/tracking-problem-codes.test.mjs` reads the server's one list of tracking codes and
 * fails unless the tracking codes here are exactly that list, and the locale test holds every
 * sentence here to both languages with none left over. Wording kept there for a code nothing
 * answers with would fail the first. Neither of these is a code: both are read off a status, for
 * refusals that arrive carrying no code at all.
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
