// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { TrackingEvent, TrackingState } from '../../api/hooks.ts';
import type { CaveViewLabelText } from '../loadCaveView.ts';
import { clusterLabelFor, type DrawnMarker } from '../liveMarkerSync.ts';
import { markerLine, type MarkerLineOptions } from '../markerLine.ts';
import type { TrackedCaver } from '../trackedCavers.ts';
import { noteAt, replayNotes, trackedCaversAt, trackedRoutesAt } from '../trackingReplay.ts';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import { movieCaverNames } from './movieCaverNames.ts';
import type { MovieSettings } from './movieSettings.ts';

/**
 * The party of one frame of an exported movie: which markers stand where, how each is labelled and
 * coloured, the trail behind each, the legend and the note in force.
 *
 * <b>Nothing here decides where anybody was.</b> Each trip's party at its instant is the replay
 * fold's answer — the same one the trip's own tracking panel draws — so a withheld position, a
 * report measured in another survey and a caver nobody has reported yet each produce no marker
 * here for exactly the reason they produce none there. What this adds is only what a movie of
 * several trips needs on top: marker ids that cannot collide between trips, colours that tell
 * trips or teams apart, and the reader's choice of how people are named.
 */

export interface MovieTripData {
  tripLogId: string;
  title: string;
  tracking: TrackingState;
  events: readonly TrackingEvent[];
  nameOf(caverId: string): string;
}

export interface MovieLegendEntry {
  /** The marker colour the line explains; null for a line that explains the legend itself. */
  color: string | null;
  label: string;
  /**
   * Explains a kind of marker, or the legend itself, rather than naming a trip or a team — the
   * grey of somebody who has come out, the line saying that colours repeat — so a legend with too
   * little room keeps it and cuts the named entries instead.
   */
  pinned?: boolean;
}

export interface MovieParty {
  /** Keyed by {@link movieMarkerId}. */
  markers: Map<string, DrawnMarker>;
  clusterLabel(ids: readonly string[]): CaveViewLabelText | null;
  trails: Map<string, { stations: string[]; color: string }>;
  legend: MovieLegendEntry[];
  /** The latest note in force, when the note caption is on. */
  note: string | null;
}

/**
 * Marker colours for telling trips or teams apart, the first being the colour a single party is
 * drawn in everywhere else.
 *
 * Chosen against the viewer's black scene rather than the page's theme, and kept clear of the
 * colours the viewer already draws with — red stations, yellow junctions, white entrances — and of
 * the grey a caver who has come out is drawn in. All opaque: the viewer drops an alpha channel and
 * would draw a translucent colour as solid black or white.
 *
 * <b>Twelve, and past twelve the legend says so.</b> A camp's movie runs to a dozen trips, and a
 * colour used twice makes two parties one to anybody reading the legend. More colours than this
 * cannot be told apart as small moving dots on a compressed frame, so beyond the list the colours
 * do repeat — in the same order — and {@link movieParty} adds a line to the legend saying it,
 * rather than leave a reader to find out from two parties that look like one. The four at the end
 * were added after the first eight and are the furthest from them, and from the viewer's own
 * colours, that still read on black; a colour is only ever appended, since a trip's colour is its
 * place in this list.
 */
export const MOVIE_MARKER_PALETTE: readonly string[] = [
  trackedCaverPalette.underground,
  '#ff8c1a',
  '#e052e0',
  '#9ccc3c',
  '#4f8cff',
  '#ff5e8e',
  '#b18cff',
  '#d9b77a',
  '#1f9e5a',
  '#f2a7d0',
  '#c2f0a0',
  '#7c5cff',
];

/** The colours teams are told apart by: the first of the palette is kept for people in no team. */
const TEAM_COLOURS = MOVIE_MARKER_PALETTE.length - 1;

/** The id of a caver's marker in a movie. The same person on two trips is two markers. */
export function movieMarkerId(tripLogId: string, caverId: string): string {
  return `${tripLogId}:${caverId}`;
}

/** Whether a marker of a movie stands for somebody of this trip. */
export function movieMarkerIsOfTrip(markerId: string, tripLogId: string): boolean {
  return markerId.startsWith(movieMarkerId(tripLogId, ''));
}

const NOBODY: ReadonlySet<string> = new Set();

/** A caver on the model, with what their marker is keyed and coloured by. */
interface Placed {
  id: string;
  trip: number;
  caver: TrackedCaver;
  station: string;
  color: string;
  teamless: boolean;
}

