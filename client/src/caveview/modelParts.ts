// SPDX-License-Identifier: AGPL-3.0-or-later
import type { AnchorKind } from '../api/hooks.ts';

/**
 * Turning something clicked in the survey viewer into a link anchor.
 *
 * <b>The names are the viewer's own, deliberately.</b> For a `.lox` model the viewer and the
 * server call the same station different things — the server prefixes the root survey's name and
 * the viewer's reader does not — and for `.3d` they agree. Rather than reconcile two spellings,
 * an anchor authored here stores the path the viewer uses, and the viewer is what resolves it
 * again: the model's own id, which the member already carries as its target, says which survey
 * the path belongs to. That is the owner's decision (2026-09-03) and it is the one that cannot
 * silently fail — an anchor written in the viewer's words and read in the viewer's words round
 * trips whatever either side calls it elsewhere.
 *
 * Nothing here imports the viewer, so all of it is arithmetic over plain objects that a test can
 * drive without a WebGL context — which matters more than usual, because there is no GPU on the
 * machine this is developed on and a test that needed a real viewer would never run.
 */

/** What was picked, ready to become a link member. */
export interface PickedModelPart {
  anchorKind: AnchorKind;
  anchor: Record<string, string>;
  /** How the part reads in a sentence, for the control that offers to link it. */
  label: string;
}

/**
 * The viewer's survey-tree node, as far as this needs it. A vendored bundle's object, so every
 * field is a question: a node that cannot say what it is called cannot be anchored to.
 */
interface TreeNodeLike {
  getPath?: () => unknown;
  name?: unknown;
}

/** The viewer's leg object: two station nodes and the length between them. */
interface LegLike {
  start?: () => unknown;
  end?: () => unknown;
}

/**
 * The path that names a node inside its model, or null when it will not say.
 *
 * The full path rather than the bare name, because a bare name is not unique in a survey of any
 * size — two chambers each having a station `1` is the ordinary case, not a pathological one —
 * and because the path is what the viewer itself takes when asked to show a named part.
 */
export function pathOf(node: unknown): string | null {
  if (typeof node !== 'object' || node === null) {
    return null;
  }

  const candidate = node as TreeNodeLike;
  if (typeof candidate.getPath === 'function') {
    const path = candidate.getPath();
    if (typeof path === 'string' && path.length > 0) {
      return path;
    }
  }

  // A node with a name and no path is still addressable when the name is all there is — a
  // single-survey model has nothing to prefix it with.
  return typeof candidate.name === 'string' && candidate.name.length > 0 ? candidate.name : null;
}

/** The last segment of a path — what a person calls the station when the survey is understood. */
export function shortNameOf(path: string): string {
  const cut = path.lastIndexOf('.');
  return cut < 0 ? path : path.slice(cut + 1);
}

/** A station picked in the viewer, or null when it cannot be named. */
export function partFromStation(node: unknown): PickedModelPart | null {
  const path = pathOf(node);
  return path === null
    ? null
    : { anchorKind: 'modelStation' as AnchorKind, anchor: { station: path }, label: path };
}

/**
 * A survey — a whole named part of the cave — picked in the viewer.
 */
export function partFromSurvey(node: unknown): PickedModelPart | null {
  const path = pathOf(node);
  return path === null
    ? null
    : { anchorKind: 'modelSurvey' as AnchorKind, anchor: { survey: path }, label: path };
}

/**
 * A leg picked in the viewer, stored as the run between its two stations.
 *
 * <b>A leg has no identity of its own that survives anything.</b> What the viewer hands over is an
 * index into the geometry it just built, which is meaningless the moment the model is re-imported.
 * What does not change is which two stations it runs between, and that is already expressible: a
 * run of stations from one to the other. So a leg is stored as what it connects rather than as a
 * thing, and no new vocabulary is needed for it.
 *
 * <b>A splay cannot be stored, and this is where that shows.</b> A splay is a shot from a station
 * to a point that was never itself a station, so its far end has no name — and an anchor naming
 * one end and inventing the other would read as exact while pointing at nothing. Such a pick is
 * refused here rather than half-recorded.
 */
export function partFromLeg(leg: unknown): PickedModelPart | null {
  if (typeof leg !== 'object' || leg === null) {
    return null;
  }

  const candidate = leg as LegLike;
  const from = typeof candidate.start === 'function' ? pathOf(candidate.start()) : null;
  const to = typeof candidate.end === 'function' ? pathOf(candidate.end()) : null;
  if (from === null || to === null) {
    return null;
  }

  // A shot from a station to itself is not a leg; it is the viewer handing over something this
  // does not understand, and an anchor from it would name a run of no length.
  if (from === to) {
    return null;
  }

  return {
    anchorKind: 'modelStationRange' as AnchorKind,
    anchor: { fromStation: from, toStation: to },
    label: `${shortNameOf(from)} → ${shortNameOf(to)}`,
  };
}

/**
 * The part of the survey a link asks the viewer to show, or null when this viewer cannot answer
 * it — either because the link names another cave's model, or because its anchor names something
 * a single section cannot stand for.
 *
 * <b>A run of stations resolves to the station it starts from.</b> The viewer shows one section,
 * so a run has to be reduced to one end of itself; the end it starts from is where somebody
 * following "the passage from 6 to 7" wants to be standing, and putting them there beats
 * refusing to move, which a reader experiences as a link that does nothing.
 *
 * <b>A run of surveys is refused.</b> Two named parts of a cave have no start in the sense that a
 * pair of stations does, so choosing one of them would be arbitrary rather than merely partial.
 */
export function sectionForRef(
  ref: { targetType: string; targetId: string; anchorKind?: string; anchor?: unknown },
  surveyModelId: string | undefined,
): string | null {
  if (surveyModelId === undefined || ref.targetType !== 'surveyModel' || ref.targetId !== surveyModelId) {
    return null;
  }

  const anchor = (typeof ref.anchor === 'object' && ref.anchor !== null ? ref.anchor : {}) as Record<string, unknown>;
  const key = ref.anchorKind === 'modelStation'
    ? 'station'
    : ref.anchorKind === 'modelSurvey'
      ? 'survey'
      : ref.anchorKind === 'modelStationRange'
        ? 'fromStation'
        : null;
  const value = key === null ? undefined : anchor[key];
  return typeof value === 'string' && value.length > 0 ? value : null;
}
