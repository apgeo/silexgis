// SPDX-License-Identifier: AGPL-3.0-or-later
import { eyeLookingAt } from '../scene3d/camera3d.ts';
import type { FlybyDocument } from './flybyDocument.ts';
import { expandFlyby, filterSubjects, type FlybyExpanded } from './flybyExpand.ts';
import { placeFlybyMoment, type FlybyStage } from './flybyPlayer.ts';
import { surveyRoute } from './flybyRoutes.ts';
import { resolveSubject, subjectsTogether, type FlybyResolvedSubject, type FlybyWorld } from './flybySubjects.ts';
import { compileFlyby, type FlybySayWords, type FlybyTimeline } from './flybyTimeline.ts';

// Getting a flyby ready to be played: finding its subjects, and loading its route.
//
// Two things stand between a document and a smooth flight, and both are waits.
//
// A cave nothing is drawn of is known only roughly — a point and a length — and a camera circling
// that guess would circle the wrong ball. So the camera is sent to look at each such cave first,
// the scene loads its survey, and the cave is found again from what is now drawn.
//
// And a scene shows ground and caves as they arrive. Flown cold, a flyby is a camera overtaking its
// own map tiles. So the route is visited once beforehand, a few moments along every section, and
// each is waited on: what the live flight then needs is already in memory.

/** What preparing needs of the scene, beyond what playing does. */
export interface FlybyPrepareStage extends Pick<FlybyStage, 'setCamera' | 'refreshData' | 'groundHeight'> {
  /**
   * Resolves true once the scene has finished drawing what its camera now shows and loading the
   * data for it, or false when `timeoutMs` passes first.
   */
  settle(timeoutMs: number): Promise<boolean>;
}

export interface FlybyPrepareProgress {
  /** Finding the subjects, then loading the route. */
  step: 'subjects' | 'route';
  done: number;
  total: number;
}

export interface FlybyPrepareOptions {
  /** Whether the route is visited beforehand so the flight finds its tiles and caves loaded. */
  loadRoute: boolean;
  /** The words for what a section says about its subject; left out, its name alone. */
  say?: FlybySayWords;
  signal?: AbortSignal;
  onProgress?(progress: FlybyPrepareProgress): void;
}

export interface FlybyPrepared {
  timeline: FlybyTimeline;
  /** The document as played: every saved filter's caves written out as subjects of their own. */
  expanded: FlybyExpanded;
  /** The caves each subject drawn from a saved filter stood for, by the subject's position. */
  filterCaves: Map<number, string[]>;
  /** Found for the written-out document's subjects. */
  resolved: (FlybyResolvedSubject | undefined)[];
  /** Moments of the route that had not finished loading when their wait ran out. */
  unsettled: number;
}

/** How long one cave is waited on to load before the rough guess at it is kept. */
const SUBJECT_SETTLE_MILLISECONDS = 12_000;
/** How long one moment of the route is waited on; the flight goes ahead without what is still missing. */
const ROUTE_SETTLE_MILLISECONDS = 8_000;
/** The route is visited at least this often along a section, in seconds of flight. */
const ROUTE_STEP_SECONDS = 4;
/** And at no more than this many moments altogether, however long the flyby is. */
const ROUTE_MAX_STOPS = 40;

function cancelled(): DOMException {
  return new DOMException('The flyby was cancelled while being prepared.', 'AbortError');
}

/** A subject drawn from a saved filter stands for no cave this reader may see — or the filter cannot be run for them. */
export class FlybyFilterEmpty extends Error {
  readonly subject: number;

  constructor(subject: number) {
    super(`flyby: the saved filter of subject ${subject} selects no cave`);
    this.name = 'FlybyFilterEmpty';
    this.subject = subject;
  }
}

/**
 * Asks the server which caves each of the document's saved filters keeps for this reader.
 * Rejects with {@link FlybyFilterEmpty} for the first that keeps none.
 */
export async function flybyFilterCaves(document: FlybyDocument, world: FlybyWorld): Promise<Map<number, string[]>> {
  const caves = new Map<number, string[]>();
  for (const index of filterSubjects(document)) {
    const subject = document.subjects[index];
    const found = subject.kind === 'filter' ? await world.filterCaves?.(subject.savedFilterId, subject.limit) : undefined;
    if (!found || found.length === 0) {
      throw new FlybyFilterEmpty(index);
    }
    caves.set(index, found);
  }
  return caves;
}

