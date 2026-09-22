// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { instantOf } from '../pages/public/publicTripParty.ts';
import type { TrackedCaver } from './trackedCavers.ts';

/**
 * How one person of a watch reads on a marker's label — the single spelling, wherever a
 * marker is drawn.
 *
 * Extracted from the 3D panel the day the scanned map sheets started drawing the same
 * party: a caver drawn at a station of the 3D scene and the same caver drawn at that
 * station's point on a map sheet are one fact, and two spellings of their label would
 * make the two surfaces disagree about the person they are both showing. Everything the
 * 3D panel's comments established still holds and is kept here verbatim.
 */

/** What a line of a label is composed against: the words, the clock and the switch. */
export interface MarkerLineOptions {
  t: TFunction;
  language: string;
  showTimes: boolean;
  /**
   * The reader's own calendar day, as their locale writes one — what a printed moment is compared
   * against to decide whether it needs a date saying with it. A string rather than an instant on
   * purpose: it changes once, at midnight, so every dependency list that carries it re-registers
   * the labels then and at no other moment, where a live clock would rebuild every collapsed
   * marker on the model every time the watch polled.
   */
  today: string;
}

/**
 * A reported moment as a marker prints it: the clock alone for something from today, the date
 * said with it for anything older.
 *
 * <b>The moment on a marker became a much older one and the label did not change with it.</b> What
 * used to be printed here was the last word about somebody, which on a live watch a radio check
 * refreshes every few minutes — so a bare clock was unambiguous in practice, because the moment
 * was always from the last half hour. This now prints the position's own moment, which is
 * routinely hours older and on an overnight trip is routinely from yesterday. A party placed at
 * ten past ten at night and heard from through the night would draw at seven the next morning as
 * "Ana · 22:10", which reads as tonight: the reader deciding whether that team is overdue would
 * get the number right and the day wrong, with nothing on the label to say which.
 *
 * <b>Said as a date rather than as a gap, which is the other way it could have been said.</b> The
 * tab and the followed page word this moment as "reported 4 hours ago", and those surfaces
 * re-render on a clock. A marker does not: the viewer is asked for a label only when a marker is
 * added, slid or removed, so a gap printed on one would be composed once and then stand unchanged
 * while it aged — "an hour ago" still on the model eight hours later, which is worse than the
 * ambiguity it would have replaced. A date is true whenever it is read.
 */
function markerClock(at: number, language: string, today: string): string {
  const moment = new Date(at);
  return moment.toLocaleDateString(language) === today
    ? moment.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' })
    : moment.toLocaleString(language, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
}

/**
 * One person as a label reads them: their name, the time beside it where that was asked for, and
 * whether they have come out.
 *
 * <b>The time is the moment the position was reported, and it has to be.</b> A marker is a station,
 * drawn at a place on the model, and a time printed against it is read as when that person was
 * there — so the moment of somebody's *last word* beside their station is a sentence nobody meant
 * to write: a party placed at noon that radioed "all fine" at four would be drawn at noon's station
 * under four o'clock's clock, and a reader deciding whether a team is overdue would take the place
 * to be four hours fresher than it is. Somebody with no dated position carries no time at all here
 * rather than borrowing their last word's — a marker is only ever drawn for a station that was
 * reported, so in practice that is the withheld and the unreadable, and both stay silent.
 *
 * <b>One spelling for a marker drawn alone and for a line of a group's label.</b> Which of the two
 * somebody appears as is the viewer's decision, taken from whether anybody else resolved to the
 * same station, and it changes under a reader who is doing nothing — so anything said one way and
 * not the other is a fact that appears and disappears as the party gathers and separates. That is
 * how the out flag came to be dropped: it survived collapsing as a colour, and a collapsed marker
 * has only one colour for all of them.
 *
 * <b>Out is said in words, not only in the muted colour.</b> The colour is still drawn where a
 * marker stands alone, and it carries nothing for a reader who cannot separate two greys on a dark
 * scene — on a surface somebody uses to decide whether a party is still underground, that is not a
 * thing to leave to a hue.
 */
export function markerLine(
  caver: TrackedCaver,
  { t, language, showTimes, today }: MarkerLineOptions,
): string {
  // Read through the shared rule rather than compared to null, because null is only one of the
  // three ways this moment goes missing and the other two reach a formatter that does not refuse
  // them: a field a server never wrote and a string that will not parse both print, on a marker
  // beside somebody's name, the words "Invalid Date".
  const at = instantOf(caver.positionAt);
  const named =
    showTimes && at !== null
      ? t('caveview.tracking.markerNameTime', {
          name: caver.name,
          when: markerClock(at, language, today),
        })
      : caver.name;
  return caver.out ? t('caveview.tracking.markerNameOut', { name: named }) : named;
}
