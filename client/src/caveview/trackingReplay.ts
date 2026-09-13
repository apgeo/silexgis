// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingEvent, TrackingState } from '../api/hooks.ts';
import type { TrackedCaver, TrackedCaverPosition } from './trackedCavers.ts';

/**
 * Where the party was at a moment of the trip, folded out of the reports themselves.
 *
 * The watch next door answers one question — where is everybody *now* — because that is the only
 * question the server folds. This answers the same question about a moment that has passed, and it
 * can only be answered here: the log is the only record that a position was ever anything other
 * than what it is, and nothing on the server replays it.
 *
 * <b>The answer has the shape the live path produces, deliberately.</b> Everything downstream of
 * `TrackedCaver[]` — the markers, the overlay list, the card — is written once and knows nothing
 * about replay. A second shape here would be a second set of rules about withholding, presence and
 * naming, kept in step by hand.
 *
 * Three rules are carried over unchanged, because they are the same rules:
 *
 * **A withheld position is said to have been withheld.** A report whose kind always carries a place
 * — at a station, at a depth — arriving with no place is a withholding and can be nothing else.
 * That is the rule the log table already reads events by, and it is stronger here than on the folded
 * watch: the folded watch keeps only the *latest* report's kind, so it cannot tell a hidden position
 * from one nobody reported, while the log holds the very report whose place was removed.
 *
 * **No marker is invented for a place nobody was told.** A withheld position produces a caver with
 * nowhere to draw, listed as withheld — never a guess, and never an absence, because an absence
 * reads as nobody knowing where somebody is when the truth is that *this reader* is not being told.
 *
 * **A report measured in another survey is not drawn on this one.** Station names mean whatever the
 * model they were measured in says they mean, so a trip whose survey was changed part-way through
 * has reports that name places this model cannot hold. They are passed over rather than drawn.
 */

/** A report with its instant resolved once, so the scan below never re-parses a date. */
interface DatedEvent {
  at: number;
  event: TrackingEvent;
}

/** The stretch of the trip a replay can be scrubbed over, as epoch milliseconds. */
export interface ReplayWindow {
  from: number;
  to: number;
}

/** A report carrying words, placed on the replay's clock. */
export interface ReplayNote {
  at: number;
  caverId: string;
  kind: TrackingEvent['kind'];
  note: string;
}

/** What one caver's reports up to the moment add up to. */
interface CaverHistory {
  position: TrackedCaverPosition | null;
  lastRecordedAt: string;
  enteredAt: string | null;
  teamId: string | null;
  out: boolean;
}

/**
 * The reports oldest first, with unreadable instants dropped.
 *
 * The server lists them newest first, so the reversal is what gives ties — several people reported
 * at one station in one breath, which is exactly how the report form records a party — the order
 * the server put them in. The sort is stable, so it keeps that order for equal instants rather than
 * shuffling a group of simultaneous reports differently on every tick of the clock.
 *
 * A report whose time cannot be read is left out: there is no instant to place it at, and placing
 * it anywhere would move somebody at a moment nothing says they moved.
 */
function datedEvents(events: readonly TrackingEvent[]): DatedEvent[] {
  const dated: DatedEvent[] = [];
  for (let index = events.length - 1; index >= 0; index--) {
    const event = events[index];
    const at = Date.parse(event.recordedAt);
    if (Number.isFinite(at)) {
      dated.push({ at, event });
    }
  }
  return dated.sort((left, right) => left.at - right.at);
}

/**
 * The stretch of time a replay of this trip covers.
 *
 * It starts where the watch was armed and ends where it was closed, or — for a party still
 * underground — at the moment the replay was opened. It is then widened to hold every report on the
 * log: a report can be stamped with when it was *said* rather than when it was written down, so one
 * relayed out of the cave can land outside that stretch, and a scrubber that could not be dragged
 * to a report is a replay with a report missing from it.
 *
 * Null when there is nothing to scrub: a watch that was never armed, or one armed and closed in the
 * same instant with nothing on its log.
 *
 * @param openedAt the moment the replay was opened, which is where a live trip's window ends. Taken
 *   from the caller rather than read here so the end of the window does not creep forward under the
 *   handle somebody is dragging.
 */