/**
 * The party of every trip at its instant, ready to be drawn.
 *
 * <b>Colours are handed out in the order the trips are given</b>, so a caller that appends a newly
 * chosen trip keeps every earlier trip's colour — and, colouring by team, every earlier team's.
 * Teams are coloured over all of each trip's teams, not only the ones on the model at this
 * instant, so a team's colour does not change as the replay plays.
 *
 * `auto` colours by trip when there are several and by team when there is one. Somebody who has
 * come out is drawn in the muted grey whatever the colouring, because that is the distinction a
 * reader must not lose; with `showOut` off they are not drawn at all.
 *
 * <b>Somebody the reader left out of the movie is not in it at all</b> (`excluded`, by
 * {@link movieMarkerId}): no marker, no trail behind one, and no caption of a note they spoke — the
 * note in force is then the latest one said by somebody who is shown. They are also left out of
 * the set that decides how much of a name tells two people apart, so nobody's label grows a
 * surname because of a person the movie does not show; a name lengthened for a reason the picture
 * gives no sign of would itself say that somebody is missing. What does <i>not</i> change is how
 * the others are coloured: trips and teams keep their colours and their lines of the legend, so a
 * movie made twice, once without somebody, shows everybody else exactly alike.
 *
 * @param instants the instant each trip is shown at, in the order of `trips` — the timeline's answer.
 */
export function movieParty(
  trips: readonly MovieTripData[],
  instants: readonly number[],
  surveyModelId: string,
  options: {
    settings: MovieSettings;
    t: TFunction;
    language: string;
    today: string;
    /** The markers the reader left out of this movie; nobody when not given. */
    excluded?: ReadonlySet<string>;
  },
): MovieParty {
  const { settings, t, language, today } = options;
  const excluded = options.excluded ?? NOBODY;
  const shown = (trip: MovieTripData, caverId: string): boolean =>
    excluded.size === 0 || !excluded.has(movieMarkerId(trip.tripLogId, caverId));
  // Whose words a caption may carry: somebody shown who is on the trip's roster. A report stays on
  // the log after its caver is taken off the trip, but such a person can have no marker and is in
  // nobody's list to untick, so the movie could quote them and offer no way to leave them out.
  const rosters = new Map(
    trips.map((trip) => [trip.tripLogId, new Set(trip.tracking.participants.map((person) => person.caverId))]),
  );
  const quoted = (trip: MovieTripData, caverId: string): boolean =>
    shown(trip, caverId) && rosters.get(trip.tripLogId)!.has(caverId);
  const colourBy =
    settings.cavers.colourBy === 'auto'
      ? trips.length > 1 ? 'trip' : 'team'
      : settings.cavers.colourBy;
  const noTeamColour = MOVIE_MARKER_PALETTE[0];

  const teamColours = new Map<string, string>();
  const teamLegend: MovieLegendEntry[] = [];
  trips.forEach((trip) => {
    for (const team of trip.tracking.teams) {
      // The first colour is kept for nobody's team, so teams start at the second.
      const color = MOVIE_MARKER_PALETTE[1 + (teamColours.size % TEAM_COLOURS)];
      teamColours.set(team.id, color);
      const title = team.title.length > 0 ? team.title : t('caveview.tracking.noTeam');
      teamLegend.push({
        color,
        label: trips.length > 1 ? t('caveview.movie.legendTripTeam', { trip: trip.title, team: title }) : title,
      });
    }
  });

  const line: MarkerLineOptions = { t, language, showTimes: settings.cavers.showTimes, today };
  const labelMode = settings.cavers.labels;
  // Everybody the movie shows of every selected trip's roster, so that whether two people's first
  // names collide is one answer for the whole movie rather than a new one at each frame.
  const names = movieCaverNames(
    trips.flatMap((trip) =>
      trip.tracking.participants
        .filter((person) => shown(trip, person.caverId))
        .map((person) => ({ caverId: person.caverId, name: trip.nameOf(person.caverId) })),
    ),
    labelMode,
  );
  const nameOf = (caverId: string, fallback: string): string => names.get(caverId) ?? fallback;
  // The out suffix and the optional time are still worded by the one spelling a marker has; only
  // the name inside it is the movie's choice.
  const lineOf = (caver: TrackedCaver): string =>
    labelMode === 'off' ? '' : markerLine({ ...caver, name: nameOf(caver.caverId, caver.name) }, line);

  const placed: Placed[] = [];
  trips.forEach((trip, index) => {
    const at = instants[index] ?? Number.NaN;
    for (const caver of trackedCaversAt(trip.tracking, trip.events, at, trip.nameOf, surveyModelId)) {
      // No marker is invented for a place nobody reported, one that was withheld, or one measured
      // in another survey: there is no station of this model to put it at.
      if (caver.position.kind !== 'station') {
        continue;
      }
      if (caver.out && !settings.cavers.showOut) {
        continue;
      }
      if (!shown(trip, caver.caverId)) {
        continue;
      }
      const teamless = caver.teamId === null || !teamColours.has(caver.teamId);
      const color = caver.out
        ? trackedCaverPalette.out
        : colourBy === 'trip'
          ? MOVIE_MARKER_PALETTE[index % MOVIE_MARKER_PALETTE.length]
          : colourBy === 'team'
            ? teamless ? noTeamColour : teamColours.get(caver.teamId!)!
            : MOVIE_MARKER_PALETTE[0];
      placed.push({
        id: movieMarkerId(trip.tripLogId, caver.caverId),
        trip: index,
        caver,
        station: caver.position.station,
        color,
        teamless,
      });
    }
  });

  const markers = new Map<string, DrawnMarker>(
    placed.map((entry) => [entry.id, { station: entry.station, label: lineOf(entry.caver), color: entry.color }]),
  );

  const clusterLabel = (ids: readonly string[]): CaveViewLabelText | null => {
    if (labelMode === 'off') {
      return null;
    }
    // In the trips' and rosters' order, never the viewer's, so the block does not re-sort itself
    // while nothing about the party changed.
    const here = new Set(ids);
    return clusterLabelFor(
      placed.filter((entry) => here.has(entry.id)).map((entry) => entry.caver),
      lineOf,
    );
  };

  const trails = new Map<string, { stations: string[]; color: string }>();
  if (settings.cavers.trails) {
    trips.forEach((trip, index) => {
      const routes = trackedRoutesAt(trip.events, instants[index] ?? Number.NaN, surveyModelId);
      for (const entry of placed) {
        if (entry.trip !== index) {
          continue;
        }
        const route = routes.get(entry.caver.caverId);
        if (route !== undefined && route.length >= 2) {
          trails.set(entry.id, { stations: route, color: entry.color });
        }
      }
    });
  }

  const legend: MovieLegendEntry[] =
    colourBy === 'trip'
      ? trips.map((trip, index) => ({
          color: MOVIE_MARKER_PALETTE[index % MOVIE_MARKER_PALETTE.length],
          label: trip.title,
        }))
      : colourBy === 'team'
        ? [
            ...teamLegend,
            ...(placed.some((entry) => !entry.caver.out && entry.teamless)
              ? [{ color: noTeamColour, label: t('caveview.tracking.noTeam') }]
              : []),
          ]
        : [];
  // Whether two trips, or two teams, are drawn in one colour is a fact about the whole movie, so
  // the line stands on every frame rather than appearing when the thirteenth party walks in.
  if (colourBy === 'trip' && trips.length > MOVIE_MARKER_PALETTE.length) {
    legend.push({ color: null, label: t('caveview.movie.legendTripColoursRepeat'), pinned: true });
  }
  if (colourBy === 'team' && teamColours.size > TEAM_COLOURS) {
    legend.push({ color: null, label: t('caveview.movie.legendTeamColoursRepeat'), pinned: true });
  }
  if (placed.some((entry) => entry.caver.out)) {
    legend.push({ color: trackedCaverPalette.out, label: t('caveview.tracking.out'), pinned: true });
  }

  return {
    markers,
    clusterLabel,
    trails,
    legend,
    note: settings.captions.note ? noteInForce(trips, instants, labelMode, nameOf, quoted, t) : null,
  };
}

