// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PublicTripParticipant, PublicTripTeam } from '../../api/hooks.ts';

/** How a published party divides up: underground, back out, or not yet heard of. */
export type PublicTripStanding = 'underground' | 'out' | 'unheard';

/**
 * Where one member of the party stands, as the three states the envelope actually distinguishes.
 *
 * <b>"Neither in nor out" is a state of its own and has to be drawn as one.</b> Somebody nobody
 * has reported yet arrives with both flags false, and a page that folded that into "not
 * underground" would draw a party which has not set off as one that is already out — which on this
 * surface, read by somebody waiting at home, is the difference between "they have not gone in yet"
 * and "they are safely back".
 */
export function standingOf(
  participant: Pick<PublicTripParticipant, 'in' | 'out'>,
): PublicTripStanding {
  if (participant.out) {
    return 'out';
  }
  return participant.in ? 'underground' : 'unheard';
}

/** How many of the party stand in each state — the line that sums a followed page up. */
export function partyStandings(
  participants: readonly Pick<PublicTripParticipant, 'in' | 'out'>[],
): Record<PublicTripStanding, number> {
  const counts: Record<PublicTripStanding, number> = { underground: 0, out: 0, unheard: 0 };
  for (const participant of participants) {
    counts[standingOf(participant)]++;
  }
  return counts;
}

/** One team's row of the page, or the gathering of everybody who is on none. */
export interface PublicTripPartyGroup<T> {
  teamId: string | null;
  /** Null for the group of people on no team; the page names that one in its own language. */
  title: string | null;
  members: T[];
}

/**
 * The party arranged the way it was organised: by team, with everybody on no team gathered at the
 * end.
 *
 * The teams keep the order the envelope sent them in. They are a club's own arrangement of its
 * party, so re-sorting them here by a count or by whoever was reported last would rearrange
 * somebody's trip to suit what the radio happened to say a minute ago.
 *
 * A participant naming a team the envelope did not carry is gathered with the unteamed rather than
 * dropped. Nothing should produce that, and the alternative to handling it is a person quietly
 * missing from the one page their family is watching.
 */
export function partyByTeam<T extends Pick<PublicTripParticipant, 'teamId'>>(
  participants: readonly T[],
  teams: readonly PublicTripTeam[],
): PublicTripPartyGroup<T>[] {
  const known = new Set(teams.map((team) => team.id));
  const groups: PublicTripPartyGroup<T>[] = teams.map((team) => ({
    teamId: team.id,
    title: team.title,
    members: participants.filter((participant) => participant.teamId === team.id),
  }));

  const loose = participants.filter(
    (participant) => participant.teamId === null || !known.has(participant.teamId),
  );
  if (loose.length > 0) {
    groups.push({ teamId: null, title: null, members: loose });
  }

  return groups.filter((group) => group.members.length > 0);
}

/**
 * How long ago something was heard, in the reader's own language.
 *
 * <b>A followed page is read for the gap, not for the clock.</b> Somebody watching from home wants
 * to know that the last word was twenty minutes ago; a timestamp makes them do that subtraction
 * themselves, on a phone, possibly in another time zone, and gets it wrong for them if the party
 * is caving across midnight. So the gap is what is said, and the exact moment is offered beside it
 * for whoever wants it.
 *
 * Rounded down to the unit below, which is the honest direction: "2 hours ago" for something two
 * hours and fifty minutes old would be a page under-reporting a silence.
 */
export function sinceInWords(iso: string, now: number, language: string): string {
  return gapInWords(new Date(iso).getTime(), now, language);
}

/**
 * A reported moment as something that can be compared and worded, or null where there is no
 * moment to be had.
 *
 * <b>There are three ways a moment can be missing and only one of them looks like it.</b> The
 * explicit `null` the server sends for a position nobody reported is the obvious one. A field a
 * server predating it never wrote arrives as `undefined`, which is not `null` and passes every
 * `=== null` guard ever written. And a string that will not parse — a clock nobody set, a value
 * mangled in transit — arrives looking like a moment and is `NaN` the instant anything reads it.
 * All three mean the same thing to a reader, which is that nobody said when, so all three answer
 * the same way here.
 *
 * <b>Why this is a shared function rather than a guard at each site.</b> What `NaN` does next
 * depends entirely on who receives it: `Intl.RelativeTimeFormat` throws — taking a followed page
 * down to its error boundary, so a family watching a trip sees no party at all rather than one
 * undated position — while `toLocaleTimeString` quietly returns the words "Invalid Date" and draws
 * them on a marker beside somebody's name. Neither is a failure a reader could act on, and a rule
 * spelled once cannot be remembered at one site and forgotten at the next.
 */
export function instantOf(value: string | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** The gap between an instant and now, in the reader's language — the one rounding rule. */
function gapInWords(instant: number, now: number, language: string): string {
  const seconds = Math.round((instant - now) / 1000);
  const format = new Intl.RelativeTimeFormat(language, { numeric: 'auto' });
  const magnitude = Math.abs(seconds);
  if (magnitude < 60) {
    return format.format(Math.trunc(seconds), 'second');
  }
  if (magnitude < 3600) {
    return format.format(Math.trunc(seconds / 60), 'minute');
  }
  if (magnitude < 86_400) {
    return format.format(Math.trunc(seconds / 3600), 'hour');
  }
  return format.format(Math.trunc(seconds / 86_400), 'day');
}

/**
 * How long ago the report that actually placed somebody was made, or null where nothing placed
 * them.
 *
 * <b>Two different moments live on a participant and this is the second one.</b> The last word
 * about somebody — a radio note, an exit, "no answer from Maria" — moves `lastRecordedAt` and
 * says nothing about where they are. The station beside it came from the last report that claimed
 * a place, and it can be hours older: a party reported at p.g.7 at noon and radioing "all fine" at
 * four has a position four hours old and a last word eight minutes old. Dating the station with
 * the second of those was the defect this exists to close, so this function reads the position's
 * own moment and there is deliberately nothing else it can read.
 *
 * <b>Silence stays silence, and the signature is how.</b> A position that was never reported and
 * one this reader may not be told apart arrive identically — as no moment at all — and neither may
 * acquire an age: every set of words for it ("just now", "0 minutes ago", a date in 1970) is a
 * report nobody made. What is taken here is therefore the moment itself rather than a participant,
 * so that a caller whose position resolution produced no place has nothing to hand over. A
 * withheld position cannot be dated by mistake, because the only branch that carries a moment is
 * the branch that drew a place.
 *
 * <b>And all three spellings of "no moment" are refused, not just the one the types admit to.</b>
 * The generated client declares this field required, so an absent one is `undefined` rather than
 * `null` and a guard written against `null` alone lets it through — into a formatter that throws,
 * out of render, and down to the error boundary: a followed page showing a family no party at all
 * because one position had no time on it. See {@link instantOf}, which is where the three are
 * named and where the only copy of that rule lives.
 *
 * The gap is worded by the same rule as every other gap on these two pages — called, never
 * copied — so a coordinator and a family reading the same position round it the same way.
 */
export function positionAgeInWords(
  positionRecordedAt: string | null | undefined,
  now: number,
  language: string,
): string | null {
  const at = instantOf(positionRecordedAt);
  return at === null ? null : gapInWords(at, now, language);
}
