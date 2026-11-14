// SPDX-License-Identifier: AGPL-3.0-or-later
import { eyeLookingAt } from '../scene3d/camera3d.ts';
import type {
  Scene3DPresenterBridge,
  Scene3DPresenterSphere,
  Scene3DPresenterTint,
} from '../scene3d/presenterBridge3d.ts';
import { PresenterOverlay } from './overlay.ts';
import { centreOf, dragValue, mouse, NotReadyError, stepsFor, type PointerStep } from './pointer.ts';
import {
  channelOf,
  durationOf,
  ease,
  scenarioLength,
  scenarioProblems,
  type Ease,
  type OrbitPose,
  type Scenario,
  type ScenarioItem,
} from './scenario.ts';

// Plays a scenario inside the page.
//
// One clock, two ways to move it. `seek(t)` puts everything in the state movie time `t` should
// show and returns; a recorder that takes one picture per frame calls it, waits for the scene to
// settle, and takes the picture, however long drawing takes. `play()` moves the same clock in real
// time, for a machine that draws fast enough to be filmed live or for a person recording their own
// screen. Time only moves forward: a click cannot be taken back.
//
// Stepped, the browser's own animations (a popover opening, a group of rows unfolding) would
// otherwise finish in the second a picture takes and be missing from the movie, so they are held
// and advanced on the movie clock too.

export type PresenterMode = 'step' | 'live';

export interface PresenterRunnerOptions {
  mode: PresenterMode;
  /**
   * The extent to move around, in degrees, when the scenario is about an imported file rather than
   * a cave model; the camera then circles the extent's middle, at the ground's height there.
   */
  extent?: { west: number; south: number; east: number; north: number };
}

/** Metres in a degree of latitude, and of longitude at the equator (WGS84, near enough here). */
const METRES_PER_DEGREE = 111_320;

/** A sphere standing in for a model: centred on the extent's middle at ground level, reaching its corners. */
export function extentSphere(
  extent: NonNullable<PresenterRunnerOptions['extent']>,
  groundHeight: (longitude: number, latitude: number) => number,
): Scene3DPresenterSphere {
  const longitude = (extent.west + extent.east) / 2;
  const latitude = (extent.south + extent.north) / 2;
  const width = (extent.east - extent.west) * METRES_PER_DEGREE * Math.cos((latitude * Math.PI) / 180);
  const height = (extent.north - extent.south) * METRES_PER_DEGREE;
  return {
    longitude,
    latitude,
    height: groundHeight(longitude, latitude),
    // A file of one point still has a size to be circled at.
    radius: Math.max(50, Math.hypot(width, height) / 2),
  };
}

export interface PresenterRunner {
  /** Seconds. */
  readonly length: number;
  prepare(): Promise<void>;
  seek(t: number): void;
  /** Resolves once the scene has drawn everything in view; false on timeout. */
  settle(timeoutMs?: number): Promise<boolean>;
  play(speed?: number): Promise<void>;
  destroy(): void;
}

export interface Pose {
  heading: number;
  pitch: number;
  range: number;
  lift: number;
  aboveGround: number | null;
}

export interface CameraLeg {
  at: number;
  duration: number;
  from: Pose;
  to: Pose;
  ease?: Ease;
}

interface ActivePointer {
  item: ScenarioItem;
  steps: Generator<PointerStep>;
  step?: PointerStep;
  stepStart: number;
  /** False until the step's target has been found on screen. */
  prepared: boolean;
  from: { x: number; y: number };
  to: { x: number; y: number };
  rail?: DOMRect;
  handleY?: number;
}

const OPENING_POSE: Pose = { heading: 0, pitch: -35, range: 3, lift: 0, aboveGround: null };
const POINTER_LINGER_SECONDS = 1.5;
/** How long, in real time, a step may wait for its target before the scenario fails. */
const TARGET_PATIENCE_MS = 15_000;
/** How long the scene's data has to stay quiet before a picture is taken. */
const QUIET_MS = 350;

function lerp(a: number, b: number, p: number): number {
  return a + (b - a) * p;
}

/** The parts of a pose that are interpolated: heading, pitch, the range's logarithm, lift. */
type PoseVector = [number, number, number, number];

