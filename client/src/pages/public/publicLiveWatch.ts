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
    // The watched party's own plan, never the link's: the two parties went in at different hours
    // and said different things about coming out.
    expectedReturnAt: row.expectedReturnAt,
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

/**
 * How long before the list of parties was refused a past list may have arrived and still count as
 * an answer of the same moment — see {@link linkListsPastOnly}.
 *
 * The time the list of past trips is itself believed for before a returning reader has it read
 * again: within it the page would not have asked again whatever it was told, so an answer of that
 * age is the freshest this page could hold without a request made only to find out.
 */
export const PAST_ONLY_EVIDENCE_MS = 5 * 60_000;

/**
 * Whether this link has stopped listing the parties in the cave today while it still opens the
 * cave's past trips.
 *
 * <b>Why the state exists.</b> A link outlives the trip it was published for: once that trip is
 * a past one, the same link goes on opening the cave's past trips, and by default goes on listing
 * whoever is being followed there now. An installation may bound the second — an article written
 * years ago need not name today's parties — and from then on the list of parties is refused for
 * good while the past trips answer as before.
 *
 * <b>Inferred, because the server never says why.</b> Every refusal of a published link is one
 * answer, on purpose, so there is no reason to read. What a page does hold is which of its reads
 * answered: the list of past trips in hand, not itself refused for good, and the list of parties
 * refused for good. That pair is the whole of the evidence and is all this asks for.
 *
 * <b>Only a settled refusal counts, and only a past list that was actually read.</b> A request
 * that did not land, a server that is busy and a wait the server asked for all clear by
 * themselves, and saying "no longer" of them would be false within the minute. A past list nobody
 * has asked for is not known to answer, and nothing is read to find out: with it unknown, or
 * refused as well, a page keeps the wording it had — a link that is over, or a read that failed.
 *
 * <b>And the two answers have to belong to one moment, because a list in hand is not a list that
 * still answers.</b> The past trips are read when a reader opens them and never on a clock, and
 * the page keeps what it was given long after it stopped asking. A link taken back while the page
 * is open refuses both lists from then on, yet leaves exactly this pair behind: an old past list,
 * and a list of parties newly refused. Telling that reader "the past trips are still here" would
 * be false — every one of them is refused when pressed. So the past list counts only when it
 * arrived
 * <ul>
 *   <li>no earlier than the list of parties last answered (`live.readAt`, zero where it
 *       never answered through this link — which is the old link, refused from the first ask):
 *       a list of parties that answered after the past trips were read and is refused now is a
 *       link that changed since, and nothing in hand says the past trips survived the change;
 *       and</li>
 *   <li>after the refusal, or no longer before it than {@link PAST_ONLY_EVIDENCE_MS}: the same
 *       press in a frame, a section opened a moment earlier on the page.</li>
 * </ul>
 * Anything older is no evidence, and the page keeps the wording it had. No request is made to
 * freshen either answer: where the past trips are read again for a reader's own reasons and
 * answer, the state is recognised from then on.
 *
 * <b>What it still does not prove</b> is that the past list would answer this second — only that
 * it did at about the moment the list of parties was refused. The sentence drawn from this
 * therefore says what the link no longer does, and points at the past trips the page was given —
 * never that anything is being refreshed.
 */
export function linkListsPastOnly(
  past: { list: unknown; error: unknown; readAt: number },
  live: { error: unknown; readAt: number; refusedAt: number },
): boolean {
  return (
    past.list !== undefined &&
    !isSettledRefusal(past.error) &&
    isSettledRefusal(live.error) &&
    past.readAt > 0 &&
    past.readAt >= live.readAt &&
    past.readAt >= live.refusedAt - PAST_ONLY_EVIDENCE_MS
  );
}