/**
 * The note said most recently before each trip's instant, over all trips — "most recently" read
 * on each trip's own clock, so that in a movie playing trips side by side the note shown is the one
 * that has just been said, whichever trip said it. Ties keep the earlier trip.
 *
 * Who said it is named the way the markers name people, and not at all when the markers are not
 * labelled.
 *
 * A note spoken by somebody the movie does not show is passed over as if it had not been said: the
 * one in force is the latest said by anybody who is shown. Captioned, it would put a person's words
 * — and, with labels on, their name — into a movie they were taken out of. Somebody whose reports
 * are on the log but who is no longer on the trip's roster is such a person too: the movie cannot
 * show them, and the reader is given no box to untick them by.
 */
function noteInForce(
  trips: readonly MovieTripData[],
  instants: readonly number[],
  labelMode: MovieSettings['cavers']['labels'],
  nameOf: (caverId: string, fallback: string) => string,
  quoted: (trip: MovieTripData, caverId: string) => boolean,
  t: TFunction,
): string | null {
  let best: { age: number; text: string } | null = null;
  for (const [index, trip] of trips.entries()) {
    const at = instants[index] ?? Number.NaN;
    if (!Number.isFinite(at)) {
      continue;
    }
    const note = noteAt(
      replayNotes(trip.events, { from: Number.NEGATIVE_INFINITY, to: at }).filter((said) => quoted(trip, said.caverId)),
      at,
    );
    if (note === null) {
      continue;
    }
    const age = at - note.at;
    if (best !== null && age >= best.age) {
      continue;
    }
    const text =
      labelMode === 'off'
        ? note.note
        : t('caveview.movie.noteBy', { name: nameOf(note.caverId, trip.nameOf(note.caverId)), note: note.note });
    best = { age, text };
  }
  return best?.text ?? null;
}
