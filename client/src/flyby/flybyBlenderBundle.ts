// SPDX-License-Identifier: AGPL-3.0-or-later
import type {
  FlybyBundleCave,
  FlybyBundleLook,
  FlybyBundleOmission,
  FlybyBundleRequest,
  FlybyBundleTexts,
} from '../api/hooks/flybyBundle.ts';
import { flybyBundleLookTracks, type FlybyBundleChanges, type FlybyBundleEasing } from './flybyBundleChanges.ts';
import type { FlybyLayerKey } from './flybyDocument.ts';
import { flybyLookAt, flybyReliefAt, FLYBY_OWN_LENS, type FlybyLookBase, type FlybyLookPlan } from './flybyLook.ts';
import type { FlybyFrameSchedule } from './flybyMovie.ts';
import type { FlybyTextPlan } from './flybyTexts.ts';
import { flybyCameraAt, flybyKnotAt, type FlybyTimeline } from './flybyTimeline.ts';

// A flyby handed over as a render bundle: what the browser has to say about it.
//
// A bundle is a zip the server writes — a scene description, the caves' meshes and a player — which
// its reader unpacks and renders with Blender on a machine of their own. The server has the caves;
// what only this browser has is the flight. The camera's path was flown over the ground this scene
// had loaded, about subjects found in this view, and its texts are in this reader's language. So
// everything here is the flyby resolved to plain numbers and words: the camera at every frame of
// the movie, the look at its first frame and what changes of it afterwards, the texts on the
// movie's own clock, which caves are in it, and — said in the same breath — what of the flyby a
// bundle does not carry.
//
// Nothing here knows the scene or the server. The camera is sampled through the very function the
// player and the movie recorder place it with, so frame `i` of a bundle is the camera frame `i` of
// a movie had.

export type { FlybyBundleCave, FlybyBundleLook, FlybyBundleOmission, FlybyBundleRequest, FlybyBundleTexts };

/**
 * The frame rate a bundle is made at. A render bundle cannot be played at a rate with a fraction —
 * the movie panel's twelve and a half is one — so a bundle has one rate, whatever the panel says.
 */
export const FLYBY_BUNDLE_FPS = 25;

/** The most frames one bundle holds; the server refuses more. */
export const FLYBY_BUNDLE_MAX_FRAMES = 20_000;

/** The most caves one bundle names; the server refuses more. */
export const FLYBY_BUNDLE_MAX_CAVES = 100;

/** The oldest Blender a bundle renders in, as its own notes say. */
export const FLYBY_BUNDLE_MIN_BLENDER = '4.2';

/** What is left out, in the order it is listed. */
export const FLYBY_BUNDLE_OMISSIONS: readonly FlybyBundleOmission[] = [
  'ground',
  'surfaceOpening',
  'entrances',
  'surfaceFeatures',
  'scans',
  'photos',
  'mapLayers',
  'tracks',
  'wallsNotDownloaded',
  'moreCavesInView',
];

function rounded(value: number, decimals: number): number {
  const scale = 10 ** decimals;
  return Math.round(value * scale) / scale;
}

/**
 * The lens at moment `t`, in degrees across the wider side of the picture: the flyby's own where it
 * names one, else the viewer's.
 */
export function flybyBundleLensAt(plan: FlybyLookPlan, base: FlybyLookBase, t: number): number {
  return flybyLookAt(plan, base, t).lens ?? base.lens ?? FLYBY_OWN_LENS;
}

/**
 * The camera at every frame of the movie, eight numbers a frame: longitude, latitude and height in
 * metres as the path has them — true height, whatever the relief is drawn at —, the heading, tilt
 * and roll in degrees, how far away the point looked at is, and the lens.
 *
 * `groundHeight` is the scene's own, asked after the route was loaded: it is what holds the camera
 * above the ground, and whoever receives these numbers has no ground to ask.
 */
export function flybyBundleFrames(
  timeline: FlybyTimeline,
  schedule: FlybyFrameSchedule,
  groundHeight: (longitude: number, latitude: number) => number,
  lensAt: (t: number) => number,
): number[][] {
  const frames: number[][] = [];
  for (let index = 0; index < schedule.count; index += 1) {
    const t = schedule.at(index);
    const camera = flybyCameraAt(timeline, t, groundHeight);
    frames.push([
      rounded(camera.longitude, 10),
      rounded(camera.latitude, 10),
      rounded(camera.height, 4),
      rounded(camera.heading, 7),
      rounded(camera.pitch, 7),
      rounded(camera.roll, 7),
      // The distance the path keeps to what it looks at; where the eye was lifted clear of the
      // ground the line of sight is a little longer, and nothing sharper than this is asked of it.
      rounded(Math.max(flybyKnotAt(timeline, t).distance, 0.01), 3),
      rounded(lensAt(t), 5),
    ]);
  }
  return frames;
}

