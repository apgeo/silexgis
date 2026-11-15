// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  flybyChangeMember,
  flybyChangeSeconds,
  flybyFeatureLayerKey,
  flybySectionStarts,
  flybyTimeIn,
  type FlybyChange,
  type FlybyColour,
  type FlybyDocument,
  type FlybyFeatureLayer,
  type FlybyLayerKey,
  type FlybyLook,
  type FlybySurface,
} from './flybyDocument.ts';

// How the scene looks at a moment of a flyby.
//
// The look at moment `t` is worked out from the document alone — the opening look, the changes
// that have begun by `t` — and from what the viewer had when the flyby started, never from what
// was applied a moment before. So a slider dragged backwards undoes a change exactly as dragging
// it forwards made it, and frame `n` of a movie is the same picture however the frames before it
// were drawn.
//
// What the flyby does not mention is not here at all: a flyby that fades the background says
// nothing about the layers, and the layers stay as the viewer set them.

/** A change placed on the flyby's clock, in real seconds. */
export interface FlybyTimedChange {
  change: FlybyChange;
  start: number;
  end: number;
}

/** What a flyby does to the look, ready to be asked about any moment. */
export interface FlybyLookPlan {
  look: FlybyLook;
  /** In the order they start. */
  changes: FlybyTimedChange[];
  /**
   * The layers the opening look or a change names, which are the only ones the flyby touches —
   * and the flyby's own layers of features, which are its to draw from start to end.
   */
  layers: FlybyLayerKey[];
  /** The layers of features the flyby draws itself, for as long as it has the scene. */
  features: FlybyFeatureLayer[];
  /** The cave each subject a change colours on its own is, by the subject's position. */
  caveOf: Record<number, string>;
}

/**
 * What the viewer had when the flyby started, for the members a flyby eases away from: a
 * background that fades to half starts from the strength the viewer had it at.
 */
export interface FlybyLookBase {
  /** The background shown, by catalogue id; undefined when none is. */
  background?: number;
  backgroundOpacity: number;
  surface: FlybySurface;
  /** The viewer's lens, degrees across the wider side; left out, the scene's own 60. */
  lens?: number;
  /**
   * The factor the viewer has the relief drawn at — their own, or the one a saved view they opened
   * brought; left out, true height.
   */
  relief?: number;
  /** For each layer the plan names. */
  layers: Record<FlybyLayerKey, { visible: boolean; opacity: number }>;
}

/** The background at a moment: one, or two being crossed from `from` into `to`, `mix` of the way. */
export interface FlybyBackgroundState {
  from?: number;
  to?: number;
  mix: number;
}

export interface FlybyLayerState {
  visible: boolean;
  /** As drawn: the layer's own strength times how far it has faded in. */
  opacity: number;
}

/**
 * The look at one moment. A member left undefined is one the flyby never mentions; a member
 * present holds the value it must be drawn at — the viewer's own, until a change moves it.
 */
export interface FlybyLookState {
  background?: FlybyBackgroundState;
  backgroundOpacity?: number;
  layers: Record<FlybyLayerKey, FlybyLayerState>;
  /** A strength of 0 is the walls' own colours. */
  walls?: { colour: FlybyColour; strength: number };
  /** null: by depth, as the scene draws them. */
  lines?: FlybyColour | null;
  surface?: FlybySurface;
  /** null: no sky. */
  sky?: FlybyColour | null;
  /** Degrees across the wider side of the picture. */
  lens?: number;
  /** How many times taller than true the scene is drawn. */
  relief?: number;
  /** Caves in a colour of their own, by cave id; a strength of 0 is the colour of the rest. */
  caves?: Record<string, { colour: FlybyColour; strength: number }>;
}

/** The lens a scene has when nobody has set one, degrees across the wider side of the picture. */
export const FLYBY_OWN_LENS = 60;

/** The plan of a document's look; changes whose section is gone are left out, as a check reports them. */
export function flybyLookPlan(document: FlybyDocument): FlybyLookPlan {
  const starts = flybySectionStarts(document);
  const changes: FlybyTimedChange[] = [];
  for (const change of document.changes ?? []) {
    const time = flybyTimeIn(document, starts, { ...change, seconds: flybyChangeSeconds(change) });
    if (time) {
      changes.push({ change, ...time });
    }
  }
  changes.sort((a, b) => a.start - b.start);
  const look = document.look ?? {};
  const features = document.features ?? [];
  const layers = new Set([...features.map((layer) => flybyFeatureLayerKey(layer.id)), ...Object.keys(look.layers ?? {})]);
  for (const { change } of changes) {
    if (change.kind === 'layer' || change.kind === 'layerOpacity') {
      layers.add(change.layer);
    }
  }
  const caveOf: Record<number, string> = {};
  for (const { change } of changes) {
    const subject = change.kind === 'cave' ? document.subjects[change.subject] : undefined;
    if (change.kind === 'cave' && subject?.kind === 'cave') {
      caveOf[change.subject] = subject.caveId;
    }
  }
  return { look, changes, layers: [...layers], features, caveOf };
}