export function replayWindow(
  tracking: Pick<TrackingState, 'armedAt' | 'closedAt'>,
  events: readonly TrackingEvent[],
  openedAt: number,
): ReplayWindow | null {
  const armed = tracking.armedAt === null ? Number.NaN : Date.parse(tracking.armedAt);
  if (!Number.isFinite(armed)) {
    return null;
  }
  const closed = tracking.closedAt === null ? Number.NaN : Date.parse(tracking.closedAt);

  let from = armed;
  let to = Number.isFinite(closed) ? closed : openedAt;
  for (const { at } of datedEvents(events)) {
    from = Math.min(from, at);
    to = Math.max(to, at);
  }
  return to > from ? { from, to } : null;
}

/**
 * The reports that said something in words, oldest first, as marks for the timeline.
 *
 * Any report can carry a note — the form offers the field whatever is being reported — so this is
 * not the `note` kind alone. What makes a mark is words somebody wrote, whatever else the report
 * also said.
 *
 * A note carries no position and never places anybody: that is the report's other fields' business,
 * and the derivation below reads them and not this.
 *
 * <b>Pictures are not here, and are not invented.</b> A report carries no attachment on the server
 * today, so there is nothing to hang on a mark and no schema to guess at. When the log grows one,
 * this is where a mark learns about it.
 */
export function replayNotes(
  events: readonly TrackingEvent[],
  window: ReplayWindow,
): ReplayNote[] {
  const notes: ReplayNote[] = [];
  for (const { at, event } of datedEvents(events)) {
    const note = event.note ?? '';
    if (note.length > 0 && at >= window.from && at <= window.to) {
      notes.push({ at, caverId: event.caverId, kind: event.kind, note });
    }
  }
  return notes;
}

/** The note in force at a moment: the latest one said at or before it, or none yet. */
export function noteAt(notes: readonly ReplayNote[], at: number): ReplayNote | null {
  let found: ReplayNote | null = null;
  for (const note of notes) {
    if (note.at > at) {
      break;
    }
    found = note;
  }
  return found;
}

/** The note before a moment, for a reader stepping back through them. Strictly before. */
export function noteBefore(notes: readonly ReplayNote[], at: number): ReplayNote | null {
  let found: ReplayNote | null = null;
  for (const note of notes) {
    if (note.at >= at) {
      break;
    }
    found = note;
  }
  return found;
}

/** The next note after a moment, for a reader stepping forward. Strictly after. */
export function noteAfter(notes: readonly ReplayNote[], at: number): ReplayNote | null {
  return notes.find((note) => note.at > at) ?? null;
}

/**
 * Everybody on the watch, as they stood at one instant.
 *
 * A caver's position is their latest report that claimed a place, at or before the instant. Going in
 * and coming out flip presence and claim no place; a note claims no place either. Somebody who has
 * come out keeps the place they were last reported at and is marked out — their marker is where they
 * were, not where they are — because taking them off the model would read as a caver who vanished
 * rather than one who is safely above ground.
 *
 * Everybody the watch names is returned, in the order the watch names them, including people no
 * report mentions yet. The live path lists them too, and a replay whose list grew and shrank as it
 * played would be a different surface from the one it is pretending to be.
 *
 * @param events the <b>whole</b> log. A replay over a partial log silently lies: the earlier pages
 *   are the older reports, so a first page alone shows everybody appearing out of nowhere at the
 *   point that page begins.
 * @param at the instant being replayed, as epoch milliseconds.
 * @param nameOf what the trip's roster calls a caver — the watch carries ids and nothing on it
 *   knows what anybody is called. Unlike the live path this asks for nothing else, because the
 *   moment somebody went in is on the log this is already reading, and the moment in force at `at`
 *   is not the same as the latest one.
 * @param surveyModelId the model the panel is showing, or undefined when it does not know.
 */