/** How one layer stands at a moment: the flyby's word where it has one, else the viewer's. */
function layerShown(
  state: ReturnType<typeof flybyLookAt>,
  base: FlybyLookBase,
  key: FlybyLayerKey,
): { visible: boolean; opacity: number } {
  return state.layers[key] ?? base.layers[key] ?? { visible: true, opacity: 1 };
}

/**
 * The moments of a clip at which what is shown can differ from the moment before: its first, and
 * wherever a change of a layer or of the ground's cut begins inside it. A layer that is switched on
 * is drawn from the very moment its change begins.
 */
function showingMoments(plan: FlybyLookPlan, schedule: Pick<FlybyFrameSchedule, 'from' | 'to'>): number[] {
  return [
    schedule.from,
    ...plan.changes
      .filter((timed) => (timed.change.kind === 'layer' || timed.change.kind === 'surface') && timed.start > schedule.from && timed.start < schedule.to)
      .map((timed) => timed.start),
  ];
}

/** Which of the two things a bundle draws of a cave the flyby shows at some moment of the clip. */
export interface FlybyBundleShows {
  walls: boolean;
  lines: boolean;
}

export function flybyBundleShows(plan: FlybyLookPlan, base: FlybyLookBase, schedule: Pick<FlybyFrameSchedule, 'from' | 'to'>): FlybyBundleShows {
  const states = showingMoments(plan, schedule).map((t) => flybyLookAt(plan, base, t));
  return {
    walls: states.some((state) => layerShown(state, base, 'walls').visible),
    lines: states.some((state) => layerShown(state, base, 'centerlines').visible),
  };
}

/**
 * What the flyby shows, at any moment from the clip's first frame to its last, that a bundle does
 * not carry.
 *
 * What it changes while it plays is not here: the relief, the colours of walls and lines, the
 * walls and the lines shown, hidden and faded, and the sky all go into the bundle, change by
 * change. What is left is what a bundle has no place for at all — the ground first of all, and
 * with it every map drawn on the ground, so a background that changes is not said again.
 *
 * `shown` are the layers the viewer has switched on besides the ones the flyby names — the flyby
 * leaves those as it finds them, so they are in the picture and not in the bundle.
 */
export function flybyBundleOmissions(
  plan: FlybyLookPlan,
  base: FlybyLookBase,
  schedule: Pick<FlybyFrameSchedule, 'from' | 'to'>,
  shown: readonly FlybyLayerKey[] = [],
): FlybyBundleOmission[] {
  // Always: a bundle holds caves and no ground.
  const found = new Set<FlybyBundleOmission>(['ground']);

  for (const t of showingMoments(plan, schedule)) {
    const state = flybyLookAt(plan, base, t);
    if ((state.surface ?? base.surface) === 'cutaway') {
      found.add('surfaceOpening');
    }

    const keys = new Set<FlybyLayerKey>([...Object.keys(base.layers), ...Object.keys(state.layers), ...shown]);
    for (const key of keys) {
      if (!layerShown(state, base, key).visible) {
        continue;
      }
      const kind = key.includes(':') ? key.slice(0, key.indexOf(':')) : key;
      switch (kind) {
        case 'entrances':
          found.add('entrances');
          break;
        case 'surface-features':
        case 'filter':
        case 'features':
          found.add('surfaceFeatures');
          break;
        case 'scans':
          found.add('scans');
          break;
        case 'photos':
          found.add('photos');
          break;
        case 'overlay':
        case 'raster':
          found.add('mapLayers');
          break;
        case 'file':
          found.add('tracks');
          break;
        default:
          break;
      }
    }
  }
  return FLYBY_BUNDLE_OMISSIONS.filter((omission) => found.has(omission));
}

/**
 * The mark saying the relief is stretched, over a clip: one line for each run of frames drawn at
 * the same factor to the tenth — the tenth a person chooses it in — and none while it is true.
 */
export function flybyBundleReliefMarks(
  schedule: Pick<FlybyFrameSchedule, 'count' | 'fps'>,
  reliefAt: (frame: number) => number,
  words: (factor: number) => string,
): { text: string; startS: number; endS: number }[] {
  const marks: { text: string; startS: number; endS: number }[] = [];
  let from = 0;
  let factor = Math.round(reliefAt(0) * 10) / 10;
  for (let frame = 1; frame <= schedule.count; frame += 1) {
    const next = frame < schedule.count ? Math.round(reliefAt(frame) * 10) / 10 : Number.NaN;
    if (next !== factor) {
      if (factor !== 1) {
        marks.push({ text: words(factor), startS: rounded(from / schedule.fps, 4), endS: rounded(frame / schedule.fps, 4) });
      }
      from = frame;
      factor = next;
    }
  }
  return marks;
}

