// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingEvent, TrackingState } from '../api/hooks.ts';
import { drawableOn } from './drawableOn.ts';
import type { CaveViewer, CaveViewTrail } from './loadCaveView.ts';
import type { TrackedCaver } from './trackedCavers.ts';

/**
 * A position that is a stretch: somebody reported between two stations of one survey.
 *
 * <b>Everything a stretch does at run time is here, and only signed-in surfaces call it.</b> Word
 * from underground is often "past the second pitch, not yet at the sump", and a report may say
 * exactly that: a first station, where every surface that draws one mark draws it, and a far end.
 * The far end is told to those who are signed in and to nobody else — a published page and the
 * file a club's own site loads are built from folds that never see it. Keeping the whole of the
 * stretch's arithmetic in a module those folds do not import is what makes that a property of the
 * build rather than of anybody's care: the party fold next door gained one optional member of a
 * type and no line of code.
 *
 * So this works by <em>adding to</em> a party that has already been folded. Where each person
 * stands, which report placed them and whether that place belongs to the drawing are all decided
 * before anything here runs; what is asked here is only whether the report that placed somebody at
 * a station also named a far end.
 */

/** The far end as a report carries it: absent, null and an empty name all say "at one station". */
function farEnd(toStationName: string | null | undefined): string | null {
  return toStationName !== null && toStationName !== undefined && toStationName.length > 0
    ? toStationName
    : null;
}

/** One person with the far end of their stretch added, or the very object where there is none. */
function withFarEnd(caver: TrackedCaver, toStation: string | null): TrackedCaver {
  if (caver.position.kind !== 'station' || toStation === null || toStation === caver.position.station) {
    return caver;
  }
  return { ...caver, position: { ...caver.position, toStation } };
}

/** The same list where nobody changed, so that a poll which moved nothing re-renders nothing. */
function mapped(
  cavers: readonly TrackedCaver[],
  farEndOf: (caver: TrackedCaver) => string | null,
): readonly TrackedCaver[] {
  let changed = false;
  const next = cavers.map((caver) => {
    const stretched = withFarEnd(caver, farEndOf(caver));
    changed ||= stretched !== caver;
    return stretched;
  });
  return changed ? next : cavers;
}

/**
 * The party of the live watch, each person standing on a stretch given its far end.
 *
 * The watch folds one position per person and carries the far end beside the first station, so
 * this is a lookup: a person the fold placed at a station of the drawing on screen, whose folded
 * position names that same station on that same survey, takes the far end the watch carries.
 * Anybody else — placed by depth, on another survey, withheld, unreported — is handed back as they
 * were. A withheld position arrives with both ends absent, so nothing here can say a far end the
 * first station was kept from.
 */
export function withStretches(
  cavers: readonly TrackedCaver[],
  tracking: Pick<TrackingState, 'participants'>,
  surveyModelId: string | undefined,
): readonly TrackedCaver[] {
  if (surveyModelId === undefined) {
    return cavers;
  }
  const participants = new Map(
    tracking.participants.map((participant) => [participant.caverId, participant]),
  );
  return mapped(cavers, (caver) => {
    const participant = participants.get(caver.caverId);
    if (
      participant === undefined
      || caver.position.kind !== 'station'
      || participant.stationName !== caver.position.station
      || !drawableOn(participant.positionSurveyModelId, surveyModelId)
    ) {
      return null;
    }
    return farEnd(participant.toStationName);
  });
}

/**
 * The party as it stood at a replayed moment, each person standing on a stretch given its far end.
 *
 * <b>The report that placed somebody is looked up, not worked out a second time.</b> The replay
 * fold says where each person stood and dates that position with the moment of the very report it
 * read it from; this finds that report — the same person, the same moment, the same first station,
 * measured on the survey being drawn — and reads the far end off it. Nothing here decides which
 * report is the latest or what a report claims: a second reading of the log would sooner or later
 * disagree with the marker it is drawn from.
 *
 * <b>Where two such reports disagree, no far end is said.</b> Two reports about one person at one
 * instant naming the same first station are a double entry. If they name different far ends, or
 * only one names any, the log does not say which stretch was meant, and the station both agree on
 * is what is drawn.
 */