/** A subject drawn from a filter, found: all of its caves together, once they are found. */
export function resolveFilterGroups(
  expanded: Pick<FlybyExpanded, 'groups'>,
  resolved: (FlybyResolvedSubject | undefined)[],
): void {
  for (const [index, members] of expanded.groups) {
    resolved[index] = subjectsTogether(
      members.flatMap((member) => (resolved[member] ? [resolved[member]!] : [])),
    );
  }
}

/** Whether preparing ended because it was cancelled, rather than because something failed. */
export function isFlybyAbort(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError';
}

/** The moments of a path worth loading beforehand: every section's ends, and steps along the long ones. */
export function routeStops(timeline: FlybyTimeline): number[] {
  const stops = new Set<number>([0]);
  for (const span of timeline.spans) {
    const pieces = Math.max(1, Math.ceil((span.end - span.start) / ROUTE_STEP_SECONDS));
    for (let piece = 1; piece <= pieces; piece += 1) {
      stops.add(span.start + ((span.end - span.start) * piece) / pieces);
    }
  }
  const sorted = [...stops].sort((a, b) => a - b);
  if (sorted.length <= ROUTE_MAX_STOPS) {
    return sorted;
  }
  // Thinned evenly, keeping the first and the last.
  return Array.from({ length: ROUTE_MAX_STOPS }, (_, index) =>
    sorted[Math.round((index * (sorted.length - 1)) / (ROUTE_MAX_STOPS - 1))],
  );
}

/**
 * Finds the document's subjects and builds its path, visiting what has to be visited on the way.
 *
 * Rejects with an `AbortError` when cancelled, with `FlybyFilterEmpty` when a saved filter it is
 * about keeps no cave for this reader, and with `FlybySubjectMissing` when a subject the flyby flies
 * around is not in this reader's world. The camera is left wherever the last visit put
 * it: whoever asked is about to play the flyby, or to put the camera back themselves.
 */
