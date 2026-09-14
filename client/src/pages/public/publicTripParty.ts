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
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);
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
