// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FlybyColour, FlybyFeatureLayer, FlybyLayerKey, FlybySurface } from './flybyDocument.ts';
import {
  flybyLookAt,
  flybyLookIsEmpty,
  type FlybyBackgroundState,
  type FlybyLayerState,
  type FlybyLookBase,
  type FlybyLookPlan,
  type FlybyLookState,
} from './flybyLook.ts';

// Putting a flyby's look on a scene, and taking it off again.
//
// The scene is somebody's view. What a flyby changes it changes in the scene alone — never in the
// viewer's own layer settings, which the flat map beside the scene shares — and when it stops,
// everything it touched is given back. A member the flyby never mentions is never touched at all.
//
// The look of a moment is worked out afresh each time; what is remembered here is only what was
// last put on the scene, so a frame that changes nothing costs nothing, and a moment reached by
// dragging the slider backwards is applied exactly like one reached by playing forwards.

/** The background as it is to be drawn: `from`, crossing into `to`, at `opacity` (else each at the viewer's own). */
export interface FlybyBackgroundShown extends FlybyBackgroundState {
  opacity?: number;
}

/**
 * What the view a flyby plays in does to its scene. Each member puts one thing in the state given;
 * undefined hands that thing back to the viewer's own setting.
 */
export interface FlybyLookTarget {
  /** What the viewer has now, for the members a flyby eases away from. */
  viewerLook(layers: readonly FlybyLayerKey[]): FlybyLookBase;
  showBackground(shown: FlybyBackgroundShown | undefined): void;
  showLayer(key: FlybyLayerKey, state: FlybyLayerState | undefined): void;
  /**
   * The layers of features the flyby draws itself, before any of them is shown; undefined takes
   * them all out of the scene. Left out by a view that cannot draw them, and they are then not drawn.
   */
  showFeatureLayers?(layers: readonly FlybyFeatureLayer[] | undefined): void;
  showWalls(tint: { colour: FlybyColour; strength: number } | undefined): void;
  /**
   * One cave in a colour of its own — its walls mixed with it by `strength`, its survey lines in it
   * — over the colour of every cave; undefined gives it back to that. Left out by a view that cannot
   * colour one cave apart, and such a change is then not shown.
   */
  showCave?(caveId: string, tint: { colour: FlybyColour; strength: number } | undefined): void;
  /** Undefined: the lines in their colours by depth. */
  showLines(colour: FlybyColour | undefined): void;
  showSurface(surface: FlybySurface | undefined): void;
  /** Undefined: no sky. */
  showSky(colour: FlybyColour | undefined): void;
  /**
   * The lens, degrees across the wider side of the picture; undefined gives the scene its own.
   * Left out by a view that cannot change its lens, and a flyby's lens is then not shown.
   */
  showLens?(degrees: number | undefined): void;
  /** Asks the scene for one frame, once a moment's changes are all made. */
  requestRender(): void;
}

/** What a player asks of the look: put moment `t` on the scene; give everything back. */
export interface FlybyLookStage {
  show(plan: FlybyLookPlan, t: number): void;
  restore(): void;
}

/** One value per member, as last put on the scene, written so two can be compared. */
type Applied = Map<string, string>;

function backgroundShown(state: FlybyLookState): FlybyBackgroundShown | undefined {
  if (!state.background && state.backgroundOpacity === undefined) {
    return undefined;
  }
  return {
    ...(state.background ?? { mix: 0 }),
    ...(state.backgroundOpacity !== undefined ? { opacity: state.backgroundOpacity } : {}),
  };
}

export function createFlybyLookStage(target: FlybyLookTarget): FlybyLookStage {
  let plan: FlybyLookPlan | undefined;
  let base: FlybyLookBase | undefined;
  let applied: Applied = new Map();

  /** Puts one member in a new state when it differs from the one it was last put in. */
  const put = (member: string, value: unknown, apply: () => void): boolean => {
    const written = JSON.stringify(value);
    if (applied.get(member) === written) {
      return false;
    }
    applied.set(member, written);
    apply();
    return true;
  };

  const restore = () => {
    for (const member of applied.keys()) {
      if (member === 'features') {
        target.showFeatureLayers?.(undefined);
      } else if (member === 'background') {
        target.showBackground(undefined);
      } else if (member.startsWith('layer:')) {
        target.showLayer(member.slice('layer:'.length), undefined);
      } else if (member === 'walls') {
        target.showWalls(undefined);
      } else if (member.startsWith('cave:')) {
        target.showCave?.(member.slice('cave:'.length), undefined);
      } else if (member === 'lines') {
        target.showLines(undefined);
      } else if (member === 'surface') {
        target.showSurface(undefined);
      } else if (member === 'sky') {
        target.showSky(undefined);
      } else if (member === 'lens') {
        target.showLens?.(undefined);
      }
    }
    const touched = applied.size > 0;
    applied = new Map();
    plan = undefined;
    base = undefined;
    if (touched) {
      target.requestRender();
    }
  };

  return {
    show(next, t) {
      if (next !== plan) {
        // Another flyby's look: the scene is given back before this one takes it, so what it
        // eases from is the viewer's own and not the last flyby's.
        restore();
        plan = next;
      }
      if (flybyLookIsEmpty(next)) {
        return;
      }
      base ??= target.viewerLook(next.layers);
      const state = flybyLookAt(next, base, t);
      let changed = false;
      // What the flyby's own layers are, first: whether each is shown comes with the other layers.
      if (next.features.length > 0 && target.showFeatureLayers) {
        changed = put('features', next.features, () => target.showFeatureLayers!(next.features)) || changed;
      }
      const background = backgroundShown(state);
      if (background) {
        changed = put('background', background, () => target.showBackground(background)) || changed;
      }
      for (const [key, layer] of Object.entries(state.layers)) {
        changed = put(`layer:${key}`, layer, () => target.showLayer(key, layer)) || changed;
      }
      if (state.walls) {
        const walls = state.walls;
        changed = put('walls', walls, () => target.showWalls(walls.strength > 0 ? walls : undefined)) || changed;
      }
      for (const [caveId, tint] of Object.entries(state.caves ?? {})) {
        if (target.showCave) {
          changed = put(`cave:${caveId}`, tint, () => target.showCave!(caveId, tint.strength > 0 ? tint : undefined)) || changed;
        }
      }
      if (state.lines !== undefined) {
        const lines = state.lines ?? undefined;
        changed = put('lines', lines ?? null, () => target.showLines(lines)) || changed;
      }
      if (state.surface !== undefined) {
        const surface = state.surface;
        changed = put('surface', surface, () => target.showSurface(surface)) || changed;
      }
      if (state.sky !== undefined) {
        const sky = state.sky ?? undefined;
        changed = put('sky', sky ?? null, () => target.showSky(sky)) || changed;
      }
      if (state.lens !== undefined && target.showLens) {
        const lens = state.lens;
        changed = put('lens', lens, () => target.showLens!(lens)) || changed;
      }
      if (changed) {
        target.requestRender();
      }
    },
    restore,
  };
}
