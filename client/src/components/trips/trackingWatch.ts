// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingParticipant } from '../../api/hooks.ts';
import {
  partyStandings,
  sinceInWords,
  standingOf,
  type PublicTripStanding,
} from '../../pages/public/publicTripParty.ts';

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