export async function prepareFlyby(
  authored: FlybyDocument,
  world: FlybyWorld,
  stage: FlybyPrepareStage,
  options: FlybyPrepareOptions,
): Promise<FlybyPrepared> {
  const check = () => {
    if (options.signal?.aborted) {
      throw cancelled();
    }
  };

  // Which caves its saved filters keep, asked first: everything after works on the document with
  // those caves written out, and never on a list of them kept anywhere else.
  const filterCaves = await flybyFilterCaves(authored, world);
  check();
  const expanded = expandFlyby(authored, filterCaves);
  const document = expanded.document;

  // Everything is found once before the camera moves at all: "what the view shows" has to mean the
  // view the person was looking at, not wherever the first visit left the camera.
  const resolved: (FlybyResolvedSubject | undefined)[] = [];
  for (const subject of document.subjects) {
    check();
    resolved.push(await resolveSubject(subject, world));
  }

  const rough = resolved.flatMap((subject, index) => (subject?.coarse ? [index] : []));
  let visited = 0;
  options.onProgress?.({ step: 'subjects', done: 0, total: rough.length });
  for (const index of rough) {
    check();
    const guess = resolved[index]!;
    const distance = Math.max(guess.radius * 3, 600);
    const eye = eyeLookingAt(guess.centre, 0, -50, distance);
    stage.setCamera({ longitude: eye.lon, latitude: eye.lat, height: eye.height, heading: 0, pitch: -50, roll: 0 });
    stage.refreshData();
    await stage.settle(SUBJECT_SETTLE_MILLISECONDS);
    check();
    // Found again from what is drawn now; a cave that still draws nothing keeps its rough guess.
    resolved[index] = (await resolveSubject(document.subjects[index], world)) ?? guess;
    visited += 1;
    options.onProgress?.({ step: 'subjects', done: visited, total: rough.length });
  }

  // The way down each cave a section follows the survey of. Read from what the scene draws with the
  // camera close over the cave, which is when the scene draws all of its survey; a way that cannot
  // be found leaves the section to dive instead.
  const descended = [
    ...new Set(
      document.sections.flatMap((section) =>
        section.kind === 'descent' && typeof section.subject === 'number' ? [section.subject] : [],
      ),
    ),
  ];
  for (const index of descended) {
    const subject = document.subjects[index];
    const found = resolved[index];
    if (subject?.kind !== 'cave' || !found || !world.caveLines) {
      continue;
    }
    check();
    const eye = eyeLookingAt(found.centre, 0, -60, Math.max(found.radius * 2.5, 300));
    stage.setCamera({ longitude: eye.lon, latitude: eye.lat, height: eye.height, heading: 0, pitch: -60, roll: 0 });
    stage.refreshData();
    await stage.settle(SUBJECT_SETTLE_MILLISECONDS);
    check();
    const route = surveyRoute(world.caveLines(subject.caveId));
    if (route) {
      resolved[index] = { ...found, route };
    }
  }

  // What the server says of each cave a section names, read now and kept only for this playing.
  const named = [
    ...new Set(
      document.sections.flatMap((section) => (section.say && typeof section.subject === 'number' ? [section.subject] : [])),
    ),
  ];
  for (const index of named) {
    const subject = document.subjects[index];
    const found = resolved[index];
    if (subject?.kind === 'cave' && found) {
      check();
      const facts = await world.caveFacts(subject.caveId);
      if (facts) {
        resolved[index] = { ...found, about: { name: facts.name, lengthM: facts.lengthM, depthM: facts.depthM } };
      }
    }
  }

  resolveFilterGroups(expanded, resolved);
  const timeline = compileFlyby(document, resolved, options.say);
  let unsettled = 0;
  if (options.loadRoute) {
    const stops = routeStops(timeline);
    options.onProgress?.({ step: 'route', done: 0, total: stops.length });
    for (let index = 0; index < stops.length; index += 1) {
      check();
      placeFlybyMoment(timeline, stage, stops[index]);
      stage.refreshData();
      if (!(await stage.settle(ROUTE_SETTLE_MILLISECONDS))) {
        unsettled += 1;
      }
      options.onProgress?.({ step: 'route', done: index + 1, total: stops.length });
    }
  }
  check();
  return { timeline, expanded, filterCaves, resolved, unsettled };
}

// ---- "has the scene finished" ----

/** What the wait below asks of a scene; a test supplies a plain object. */
export interface FlybySettleScene {
  /** True when every tile and model the present view needs has arrived. */
  isSettled(): boolean;
  /** True while the scene's own data — caves, lines, walls, files in view — is still being read. */
  dataLoading(): boolean;
  isDestroyed(): boolean;
  requestRender(): void;
  /** Resolves when the browser has drawn another frame. */
  nextFrame(): Promise<void>;
  /** Milliseconds on a clock that only goes forward. */
  now(): number;
}

/** How long the scene has to stay finished before it is believed. */
const QUIET_MILLISECONDS = 350;
/** And for how many frames in a row. */
const QUIET_FRAMES = 2;

/**
 * Waits until the scene has finished drawing its present view, or until `timeoutMs` has passed.
 *
 * One finished frame is not enough to believe. A tile that arrives asks for a redraw, and that
 * redraw is what discovers the next tiles; and the scene's own data is asked for a moment after the
 * camera was placed, so "nothing is loading" is briefly true before anything has started. The
 * scene therefore has to stay finished for a short time, over several frames, before this answers.
 */
export async function settleScene(scene: FlybySettleScene, timeoutMs: number): Promise<boolean> {
  const deadline = scene.now() + timeoutMs;
  let frames = 0;
  let quietSince: number | undefined;
  while (scene.now() < deadline) {
    if (scene.isDestroyed()) {
      return false;
    }
    // Asked for on every pass: the scene draws only on request, and a check made over a frame that
    // was never drawn would be a check of the picture before.
    scene.requestRender();
    await scene.nextFrame();
    if (scene.isDestroyed()) {
      return false;
    }
    if (!scene.isSettled() || scene.dataLoading()) {
      frames = 0;
      quietSince = undefined;
      continue;
    }
    frames += 1;
    quietSince ??= scene.now();
    if (frames >= QUIET_FRAMES && scene.now() - quietSince >= QUIET_MILLISECONDS) {
      return true;
    }
  }
  return false;
}
