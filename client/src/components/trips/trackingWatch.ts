// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingParticipant, TripTrackingState } from '../../api/hooks.ts';
import {
  partyStandings,
  positionAgeInWords,
  sinceInWords,
  standingOf,
  type PublicTripStanding,
} from '../../pages/public/publicTripParty.ts';

/**
 * Whether a watch in this state may have its log written — a report recorded, corrected or removed.
 *
 * <b>The server's rule, said once here so every surface that gates a write reads the same
 * answer.</b> An armed watch's log is written as the trip runs, and a closed one's stays writable,
 * because a finished trip is written up afterwards, from notes, days later — and the server accepts
 * that for recording, correcting and removing alike. Only a watch that was never started refuses,
 * for a reason of its own: it names no survey, so a claimed place has nothing to resolve against.
 * The card under the watch and the offer a pressed station makes used to gate on "armed", which
 * was the client saying no where the server says yes, on exactly the trips most in need of writing
 * up: a coordinator could correct and remove a closed log's rows and not add one.
 */
export function trackingLogWritable(state: TripTrackingState): boolean {
  return state !== 'off';
}

/**
 * Where one member of the party stands, in the three states a watch actually distinguishes.
 *
 * Deliberately the followed page's type and the followed page's rule rather than a second set of
 * words for the same three things: a coordinator and a family are reading one party, and a surface
 * that folded silence into "out" on one of those pages and not the other would be two applications
 * disagreeing about whether anybody is still underground.
 */
export type TrackingStanding = PublicTripStanding;

/**
 * Where one person on the watch stands — asked of the read, and no longer worked out here.
 *
 * <b>The client used to fold this and does not any more, which is the whole of what changed.</b>
 * The read now carries the server's own `in`, folded in Domain over *every* report ever made about
 * somebody, beside the flag the published page has always answered from. What stood here was a
 * fold over a single step — the kind of the latest report — and it was wrong in two ways that a
 * second copy of the rule is always wrong in. It could not tell "a note, after an entry" from "a
 * note, and nothing before it", so it read both as underground and drew somebody nobody had ever
 * placed as a caver in a cave. And it read a station report as an entry, which the domain rule no
 * longer does: entering and exiting *state* a standing, while a station or a depth only raises
 * somebody from unheard to underground and never overturns one that was stated.
 *
 * So this derives nothing. It reads two flags and names the pair, through the followed page's own
 * naming, so that a coordinator and a family cannot be shown different answers about whether
 * anybody is still underground.
 */
export function trackingStandingOf(
  participant: Pick<TrackingParticipant, 'in' | 'out'>,
): TrackingStanding {
  return standingOf(participant);
}

/**
 * How many of the party stand in each state — the line the coordinator's screen was missing.
 *
 * Counted through the followed page's own counter so the three buckets cannot drift apart, and it
 * counts all three: a watch that said "4 underground, 1 out" over a party of six would be silently
 * leaving out the one person the coordinator most needs to think about.
 */
export function trackingStandings(
  participants: readonly Pick<TrackingParticipant, 'in' | 'out'>[],
): Record<TrackingStanding, number> {
  return partyStandings(participants);
}

/**
 * The moment one person's row is aged from.
 *
 * <b>Two different moments live on a participant row and this is the first of them.</b>
 * `lastRecordedAt` is the last report of *any* kind about somebody — a station, a depth, an exit,
 * or a radio note that says nothing about where they are. The place drawn in the same row comes
 * from the last report that *claimed a place*, and it can be far older: a party reported at p.g.7
 * at noon and radioing "all fine" at four o'clock has a position four hours older than its last
 * word.
 *
 * <b>The second moment arrived and is drawn beside the place, not here.</b> The read carries
 * `positionRecordedAt`, and the position is dated from that — see `positionAgeInWords` on the
 * followed page's module, which both this tab and that page word their position's age through.
 * This function stays exactly what it was: the age of the last word, under the column named for
 * the last word. The two are kept side by side and are never folded into one figure, because the
 * gap between them is the answer to a question a coordinator is actually asking — somebody can
 * have been heard from ten minutes ago and last *placed* four hours ago, and a single age would
 * have to lie about one of those.
 */
export function lastHeardAtIso(
  participant: Pick<TrackingParticipant, 'lastRecordedAt'>,
): string | null {
  return participant.lastRecordedAt;
}

/**
 * How long ago somebody was last heard from, in the reader's own language, or null for a person
 * nobody has said anything about.
 *
 * <b>Null rather than a dash chosen here.</b> What silence is drawn as is the caller's business and
 * differs between a table cell and a count; what this function will not do is turn an absent moment
 * into words, because every set of words for it — "just now", "0 minutes ago", an epoch date — is a
 * report this watch never received.
 *
 * The rule that turns a gap into words is the followed page's and is called rather than copied: the
 * coordinator and the family read the same silence, and two roundings of it would have them
 * disagreeing about how long it has been by up to a whole unit.
 */
export function lastHeardInWords(
  participant: Pick<TrackingParticipant, 'lastRecordedAt'>,
  now: number,
  language: string,
): string | null {
  const iso = lastHeardAtIso(participant);
  return iso === null ? null : sinceInWords(iso, now, language);
}