/**
 * The flyby's texts on the clip's own clock: seconds from the clip's first frame, for the stretch
 * of the flyby the clip is of, with the frames held at its end counted in its length.
 *
 * `relief` are the marks saying the relief is stretched, already in the reader's words and on the
 * clip's clock, for the stretches of it drawn other than true.
 */
export function flybyBundleTexts(
  texts: FlybyTextPlan,
  schedule: FlybyFrameSchedule,
  relief: readonly { text: string; startS: number; endS: number }[] = [],
): FlybyBundleTexts {
  const { from, to } = schedule;
  const clip = schedule.count / schedule.fps;
  const flown = schedule.moving / schedule.fps;
  const seconds = (value: number) => rounded(value, 4);

  const title =
    texts.title && texts.title.end > from
      ? { text: texts.title.text, subtitle: texts.title.subtitle ?? null, seconds: seconds(Math.min(texts.title.end - from, clip)) }
      : null;
  // The end card comes at its moment and stays to the last frame, held frames and all.
  const end =
    texts.end && to > texts.end.start
      ? {
          text: texts.end.text,
          subtitle: texts.end.subtitle ?? null,
          seconds: seconds(clip - Math.max(0, texts.end.start - from)),
        }
      : null;
  const captions = texts.captions
    .map((caption) => ({
      text: caption.text,
      startS: seconds(Math.max(0, caption.start - from)),
      endS: seconds(Math.min(flown, caption.end - from)),
    }))
    .filter((caption) => caption.endS > caption.startS);

  return {
    title,
    end,
    captions,
    corner: texts.corner ?? null,
    cornerCaptions: [...relief],
    // The part of each fade the clip holds: a clip that starts after the picture has risen out of
    // black starts lit, and one that stops short of the flyby's end does not sink into it.
    fadeInS: seconds(Math.min(Math.max(0, texts.fadeIn - from), clip)),
    fadeOutS: to >= texts.length - 1e-6 ? seconds(Math.min(texts.fadeOut, clip)) : 0,
    northArrow: texts.northArrow,
  };
}

/** What a bundle is asked for with, beside the flyby itself. */
export interface FlybyBundleAsk {
  name: string;
  timeline: FlybyTimeline;
  schedule: FlybyFrameSchedule;
  size: { width: number; height: number };
  quality: 'low' | 'medium' | 'high';
  groundHeight(longitude: number, latitude: number): number;
  /** What the viewer has, for the members the flyby leaves alone. */
  base: FlybyLookBase;
  /** The layers the viewer has on besides those the flyby names. */
  shown?: readonly FlybyLayerKey[];
  /** Whether the view lets a flyby stretch the relief; where it does not, the viewer's own is drawn throughout. */
  flybyRelief: boolean;
  /** How the scene turns a surveyed altitude into a drawn height. */
  placement: { absolute: boolean; offsetM: number };
  /** The flyby's own caves, in the order it visits them. */
  routeCaves: readonly string[];
  /** Other caves the view is drawing, nearest first. */
  cavesInView: readonly string[];
  /** Of the flyby's own caves, those whose walls the viewer has not downloaded and does not want in the bundle. */
  wallsDeclined?: readonly string[];
  /** The surveyed altitude the scene hangs a cave from, when it has been told one. */
  surveyTop(caveId: string): number | undefined;
  /** The height of the ground at a cave in the scene, for lines that carry no altitudes. */
  groundAt(caveId: string): number | undefined;
  /** The colours survey lines are drawn in by depth, shallowest first. */
  depthBands: readonly { fromMeters: number; color: string }[];
  /** The mark saying the relief is stretched, in the reader's words, for a factor rounded to a tenth. */
  reliefWords(factor: number): string;
}

/** Why a bundle cannot be made of a flyby as it stands; null when it can. */
export type FlybyBundleRefusal =
  | { kind: 'tooLong'; frames: number; max: number }
  /** The flyby is about no cave: a bundle holds caves and nothing else. */
  | { kind: 'noCaves' }
  /** Walls and survey lines are both hidden throughout the clip, so no cave would be drawn. */
  | { kind: 'nothingShown' }
  | null;

/**
 * The request a bundle is asked for with, what it leaves out, and whether it can be asked for at
 * all.
 */
