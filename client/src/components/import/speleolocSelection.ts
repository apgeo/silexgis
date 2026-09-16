// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SpeleolocPoint, SpeleolocPointDecision } from '../../api/hooks.ts';

/**
 * What the confirmation asks of one scan, in one place.
 *
 * The server asks these same questions twice — once in the dry run, to say which scans "take all"
 * may offer, and again at the confirmation, to decide what is written — and it keeps them in one
 * place for the reason this file exists: two copies of the rule drift in one direction only, and
 * the direction is a dry run offering scans the confirmation then refuses, which is the one thing
 * a dry run may never do. The browser needs its own copy because a decision made half a second ago
 * has not been saved yet and the server's answer is worked out from what it has stored; so it is
 * the same questions in the same order, and it lives here rather than in a screen.
 */

/**
 * Whether a scan has a station at all — one the reviewer named, or one its marker resolved to.
 * Every state but `proposed` is a way in which nothing could be proposed, so only a name given by
 * hand rescues those.
 *
 * That last clause is the whole of it and it binds the table as much as this file: a scan whose
 * marker resolved to nothing but which carries a name somebody typed *will* be recorded, at that
 * name, and the confirmation asks exactly this question. So the row may not be drawn as one that
 * was left out. The reason a proposal could not be made is still said — it is the truth about the
 * marker — but the name that will be written is said beside it, and there is a way to take it back.
 */
export function hasStation(point: SpeleolocPoint, decision: SpeleolocPointDecision | undefined) {
  return Boolean(decision?.stationName) || point.state === 'proposed';
}

/**
 * The fewest stations one scan may be offered as.
 *
 * Not a taste in defaults. The offers are cut to this number by the server, and a scan cut to one
 * offer is a scan whose tie has been hidden: a depth that reaches two branches of the survey
 * equally well comes back carrying a single station, the row reads as an ordinary proposal, and the
 * arbitrary branch is what gets recorded. Two is the smallest number that cannot hide one, because
 * a tie is always between the nearest two.
 */
export const MIN_STATION_CANDIDATES = 2;

/** The most, which is what the server accepts. */
export const MAX_STATION_CANDIDATES = 25;

/** A stored or typed number of offers, brought inside what may be shown honestly. */
export function clampCandidateCount(value: number | null | undefined) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return 5;
  }
  return Math.min(MAX_STATION_CANDIDATES, Math.max(MIN_STATION_CANDIDATES, Math.round(value)));
}

/**
 * Whether the marker's depth reaches more than one station equally well.
 *
 * Two branches of a survey regularly share a horizon, and where they do, "the nearest station" is
 * a coin toss rather than an answer. The proposal still stands — hiding it would leave the reviewer
 * nothing to judge — but a row where this is true is marked as the unsettled thing it is instead of
 * sitting in the same colour as one whose depth reached exactly one place.
 */
export function proposalIsTied(point: SpeleolocPoint) {
  const [nearest, next] = point.candidates;
  return Boolean(nearest && next && Math.abs(nearest.deltaM) === Math.abs(next.deltaM));
}

/**
 * Whether the reviewer's own decision is the only thing between this scan and being recorded: a
 * station, somebody the scan is about, and that somebody on the roster of the trip it goes onto.
 *
 * `caversNotOnRoster` is the server's own list and is empty when the confirmation is to create the
 * trip — a created trip is created with exactly the people these scans name, so there is no roster
 * for anybody to be missing from.
 */
export function couldBeRecorded(
  point: SpeleolocPoint,
  decision: SpeleolocPointDecision | undefined,
  caversNotOnRoster: ReadonlySet<string>,
) {
  if (!hasStation(point, decision)) {
    return false;
  }
  const caverId = decision?.caverId ?? point.caverId;
  return caverId !== null && caverId !== undefined && !caversNotOnRoster.has(caverId);
}