/** True when the plan changes nothing: then nothing of the scene is touched at all. */
export function flybyLookIsEmpty(plan: FlybyLookPlan): boolean {
  return plan.changes.length === 0 && Object.keys(plan.look).length === 0 && plan.features.length === 0;
}

/** Eased so a change starts and ends gently rather than at a jolt. */
export function easeFlyby(p: number): number {
  const s = Math.min(1, Math.max(0, p));
  return s * s * (3 - 2 * s);
}

/** How far through a change moment `t` is: 0 before it, 1 after it, eased in between; a cut is 0 or 1. */
function progress(timed: FlybyTimedChange, t: number): number {
  if (t < timed.start) {
    return 0;
  }
  if (!(timed.end > timed.start) || t >= timed.end) {
    return 1;
  }
  return easeFlyby((t - timed.start) / (timed.end - timed.start));
}

function lerp(a: number, b: number, p: number): number {
  return a + (b - a) * p;
}

function channels(colour: FlybyColour): [number, number, number] {
  return [1, 3, 5].map((at) => Number.parseInt(colour.slice(at, at + 2), 16)) as [number, number, number];
}

/** Two colours mixed, `p` of the way from the first to the second. */
export function mixFlybyColours(from: FlybyColour, to: FlybyColour, p: number): FlybyColour {
  if (p <= 0) {
    return from;
  }
  if (p >= 1) {
    return to;
  }
  const a = channels(from);
  const b = channels(to);
  return `#${a.map((value, i) => Math.round(lerp(value, b[i], p)).toString(16).padStart(2, '0')).join('')}`;
}

/** The changes of one member, in order. They never overlap — a document where they do is refused. */
function changesOf(plan: FlybyLookPlan, member: string): FlybyTimedChange[] {
  return plan.changes.filter((timed) => flybyChangeMember(timed.change) === member);
}

/**
 * Walks one member's changes up to `t`: each change that has ended leaves its target in force, the
 * one in progress gives a value between what was in force and its target, and the rest have not
 * happened yet.
 */
function follow<T>(
  changes: readonly FlybyTimedChange[],
  t: number,
  initial: T,
  step: (inForce: T, change: FlybyChange, p: number) => T,
): T {
  let value = initial;
  for (const timed of changes) {
    if (t < timed.start) {
      break;
    }
    value = step(value, timed.change, progress(timed, t));
  }
  return value;
}

/**
 * A tint stepped towards a change of the walls or of one cave. Walls in their own colours have no
 * tint to mix from: the new colour is taken whole and only its strength rises. Going back to their
 * own, the colour is kept as it fades.
 */
function tintStep(
  inForce: { colour: FlybyColour; strength: number },
  change: FlybyChange,
  p: number,
): { colour: FlybyColour; strength: number } {
  if (change.kind !== 'walls' && change.kind !== 'cave') {
    return inForce;
  }
  const from = inForce.strength > 0 ? inForce.colour : change.colour;
  const to = change.strength > 0 ? change.colour : from;
  return { colour: mixFlybyColours(from, to, p), strength: lerp(inForce.strength, change.strength, p) };
}

/** The background shown at `t`. */
function backgroundAt(plan: FlybyLookPlan, base: FlybyLookBase, t: number): FlybyBackgroundState {
  const initial: FlybyBackgroundState = { from: plan.look.background ?? base.background, mix: 0 };
  return follow(changesOf(plan, 'background'), t, initial, (inForce, change, p) => {
    if (change.kind !== 'background') {
      return inForce;
    }
    if (p >= 1 || inForce.from === undefined || inForce.from === change.background) {
      return { from: change.background, mix: 0 };
    }
    return p <= 0 ? inForce : { from: inForce.from, to: change.background, mix: p };
  });
}

function numberAt(
  changes: readonly FlybyTimedChange[],
  t: number,
  initial: number,
  target: (change: FlybyChange) => number | undefined,
): number {
  return follow(changes, t, initial, (inForce, change, p) => {
    const wanted = target(change);
    return wanted === undefined ? inForce : lerp(inForce, wanted, p);
  });
}

