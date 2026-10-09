// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Everything an exported movie of a survey model and its tracked trips is made with, what it
 * starts as, and the repair of whatever comes back from storage.
 *
 * <b>The defaults lean private, because a file outlives the check that produced it.</b> The movie
 * is a copy of what the reader could see on screen, and it will be passed on. So the viewer's
 * heads-up display (which prints altitudes and a compass bearing), the grid and the note caption
 * (free text that can carry safety details) all start off, and the reader turns them on knowingly.
 */

import type { CaveViewLayerGetter } from '../loadCaveView.ts';
import { GIF_FRAME_RATES, type MovieFormat, type MovieQuality } from './encode/movieFormats.ts';
import { movieShadingId, movieShadingRememberedAs, type MovieShadingId } from './movieShadings.ts';

// The format and quality are the encoders' words, and a GIF's frame rates are a fact about the
// GIF format; each has its one home beside the encoders, and the settings speak them.
export type { MovieFormat, MovieQuality } from './encode/movieFormats.ts';

/**
 * How a caver's marker names them. A caver record holds one full name, so `first` is its first
 * word, lengthened only where two people in the movie would otherwise read the same.
 */
export type MovieCaverLabels = 'first' | 'full' | 'initials' | 'off';
export type MovieColourBy = 'auto' | 'trip' | 'team' | 'single';
export type MovieTimelineMode = 'calendar' | 'together';

/** The viewer's own layers a movie can switch, named as the viewer's properties are. */
export type MovieViewLayer =
  | 'legs'
  | 'stations'
  | 'stationLabels'
  | 'stationComments'
  | 'entrances'
  | 'entrance_dots'
  | 'splays'
  | 'walls'
  | 'scraps'
  | 'duplicateLegs'
  | 'surfaceLegs'
  | 'traces'
  | 'warnings'
  | 'fog'
  | 'HUD'
  | 'box'
  | 'grid';

/**
 * How the camera looks at the model when the movie starts: the viewer's own five views, named as
 * the viewer names them. `plan` looks down from above; `north` is the viewer's north elevation,
 * whose camera stands level to the south of the model and faces north — and so on round. That is
 * the reverse of a surveyor's "elevation seen from the north", which is why the reader is told the
 * way the camera faces rather than an elevation's name. With rotation on, the camera orbits from
 * there, so an elevation turns the model about its vertical axis seen from the side.
 */
export type MovieViewDirection = 'plan' | 'north' | 'south' | 'east' | 'west';

/** How the photographs hung on a trip's moments are shown in its movie: not at all, over the whole frame, or in a corner of it. */
export type MoviePictureMode = 'off' | 'full' | 'corner';
export type MoviePictureCorner = 'bottomRight' | 'bottomLeft' | 'topRight' | 'topLeft';

export const MOVIE_PICTURE_MODES: readonly MoviePictureMode[] = ['off', 'corner', 'full'];
export const MOVIE_PICTURE_CORNERS: readonly MoviePictureCorner[] = ['bottomRight', 'bottomLeft', 'topRight', 'topLeft'];
/** The seconds of a movie one photograph may be given. */
export const MOVIE_PICTURE_SECONDS_RANGE = { min: 0.5, max: 15 } as const;

/** Every view a movie can start from, in the order they are offered. */
export const MOVIE_VIEW_DIRECTIONS: readonly MovieViewDirection[] = ['plan', 'north', 'south', 'east', 'west'];