export function trackedCaversAt(
  tracking: TrackingState,
  events: readonly TrackingEvent[],
  at: number,
  nameOf: (caverId: string) => string,
  surveyModelId: string | undefined,
): TrackedCaver[] {
  if (
    surveyModelId === undefined
    || tracking.surveyModelId === null
    || tracking.surveyModelId !== surveyModelId
    || !Number.isFinite(at)
  ) {
    return [];
  }

  const teamTitles = new Map(tracking.teams.map((team) => [team.id, team.title]));
  const histories = new Map<string, CaverHistory>();

  for (const { at: when, event } of datedEvents(events)) {
    if (when > at) {
      break;
    }
    const history = histories.get(event.caverId) ?? {
      position: null,
      lastRecordedAt: event.recordedAt,
      enteredAt: null,
      teamId: null,
      out: false,
    };
    history.lastRecordedAt = event.recordedAt;
    if (event.kind === 'entered') {
      // Somebody who came out and went back in is in again, on the strength of the later entry.
      history.enteredAt = event.recordedAt;
      history.out = false;
    } else if (event.kind === 'exited') {
      history.out = true;
    }
    if (event.teamId !== null) {
      history.teamId = event.teamId;
    }
    const place = placeReported(event, surveyModelId);
    if (place !== null) {
      history.position = place;
    }
    histories.set(event.caverId, history);
  }

  return tracking.participants.map((participant) => {
    const history = histories.get(participant.caverId);
    // The team a report carried at the time, and nothing else.
    //
    // There is deliberately no falling back to the label the watch itself holds, because that
    // label is not a roster field: the server folds it out of this same log, as the team named on
    // the caver's *latest* team-bearing report. So at any moment where no report has named a team
    // yet, the watch's label is either nothing at all or a team the caver is put in later — and a
    // fallback to it could therefore only ever draw a caver in a team they did not belong to at
    // the moment on screen, which is the anachronism this whole surface exists to remove.
    // Somebody no report has labelled yet is shown with no team, which is what was known then.
    const teamId = history?.teamId ?? null;
    return {
      caverId: participant.caverId,
      name: nameOf(participant.caverId),
      teamTitle: teamId === null ? null : (teamTitles.get(teamId) ?? null),
      position: history?.position ?? unplaced(history !== undefined, tracking.positionsWithheld),
      lastRecordedAt: history?.lastRecordedAt ?? null,
      enteredAt: history?.enteredAt ?? null,
      out: history?.out ?? false,
    };
  });
}

/**
 * What one report says about a place, or null where it claims none.
 *
 * Only a station report and a depth report claim one. Going in, coming out and a note say that
 * something happened, not where — so they leave the last place claimed standing rather than
 * clearing it, which is the same reading the watch's own position column has.
 */
function placeReported(
  event: TrackingEvent,
  surveyModelId: string,
): TrackedCaverPosition | null {
  if (event.kind !== 'atStation' && event.kind !== 'atDepth') {
    return null;
  }
  // Measured in another survey: the name means a place in another cave, and drawing it here would
  // be a confident claim about where somebody is, made from a name that happens to collide. A model
  // id that is absent is not another model — it is the withholding the next lines say out loud.
  if (event.surveyModelId !== null && event.surveyModelId !== surveyModelId) {
    return null;
  }
  if (event.stationName !== null && event.stationName.length > 0) {
    return { kind: 'station', station: event.stationName };
  }
  // A depth is a position and is not a station: it is somewhere on a line the model does not draw,
  // so it is said rather than placed at a station it might not be at.
  if (event.depthEnteredM !== null) {
    return { kind: 'depth', depthM: event.depthEnteredM };
  }
  // A report of this kind always carries a place. Arriving without one, it was kept from this
  // reader, and there is no second reading of it.
  return { kind: 'withheld', certain: true };
}

/**
 * An absence where a position would be, said no more strongly than the live path would say it.
 *
 * Nobody has claimed a place for this person at this moment. If they have said nothing at all there
 * is no position to keep from anybody and saying "withheld" would invent a secret. If they have
 * said something and positions are being withheld on this trip, this cannot be told apart from a
 * position that was reported and removed — the log holds every report, but a report that never
 * reached this reader as a report cannot be counted — so it is said as the weaker thing and claims
 * neither.
 */
function unplaced(hasReported: boolean, positionsWithheld: boolean): TrackedCaverPosition {
  if (!hasReported || !positionsWithheld) {
    return { kind: 'unreported' };
  }
  return { kind: 'withheld', certain: false };
}
