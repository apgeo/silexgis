// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink, ResLinkMember, TrackingEvent, TrackingState } from '../api/hooks.ts';
import type { CaveViewMediaEntry } from './loadCaveView.ts';
import { placeOnModel } from './drawableOn.ts';
import { pictureOf } from './stationMedia.ts';
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
 * **A report measured in another survey is not drawn on this one, and is said rather than passed
 * over.** Station names mean whatever the model they were measured in says they mean, so a trip
 * whose survey was changed part-way through has reports that name places this model cannot hold.
 * Such a report used to be skipped, which left the earlier place standing or, far more often —
 * because the reports naming the old survey are the *older* ones — left the person reading as
 * somebody nobody had reported at all. It now takes effect as a place that exists and cannot be
 * shown here, which is what it is.
 */

/**
 * A report with its instant resolved once, so the scan below never re-parses a date.
 *
 * Generic in the row, because two different logs are dated by this one function. The signed-in log
 * is a flat list of {@link TrackingEvent}; a published trip's past track is the same reports keyed
 * by a place in the party and carrying no ids at all. Only `recordedAt` is read here, and the row
 * comes back out unchanged — so a caller reading a note off it still gets a note, and the one
 * spelling of "which instant is this, and is it readable" serves both surfaces.
 */
interface DatedRow<T> {
  at: number;
  event: T;
}

/** The stretch of the trip a replay can be scrubbed over, as epoch milliseconds. */
export interface ReplayWindow {
  from: number;
  to: number;
}

/**
 * How far outside the trip's own stretch a photograph may drag the scrubber — see
 * {@link replayWindow}, which is where the reasoning lives.
 *
 * A day, because a day is the scale a camera clock is actually wrong by: the wrong hour, the wrong
 * time zone, the date rolled over at midnight. Beyond that the number is not a clock that drifted
 * but one that was never set, and treating it as part of the trip destroys the replay of everything
 * that was.
 */