export function withStretchesAt(
  cavers: readonly TrackedCaver[],
  events: readonly TrackingEvent[],
  surveyModelId: string | undefined,
): readonly TrackedCaver[] {
  if (surveyModelId === undefined) {
    return cavers;
  }
  const placed = new Map<string, { station: string; at: string }>();
  for (const caver of cavers) {
    if (caver.position.kind === 'station' && caver.positionAt !== null) {
      placed.set(caver.caverId, { station: caver.position.station, at: caver.positionAt });
    }
  }
  if (placed.size === 0) {
    return cavers;
  }
  // Undefined is "no placing report met yet"; null is "met, and it names no far end" or "met
  // twice, and the two disagree".
  const farEnds = new Map<string, string | null>();
  for (const event of events) {
    const place = placed.get(event.caverId);
    if (
      place === undefined
      || event.kind !== 'atStation'
      || event.recordedAt !== place.at
      || event.stationName !== place.station
      || !drawableOn(event.surveyModelId, surveyModelId)
    ) {
      continue;
    }
    const said = farEnd(event.toStationName);
    const before = farEnds.get(event.caverId);
    farEnds.set(event.caverId, before === undefined || before === said ? said : null);
  }
  return mapped(cavers, (caver) => farEnds.get(caver.caverId) ?? null);
}

/**
 * Whether any report of these logs says somebody was between two stations.
 *
 * For a surface that draws each person at one station and nothing more — an exported movie — so
 * that it can say so exactly where it applies, instead of on every movie ever made.
 */
export function logsNameAStretch(logs: readonly (readonly TrackingEvent[])[]): boolean {
  return logs.some((events) => events.some((event) => farEnd(event.toStationName) !== null));
}

/**
 * What names one stretch: its two stations in the order the report gave them.
 *
 * Written as a pair rather than joined with a separator, because a station name may hold any
 * character a survey file allows and a joined string could be read back as a different pair.
 */
export function stretchKey(from: string, to: string): string {
  return JSON.stringify([from, to]);
}

/** What every stretch line's id starts with on the viewer, so they are told from other trails. */
export const STRETCH_TRAIL_PREFIX = 'tracked-stretch:';

/** One stretch line as it was last handed to the viewer. */
export interface DrawnStretch {
  from: string;
  to: string;
  color: string;
}

/**
 * The stretch lines a party asks for: one per distinct stretch, whoever and however many stand on
 * it.
 *
 * <b>Keyed by the stretch and not by the person</b>, because a team reported between two stations
 * is one line on the model, and four lines drawn over one another are a thicker line saying
 * nothing more. The line is drawn in the colour of somebody still underground wherever anybody on
 * it is; only a stretch everybody has come out from is drawn muted.
 *
 * A person whose first station the drawing is known not to hold asks for no line: their marker is
 * drawn nowhere, and a line from nowhere to the far end would mark a place nobody reported.
 */
export function wantedStretches(
  cavers: readonly TrackedCaver[],
  colors: { underground: string; out: string },
  unplacedStations: ReadonlySet<string>,
): Map<string, DrawnStretch> {
  const wanted = new Map<string, DrawnStretch>();
  for (const caver of cavers) {
    const position = caver.position;
    if (
      position.kind !== 'station'
      || position.toStation === undefined
      || unplacedStations.has(position.station)
    ) {
      continue;
    }
    const key = stretchKey(position.station, position.toStation);
    const before = wanted.get(key);
    if (before === undefined) {
      wanted.set(key, {
        from: position.station,
        to: position.toStation,
        color: caver.out ? colors.out : colors.underground,
      });
    } else if (!caver.out) {
      before.color = colors.underground;
    }
  }
  return wanted;
}

/**
 * Adds, recolours and removes stretch lines so that what the viewer holds is `wanted`, touching
 * only the lines that differ from `drawn`.
 *
 * <b>A line is removed when the position changes, and that needs no rule of its own.</b> A person
 * reported somewhere else no longer asks for the line, so it is no longer wanted and is taken off
 * here; one who moves on to another stretch asks for a different line under a different key.
 *
 * Drawn dashed, and from the first station to the far one as the survey's own legs go: it says
 * "somewhere along here", and a solid line would read as a route somebody walked — which is what
 * an exported movie draws, in the same colours.
 *
 * @returns what is now drawn, to be handed back as `drawn` on the next pass.
 */