function layerAt(plan: FlybyLookPlan, base: FlybyLookBase, key: FlybyLayerKey, t: number): FlybyLayerState {
  // A layer of the flyby's own has no setting of the viewer's: it is shown unless the flyby says not.
  const own = key.startsWith('features:') ? { visible: true, opacity: 1 } : (base.layers[key] ?? { visible: true, opacity: 1 });
  const opening = plan.look.layers?.[key] ?? {};
  // How far the layer has faded in, 0–1, and whether it is drawn at all: a layer fading out is
  // drawn until it has gone, a layer fading in is drawn from the moment it starts to.
  const initial = { fade: (opening.visible ?? own.visible) ? 1 : 0, on: opening.visible ?? own.visible };
  const shown = follow(changesOf(plan, `layer:${key}`), t, initial, (inForce, change, p) => {
    if (change.kind !== 'layer') {
      return inForce;
    }
    // Only changes that have begun are stepped through, so one turning a layer on has it drawn
    // from its very first moment, at nothing.
    const fade = lerp(inForce.fade, change.visible ? 1 : 0, p);
    return { fade, on: change.visible || p < 1 };
  });
  const opacity = numberAt(changesOf(plan, `layerOpacity:${key}`), t, opening.opacity ?? own.opacity, (change) =>
    change.kind === 'layerOpacity' ? change.opacity : undefined,
  );
  return { visible: shown.on, opacity: shown.on ? opacity * shown.fade : 0 };
}

/**
 * The look at moment `t`: the opening look with every change that has begun by then applied, the
 * one in progress part of the way. `base` is what the viewer had when the flyby started.
 */
export function flybyLookAt(plan: FlybyLookPlan, base: FlybyLookBase, t: number): FlybyLookState {
  const { look } = plan;
  const has = (member: string) => plan.changes.some((timed) => flybyChangeMember(timed.change) === member);
  const state: FlybyLookState = { layers: {} };

  if (look.background !== undefined || has('background')) {
    state.background = backgroundAt(plan, base, t);
  }
  if (look.backgroundOpacity !== undefined || has('backgroundOpacity')) {
    state.backgroundOpacity = numberAt(
      changesOf(plan, 'backgroundOpacity'),
      t,
      look.backgroundOpacity ?? base.backgroundOpacity,
      (change) => (change.kind === 'backgroundOpacity' ? change.opacity : undefined),
    );
  }
  for (const key of plan.layers) {
    state.layers[key] = layerAt(plan, base, key, t);
  }
  if (look.walls || has('walls')) {
    state.walls = follow(changesOf(plan, 'walls'), t, look.walls ?? { colour: '#000000', strength: 0 }, tintStep);
  }
  for (const [subject, caveId] of Object.entries(plan.caveOf)) {
    state.caves ??= {};
    state.caves[caveId] = follow(changesOf(plan, `cave:${subject}`), t, { colour: '#000000', strength: 0 }, tintStep);
  }
  if (look.lines !== undefined || has('lines')) {
    state.lines = follow<FlybyColour | null>(changesOf(plan, 'lines'), t, look.lines ?? null, (inForce, change) =>
      change.kind === 'lines' ? change.colour : inForce,
    );
  }
  if (look.surface !== undefined || has('surface')) {
    state.surface = follow<FlybySurface>(changesOf(plan, 'surface'), t, look.surface ?? base.surface, (inForce, change) =>
      change.kind === 'surface' ? change.surface : inForce,
    );
  }
  if (look.lens !== undefined || has('lens')) {
    state.lens = numberAt(changesOf(plan, 'lens'), t, look.lens ?? base.lens ?? FLYBY_OWN_LENS, (change) =>
      change.kind === 'lens' ? change.degrees : undefined,
    );
  }
  if (look.relief !== undefined || has('relief')) {
    state.relief = flybyReliefAt(plan, base.relief, t);
  }
  if (look.sky !== undefined || has('sky')) {
    state.sky = follow<FlybyColour | null>(changesOf(plan, 'sky'), t, look.sky ?? null, (inForce, change, p) => {
      if (change.kind !== 'sky') {
        return inForce;
      }
      if (p >= 1) {
        return change.colour;
      }
      // No sky is black above the horizon, so that is what a sky fades from and to.
      return mixFlybyColours(inForce ?? '#000000', change.colour ?? '#000000', p);
    });
  }
  return state;
}

/**
 * The factor the relief is drawn at, at moment `t`: the opening factor or the viewer's own
 * (`viewer`, true height when left out), with every change of the relief that has begun by then
 * eased towards its target. For a flyby that never mentions the relief, the viewer's own throughout
 * — which is what a picture of any moment of it is drawn at, and what it must say it is drawn at.
 */
export function flybyReliefAt(plan: FlybyLookPlan, viewer: number | undefined, t: number): number {
  const opening = plan.look.relief ?? viewer ?? 1;
  return numberAt(changesOf(plan, 'relief'), t, opening, (change) =>
    change.kind === 'relief' ? (change.factor ?? opening) : undefined,
  );
}
