// SPDX-License-Identifier: AGPL-3.0-or-later
import { surveyModelReadableByViewer, type SurveyModelInfo } from '../api/hooks.ts';
import type {
  CaveViewRef,
  CaveViewSectionBounds,
  CaveViewTreeNode,
  CaveViewer,
} from './loadCaveView.ts';
import { viewerFileName } from './viewerFileName.ts';

/**
 * Comparing two surveys of one cave in the survey viewer: what can be compared, how the two are
 * told apart, whether they can be laid over one another, and which of their parts are shown.
 *
 * Kept beside the rest of what this application knows about the viewer and away from the
 * components that draw it, for the reason its neighbours are: none of it needs a drawing context,
 * so every rule here is a function over plain values that a test can drive directly.
 */

/** The two ways two surveys are looked at together. */
export type CompareMode = 'overlaid' | 'sideBySide';

/** Which of the two surveys: the one the panel was opened on, or the one it is compared with. */
export type CompareSide = 'primary' | 'other';

export const COMPARE_SIDES: readonly CompareSide[] = ['primary', 'other'];

/** A survey the viewer can read, as a comparison needs it. */
export interface ComparableModel {
  id: string;
  name: string;
  /** Where the file is read from. Signed, and short-lived: read when a comparison starts. */
  fileUrl: string;
  /** The name the viewer chooses its parser by. */
  fileName: string;
}

/**
 * What a panel is offered to compare: the survey it shows, and the cave's other line plots.
 *
 * The survey it shows is named here as well as by the panel's own file, because the panel's file is
 * an address captured when it was opened and this one is re-read with the cave's list — and laying
 * two surveys over one another reads the first one's file a second time.
 */
export interface SurveyCompareOffer {
  current: ComparableModel;
  others: readonly ComparableModel[];
}

/**
 * The comparison a cave's surveys allow for one of them, or null when there is nothing to compare
 * it with.
 *
 * <b>Line plots only, and only the cave's own.</b> A wall mesh is not something this viewer reads,
 * and a survey of another cave is not a second look at this one. Whether a model is still being
 * read into its stations does not matter here: the viewer parses the uploaded file itself.
 */
export function compareOffer(
  models: readonly SurveyModelInfo[] | undefined,
  currentId: string | undefined,
): SurveyCompareOffer | null {
  if (models === undefined || currentId === undefined) {
    return null;
  }
  const readable = models.filter(surveyModelReadableByViewer).map(
    (model): ComparableModel => ({
      id: model.id,
      name: model.name,
      fileUrl: model.modelUrl,
      fileName: viewerFileName(model),
    }),
  );
  const current = readable.find((model) => model.id === currentId);
  const others = readable.filter((model) => model.id !== currentId);
  return current === undefined || others.length === 0 ? null : { current, others };
}

/** A comparison as the panel holds it: which other survey, and how the two are shown. */
export interface SurveyComparison {
  otherId: string;
  mode: CompareMode;
}

/**
 * The comparison actually under way: the one asked for, as long as it is still on offer.
 *
 * A comparison outlives neither its offer nor the survey it is with. The cave's list is re-read
 * while the panel is open, and a survey deleted from it meanwhile is not one to go on comparing
 * with: the panel is then showing its own survey again, and says so by offering to compare.
 */
export function comparisonUnderWay(
  offer: SurveyCompareOffer | undefined,
  comparison: SurveyComparison | null,
): { mode: CompareMode; current: ComparableModel; other: ComparableModel } | null {
  if (offer === undefined || comparison === null) {
    return null;
  }
  const other = offer.others.find((model) => model.id === comparison.otherId);
  return other === undefined ? null : { mode: comparison.mode, current: offer.current, other };
}

/**
 * The colours the two surveys are drawn in when one is laid over the other.
 *
 * <b>Chosen against the viewer's background, not the page's.</b> The model is drawn on black
 * whatever theme the application is in, as lines a pixel wide. Blue and orange are a pair that
 * stays apart for a reader who does not tell red from green, and for one who does not tell blue
 * from yellow, and each of the two stands well clear of black. The test beside this file computes
 * both properties rather than taking them on trust.
 *
 * The first survey is always the blue one. A colour follows the survey it was given to and never
 * its place in a list, so switching what a survey is compared with does not repaint it.
 */