export interface MovieSettings {
  format: MovieFormat;
  /** One of {@link MOVIE_SIZES}' ids, e.g. '1280x720'. */
  size: string;
  fps: number;
  /** Length of the replay part, in seconds of the movie. */
  durationS: number;
  /** Still frames at the end, in seconds. */
  holdEndS: number;
  quality: MovieQuality;
  rotation: {
    enabled: boolean;
    mode: 'speed' | 'fullTurn';
    degreesPerSecond: number;
    clockwise: boolean;
  };
  timeline: { mode: MovieTimelineMode; shortenQuiet: boolean; quietGapMin: number };
  cavers: {
    labels: MovieCaverLabels;
    /** Pixels of the output frame. */
    labelSize: number;
    labelPlate: boolean;
    showTimes: boolean;
    colourBy: MovieColourBy;
    showOut: boolean;
    /** How long a marker takes to slide to its next station, in seconds of the movie. */
    transitionS: number;
    trails: boolean;
  };
  view: Record<MovieViewLayer, boolean> & {
    /**
     * Whether the surface over the cave is drawn, where the survey's file carries one. Off unless
     * the reader turns it on: a cave drawn under its hills is a cave somebody can place, which a
     * drawing of its passages alone is not — the same reason the compass and the scale start off.
     */
    terrain: boolean;
    /**
     * The view the preview turns to when the model loads and when this changes. The movie starts
     * from what the preview shows, so turning or zooming the preview afterwards is kept.
     */
    direction: MovieViewDirection;
    /**
     * The shading the model is drawn in, by name, or null for the viewer's own. A name rather than
     * the viewer's number for it, which nothing promises is the same in the next viewer.
     */
    shading: MovieShadingId | null;
    camera: 'perspective' | 'orthographic';
    /** The viewer's line-width slider, 0..1. */
    linewidth: number;
    /** The viewer's vertical-scale slider, 0..1. */
    zScale: number;
  };
  captions: {
    title: boolean;
    /** '' means the title is composed from the trips. */
    titleText: string;
    clock: boolean;
    /**
     * Whether the clock says how many times faster than life the movie runs. Part of the clock's
     * caption, so it is drawn only while the clock is.
     */
    speed: boolean;
    legend: boolean;
    progress: boolean;
    note: boolean;
    /** A multiplier on the caption sizes, which otherwise follow the frame height. */
    size: number;
  };
  /**
   * The photographs hung on the trips' moments.
   *
   * Not shown until the reader asks, for the reason the notes and the altitudes are not: the file
   * goes on to people nobody checked, and a photograph of the party says more about them than a
   * first name on a marker. `seconds` is of the movie, not of the trip — see
   * {@link moviePictureSchedule}.
   */
  pictures: {
    mode: MoviePictureMode;
    /** How long each photograph is on screen, in seconds of the movie. */
    seconds: number;
    /** Whether a photograph comes up and goes down gradually. */
    fade: boolean;
    /** Which corner, in the corner mode. Bottom right is the one no caption stands in. */
    corner: MoviePictureCorner;
    /** Whether a photograph's own caption is written along its bottom edge. */
    captions: boolean;
  };
}

/**
 * The frame sizes offered. `gif` says whether a GIF may be made at it: past 800 pixels wide a GIF
 * of a turning model grows into tens of megabytes, which no messaging surface it is meant for takes.
 */
export const MOVIE_SIZES: readonly { id: string; width: number; height: number; gif: boolean }[] = [
  { id: '320x180', width: 320, height: 180, gif: true },
  { id: '480x270', width: 480, height: 270, gif: true },
  { id: '640x360', width: 640, height: 360, gif: true },
  { id: '800x450', width: 800, height: 450, gif: true },
  { id: '1280x720', width: 1280, height: 720, gif: false },
  { id: '1920x1080', width: 1920, height: 1080, gif: false },
  { id: '320x240', width: 320, height: 240, gif: true },
  { id: '640x480', width: 640, height: 480, gif: true },
  { id: '800x600', width: 800, height: 600, gif: true },
  { id: '1024x768', width: 1024, height: 768, gif: false },
];

/** Frame rates offered for the video formats. */
export const MOVIE_VIDEO_FRAME_RATES: readonly number[] = [10, 12.5, 15, 20, 24, 25, 30];
/** The most frames a GIF is made of, still frames at the end included. */
export const MOVIE_GIF_MAX_FRAMES = 600;
/** The widest GIF offered, in pixels. */
export const MOVIE_GIF_MAX_WIDTH = 800;

/**
 * The layers a movie may switch, each with the viewer's getter saying whether the loaded model has
 * that layer at all — null for the ones every model has.
 */
export const MOVIE_VIEW_LAYERS: readonly { key: MovieViewLayer; has: CaveViewLayerGetter | null }[] = [
  { key: 'legs', has: 'hasLegs' },
  { key: 'stations', has: 'hasStations' },
  { key: 'stationLabels', has: 'hasStationLabels' },
  { key: 'stationComments', has: 'hasStationComments' },
  { key: 'entrances', has: 'hasEntrances' },
  { key: 'entrance_dots', has: 'hasEntrance_dots' },
  { key: 'splays', has: 'hasSplays' },
  { key: 'walls', has: 'hasWalls' },
  { key: 'scraps', has: 'hasScraps' },
  { key: 'duplicateLegs', has: 'hasDuplicateLegs' },
  { key: 'surfaceLegs', has: 'hasSurfaceLegs' },
  { key: 'traces', has: 'hasTraces' },
  { key: 'warnings', has: 'hasWarnings' },
  { key: 'fog', has: null },
  { key: 'HUD', has: null },
  { key: 'box', has: 'hasBox' },
  { key: 'grid', has: 'hasGrid' },
];

/**
 * The size a caver's label may be drawn at, in pixels of the frame. The viewer's glyph atlas is
 * built for text up to 45 pixels and refuses anything larger, leaving the labels as they were.
 */
export const MOVIE_LABEL_SIZE_RANGE = { min: 8, max: 45 } as const;