const PICTURE_WIDENING_MS = 24 * 60 * 60_000;

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
  /** The instant of the report `position` came from — which is rarely the latest one. */
  positionAt: string | null;
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
function datedEvents<T extends { recordedAt: string }>(events: readonly T[]): DatedRow<T>[] {
  const dated: DatedRow<T>[] = [];
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
 * It is widened by the pictures too, and that is not tidiness. A picture carries the moment its
 * camera says it was taken, which is a clock nobody synchronised against anything underground — so
 * one routinely lands minutes or hours outside the stretch the reports cover. A window that did not
 * hold it would leave a photograph on this trip that the scrubber cannot be dragged to, and the
 * reader would have no way to tell that from a photograph that is not there.
 *
 * <b>But only so far, and the bound is what keeps one bad file from destroying the whole replay.</b>
 * A camera whose battery died reports 1970 or 2000, not "an hour out" — and the server accepts it,
 * deliberately, because refusing would throw away the one record of a photograph over a number the
 * person attaching it can fix afterwards. Widened without a bound, one such file makes the window
 * decades long: the handle then moves in steps of weeks, every real report collapses onto one end
 * of the rail, and nothing on screen says why the trip can no longer be replayed at all. So a
 * picture drags the window by at most {@link PICTURE_WIDENING_MS}, measured from the stretch the
 * reports themselves cover, and one further out than that is left off the rail rather than allowed
 * to size it. Nothing is lost by that: the trip's own list of photographs shows every one of them
 * with the moment it claims, which is where a wrong clock is actually visible as a wrong clock.
 *
 * @param openedAt the moment the replay was opened, which is where a live trip's window ends. Taken
 *   from the caller rather than read here so the end of the window does not creep forward under the
 *   handle somebody is dragging.
 * @param pictures the pictures hung on this trip's moments, or nothing where none have been read.
 */
export function replayWindow(
  tracking: Pick<TrackingState, 'armedAt' | 'closedAt'>,
  events: readonly { recordedAt: string }[],
  openedAt: number,
  pictures: readonly ReplayPicture[] = [],
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
  // Measured against the reports' own stretch and fixed before the loop, so that one picture a day
  // early cannot become the anchor a second one is allowed a day beyond — which is how a bound
  // applied to a moving window ends up bounding nothing.
  const earliest = from - PICTURE_WIDENING_MS;
  const latest = to + PICTURE_WIDENING_MS;
  for (const picture of pictures) {
    if (!Number.isFinite(picture.at)) {
      continue;
    }
    if (picture.at >= earliest) {
      from = Math.min(from, picture.at);
    }
    if (picture.at <= latest) {
      to = Math.max(to, picture.at);
    }
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
 * <b>Pictures are not here, and are not a property of a report.</b> They hang on the trip at an
 * instant rather than on any report row — see <see cref="replayPictures"/> — so they are derived
 * from the trip's links and not from this log, and they survive a report being deleted and
 * re-recorded, which is what a correction is.
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

// ---- pictures on the trip's moments -------------------------------------------------------

/**
 * A photograph belonging to one moment of the trip, placed on the replay's clock.
 *
 * `caverId` is who the moment is about, and null where it is about the party. That difference
 * decides whether the picture is ever drawn on the model: see {@link replayPictures}.
 */
export interface ReplayPicture {
  at: number;
  caverId: string | null;
  documentId: string;
  /**
   * The membership row this picture is hung by — what a detach names.
   *
   * The membership and not the document: the same photograph can hang on two moments of one trip,
   * and taking it off the 14:05 one has to leave the 15:40 one alone.
   */
  memberId: string;
  entry: CaveViewMediaEntry;
}

/**
 * How many pictures one moment is handed over with — the same bound, for the same reason, as a
 * station's strip: every thumbnail in a strip is a request, and this is read on a phone on a
 * hillside.
 */
const MAX_PER_MOMENT = 12;

/**
 * The instant a member anchors its link to on this trip, or null when it anchors to something else.
 *
 * <b>The moment has to be what the link is about — its main member — and that is the same rule the
 * write and the detach hold to.</b> Anyone who may read two things may relate them through the
 * general link route and choose which of them is the subject, so a link can mention a moment of
 * this trip while being about a document: it is curated by that document's writers, this trip's
 * write path will not extend it and its detach route will not touch it. Reading it here as a
 * picture of this trip's moment would put a photograph on the trip's own strip with a control
 * beside it that is refused — three surfaces disagreeing about what a picture on a moment is. Such
 * a link is still on the trip's links panel, which is where an association somebody authored by
 * hand belongs.
 */
function momentOf(member: ResLinkMember, tripLogId: string): number | null {
  if (
    member.targetType !== 'tripLog'
    || member.targetId !== tripLogId
    || member.anchorKind !== 'tripMoment'
    || !member.isMain
  ) {
    return null;
  }
  // The payload is withheld from a reader who may not read the target and arrives as null; a
  // picture placed at a guessed moment is the failure this shape exists to avoid.
  const anchor = (typeof member.anchor === 'object' && member.anchor !== null ? member.anchor : {}) as Record<
    string,
    unknown
  >;
  const at = anchor.at;
  if (typeof at !== 'string') {
    return null;
  }
  const parsed = Date.parse(at);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * The photographs hung on this trip's moments, oldest first, read out of the links the trip has.
 *
 * <b>There is no picture-per-report table, and none is invented here — nor could there be one.</b>
 * The log is append-only and a correction is a deletion followed by a fresh report with a new id,
 * so an attachment keyed to a report would be destroyed by somebody fixing a typo in a time. What
 * is stored instead is a link relating the trip *at an instant* to the photograph, which no
 * correction touches; where the picture is drawn is folded out of the corrected log at read time by
 * the very code that draws the party.
 *
 * <b>A picture with a caver hangs at that caver's position; one with none is timeline-only and is
 * never placed.</b> A party that has split is in two places at once, so "the party's station" is
 * not a thing a derivation may invent — it would put a photograph somewhere nobody was, drawn with
 * exactly the confidence of one that is right.
 *
 * A link holding several moments and several pictures puts all of its pictures on all of its
 * moments, for the reason the station strip gives: what a link says is that these things belong
 * together, and it names no pairing inside itself for this to read one out of.
 */
export function replayPictures(
  links: readonly ResLink[],
  tripLogId: string,
): ReplayPicture[] {
  const pictures: ReplayPicture[] = [];
  const seen = new Set<string>();

  for (const link of links) {
    const moments: number[] = [];
    let subject: string | null = null;
    let subjects = 0;
    const entries: { documentId: string; memberId: string; entry: CaveViewMediaEntry }[] = [];

    for (const member of link.members) {
      const at = momentOf(member, tripLogId);
      if (at !== null) {
        moments.push(at);
        continue;
      }
      if (member.targetType === 'caver') {
        // Exactly one, or none: a link naming two people says the moment is about both, and
        // drawing the picture at one of their positions would be a choice nothing authorised.
        subjects += 1;
        subject = member.targetId;
        continue;
      }
      const entry = pictureOf(member);
      if (entry !== null) {
        entries.push({ documentId: member.targetId, memberId: member.id, entry });
      }
    }

    const caverId = subjects === 1 ? subject : null;
    for (const at of moments) {
      let placed = pictures.filter((picture) => picture.at === at).length;
      for (const { documentId, memberId, entry } of entries) {
        if (placed >= MAX_PER_MOMENT) {
          break;
        }
        // The same photograph can reach one moment through two links — somebody attached it and
        // somebody else attached it again — and the strip would then show it twice.
        const identity = `${at} ${documentId}`;
        if (seen.has(identity)) {
          continue;
        }
        seen.add(identity);
        pictures.push({ at, caverId, documentId, memberId, entry });
        placed += 1;
      }
    }
  }

  return pictures.sort((left, right) => left.at - right.at);
}

/**
 * The pictures in force at a moment: those of the latest moment at or before it, or none yet.
 *
 * Shaped exactly like {@link noteAt}, and for the same reason. A scrubber stops at a thousand
 * places across a window that is hours long, so it never lands on an instant a camera recorded;
 * "in force" is what makes a picture reachable at all, and it is the same reading the note beside
 * it already has.
 */
export function picturesAt(pictures: readonly ReplayPicture[], at: number): ReplayPicture[] {
  let moment: number | null = null;
  for (const picture of pictures) {
    if (picture.at > at) {
      break;
    }
    moment = picture.at;
  }
  return moment === null ? [] : pictures.filter((picture) => picture.at === moment);
}

/** The moment before this one that carries pictures, for a reader stepping back. Strictly before. */
export function picturesBefore(pictures: readonly ReplayPicture[], at: number): ReplayPicture | null {
  let found: ReplayPicture | null = null;
  for (const picture of pictures) {
    if (picture.at >= at) {
      break;
    }
    found = picture;
  }
  return found;
}

/** The next moment carrying pictures, for a reader stepping forward. Strictly after. */
export function picturesAfter(pictures: readonly ReplayPicture[], at: number): ReplayPicture | null {
  return pictures.find((picture) => picture.at > at) ?? null;
}

/**
 * Where the pictures in force at a moment are drawn on the model, by the station they hang under.
 *
 * <b>Each one is placed where its subject was when it was taken, never where they are at the
 * instant on the scrubber, and the difference is the whole correctness of this function.</b> A
 * picture stays in force until the next one — it has to, or a scrubber that stops at a thousand
 * places across a day would never land on a camera's instant and no photograph would ever be
 * reachable — so at 15:30 the picture in force can easily be the one taken at 14:05. Folding the
 * log at 15:30 to place it draws a photograph of the upper series at whatever station its subject
 * has since been reported at, following them down the cave as the replay plays, with exactly the
 * confidence of a picture that is in the right place. That is the one failure this whole surface
 * says it refuses: putting a photograph somewhere nobody was.
 *
 * So the log is folded at the picture's own moment. Once per distinct moment rather than once per
 * picture — a memory card lands several on one instant — and only for the moments actually in
 * force, so scrubbing costs one fold and not one per photograph on the trip.
 *
 * A picture about nobody in particular is never placed: a party that has split is in two places at
 * once, and "the party's station" is not something a derivation may invent. A picture whose subject
 * had no position then — nobody had reported them yet, or the report's place was withheld from this
 * reader, or it was measured in another survey — is not placed either, and for the same reason:
 * there is no station under which it could be drawn that would be true.
 *
 * @param events the <b>whole</b> log, which is what the positions are folded out of.
 * @param at the instant being replayed, as epoch milliseconds.
 * @param surveyModelId the model being drawn, or undefined when the panel does not know it — in
 *   which case nothing is placed at all, exactly as the party's own markers are not.
 */
export function placedPicturesAt(
  pictures: readonly ReplayPicture[],
  events: readonly TrackingEvent[],
  at: number,
  surveyModelId: string | undefined,
): Map<string, CaveViewMediaEntry[]> {
  const byStation = new Map<string, CaveViewMediaEntry[]>();
  if (surveyModelId === undefined || !Number.isFinite(at)) {
    return byStation;
  }

  const folds = new Map<number, Map<string, TrackedCaverPosition>>();
  for (const picture of picturesAt(pictures, at)) {
    if (picture.caverId === null) {
      continue;
    }
    let fold = folds.get(picture.at);
    if (fold === undefined) {
      fold = positionsAt(events, picture.at, surveyModelId);
      folds.set(picture.at, fold);
    }
    const position = fold.get(picture.caverId);
    if (position === undefined || position.kind !== 'station') {
      continue;
    }
    byStation.set(position.station, [...(byStation.get(position.station) ?? []), picture.entry]);
  }
  return byStation;
}

/**
 * Where everybody who had been placed by a moment stood at it, as the same folded positions the
 * party's own markers are drawn from.
 *
 * Only the people a report had actually placed by then are in it — somebody nobody had reported
 * yet, or whose reports this reader is not being told the places of, is simply absent, and a caller
 * reading an absence must say nothing rather than guess.
 */
function positionsAt(
  events: readonly TrackingEvent[],
  at: number,
  surveyModelId: string,
): Map<string, TrackedCaverPosition> {
  const positions = new Map<string, TrackedCaverPosition>();
  for (const [caverId, history] of historiesAt(events, at, surveyModelId)) {
    if (history.position !== null) {
      positions.set(caverId, history.position);
    }
  }
  return positions;
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
  // A panel that does not know its own model can compare no report against it, and an instant that
  // is not a number names no moment. Which survey the *watch* currently points at is deliberately
  // not asked here: every report carries the survey it was made in, the scan below tests each one,
  // and a watch re-pointed mid-trip is precisely the case where those two answers differ.
  if (surveyModelId === undefined || !Number.isFinite(at)) {
    return [];
  }

  const teamTitles = new Map(tracking.teams.map((team) => [team.id, team.title]));
  const histories = historiesAt(events, at, surveyModelId);

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
      teamId,
      teamTitle: teamId === null ? null : (teamTitles.get(teamId) ?? null),
      position: history?.position ?? unplaced(history !== undefined, tracking.positionsWithheld),
      lastRecordedAt: history?.lastRecordedAt ?? null,
      positionAt: history?.positionAt ?? null,
      enteredAt: history?.enteredAt ?? null,
      out: history?.out ?? false,
    };
  });
}

/**
 * What every caver's reports up to a moment add up to, keyed by caver.
 *
 * <b>One fold, two readers.</b> The party's markers are drawn from it and so is the station a
 * photograph of a moment hangs under, and those two must be the same arithmetic: a picture placed
 * by a second reading of the log would sooner or later disagree with the marker standing beside it
 * about where somebody was, and neither surface would say which of them was lying.
 *
 * Nobody the log has not mentioned by this moment appears here at all — what to say about them is
 * a question about the watch's roster rather than about the reports, and it is answered by the
 * caller that has the roster.
 */
function historiesAt(
  events: readonly TrackingEvent[],
  at: number,
  surveyModelId: string,
): Map<string, CaverHistory> {
  const histories = new Map<string, CaverHistory>();
  for (const { at: when, event } of datedEvents(events)) {
    if (when > at) {
      break;
    }
    const history = histories.get(event.caverId) ?? {
      position: null,
      positionAt: null,
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
      // The replay reads the very report that placed somebody, so unlike the folded watch it always
      // knows how old a position is — which is what anything comparing two people's positions needs.
      history.positionAt = event.recordedAt;
    }
    histories.set(event.caverId, history);
  }
  return histories;
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
  // Whether this report claims a place, and whether that place belongs to the model being drawn,
  // are one question with one home — the same one the live watch asks. It used to be written out
  // here, and the copy read a report whose model id is absent as a withholding and drew its
  // station anyway. That shape is not a withholding: deleting a survey nulls the model on every
  // row that named it while leaving the station name standing, so what the copy drew was an old
  // survey's station placed confidently on the model that replaced it.
  const place = placeOnModel(
    {
      stationName: event.stationName,
      depthM: event.depthEnteredM,
      surveyModelId: event.surveyModelId,
    },
    surveyModelId,
  );
  if (place !== null) {
    return place;
  }
  // A report of this kind always carries a place. Arriving without one — no station, no depth —
  // it was kept from this reader, and there is no second reading of it.
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
