// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PastFollow } from './pastTrackReplay.ts';

/**
 * A past trip, a team or a caver in it, and a moment of it — written into the page's own address.
 *
 * <b>The plainest hyperlink there is, and the one a club can actually use.</b> The embed protocol
 * next door lets an article's prose drive a framed viewer, which is the right answer for a website
 * that has embedded one; it is no answer at all for a message, a forum post, a printed programme or
 * a club's own page that simply links to the published trip. Those need an address somebody can
 * paste, and this is it — the same address the page already lives at, with what to show written on
 * the query string.
 *
 * <b>It discloses nothing the page does not already hand over.</b> The trip id comes from the
 * archive list this very token opens, the team id from the track that list leads to, and the caver
 * is a place in the party rather than anybody's identity. The token in the path remains the whole
 * of the capability; none of these opens anything without it.
 *
 * <b>Unreadable values are left alone rather than guessed at</b>, exactly as the embed protocol
 * leaves an unknown target alone. A `past` naming a trip of another cave, a `team` naming a team of
 * another trip, a `caver` beyond the end of the roster — each answers by simply not being found,
 * and the page says what it can say.
 */

/** The past trip a link asked for, or null when it asked for none. */
export interface PastTripLink {
  tripLogId: string;
  follow: PastFollow | null;
  /** The instant asked for, as written — parsed where it is used, so a bad one is one absence. */
  at: string | null;
}

/** The names this page answers to on its query string. */
export const PAST_LINK_PARAMS = ['past', 'team', 'caver', 'at'] as const;

/**
 * What a query string is asking for, or null when it asks for nothing of the past.
 *
 * A `team` and a `caver` together is a link that cannot mean one thing, so the narrower of the two
 * wins: a person is a place, a team is a group of places, and somebody who wrote both plainly meant
 * to point at the person.
 */
export function readPastLink(params: URLSearchParams): PastTripLink | null {
  const tripLogId = params.get('past');
  if (tripLogId === null || tripLogId.length === 0) {
    return null;
  }
  const caver = params.get('caver');
  const team = params.get('team');
  const follow: PastFollow | null =
    caver !== null && caver.length > 0
      ? { kind: 'caver', id: caver }
      : team === null
        ? null
        // The empty string is the group of everybody on no team, which is a real group with no id
        // — the one place where an absent value and an empty one mean different things.
        : { kind: 'team', id: team.length === 0 ? null : team };
  return { tripLogId, follow, at: params.get('at') };
}

/**
 * The same query string with what is on screen written into it, so the address bar is something a
 * reader can copy and send.
 *
 * <b>The moment is deliberately not written.</b> A scrubber moves five times a second while a
 * replay plays; an address rewritten at that rate is a history a reader cannot get out of with the
 * back button, and a number nobody chose. A link *may* carry a moment — that is what `at` is read
 * for — but this page never invents one on somebody's behalf.
 *
 * Every parameter this page owns is cleared first, so leaving the past leaves no trace of it behind
 * and a link copied afterwards opens the live page it appears to be.
 */
export function writePastLink(
  params: URLSearchParams,
  tripLogId: string | null,
  follow: PastFollow | null,
): URLSearchParams {
  const next = new URLSearchParams(params);
  for (const name of PAST_LINK_PARAMS) {
    next.delete(name);
  }
  if (tripLogId === null) {
    return next;
  }
  next.set('past', tripLogId);
  if (follow?.kind === 'caver' && follow.id !== null) {
    next.set('caver', follow.id);
  } else if (follow?.kind === 'team') {
    next.set('team', follow.id ?? '');
  }
  return next;
}