export const DEFAULT_MOVIE_SETTINGS: MovieSettings = {
  format: 'gif',
  size: '640x360',
  fps: 10,
  durationS: 20,
  holdEndS: 2,
  quality: 'medium',
  rotation: { enabled: true, mode: 'speed', degreesPerSecond: 6, clockwise: true },
  timeline: { mode: 'calendar', shortenQuiet: true, quietGapMin: 30 },
  cavers: {
    labels: 'first',
    labelSize: 16,
    labelPlate: true,
    showTimes: false,
    colourBy: 'auto',
    showOut: true,
    transitionS: 1,
    trails: false,
  },
  view: {
    legs: true,
    stations: false,
    stationLabels: false,
    stationComments: false,
    entrances: true,
    entrance_dots: true,
    splays: false,
    walls: true,
    scraps: true,
    duplicateLegs: false,
    surfaceLegs: true,
    traces: false,
    warnings: false,
    fog: false,
    HUD: false,
    box: false,
    grid: false,
    terrain: false,
    direction: 'north',
    shading: null,
    camera: 'perspective',
    linewidth: 0,
    zScale: 0.5,
  },
  captions: {
    title: true,
    titleText: '',
    clock: true,
    speed: true,
    legend: true,
    progress: true,
    note: false,
    size: 1,
  },
  pictures: { mode: 'off', seconds: 2, fade: true, corner: 'bottomRight', captions: true },
};

// ---- repair ----------------------------------------------------------------------------------

type Loose = Record<string, unknown>;

const record = (value: unknown): Loose =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Loose) : {};

/** The key the shading was remembered under while it was remembered as the viewer's number. */
const SHADING_NUMBER_KEY = 'shadingMode';

/**
 * Whether remembered settings were written in a shape no longer written, so that whoever read them
 * writes them back repaired: once that is done, the old shape is gone from this browser and is not
 * translated again on every opening.
 */
export function movieSettingsNeedRewriting(raw: unknown): boolean {
  return Object.hasOwn(record(record(raw).view), SHADING_NUMBER_KEY);
}

const oneOf = <T extends string>(value: unknown, allowed: readonly T[], fallback: T): T =>
  typeof value === 'string' && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;

const flag = (value: unknown, fallback: boolean): boolean =>
  typeof value === 'boolean' ? value : fallback;

function number(value: unknown, fallback: number, min: number, max: number): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, value));
}

/** The allowed rate nearest to the one asked for, the lower one on a tie. */
function nearestRate(fps: number, allowed: readonly number[]): number {
  let best = allowed[0];
  for (const rate of allowed) {
    if (Math.abs(rate - fps) < Math.abs(best - fps)) {
      best = rate;
    }
  }
  return best;
}

/**
 * Settings that can be used as they are, from anything: a whole saved object, one saved by an
 * older version with groups missing, one with a value nothing offers any more, or nothing at all.
 *
 * Each field falls back to its default on its own, so one bad value never costs the reader the
 * rest of what they chose. Numbers are clamped into what the controls offer, and the limits of a
 * GIF are then applied: its frame rates, its widest size, and its frame count — which is met by
 * shortening the replay part rather than the still frames at the end, since those are what the
 * reader looks at when the movie stops.
 */
