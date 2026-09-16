// SPDX-License-Identifier: AGPL-3.0-or-later
import type { AnchorKind } from '../api/hooks.ts';

/**
 * Turning something clicked in the survey viewer into a link anchor.
 *
 * <b>The names are the viewer's own, deliberately.</b> For a `.lox` model the viewer and the
 * survey rows can call the same station different things — the rows carry the root survey's name at
 * the front of the path and the viewer's reader never added it, which shows whenever a file names
 * its root survey — and for `.3d` they always agree. Rather than depend on knowing which of those
 * a given file is, an anchor authored here stores the path the viewer uses, and the viewer is what
 * resolves it again: the model's own id, which the member already carries as its
 * target, says which survey the path belongs to. That is the owner's decision (2026-09-03) and it
 * is the one that cannot silently fail — an anchor written in the viewer's words and read in the
 * viewer's words round trips whatever either side calls it elsewhere.
 *
 * <b>It is no longer only an anchor's rule.</b> A recorded position now travels the same way: the
 * server converts a reported station into the viewer's spelling as it is written down, so a station
 * pressed here, an anchor stored here and a party drawn on the model are all named the same, and
 * nothing on this side translates between two vocabularies. Which is what makes that conversion a
 * server-side rule with one home rather than a step every caller has to remember.
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
 * Something of the model that can name itself, as far as this needs it. A vendored bundle's
 * object, so every field is a question: a thing that cannot say what it is called cannot be
 * anchored to.
 *
 * Two shapes arrive here and both are the viewer's. Its survey-tree nodes carry `getPath()`, and
 * the station objects its click and hover events carry name themselves with `name()` — a method
 * despite the word, which is why the string form is checked last and separately. Missing the
 * second is silent: every pick simply produces nothing.
 */
interface NamedPartLike {
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

  const candidate = node as NamedPartLike;
  if (typeof candidate.getPath === 'function') {
    const path = candidate.getPath();
    if (typeof path === 'string' && path.length > 0) {
      return path;
    }
  }

  // The station objects handed over with a click carry their full path from `name()`, which is
  // the same dotted string the tree node's `getPath()` gives and the same string the viewer
  // resolves a reference against.
  if (typeof candidate.name === 'function') {
    const named = (candidate.name as () => unknown)();
    if (typeof named === 'string' && named.length > 0) {
      return named;
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

/** What the viewer is asked to do to show a linked part: which move, and what to name it. */
export interface ModelFocus {
  /** Which of the viewer's two moves answers this anchor. */
  call: 'station' | 'survey';
  /**
   * The reference to give it — the path exactly as the anchor stores it, which is the viewer's
   * own spelling of it.
   *
   * Passed through unsplit deliberately. The viewer also takes a reference as an array of path
   * components, which is the unambiguous form for a name containing a dot of its own; but what is
   * stored is one dotted string, so splitting it here would invent component boundaries nobody
   * ever observed. The viewer retries a path that matches nothing with its last two components
   * rejoined, which covers the case that can arise from a stored string.
   */
  ref: string;
}

/**
 * How the viewer should answer a link, or null when this viewer cannot answer it — either because
 * the link names another cave's model, or because its anchor names something the viewer has no
 * single move for.
 *
 * <b>A survey is framed, not reduced to a station.</b> Showing a named part of a cave is a move of
 * its own, so a link to one moves the camera to frame the whole of it. Nothing is reduced and
 * nothing is reloaded: both of those were consequences of the viewer once having a single way in.
 *
 * <b>A run of stations resolves to the station it starts from.</b> A run is two places, and the
 * camera can only be at one of them; the end it starts from is where somebody following "the
 * passage from 6 to 7" wants to be standing, and putting them there beats refusing to move, which
 * a reader experiences as a link that does nothing.
 *
 * <b>A run of surveys is refused.</b> Two named parts of a cave have no start in the sense that a
 * pair of stations does, so choosing one of them would be arbitrary rather than merely partial —
 * and framing both would frame everything between them, which is most of the cave.
 */
export function focusForRef(
  ref: { targetType: string; targetId: string; anchorKind?: string; anchor?: unknown },
  surveyModelId: string | undefined,
): ModelFocus | null {
  if (surveyModelId === undefined || ref.targetType !== 'surveyModel' || ref.targetId !== surveyModelId) {
    return null;
  }

  const anchor = (typeof ref.anchor === 'object' && ref.anchor !== null ? ref.anchor : {}) as Record<string, unknown>;
  const plan = ref.anchorKind === 'modelStation'
    ? { call: 'station' as const, key: 'station' }
    : ref.anchorKind === 'modelSurvey'
      ? { call: 'survey' as const, key: 'survey' }
      : ref.anchorKind === 'modelStationRange'
        ? { call: 'station' as const, key: 'fromStation' }
        : null;
  const value = plan === null ? undefined : anchor[plan.key];
  return plan !== null && typeof value === 'string' && value.length > 0
    ? { call: plan.call, ref: value }
    : null;
}