export const COMPARE_COLOURS: Record<CompareSide, string> = {
  primary: '#3987e5',
  other: '#d95926',
};

/** The longest label a survey is given in the viewer's tree, in characters. */
const LABEL_MAX = 40;

/**
 * The names the two surveys go by inside the viewer when they are loaded as one model.
 *
 * The viewer hangs each file's surveys from a survey named by its label, and shows that name in
 * its own tree and in a station's path. So the label is the survey's own name, made fit to be one
 * name of a path: a full stop is what separates the names of a path, so it is replaced; and two
 * surveys named alike are numbered, because two files under one label are one survey again.
 */
export function compareLabels(primaryName: string, otherName: string): Record<CompareSide, string> {
  const clean = (name: string, fallback: string) => {
    const label = name.replace(/\./g, ' ').replace(/\s+/g, ' ').trim().slice(0, LABEL_MAX).trim();
    return label.length === 0 ? fallback : label;
  };
  const primary = clean(primaryName, 'A');
  const other = clean(otherName, 'B');
  return primary === other
    ? { primary: `${primary} (1)`, other: `${other} (2)` }
    : { primary, other };
}

/** One named part of a survey that can be shown or hidden. */
export interface CompareSurvey {
  /** What the part is matched by across two surveys: its path from the top of its own file. */
  key: string;
  /** The names on that path. */
  path: readonly string[];
  /** Its own name, the last of them. */
  name: string;
}

const byName = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

const keyOf = (path: readonly string[]) => JSON.stringify(path);

/**
 * The parts of one file's survey a reader is offered to show and hide.
 *
 * <b>The first level of the tree that offers a choice.</b> A survey file usually wraps everything
 * in one survey named for the cave, and sometimes in several, one inside the other; a list of that
 * one name would be a list of one. So a survey that is the only one at its level is stepped
 * through, and what is listed is the first level holding more than one.
 *
 * A file that never branches — one survey, or a chain of them — is listed as its topmost survey,
 * which is the whole of it: one tick still shows and hides it. A file with no survey at all, only
 * stations, has nothing that can be named and lists nothing.
 *
 * Sorted by name, because the viewer's own tree page reorders a survey's children in place and the
 * order found here would otherwise depend on whether that page had been opened.
 */
export function surveysOf(top: CaveViewTreeNode | null | undefined): CompareSurvey[] {
  if (top == null) {
    return [];
  }
  const sections = (node: CaveViewTreeNode) => node.children.filter((child) => !child.isStation());
  const path: string[] = [];
  let level = sections(top);
  while (level.length === 1) {
    path.push(level[0].name);
    level = sections(level[0]);
  }
  if (level.length === 0) {
    return path.length === 0 ? [] : [{ key: keyOf([path[0]]), path: [path[0]], name: path[0] }];
  }
  return level
    .map((section) => {
      const own = [...path, section.name];
      return { key: keyOf(own), path: own, name: section.name };
    })
    .sort((a, b) => byName.compare(a.name, b.name));
}

/** The survey a labelled file's own surveys hang from, in a model loaded from several files. */
export function fileSurvey(tree: CaveViewTreeNode, label: string): CaveViewTreeNode | null {
  return tree.children.find((child) => !child.isStation() && child.name === label) ?? null;
}

/** A hundred metres: what two surveys may stand apart by beyond the length of the longer one. */
const ADJOINING_ALLOWANCE = 100;

/**
 * Whether two surveys lie in the same place, so that one can be laid over the other.
 *
 * <b>The viewer draws each file where its own numbers put it.</b> Two surveys of one cave made
 * about the same fixed point coincide. One made about a point of its own and one in a national
 * grid, or two made about different points, are drawn hundreds of kilometres apart — and a picture
 * of that offered as a comparison would be two specks at opposite corners of an empty box.
 *
 * They lie in the same place when their extents overlap or adjoin: when the gap between the two
 * boxes is no more than the longer survey is long, and a little over for surveys so small that
 * their own length says nothing. Surveys that pass may still be misplaced by less than that — two
 * local points a few metres apart cannot be told from a real difference between two surveys.
 *
 * A survey with no extent at all cannot be shown to be elsewhere, and passes.
 */