export function normaliseMovieSettings(raw: unknown): MovieSettings {
  const d = DEFAULT_MOVIE_SETTINGS;
  const top = record(raw);
  const rotation = record(top.rotation);
  const timeline = record(top.timeline);
  const cavers = record(top.cavers);
  const view = record(top.view);
  const captions = record(top.captions);
  const pictures = record(top.pictures);

  const format = oneOf<MovieFormat>(top.format, ['gif', 'webm', 'mp4'], d.format);
  const known = MOVIE_SIZES.find((size) => size.id === top.size);
  let size = known ?? MOVIE_SIZES.find((entry) => entry.id === d.size)!;
  if (format === 'gif' && !size.gif) {
    // The largest GIF size of the same shape, so the framing the reader chose survives.
    const shape = size.width / size.height;
    size =
      [...MOVIE_SIZES]
        .filter((entry) => entry.gif && Math.abs(entry.width / entry.height - shape) < 0.01)
        .sort((left, right) => right.width - left.width)[0]
      ?? MOVIE_SIZES.find((entry) => entry.id === d.size)!;
  }

  const rates = format === 'gif' ? GIF_FRAME_RATES : MOVIE_VIDEO_FRAME_RATES;
  const fps = nearestRate(number(top.fps, d.fps, 1, 60), rates);
  const holdEndS = number(top.holdEndS, d.holdEndS, 0, 10);
  let durationS = number(top.durationS, d.durationS, 1, 300);
  if (format === 'gif') {
    const replayFrames = MOVIE_GIF_MAX_FRAMES - Math.round(holdEndS * fps);
    if (Math.round(durationS * fps) > replayFrames) {
      durationS = replayFrames / fps;
    }
  }

  const layers = Object.fromEntries(
    MOVIE_VIEW_LAYERS.map(({ key }) => [key, flag(view[key], d.view[key])]),
  ) as Record<MovieViewLayer, boolean>;
  // Settings written before the shading was remembered by name hold the viewer's number for it,
  // under another key; that number is read as the shading it stood for. A number is never taken
  // for a name, and a name nothing offers is the viewer's own shading.
  const shading =
    view.shading === undefined ? movieShadingRememberedAs(view[SHADING_NUMBER_KEY]) : movieShadingId(view.shading);

  return {
    format,
    size: size.id,
    fps,
    durationS,
    holdEndS,
    quality: oneOf<MovieQuality>(top.quality, ['low', 'medium', 'high'], d.quality),
    rotation: {
      enabled: flag(rotation.enabled, d.rotation.enabled),
      mode: oneOf(rotation.mode, ['speed', 'fullTurn'] as const, d.rotation.mode),
      degreesPerSecond: number(rotation.degreesPerSecond, d.rotation.degreesPerSecond, 0.5, 90),
      clockwise: flag(rotation.clockwise, d.rotation.clockwise),
    },
    timeline: {
      mode: oneOf<MovieTimelineMode>(timeline.mode, ['calendar', 'together'], d.timeline.mode),
      shortenQuiet: flag(timeline.shortenQuiet, d.timeline.shortenQuiet),
      quietGapMin: number(timeline.quietGapMin, d.timeline.quietGapMin, 1, 24 * 60),
    },
    cavers: {
      labels: oneOf<MovieCaverLabels>(cavers.labels, ['first', 'full', 'initials', 'off'], d.cavers.labels),
      labelSize: Math.round(
        number(cavers.labelSize, d.cavers.labelSize, MOVIE_LABEL_SIZE_RANGE.min, MOVIE_LABEL_SIZE_RANGE.max),
      ),
      labelPlate: flag(cavers.labelPlate, d.cavers.labelPlate),
      showTimes: flag(cavers.showTimes, d.cavers.showTimes),
      colourBy: oneOf<MovieColourBy>(cavers.colourBy, ['auto', 'trip', 'team', 'single'], d.cavers.colourBy),
      showOut: flag(cavers.showOut, d.cavers.showOut),
      transitionS: number(cavers.transitionS, d.cavers.transitionS, 0, 10),
      trails: flag(cavers.trails, d.cavers.trails),
    },
    view: {
      ...layers,
      terrain: flag(view.terrain, d.view.terrain),
      direction: oneOf<MovieViewDirection>(view.direction, MOVIE_VIEW_DIRECTIONS, d.view.direction),
      shading,
      camera: oneOf(view.camera, ['perspective', 'orthographic'] as const, d.view.camera),
      linewidth: number(view.linewidth, d.view.linewidth, 0, 1),
      zScale: number(view.zScale, d.view.zScale, 0, 1),
    },
    captions: {
      title: flag(captions.title, d.captions.title),
      titleText: typeof captions.titleText === 'string' ? captions.titleText.slice(0, 200) : d.captions.titleText,
      clock: flag(captions.clock, d.captions.clock),
      speed: flag(captions.speed, d.captions.speed),
      legend: flag(captions.legend, d.captions.legend),
      progress: flag(captions.progress, d.captions.progress),
      note: flag(captions.note, d.captions.note),
      size: number(captions.size, d.captions.size, 0.5, 2),
    },
    pictures: {
      mode: oneOf<MoviePictureMode>(pictures.mode, MOVIE_PICTURE_MODES, d.pictures.mode),
      seconds: number(
        pictures.seconds,
        d.pictures.seconds,
        MOVIE_PICTURE_SECONDS_RANGE.min,
        MOVIE_PICTURE_SECONDS_RANGE.max,
      ),
      fade: flag(pictures.fade, d.pictures.fade),
      corner: oneOf<MoviePictureCorner>(pictures.corner, MOVIE_PICTURE_CORNERS, d.pictures.corner),
      captions: flag(pictures.captions, d.pictures.captions),
    },
  };
}

/** The frame size a settings object names, which {@link normaliseMovieSettings} guarantees exists. */
export function movieSize(settings: MovieSettings): { width: number; height: number } {
  const size = MOVIE_SIZES.find((entry) => entry.id === settings.size)
    ?? MOVIE_SIZES.find((entry) => entry.id === DEFAULT_MOVIE_SETTINGS.size)!;
  return { width: size.width, height: size.height };
}