export function flybyBundleRequest(ask: FlybyBundleAsk): {
  request: FlybyBundleRequest;
  omissions: FlybyBundleOmission[];
  refusal: FlybyBundleRefusal;
} {
  const { timeline, schedule, base } = ask;
  const omissions = new Set(flybyBundleOmissions(timeline.look, base, schedule, ask.shown));

  const own = [...new Set(ask.routeCaves)];
  const declined = new Set(ask.wallsDeclined ?? []);
  // What the flyby does to the look over the clip, as keys on the clip's frames — and from the
  // same keys, what the clip opens with and whether walls and lines are ever shown in it.
  const tooLong = schedule.count > FLYBY_BUNDLE_MAX_FRAMES;
  const tracks = tooLong
    ? // Nothing is worked out frame by frame for a clip no bundle will be made of.
      { ...flybyBundleShows(timeline.look, base, schedule), reliefAt: () => openingRelief(ask), sky: undefined, lineColour: undefined, changes: null }
    : flybyBundleLookTracks(timeline.look, base, schedule, [...own, ...ask.cavesInView], ask.flybyRelief);
  const relief = tracks.reliefAt(0);

  // The flyby's own caves first; others in view only where lines are drawn at all, since lines
  // are all a bundle draws of them.
  const others = tracks.lines ? ask.cavesInView.filter((caveId) => !own.includes(caveId)) : [];
  const listed = [...own, ...others];
  if (listed.length > FLYBY_BUNDLE_MAX_CAVES) {
    omissions.add('moreCavesInView');
  }
  const caves: FlybyBundleCave[] = listed.slice(0, FLYBY_BUNDLE_MAX_CAVES).map((caveId) => {
    const isOwn = own.includes(caveId);
    if (isOwn && tracks.walls && declined.has(caveId)) {
      omissions.add('wallsNotDownloaded');
    }
    return {
      caveId,
      walls: isOwn && tracks.walls && !declined.has(caveId),
      surveyTopM: ask.surveyTop(caveId) ?? null,
      groundM: ask.groundAt(caveId) ?? null,
    };
  });
  const named = new Set(caves.map((cave) => cave.caveId));

  const look: FlybyBundleLook = {
    lines: tracks.lines,
    sky: tracks.sky ?? null,
    lineColour: tracks.lineColour ?? null,
    depthBands: ask.depthBands.map((band) => ({ fromM: band.fromMeters, colour: band.color })),
  };

  const refusal: FlybyBundleRefusal = tooLong
    ? { kind: 'tooLong', frames: schedule.count, max: FLYBY_BUNDLE_MAX_FRAMES }
    : own.length === 0
      ? { kind: 'noCaves' }
      : !tracks.walls && !tracks.lines
        ? { kind: 'nothingShown' }
        : null;

  const leftOut = FLYBY_BUNDLE_OMISSIONS.filter((omission) => omissions.has(omission));
  const changes = tracks.changes ? changesRequest(tracks.changes, named) : null;
  return {
    request: {
      name: ask.name,
      output: { width: ask.size.width, height: ask.size.height, fps: schedule.fps, quality: ask.quality },
      origin: {
        longitude: timeline.origin.lon,
        latitude: timeline.origin.lat,
        heightM: timeline.origin.height,
      },
      placement: { absolute: ask.placement.absolute, offsetM: ask.placement.offsetM },
      relief,
      frames: tooLong
        ? []
        : flybyBundleFrames(timeline, schedule, ask.groundHeight, (t) => flybyBundleLensAt(timeline.look, base, t)),
      caves,
      look,
      texts: flybyBundleTexts(
        timeline.texts,
        schedule,
        timeline.texts.reliefMark && !tooLong ? flybyBundleReliefMarks(schedule, tracks.reliefAt, ask.reliefWords) : [],
      ),
      leftOut,
      ...(changes ? { changes } : {}),
    },
    omissions: leftOut,
    refusal,
  };
}

/** The relief a clip opens at: the flyby's where the view lets a flyby stretch it, else the viewer's own. */
function openingRelief(ask: FlybyBundleAsk): number {
  return ask.flybyRelief ? flybyReliefAt(ask.timeline.look, ask.base.relief, ask.schedule.from) : (ask.base.relief ?? 1);
}

type Eased<T> = T & { easing: 'linear' | FlybyBundleEasing | null };

/** The changes as the request writes them: every member said, the ones that do not change as null. */
function changesRequest(changes: FlybyBundleChanges, named: ReadonlySet<string>): NonNullable<FlybyBundleRequest['changes']> {
  const eased = <T extends { easing?: FlybyBundleEasing }>(keys: readonly T[] | undefined): Eased<T>[] | null =>
    keys ? keys.map((key) => ({ ...key, easing: key.easing ?? null })) : null;
  const layer = (said: FlybyBundleChanges['walls']) =>
    said ? { shown: said.shown ?? null, opacity: eased(said.opacity), tint: eased(said.tint) } : null;
  const caves = (changes.caves ?? [])
    .filter((cave) => named.has(cave.caveId))
    .map((cave) => ({ caveId: cave.caveId, walls: eased(cave.walls), lines: eased(cave.lines) }));
  return {
    relief: eased(changes.relief),
    sky: eased(changes.sky),
    walls: layer(changes.walls),
    lines: layer(changes.lines),
    caves: caves.length > 0 ? caves : null,
  };
}
