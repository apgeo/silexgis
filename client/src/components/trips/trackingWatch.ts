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
 * A tripwire, and the only thing in this file that is not used by anything.
 *
 * <b>What it is for.</b> The standing below is folded here because the read this client is
 * generated against does not carry the server's own fold. That is temporary: the tracking read is
 * growing an `in` of its own, folded in Domain beside the rule the published page already answers
 * from. The moment `schema.d.ts` is generated against that server, this stops compiling and says
 * what to do — because the alternative is the failure this guards against, which is that nothing
 * whatever happens. The field would simply arrive as one nobody reads: no type error, no failing
 * test, two surfaces quietly disagreeing about whether a caver is underground, and a doc comment
 * asking politely to be remembered. A note in a comment is not a check; this is.
 *
 * <b>What to do when it fires.</b> Read the standing off the participant instead of folding it:
 * `standingOf({ in: participant.in, out: participant.out })`, with `null` last kind still the
 * "nothing at all" case if the fold ever needs it. Then delete {@link readAsUnderground} and this
 * constant together — both exist only until that field lands.
 */
export const STANDING_IS_FOLDED_HERE: 'in' extends keyof TrackingParticipant
  ? 'The tracking read now carries `in`: read the standing off it and delete this whole tripwire.'
  : true = true;

/**
 * Whether the last thing anybody said leaves this person inside the cave.
 *
 * <b>An approximation, named as one, in the one place it is made.</b> The three states are the
 * server's conclusion over *every* report about somebody, and this read carries only the last one,
 * so this is a fold over a single step. Four of the five kinds answer exactly: nothing reported at
 * all is nobody having said a word; an entry, a station and a depth each put somebody inside; an
 * exit takes them out. The fifth does not, and it is worth being plain about which way it is wrong.
 *
 * <b>A note is the ambiguous one.</b> A note says something happened, not where and not whether —
 * so in Domain it leaves the standing exactly as it found it, which may be "underground" from an
 * earlier entry or "nothing said yet" from no report at all. From one report those two cannot be
 * told apart, and this reads it as underground. That over-claims for exactly one person: somebody
 * whose only report is a note and who was never reported as going in — "no answer from Maria", the
 * commonest thing a relayed phone call carries — is drawn as underground and is missing from the
 * count of people nobody has heard from.
 *
 * <b>Why that way round rather than the other.</b> Reading a note as silence would be wrong far
 * more often and far more dangerously: a party that went in at nine and radioed "all fine" at
 * eleven has a note as its last word, and calling them "not heard from" would report a party
 * demonstrably underground as one that may never have set off — on the screen somebody reads while
 * deciding whether to call a rescue out. So the rarer error is taken, in the direction that says
 * somebody may still be in a cave, and it is removed rather than reduced the moment the read
 * carries the fold itself. See the tripwire above.
 */
function readAsUnderground(participant: Pick<TrackingParticipant, 'lastKind'>): boolean {
  // Nobody has said a single word about them. Exact, and the state the whole count exists for.
  if (participant.lastKind === null) {
    return false;
  }
  // An exit is a statement and it ends the watch for a person. Exact, and it agrees with `out`,
  // which is what the caller actually reads it through.
  if (participant.lastKind === 'exited') {
    return false;
  }
  // Entered, a station, a depth — and a note, for the reason given above. A kind added to the
  // server later lands here too and is read as still inside, which is the same direction.
  return true;
}

/**
 * Where one person on the watch stands.
 *
 * <b>Silence is a state of its own, and getting that one right is the whole point of this
 * function.</b> Somebody nobody has said a word about arrives with no report at all, and folding
 * that into "not out" would draw a caver who may never have reached the cave as one who is
 * underground — on the surface somebody reads while deciding whether to call a rescue out.
 *
 * Read from the kind of the last report rather than from whether a moment exists, because the kind
 * is the vocabulary the rule is actually written in: "has anybody said anything at all" and "has
 * anybody said they went in" are two questions, and a surface that asked the first while answering
 * the second could not even describe the case it gets wrong. See {@link readAsUnderground}, which
 * is where that case is written down.
 */
export function trackingStandingOf(
  participant: Pick<TrackingParticipant, 'lastKind' | 'out'>,
): TrackingStanding {
  return standingOf({ in: readAsUnderground(participant), out: participant.out });
}

/**
 * How many of the party stand in each state — the line the coordinator's screen was missing.
 *
 * Counted through the followed page's own counter so the three buckets cannot drift apart, and it
 * counts all three: a watch that said "4 underground, 1 out" over a party of six would be silently
 * leaving out the one person the coordinator most needs to think about.
 */
export function trackingStandings(
  participants: readonly Pick<TrackingParticipant, 'lastKind' | 'out'>[],
): Record<TrackingStanding, number> {
  return partyStandings(
    participants.map((participant) => ({
      in: readAsUnderground(participant),
      out: participant.out,
    })),
  );
}

/**
 * The moment one person's row is aged from.
 *
 * <b>Two different moments live on a participant row and this is the one the read carries.</b>
 * `lastRecordedAt` is the last report of *any* kind about somebody — a station, a depth, an exit,
 * or a radio note that says nothing about where they are. The place drawn in the same row is the
 * last report that *claimed a place*, and it can be far older: a party reported at P12 at noon and
 * radioing "all fine" at four o'clock has a position four hours older than its last word. The two
 * are different questions and the read answers only the first, so this answers only the first —
 * and the column it is drawn under is named for the last word rather than for the position beside
 * it, because an age under a heading that suggests it dates the station would be a wrong sentence
 * rather than a missing one.
 *
 * <b>The follow-up is an addition, not a correction, and it is here.</b> The tracking read is
 * growing a `positionRecordedAt` carrying the position's own moment. When it reaches the generated
 * client, the row gains a *second* age drawn from that field and shown beside the place, and this
 * function stays exactly what it is — the age of the last word, under the column named for it.
 * Nothing else in this application derives an age from a participant, so nothing else has to be
 * found.
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
