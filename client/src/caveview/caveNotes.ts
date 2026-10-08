// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingEvent, TripPositionEventKind } from '../api/hooks.ts';
import { placeOnModel } from './drawableOn.ts';
import type { DrawnMarker } from './liveMarkerSync.ts';

/**
 * Notes about the cave — the rows of a tracking log that are about nobody — as a signed-in surface
 * lists and draws them.
 *
 * <b>They are kept apart from the party at the root, not filtered out of it afterwards.</b> Where
 * a person is gets read off their latest report that names a place, so a hazard folded in with the
 * reports about people would move somebody to the hazard, or — keyed by a person it does not have —
 * invent a member of the party called nobody. So every fold over people leaves these rows out
 * through {@link aboutPeople}, and everything that wants them asks {@link caveNotesAt}; no reader
 * takes both from one pass.
 *
 * <b>Signed-in surfaces only.</b> Nothing a visitor without an account is sent holds one, and a
 * page built for visitors hands the model's panel none, so nothing here is asked or drawn there.
 * The panel itself imports this module, so those pages do load it; what never does is the fold of
 * a published trip and the file a club's own site loads, which are built from modules that do not
 * import this one.
 */

/**
 * The one kind of report that is about the cave and nobody in it — loose rock above a pitch, water
 * rising in a passage. It names no people, always has words and may name one station.
 *
 * Named here and kept out of the list of what a report about <em>people</em> can say: a report is
 * never corrected from one side of that line to the other — the server refuses it — so a surface
 * offers this kind beside those only where a new report is being composed.
 */
export const TRACKING_CAVE_NOTE_KIND = 'caveNote' satisfies TripPositionEventKind;

/** One note about the cave, as it is listed beside a model and drawn on it. */
export interface CaveNoteMark {
  /** The report's own id — what its mark on the model is called after. */
  id: string;
  recordedAt: string;
  /** What was said. Never empty: a note about the cave is its words. */
  note: string;
  /**
   * The station it names, as this reader was told it. Null where it names none — "the water is
   * up" is about the cave and no one place in it — and equally where the place is kept from this
   * reader, who is sent the words without it and cannot tell the two apart.
   */
  stationName: string | null;
  /**
   * Whether that station is a name of the survey being drawn, so a mark may be asked for there. A
   * note made on another survey names a station of that survey, and is listed without a mark.
   */
  onThisModel: boolean;
}

/** The reports that are about somebody: every row but a note about the cave. */
export function aboutPeople<T extends Pick<TrackingEvent, 'caverId'>>(
  events: readonly T[],
): (T & { caverId: string })[] {
  return events.filter((event): event is T & { caverId: string } => event.caverId !== null);
}

/**
 * The notes about the cave in force at a moment, newest first.
 *
 * <b>In force means said at or before the moment, and nothing takes one back but removing it.</b>
 * A hazard does not lapse when somebody walks past it or when the next report comes in; loose rock
 * reported at ten is still loose rock at four unless somebody takes the note off the log. So a
 * replay shows the notes that had been said by the moment it stands at, and the live watch — asked
 * with no moment — shows all of them.
 *
 * A note whose time cannot be read is in force only where no moment is asked for: there is no
 * instant to compare it with, and the live list is the one place that does not need one.
 *
 * @param at the replayed moment, or null for the watch as it stands now.
 * @param modelInUse the survey being drawn, or undefined where the surface draws none.
 */
export function caveNotesAt(
  events: readonly TrackingEvent[],
  at: number | null,
  modelInUse: string | null | undefined,
): CaveNoteMark[] {
  const notes: (CaveNoteMark & { at: number })[] = [];
  for (const event of events) {
    const words = event.note ?? '';
    if (event.kind !== TRACKING_CAVE_NOTE_KIND || words.length === 0) {
      continue;
    }
    const said = Date.parse(event.recordedAt);
    if (at !== null && !(Number.isFinite(said) && said <= at)) {
      continue;
    }
    // Asked of the one rule every surface asks about a reported place: whether there is one, and
    // whether it belongs to the drawing. A note never has a depth.
    const place = placeOnModel(
      { stationName: event.stationName, depthM: null, surveyModelId: event.surveyModelId },
      modelInUse,
    );
    notes.push({
      id: event.id,
      recordedAt: event.recordedAt,
      note: words,
      stationName: event.stationName !== null && event.stationName.length > 0 ? event.stationName : null,
      onThisModel: place?.kind === 'station',
      at: Number.isFinite(said) ? said : Number.NEGATIVE_INFINITY,
    });
  }
  return notes
    .sort((left, right) => right.at - left.at || left.id.localeCompare(right.id))
    .map(({ at: _at, ...note }) => note);
}

/**
 * What the mark of a note is called on the model.
 *
 * Prefixed so that it can never be taken for a person's marker, which is called by the person's
 * id: the two sets stand on one model, are brought into line separately, and a collapsed group of
 * markers is asked who its members are by these names.
 */
export const CAVE_NOTE_MARKER_PREFIX = 'cave-note:';

export function caveNoteMarkerId(noteId: string): string {
  return CAVE_NOTE_MARKER_PREFIX + noteId;
}

/**
 * The colour of a note's mark.
 *
 * Stated rather than taken from the interface theme, for the reason the party's own colours are:
 * the scene behind a marker is the viewer's and not the page's. Amber, which neither a person
 * underground nor a person out is drawn in, so a hazard is never read as somebody standing there.
 */
export const CAVE_NOTE_MARK_COLOR = '#d48806';

/** How much of a note is drawn on its mark; the whole of it is in the list beside the model. */
const MARK_WORDS = 48;

/** A note's words cut to what a mark on the model can carry, on one line. */
export function caveNoteMarkWords(note: string): string {
  const line = note.replace(/\s+/g, ' ').trim();
  return line.length <= MARK_WORDS ? line : `${line.slice(0, MARK_WORDS - 1).trimEnd()}…`;
}

/**
 * The marks to stand on the model for these notes: one per note that names a station of the
 * drawing, each at its station and under its own name.
 *
 * <b>Never one of the party's markers and never part of a team.</b> A note with no station, one
 * whose place this reader is not told and one made on another survey have nowhere to stand here
 * and are listed only.
 *
 * @param labelOf the words a mark carries, in the reader's language.
 */
export function wantedCaveNoteMarkers(
  notes: readonly CaveNoteMark[],
  labelOf: (note: CaveNoteMark) => string,
): Map<string, DrawnMarker> {
  const wanted = new Map<string, DrawnMarker>();
  for (const note of notes) {
    if (note.stationName === null || !note.onThisModel) {
      continue;
    }
    wanted.set(caveNoteMarkerId(note.id), {
      station: note.stationName,
      label: labelOf(note),
      color: CAVE_NOTE_MARK_COLOR,
    });
  }
  return wanted;
}