export function syncStretchTrails(
  viewer: Pick<CaveViewer, 'addTrail' | 'updateTrail' | 'removeTrail'>,
  drawn: ReadonlyMap<string, DrawnStretch>,
  wanted: ReadonlyMap<string, DrawnStretch>,
): Map<string, DrawnStretch> {
  for (const [key, stretch] of wanted) {
    const before = drawn.get(key);
    if (before === undefined) {
      viewer.addTrail(STRETCH_TRAIL_PREFIX + key, [stretch.from, stretch.to], {
        color: stretch.color,
        style: 'dashed',
      });
    } else if (before.color !== stretch.color) {
      // Null leaves the route alone: the two stations of a key never change.
      viewer.updateTrail(STRETCH_TRAIL_PREFIX + key, null, { color: stretch.color });
    }
  }
  for (const key of drawn.keys()) {
    if (!wanted.has(key)) {
      viewer.removeTrail(STRETCH_TRAIL_PREFIX + key);
    }
  }
  return new Map(wanted);
}

/**
 * Why a stretch has no line on the drawing.
 *
 * `toNotOnModel`: the drawing holds no station of the far end's name. `unroutable`: it holds both
 * stations and no way along the survey from one to the other, which is what two parts of a cave
 * surveyed apart and never tied together look like.
 */
export type StretchFault = 'toNotOnModel' | 'unroutable';

/** The answer for a panel with no model loaded: no stretch is claimed to be undrawable on one. */
export const noStretchFaults: ReadonlyMap<string, StretchFault> = new Map<string, StretchFault>();

/**
 * Which stretches the drawing on screen turned out unable to show: everything already known, plus
 * what the viewer has just reported of the stretch lines it holds.
 *
 * <b>Asked of the viewer, never inferred</b> — whether a survey holds a station and whether its
 * legs join two of them are facts about a parsed file, and only the viewer has parsed it. It keeps
 * a line it cannot place rather than refusing it, and describes it back as unresolved, or as
 * resolved with a gap where it found no way through. Either would otherwise be a marker standing
 * at the first station with nothing beside it, on a surface whose list says "between A and B": a
 * reader would take the missing line for a report at one station.
 *
 * <b>Learned and kept, like the stations a drawing does not hold</b>, and for the same reason: a
 * line taken off the model takes its evidence with it, and what was learned stays true for as long
 * as the same survey is loaded. Whoever loads another starts this again from nothing.
 *
 * <b>A first station the drawing does not hold is not recorded here.</b> That is already said
 * about the person — their marker is drawn nowhere and the list says so — and saying it a second
 * time as a fault of the stretch would put two warnings on one fact.
 *
 * Answers the very map it was given when there is nothing new.
 */
export function stretchFaults(
  known: ReadonlyMap<string, StretchFault>,
  trails: readonly Pick<CaveViewTrail, 'id' | 'resolved' | 'points' | 'gaps'>[],
): ReadonlyMap<string, StretchFault> {
  let learned: Map<string, StretchFault> | null = null;
  for (const trail of trails) {
    if (!trail.id.startsWith(STRETCH_TRAIL_PREFIX)) {
      continue;
    }
    const key = trail.id.slice(STRETCH_TRAIL_PREFIX.length);
    if (known.has(key)) {
      continue;
    }
    let fault: StretchFault | null = null;
    if (!trail.resolved) {
      // Only where the first station is held: see above.
      const [first, far] = trail.points;
      fault = first?.resolved === true && far?.resolved === false ? 'toNotOnModel' : null;
    } else if (trail.gaps.length > 0) {
      fault = 'unroutable';
    }
    if (fault !== null) {
      learned ??= new Map(known);
      learned.set(key, fault);
    }
  }
  return learned ?? known;
}

/** What is known to be wrong with the line of the stretch this person stands on, or null. */
export function stretchFaultOf(
  faults: ReadonlyMap<string, StretchFault>,
  caver: TrackedCaver,
): StretchFault | null {
  const position = caver.position;
  if (position.kind !== 'station' || position.toStation === undefined) {
    return null;
  }
  return faults.get(stretchKey(position.station, position.toStation)) ?? null;
}