function vectorOf(pose: Pose): PoseVector {
  return [pose.heading, pose.pitch, Math.log(pose.range), pose.lift];
}

/** Two `smooth` moves back to back, flown as one curve. */
function joined(a: CameraLeg | undefined, b: CameraLeg | undefined): boolean {
  return Boolean(
    a &&
      b &&
      a.ease === 'smooth' &&
      b.ease === 'smooth' &&
      a.duration > 0 &&
      b.duration > 0 &&
      Math.abs(a.at + a.duration - b.at) < 1e-6,
  );
}

/**
 * How fast each part of the pose changes where move `a` hands over to move `b`: the average rate
 * from a's start to b's end (a Catmull-Rom curve over uneven times). Both moves use the same rate
 * there, so the camera neither stops nor jerks at the join.
 */
function rateAtJoin(a: CameraLeg, b: CameraLeg): PoseVector {
  const span = b.at + b.duration - a.at;
  const from = vectorOf(a.from);
  const to = vectorOf(b.to);
  return from.map((value, i) => (to[i] - value) / span) as PoseVector;
}

/**
 * Where camera move `index` has got to at time `t`, and how far through it the move is (0–1, for
 * what is blended rather than flown, such as the limit above ground).
 */
export function cameraPoseAt(legs: readonly CameraLeg[], index: number, t: number): { vector: PoseVector; p: number } {
  const leg = legs[index];
  const s = leg.duration > 0 ? Math.min(1, Math.max(0, (t - leg.at) / leg.duration)) : 1;
  const from = vectorOf(leg.from);
  const to = vectorOf(leg.to);
  if (leg.ease !== 'smooth' || leg.duration <= 0) {
    const p = ease(leg.ease, s);
    return { vector: from.map((value, i) => lerp(value, to[i], p)) as PoseVector, p };
  }
  const still: PoseVector = [0, 0, 0, 0];
  const start = joined(legs[index - 1], leg) ? rateAtJoin(legs[index - 1], leg) : still;
  const end = joined(leg, legs[index + 1]) ? rateAtJoin(leg, legs[index + 1]) : still;
  // Cubic Hermite: the two poses, and the rates at either end, scaled to the move's length.
  const s2 = s * s;
  const s3 = s2 * s;
  const h00 = 2 * s3 - 3 * s2 + 1;
  const h10 = s3 - 2 * s2 + s;
  const h01 = -2 * s3 + 3 * s2;
  const h11 = s3 - s2;
  const vector = from.map(
    (value, i) => h00 * value + h10 * leg.duration * start[i] + h01 * to[i] + h11 * leg.duration * end[i],
  ) as PoseVector;
  return { vector, p: ease('inOut', s) };
}

function colourOf(hex: string): { red: number; green: number; blue: number } {
  const value = Number.parseInt(hex.slice(1), 16);
  return { red: ((value >> 16) & 255) / 255, green: ((value >> 8) & 255) / 255, blue: (value & 255) / 255 };
}

/** The tint the walls have at `t`, after every colour change begun by then; undefined is their own. */
export function modelTintAt(items: readonly ScenarioItem[], t: number): Scene3DPresenterTint | undefined {
  let current: Scene3DPresenterTint | undefined;
  for (const item of items) {
    if (item.kind !== 'modelColor' || item.at > t) {
      continue;
    }
    const target: Scene3DPresenterTint | undefined =
      item.to === null
        ? current && { ...current, amount: 0 }
        : { ...colourOf(item.to), amount: Math.min(1, Math.max(0, item.strength ?? 0.8)) };
    if (!target) {
      continue;
    }
    // From the walls' own colours, the new colour fades in rather than sweeping from black.
    const from = current ?? { ...target, amount: 0 };
    const duration = durationOf(item);
    const p = duration > 0 ? ease(item.ease, (t - item.at) / duration) : 1;
    current = {
      red: lerp(from.red, target.red, p),
      green: lerp(from.green, target.green, p),
      blue: lerp(from.blue, target.blue, p),
      amount: lerp(from.amount, target.amount, p),
    };
  }
  return current && current.amount > 0 ? current : undefined;
}

function poseAfter(previous: Pose, to: OrbitPose): Pose {
  return {
    heading: to.heading ?? previous.heading + (to.headingBy ?? 0),
    pitch: to.pitch ?? previous.pitch,
    range: to.range ?? previous.range,
    lift: to.lift ?? previous.lift,
    aboveGround: to.aboveGround === undefined ? previous.aboveGround : to.aboveGround,
  };
}

