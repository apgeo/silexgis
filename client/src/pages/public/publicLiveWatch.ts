// SPDX-License-Identifier: AGPL-3.0-or-later
import { isSettledRefusal } from '../../api/client.ts';
import type { PublicLiveTrip, PublicLiveTripList, PublicTripEnvelope } from '../../api/hooks.ts';

/**
 * Watching another party of the cave from a published link: what is on screen, and whose it is.
 *
 * <b>Nothing here asks the server for anything.</b> The list of parties being followed in the
 * link's cave is already handed to every holder of the link, each row with its whole party, and
 * every place in it was decided by the server against the survey the link itself was published
 * with: a place measured on another survey arrives without a station and flagged as such. So
 * watching a row is a matter of drawing what the browser already holds on the drawing it already
 * holds — and the rules below are only about never drawing one party under another's name.
 */

/**
 * A row of the list, shaped as the envelope everything on the page draws from.
 *
 * <b>The survey is the link's own, and never anything else.</b> A row carries no survey — its
 * places were decided against the link's — so the drawing, its pictures and its map sheets stay
 * exactly what the link was published with. That is also what keeps the viewer from loading
 * anything: the address it was given does not change when the party on it does.
 */
export function liveTripAsEnvelope(row: PublicLiveTrip, own: PublicTripEnvelope): PublicTripEnvelope {
  return {
    tripLogId: row.tripLogId,
    expedition: row.expedition,
    title: row.title,
    tripDate: row.tripDate,
    tripDateEnd: row.tripDateEnd,
    state: row.state,
    armedAt: row.armedAt,
    closedAt: row.closedAt,
    positionsWithheld: row.positionsWithheld,
    model: own.model,
    teams: row.teams,
    participants: row.participants,
  };
}

/** What the list says about the party a reader asked to watch. */
export type WatchedParty =
  /** Nobody was asked for — or the link's own trip was, which is not a watch at all. */
  | { kind: 'none' }
  /** Somebody was asked for and no list has been read yet, so there is nothing to say of them. */
  | { kind: 'waiting' }
  | { kind: 'watching'; trip: PublicLiveTrip }
  /** A list that was read does not carry them. */
  | { kind: 'gone' };

/**
 * Finds the party a reader asked to watch in the list of parties being followed.
 *
 * <b>Only a list that was actually read is evidence that a party has left it.</b> No list in hand
 * is "not known yet", not "gone" — and a list is only ever in hand because a read succeeded: a
 * read that fails leaves the previous one standing, with the party still in it, which is the
 * right thing to keep drawing under a notice that it is not being refreshed.
 *
 * <b>"Gone" means gone from the list, and claims nothing more.</b> A watch whose grace ran out
 * and a link taken back both drop a row; so does a cave with more parties being followed than
 * one list carries, where the row may merely have fallen past the end. All three leave this page
 * with nothing fresh to draw of that party, so all three end the watch — and what the reader is
 * told has to stay inside what is certain: the party is no longer in the list.
 *
 * <b>The link's own trip is never a watch.</b> It is in the list too, but the page reads it by
 * its own route, with its survey, and more often; asking for it is asking to go back.
 */
export function watchedParty(
  list: PublicLiveTripList | undefined,
  watchId: string | null,
  ownTripLogId: string | null | undefined,
): WatchedParty {
  if (watchId === null || watchId === ownTripLogId) {
    return { kind: 'none' };
  }
  if (list === undefined) {
    return { kind: 'waiting' };
  }
  const trip = list.trips.find((candidate) => candidate.tripLogId === watchId);
  return trip === undefined ? { kind: 'gone' } : { kind: 'watching', trip };
}

/** Which of the three things a published page can be showing. */
export type PublicTripViewMode = 'own' | 'watched' | 'past';

export interface PublicTripView {
  mode: PublicTripViewMode;
  /**
   * The party to draw, or undefined where there is honestly nobody to draw yet — a past trip
   * still being read, a watched party whose list has not arrived.
   */
  envelope: PublicTripEnvelope | undefined;
}

/**
 * What a published page shows: the link's own trip, another party of the cave being followed
 * now, or a past trip wound back to a moment.
 *
 * <b>One party at a time, under its own name, and the precedence is fixed.</b> A past trip the
 * reader picked wins over a watch — picking it is the later, more deliberate act, and the page
 * drops the watch when it happens — and a watch wins over the link's own trip.
 *
 * <b>The link's own party is the fallback of exactly one case: nothing else was asked for, or
 * what was asked for is gone.</b> While a chosen past trip or a watched party is merely not in
 * hand yet the answer is nobody, never the link's own party: drawn there, it would stand under a
 * banner naming somebody else, which is the one sentence these views must never produce. A
 * watched party that has left the list is the opposite case — there is no banner to stand under
 * any more, and the page says in a notice of its own that the watch has ended.
 */
export function publicTripView(
  own: PublicTripEnvelope | undefined,
  past: { engaged: boolean; envelope: PublicTripEnvelope | null },
  watched: WatchedParty,
): PublicTripView {
  if (past.engaged) {
    return { mode: 'past', envelope: past.envelope ?? undefined };
  }
  if (watched.kind === 'watching') {
    return {
      mode: 'watched',
      envelope: own === undefined ? undefined : liveTripAsEnvelope(watched.trip, own),
    };
  }
  if (watched.kind === 'waiting') {
    return { mode: 'watched', envelope: undefined };
  }
  return { mode: 'own', envelope: own };
}

/**
 * Whether the read that feeds what is on screen has been refused for good.
 *
 * <b>Asked of the read the screen is drawn from, because the two reads of one link do not end
 * together.</b> The link's own party arrives by the link's own read; another party of the cave
 * arrives by the list of parties being followed. A server stops answering the first as soon as
 * the link's own trip is no longer published, and may go on answering the second for as long as
 * that trip can still be read as a past one — so a link whose own read was refused this minute
 * can still be handing over, every minute, a party that is underground. Saying "this will not be
 * refreshed again" over that party on the strength of the other read would be false, and it
 * would be said at the moment somebody is reading the page for exactly that.
 *
 * And the other way about: the link's own read is not repeated once its trip is closed and
 * carries nothing that needs re-signing, so it can stand unrefused for hours after the link was
 * taken back. The list being refused for good is then the only word there is, and it is final
 * for the party on screen whatever the link's own read last said.
 *
 * A replay is drawn from neither, and is answered for as the link's own trip is.
 */
export function shownReadEnded(
  mode: PublicTripViewMode,
  ownError: unknown,
  listError: unknown,
): boolean {
  return isSettledRefusal(mode === 'watched' ? listError : ownError);
}