export function surveysLieTogether(
  a: CaveViewSectionBounds | null,
  b: CaveViewSectionBounds | null,
): boolean {
  if (a === null || b === null) {
    return true;
  }
  const axes = ['x', 'y', 'z'] as const;
  const gap = Math.hypot(
    ...axes.map((axis) => Math.max(0, a.min[axis] - b.max[axis], b.min[axis] - a.max[axis])),
  );
  const length = (box: CaveViewSectionBounds) =>
    Math.hypot(...axes.map((axis) => box.max[axis] - box.min[axis]));
  return gap <= Math.max(length(a), length(b)) + ADJOINING_ALLOWANCE;
}

/** Which parts of each survey are hidden, by their keys. Nothing hidden is the starting state. */
export type CompareHidden = Record<CompareSide, ReadonlySet<string>>;

export const NOTHING_HIDDEN: CompareHidden = { primary: new Set(), other: new Set() };

/** The parts each survey offers, or null for a survey whose file has not been read yet. */
export type CompareLists = Record<CompareSide, readonly CompareSurvey[] | null>;

const otherSide = (side: CompareSide): CompareSide => (side === 'primary' ? 'other' : 'primary');

/**
 * Shows or hides one part of one survey — and, in step, the part of the same path in the other.
 *
 * In step means only that: a part the other survey does not have is left alone, which is the
 * ordinary case for a passage found since the earlier survey was made.
 */
export function setSurveyShown(
  hidden: CompareHidden,
  side: CompareSide,
  keys: readonly string[],
  shown: boolean,
  inStep: boolean,
  lists: CompareLists,
): CompareHidden {
  const apply = (before: ReadonlySet<string>, changing: readonly string[]) => {
    const after = new Set(before);
    for (const key of changing) {
      if (shown) {
        after.delete(key);
      } else {
        after.add(key);
      }
    }
    return after;
  };
  const next = { ...hidden, [side]: apply(hidden[side], keys) };
  if (inStep) {
    const across = otherSide(side);
    const there = new Set((lists[across] ?? []).map((survey) => survey.key));
    next[across] = apply(
      hidden[across],
      keys.filter((key) => there.has(key)),
    );
  }
  return next;
}

/** Shows every part of one survey — and, in step, the same parts of the other. */
export function showAllSurveys(
  hidden: CompareHidden,
  side: CompareSide,
  inStep: boolean,
  lists: CompareLists,
): CompareHidden {
  const keys = (lists[side] ?? []).map((survey) => survey.key);
  return setSurveyShown(hidden, side, keys, true, inStep, lists);
}

/**
 * Brings one viewer's hidden surveys to what is asked for one file drawn in it.
 *
 * `under` is where that file's surveys hang in this viewer's tree: nothing for a viewer showing
 * the file alone, its label for a viewer showing it with another. Only what differs is touched —
 * every call redraws the model — and only the listed parts are, so what is hidden of the other
 * file in the same viewer is left as it is.
 */
export function applyHiddenSurveys(
  viewer: Pick<CaveViewer, 'getHiddenSections' | 'setSectionVisible'>,
  under: readonly string[],
  surveys: readonly CompareSurvey[],
  hidden: ReadonlySet<string>,
): void {
  const now = new Set(viewer.getHiddenSections().map((path) => JSON.stringify(path)));
  for (const survey of surveys) {
    const ref: CaveViewRef = [...under, ...survey.path];
    const want = hidden.has(survey.key);
    if (now.has(JSON.stringify(ref)) !== want) {
      viewer.setSectionVisible(ref, !want);
    }
  }
}

/**
 * Which of the viewer's own controls go over a model that is being compared.
 *
 * Taken out of the set the panel would otherwise show, never added to it. Fullscreen goes in both
 * arrangements: it puts one drawing surface over the whole screen, which is half of a comparison
 * with the names, the list of parts and the way back all left behind it. The shading chooser goes
 * where one survey is laid over the other, because there the colours are the two surveys, and a
 * model shaded by height is two surveys nobody can tell apart under a key that still names them.
 */
export function compareToolbarButtons(buttons: readonly string[], mode: CompareMode): string[] {
  return buttons.filter(
    (button) => button !== 'fullscreen' && (mode === 'sideBySide' || button !== 'shadingMode'),
  );
}