function bridge(): Scene3DPresenterBridge {
  const found = window.__silexgisPresenter?.scene3d;
  if (!found) {
    throw new Error('presenter: the 3D view is not open, or this build has no recording handle');
  }
  return found;
}

/**
 * 0→1 over `fade` seconds at the start of [start, end) and 1→0 over the same at its end. Something
 * shown from the very first frame is there at once: fading it in would flash what is behind it.
 */
function envelope(t: number, start: number, end: number, fade: number): number {
  if (t < start || t >= end) {
    return 0;
  }
  return Math.min(1, start === 0 ? 1 : (t - start) / fade, (end - t) / fade);
}

export function createRunner(scenario: Scenario, options: PresenterRunnerOptions): PresenterRunner {
  const problems = scenarioProblems(scenario);
  if (problems.length > 0) {
    throw new Error(`presenter: the scenario cannot be played:\n- ${problems.join('\n- ')}`);
  }
  const items = [...scenario.items].sort((a, b) => a.at - b.at);
  const pointerItems = items.filter((item) => channelOf(item) === 'pointer');
  const length = scenarioLength(scenario);

  let overlay: PresenterOverlay | undefined;
  let sphere: Scene3DPresenterSphere | undefined;
  const legs: CameraLeg[] = [];
  let lastT = -1;

  let nextPointer = 0;
  let pointerFree = 0;
  let active: ActivePointer | undefined;
  let pointer = { x: window.innerWidth * 0.72, y: window.innerHeight * 0.62 };
  let lastPointerActivity = Number.NEGATIVE_INFINITY;
  let click: { x: number; y: number; t: number } | undefined;

  const animationStarts = new WeakMap<Animation, number>();

  function buildLegs() {
    let pose = OPENING_POSE;
    for (const item of items) {
      if (item.kind !== 'camera') {
        continue;
      }
      const next = poseAfter(pose, item.to);
      legs.push({ at: item.at, duration: durationOf(item), from: pose, to: next, ease: item.ease });
      pose = next;
    }
  }

  function applyCamera(t: number) {
    if (!sphere) {
      return;
    }
    const index = legs.findLastIndex((candidate) => candidate.at <= t);
    if (index < 0) {
      return;
    }
    const leg = legs[index];
    const {
      vector: [heading, flownPitch, logRange, lift],
      p,
    } = cameraPoseAt(legs, index, t);
    let pitch = flownPitch;
    const range = Math.exp(logRange);
    const pivot = {
      lon: sphere.longitude,
      lat: sphere.latitude,
      height: sphere.height + lift * sphere.radius,
    };
    const eye = eyeLookingAt(pivot, heading, pitch, range * sphere.radius);
    // Kept above a hillside the orbit would otherwise pass through. Blended between the limits at
    // either end of the move, so lifting the limit to go underground is a descent, not a drop.
    const ground = bridge().groundHeight(eye.lon, eye.lat);
    const held = (floor: number | null) => (floor === null ? eye.height : Math.max(eye.height, ground + floor));
    const height = lerp(held(leg.from.aboveGround), held(leg.to.aboveGround), p);
    if (height !== eye.height) {
      const horizontal = range * sphere.radius * Math.cos((pitch * Math.PI) / 180);
      pitch = (Math.atan2(pivot.height - height, horizontal) * 180) / Math.PI;
    }
    bridge().setCamera({
      longitude: eye.lon,
      latitude: eye.lat,
      height,
      heading: ((heading % 360) + 360) % 360,
      pitch,
      roll: 0,
    });
  }

  const colourItems = items.filter((item) => item.kind === 'modelColor');
  let lastTint: string | undefined;

  function applyTint(t: number) {
    if (colourItems.length === 0) {
      return;
    }
    const tint = modelTintAt(colourItems, t);
    const key = JSON.stringify(tint ?? null);
    if (key !== lastTint) {
      bridge().setModelTint(tint);
      lastTint = key;
    }
  }

  function stepLength(step: PointerStep): number {
    return step.kind === 'click' || step.kind === 'wait' ? 0 : step.duration;
  }

  function startStep(state: ActivePointer, at: number) {
    const next = state.steps.next();
    state.stepStart = at;
    state.step = next.done ? undefined : next.value;
    state.prepared = false;
  }

  /** Finds what the step acts on; throws NotReadyError while it is not on screen yet. */
  function prepareStep(state: ActivePointer) {
    const step = state.step!;
    if (step.kind === 'wait') {
      if (!step.ready()) {
        throw new NotReadyError(`presenter: still waiting for ${step.what}`);
      }
    } else if (step.kind === 'move') {
      state.to = centreOf(step.target());
      state.from = { ...pointer };
    } else if (step.kind === 'drag') {
      const handle = step.handle();
      const rail = step.rail().getBoundingClientRect();
      const centre = centreOf(handle);
      state.rail = rail;
      state.handleY = centre.y;
      pointer = centre;
      mouse('mousedown', handle, centre.x, centre.y);
    }
  }

  function dragTo(state: ActivePointer, step: Extract<PointerStep, { kind: 'drag' }>, p: number) {
    const rail = state.rail!;
    const value = dragValue(step.values, p, step.ease);
    pointer = { x: rail.left + (rail.width * value) / 100, y: state.handleY! };
    mouse('mousemove', document, pointer.x, pointer.y);
  }

  function finishStep(state: ActivePointer, end: number) {
    const step = state.step!;
    if (step.kind === 'move') {
      pointer = state.to;
    } else if (step.kind === 'click') {
      (step.target() as HTMLElement).click();
      click = { ...pointer, t: end };
    } else if (step.kind === 'drag') {
      dragTo(state, step, 1);
      mouse('mouseup', document, pointer.x, pointer.y);
    }
  }

  let waitingSince: number | undefined;

  /**
   * Runs `act`, and answers false when its target is not on screen yet: the step waits, and the
   * pointer holds still, until it is. In a stepped recording that is time the movie spends on the
   * interface catching up, which is what a viewer of the real thing would see too.
   */
  function whenReady(act: () => void): boolean {
    try {
      act();
      waitingSince = undefined;
      return true;
    } catch (error) {
      if (!(error instanceof NotReadyError)) {
        throw error;
      }
      waitingSince ??= performance.now();
      if (performance.now() - waitingSince > TARGET_PATIENCE_MS) {
        throw error;
      }
      return false;
    }
  }

  function advancePointer(t: number) {
    for (let guard = 0; guard < 10_000; guard += 1) {
      if (!active) {
        const item = pointerItems[nextPointer];
        if (!item) {
          return;
        }
        const start = Math.max(item.at, pointerFree);
        if (start > t) {
          return;
        }
        nextPointer += 1;
        active = { item, steps: stepsFor(item), stepStart: start, prepared: false, from: pointer, to: pointer };
        startStep(active, start);
        continue;
      }
      lastPointerActivity = t;
      const state = active;
      const step = state.step;
      if (!step) {
        pointerFree = state.stepStart;
        lastPointerActivity = state.stepStart;
        active = undefined;
        continue;
      }
      if (!state.prepared) {
        if (!whenReady(() => prepareStep(state))) {
          // Waited for: the step begins when its target appears, not when it was due.
          state.stepStart = t;
          return;
        }
        state.prepared = true;
      }
      const end = state.stepStart + stepLength(step);
      if (end <= t) {
        if (!whenReady(() => finishStep(state, end))) {
          return;
        }
        startStep(state, end);
        continue;
      }
      const p = (t - state.stepStart) / stepLength(step);
      if (step.kind === 'move') {
        const q = ease('inOut', p);
        pointer = { x: lerp(state.from.x, state.to.x, q), y: lerp(state.from.y, state.to.y, q) };
      } else if (step.kind === 'drag') {
        dragTo(state, step, p);
      }
      return;
    }
    throw new Error('presenter: a pointer action did not finish');
  }

  function driveAnimations(t: number) {
    for (const animation of document.getAnimations()) {
      if (animation.playState === 'finished') {
        continue;
      }
      let start = animationStarts.get(animation);
      if (start === undefined) {
        start = Math.max(0, lastT);
        animationStarts.set(animation, start);
      }
      const elapsed = (t - start) * 1000;
      const end = animation.effect?.getComputedTiming().endTime;
      if (typeof end === 'number' && Number.isFinite(end) && elapsed >= end) {
        animation.finish();
      } else {
        animation.pause();
        animation.currentTime = elapsed;
      }
    }
  }

  function drawOverlay(t: number) {
    if (!overlay) {
      return;
    }
    const pointerOpacity = active
      ? 1
      : Math.max(0, Math.min(1, 1 - (t - lastPointerActivity - POINTER_LINGER_SECONDS) / 0.5));
    overlay.setPointer(pointer.x, pointer.y, pointerOpacity);
    overlay.setRipple(click?.x ?? 0, click?.y ?? 0, click ? t - click.t : undefined);

    const caption = items.findLast(
      (item) => item.kind === 'caption' && item.at <= t && t < item.at + durationOf(item),
    );
    overlay.setCaption(
      caption?.kind === 'caption' ? caption.text : undefined,
      caption ? envelope(t, caption.at, caption.at + durationOf(caption), 0.4) : 0,
    );
    const title = items.findLast((item) => item.kind === 'title' && item.at <= t && t < item.at + durationOf(item));
    overlay.setTitle(
      title?.kind === 'title' ? title.text : undefined,
      title?.kind === 'title' ? title.subtitle : undefined,
      title ? envelope(t, title.at, title.at + durationOf(title), 0.6) : 0,
    );
    let fade = 0;
    for (const item of items) {
      if (item.kind !== 'fade' || item.at > t) {
        continue;
      }
      const duration = durationOf(item);
      fade = duration > 0 ? lerp(fade, item.to, ease(item.ease, (t - item.at) / duration)) : item.to;
    }
    overlay.setFade(fade);
  }

  const runner: PresenterRunner = {
    length,
    async prepare() {
      sphere = options.extent
        ? extentSphere(options.extent, (longitude, latitude) => bridge().groundHeight(longitude, latitude))
        : bridge().modelSphere();
      if (!sphere) {
        throw new Error('presenter: no cave model is drawn in the 3D view, so there is nothing to move around');
      }
      if (colourItems.length > 0 && typeof bridge().setModelTint !== 'function') {
        throw new Error('presenter: this build of the application cannot recolour a model; record against a newer one');
      }
      buildLegs();
      overlay = new PresenterOverlay();
    },
    seek(t: number) {
      if (t < lastT) {
        throw new Error(`presenter: time moves forward only (asked for ${t}s after ${lastT}s)`);
      }
      if (options.mode === 'step') {
        driveAnimations(t);
      }
      applyCamera(t);
      applyTint(t);
      advancePointer(t);
      drawOverlay(t);
      lastT = t;
    },
    async settle(timeoutMs = 60_000) {
      // The scene's own data (caves, lines and files in view) is fetched again shortly after the
      // camera stops, so "nothing loading" has to hold for a moment before it is believed.
      const deadline = performance.now() + timeoutMs;
      const loading = () =>
        document.querySelector<HTMLElement>('[data-testid="scene3d-data"]')?.dataset.loading === 'true';
      while (performance.now() < deadline) {
        const tiles = await bridge().whenSettled(Math.max(0, deadline - performance.now()), 2);
        if (!tiles) {
          return false;
        }
        if (!loading()) {
          await new Promise((resolve) => setTimeout(resolve, QUIET_MS));
          if (!loading() && (await bridge().whenSettled(Math.max(0, deadline - performance.now()), 1))) {
            return true;
          }
        } else {
          await new Promise((resolve) => setTimeout(resolve, 50));
        }
      }
      return false;
    },
    play(speed = 1) {
      return new Promise<void>((resolve, reject) => {
        const started = performance.now();
        const tick = () => {
          try {
            const t = Math.min(length, ((performance.now() - started) / 1000) * speed);
            runner.seek(t);
            if (t >= length) {
              resolve();
              return;
            }
            requestAnimationFrame(tick);
          } catch (error) {
            reject(error);
          }
        };
        requestAnimationFrame(tick);
      });
    },
    destroy() {
      if (lastTint !== undefined && lastTint !== 'null') {
        window.__silexgisPresenter?.scene3d?.setModelTint(undefined);
      }
      overlay?.destroy();
      overlay = undefined;
    },
  };
  return runner;
}