/**
 * How old the place drawn in somebody's row is — the second of the two moments above.
 *
 * <b>Here rather than imported straight into the tab, because a page slice may not reach into
 * another page slice.</b> The rule that turns a gap into words lives with the followed page's fold
 * and is called rather than copied — a coordinator and a family read the same silence, and two
 * roundings of it would have them disagreeing by up to a whole unit. What this module adds is the
 * one legal door to it: everything else in this file is already the trips slice's way of asking the
 * same fold its questions.
 */
export function positionAgeOf(
  positionRecordedAt: string | null | undefined,
  now: number,
  language: string,
): string | null {
  return positionAgeInWords(positionRecordedAt, now, language);
}

/**
 * How many of the party the read marks as not heard from for too long.
 *
 * Counted off the server's own mark and never worked out here from a threshold and this browser's
 * clock: a second reading of the rule would be free to disagree with the tags in the rows below
 * it, by however far this machine's clock is from the server's.
 */
export function quietCount(participants: readonly Pick<TrackingParticipant, 'quiet'>[]): number {
  return participants.filter((participant) => participant.quiet).length;
}

/** A length of time in the one unit it is said in on the watch. */
export interface WatchSpan {
  unit: 'hours' | 'minutes';
  amount: number;
}

/**
 * The installation's quiet threshold, as the number the mark is worded with — or null where the
 * read sent none, which is how it says that nobody is being marked here.
 *
 * Hours where the threshold is a whole number of them, which is what nearly every installation
 * sets; minutes otherwise, so that an hour and a half is said as "90 min" and not rounded to a
 * figure the server is not applying.
 */
export function quietThresholdOf(quietAfterSeconds: number | null | undefined): WatchSpan | null {
  if (quietAfterSeconds == null || quietAfterSeconds <= 0) {
    return null;
  }
  return quietAfterSeconds % 3600 === 0
    ? { unit: 'hours', amount: quietAfterSeconds / 3600 }
    : { unit: 'minutes', amount: Math.max(1, Math.round(quietAfterSeconds / 60)) };
}

/**
 * Orders two people by when each was last heard from, the longest silence first.
 *
 * Somebody nobody has said a word about sorts ahead of everybody: of all the silences on the
 * table theirs is the one with no end to measure from, and a sort that put them last would file
 * the row a coordinator most needs under the ones just reported.
 */
export function byLastHeard(
  a: Pick<TrackingParticipant, 'lastRecordedAt'>,
  b: Pick<TrackingParticipant, 'lastRecordedAt'>,
): number {
  const moment = (participant: Pick<TrackingParticipant, 'lastRecordedAt'>) =>
    participant.lastRecordedAt === null
      ? Number.NEGATIVE_INFINITY
      : Date.parse(participant.lastRecordedAt);
  const [first, second] = [moment(a), moment(b)];
  return first === second ? 0 : first < second ? -1 : 1;
}

/** How the party stands against the hour it said it would be out by. */
export interface PlanStanding {
  /** The hour the party planned to be out by, as the trip records it. */
  dueAt: string;
  /** Whether that hour has passed with somebody still underground. */
  late: boolean;
  /** By how long, in milliseconds; zero while not late. */
  lateByMs: number;
}

/**
 * The trip's planned return read against the party as it stands, or null for a trip with no plan.
 *
 * <b>Late only while somebody is still underground.</b> A plan is a promise about people in a
 * cave: once the last of them is reported out it has been kept or broken and is over either way,
 * and a figure that went on counting up over a party safely out would be an alarm about nobody.
 * Somebody never heard from does not make a party late either — the watch has no word that they
 * went in at all, and their silence already has its own count.
 *
 * <b>It reads the hour and nothing else.</b> Whether an overdue callout was arranged for this
 * trip, whether it has fired or been stood down, is not asked: this is a figure for whoever is
 * looking at the watch, and it sends nothing and changes nothing.
 */
export function planStanding(input: {
  expectedReturnAt: string | null | undefined;
  underground: number;
  now: number;
}): PlanStanding | null {
  if (input.expectedReturnAt == null) {
    return null;
  }
  const due = Date.parse(input.expectedReturnAt);
  if (Number.isNaN(due)) {
    return null;
  }
  const late = input.underground > 0 && input.now > due;
  return { dueAt: input.expectedReturnAt, late, lateByMs: late ? input.now - due : 0 };
}

/**
 * A lateness in the unit it is said in: whole hours once there is one, minutes before that.
 *
 * Rounded down, both of them. "2 h late" at an hour and fifty minutes would be the screen claiming
 * more than has happened, on the one figure of this page most likely to be read out over a phone.
 */
export function lateSpanOf(lateByMs: number): WatchSpan {
  const minutes = Math.floor(lateByMs / 60_000);
  return minutes >= 60
    ? { unit: 'hours', amount: Math.floor(minutes / 60) }
    : { unit: 'minutes', amount: Math.max(1, minutes) };
}

/**
 * The planned hour as a clock reading: the hour alone when it falls on the reader's today, with
 * its day in front when it does not.
 *
 * A camp's trip can plan to be out tomorrow morning, and "09:00" on its own the evening before
 * reads as an hour long gone.
 */
export function planHourInWords(dueAt: string, now: number, language: string): string {
  const due = new Date(dueAt);
  const today = new Date(now);
  const sameDay =
    due.getFullYear() === today.getFullYear() &&
    due.getMonth() === today.getMonth() &&
    due.getDate() === today.getDate();
  return sameDay
    ? due.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' })
    : due.toLocaleString(language, {
        day: 'numeric',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
      });
}
