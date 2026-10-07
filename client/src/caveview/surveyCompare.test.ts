// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { SurveyModelInfo } from '../api/hooks.ts';
import type { CaveViewSectionBounds, CaveViewTreeNode } from './loadCaveView.ts';
import {
  COMPARE_COLOURS,
  NOTHING_HIDDEN,
  applyHiddenSurveys,
  compareLabels,
  compareOffer,
  compareToolbarButtons,
  comparisonUnderWay,
  fileSurvey,
  setSurveyShown,
  showAllSurveys,
  surveysLieTogether,
  surveysOf,
  type CompareLists,
} from './surveyCompare.ts';

const model = (id: string, format: SurveyModelInfo['format'], name = id): SurveyModelInfo =>
  ({ id, name, format, modelUrl: `http://files.local/${id}`, caveId: 'cave' }) as SurveyModelInfo;

/** A survey tree written as nested names: an object is a survey, null a station. */
type Spec = { [name: string]: Spec | null };
const tree = (spec: Spec, name = ''): CaveViewTreeNode => ({
  name,
  isStation: () => false,
  children: Object.entries(spec).map(([child, inner]) =>
    inner === null ? { name: child, isStation: () => true, children: [] } : tree(inner, child),
  ),
});

const box = (min: [number, number, number], max: [number, number, number]): CaveViewSectionBounds => ({
  min: { x: min[0], y: min[1], z: min[2] },
  max: { x: max[0], y: max[1], z: max[2] },
});

describe('what a survey can be compared with', () => {
  it('is the cave\'s other line plots, and nothing when there is none', () => {
    const models = [model('a', 'survex3d'), model('b', 'lox'), model('walls', 'stl')];

    const offer = compareOffer(models, 'a');
    expect(offer?.current.id).toBe('a');
    // A wall mesh is not something the viewer reads, so it is not something to lay over a plot.
    expect(offer?.others.map((other) => other.id)).toEqual(['b']);
    expect(offer?.others[0]).toMatchObject({ fileUrl: 'http://files.local/b', fileName: 'b.lox' });

    // One line plot and a mesh is one line plot: fewer than two, so nothing to compare.
    expect(compareOffer([model('a', 'survex3d'), model('walls', 'stl')], 'a')).toBeNull();
    expect(compareOffer([model('a', 'survex3d')], 'a')).toBeNull();
    // The list not read yet, a survey that is not on it, and a mesh asked about.
    expect(compareOffer(undefined, 'a')).toBeNull();
    expect(compareOffer(models, 'gone')).toBeNull();
    expect(compareOffer(models, 'walls')).toBeNull();
  });

  it('is under way only while the other survey is still on offer', () => {
    const offer = compareOffer([model('a', 'survex3d'), model('b', 'lox')], 'a')!;

    expect(comparisonUnderWay(offer, { otherId: 'b', mode: 'overlaid' })).toMatchObject({
      mode: 'overlaid',
      other: { id: 'b' },
      current: { id: 'a' },
    });
    expect(comparisonUnderWay(offer, null)).toBeNull();
    expect(comparisonUnderWay(undefined, { otherId: 'b', mode: 'overlaid' })).toBeNull();
    // Deleted from the cave's list while it was being compared with.
    expect(comparisonUnderWay(offer, { otherId: 'deleted', mode: 'sideBySide' })).toBeNull();
  });
});

describe('the names two surveys go by in one model', () => {
  it('are their own names, made fit to be one name of a path', () => {
    expect(compareLabels('Survey 2023', 'Survey 2024')).toEqual({
      primary: 'Survey 2023',
      other: 'Survey 2024',
    });
    // A full stop separates the names of a path, and the viewer refuses one inside a label.
    const dotted = compareLabels('P8 v1.2', '  resurvey.final  ');
    expect(dotted).toEqual({ primary: 'P8 v1 2', other: 'resurvey final' });
    expect(Object.values(dotted).some((label) => label.includes('.'))).toBe(false);
  });

  it('are never the same name twice, and never empty', () => {
    // Two files under one label are one survey again, which is what a label exists to prevent.
    expect(compareLabels('P8 Master', 'P8 Master')).toEqual({
      primary: 'P8 Master (1)',
      other: 'P8 Master (2)',
    });
    const blank = compareLabels('...', '');
    expect(blank.primary).not.toBe('');
    expect(blank.other).not.toBe('');
    expect(blank.primary).not.toBe(blank.other);
    // Long names are cut, and two that become the same by it are still told apart.
    const long = compareLabels(`${'a'.repeat(60)} one`, `${'a'.repeat(60)} two`);
    expect(long.primary.length).toBeLessThanOrEqual(44);
    expect(long.primary).not.toBe(long.other);
  });
});

describe('the parts of a survey that can be shown and hidden', () => {
  it('are the first level of the tree that offers a choice', () => {
    // One survey named for the cave wrapping everything: stepped through, not listed.
    const wrapped = tree({ p8: { main: { 1: null }, bens_dig: { 1: null }, aven10: {}, aven2: {} } });

    expect(surveysOf(wrapped).map((survey) => survey.name)).toEqual(['aven2', 'aven10', 'bens_dig', 'main']);
    expect(surveysOf(wrapped)[0]).toEqual({ key: '["p8","aven2"]', path: ['p8', 'aven2'], name: 'aven2' });

    // Several at the top: those are the choice.
    const flatTop = tree({ upper: { 1: null }, lower: { 1: null } });
    expect(surveysOf(flatTop).map((survey) => survey.path)).toEqual([['lower'], ['upper']]);
  });

  it('are the whole survey, once, for a file that never branches', () => {
    const chain = tree({ cave: { all: { 1: null, 2: null } } });

    // The topmost survey, which holds everything: one tick still shows and hides the file.
    expect(surveysOf(chain)).toEqual([{ key: '["cave"]', path: ['cave'], name: 'cave' }]);
    expect(surveysOf(tree({ only: { 1: null } }))).toEqual([{ key: '["only"]', path: ['only'], name: 'only' }]);
  });

  it('are none for a file with stations and no survey, and for no file at all', () => {
    expect(surveysOf(tree({ 1: null, 2: null }))).toEqual([]);
    expect(surveysOf(null)).toEqual([]);
  });

  it('are found under a file\'s label in a model made of two', () => {
    const both = tree({ 'Survey 2023': { p8: { a: {}, b: {} } }, 'Survey 2024': { p8: { a: {}, c: {} } } });

    expect(surveysOf(fileSurvey(both, 'Survey 2024')).map((survey) => survey.key)).toEqual([
      '["p8","a"]',
      '["p8","c"]',
    ]);
    expect(fileSurvey(both, 'Survey 1999')).toBeNull();
  });
});

describe('whether two surveys can be laid over one another', () => {
  const cave = box([0, 0, -40], [300, 450, 20]);

  it('says yes to two that overlap or adjoin', () => {
    expect(surveysLieTogether(cave, cave)).toBe(true);
    expect(surveysLieTogether(cave, box([250, 400, -60], [600, 800, 0]))).toBe(true);
    // The far series of the same system, a few hundred metres on: the same coordinates.
    expect(surveysLieTogether(cave, box([700, 450, -40], [900, 600, 0]))).toBe(true);
    // Two small pieces a short walk apart, neither of them long enough to bridge the gap alone.
    expect(surveysLieTogether(box([0, 0, 0], [10, 10, 5]), box([60, 0, 0], [70, 10, 5]))).toBe(true);
  });

  it('says no to one about a point of its own and one in a national grid', () => {
    const inGrid = box([512_300, 468_900, 1180], [512_600, 469_350, 1240]);

    expect(surveysLieTogether(cave, inGrid)).toBe(false);
    expect(surveysLieTogether(inGrid, cave)).toBe(false);
    // The same place in plan, more than a kilometre apart in height: one of them is not from the
    // datum the other is.
    expect(surveysLieTogether(cave, box([0, 0, 1160], [300, 450, 1220]))).toBe(false);
  });

  it('cannot refuse a survey with no extent', () => {
    expect(surveysLieTogether(cave, null)).toBe(true);
    expect(surveysLieTogether(null, null)).toBe(true);
  });
});

describe('showing and hiding a part of one survey', () => {
  const lists: CompareLists = {
    primary: surveysOf(tree({ p8: { main: {}, bens_dig: {} } })),
    other: surveysOf(tree({ p8: { main: {}, bens_dig: {}, new_series: {} } })),
  };
  const key = (name: string) => JSON.stringify(['p8', name]);

  it('does the same to the part of that path in the other survey, in step', () => {
    const hidden = setSurveyShown(NOTHING_HIDDEN, 'primary', [key('bens_dig')], false, true, lists);

    expect([...hidden.primary]).toEqual([key('bens_dig')]);
    expect([...hidden.other]).toEqual([key('bens_dig')]);

    const shown = setSurveyShown(hidden, 'other', [key('bens_dig')], true, true, lists);
    expect(shown.primary.size).toBe(0);
    expect(shown.other.size).toBe(0);
  });

  it('touches only its own survey, out of step', () => {
    const both = setSurveyShown(NOTHING_HIDDEN, 'primary', [key('bens_dig')], false, true, lists);
    const one = setSurveyShown(both, 'primary', [key('bens_dig')], true, false, lists);

    expect(one.primary.size).toBe(0);
    expect([...one.other]).toEqual([key('bens_dig')]);
  });

  it('leaves the other survey alone for a part only one of them has', () => {
    // A passage found since the earlier survey was made: nothing in the earlier one answers to it.
    const hidden = setSurveyShown(NOTHING_HIDDEN, 'other', [key('new_series')], false, true, lists);

    expect([...hidden.other]).toEqual([key('new_series')]);
    expect(hidden.primary.size).toBe(0);
  });

  it('shows all of one survey, and in step the same parts of the other', () => {
    let hidden = setSurveyShown(NOTHING_HIDDEN, 'other', [key('main'), key('bens_dig'), key('new_series')], false, true, lists);
    expect(hidden.primary.size).toBe(2);
    expect(hidden.other.size).toBe(3);

    // Everything the first survey has is shown in both; the part only the second has stays as it was.
    hidden = showAllSurveys(hidden, 'primary', true, lists);
    expect(hidden.primary.size).toBe(0);
    expect([...hidden.other]).toEqual([key('new_series')]);

    hidden = setSurveyShown(NOTHING_HIDDEN, 'primary', [key('main')], false, true, lists);
    hidden = showAllSurveys(hidden, 'primary', false, lists);
    expect(hidden.primary.size).toBe(0);
    expect([...hidden.other]).toEqual([key('main')]);
  });
});

describe('bringing a viewer to what is asked of it', () => {
  const surveys = surveysOf(tree({ p8: { main: {}, bens_dig: {} } }));
  const viewerHiding = (...paths: string[][]) => ({
    getHiddenSections: () => paths,
    setSectionVisible: vi.fn(() => true),
  });

  it('touches only what differs, under the file\'s label where it has one', () => {
    const viewer = viewerHiding(['Survey 2023', 'p8', 'main'], ['Survey 2024', 'p8', 'bens_dig']);

    applyHiddenSurveys(viewer, ['Survey 2023'], surveys, new Set([JSON.stringify(['p8', 'bens_dig'])]));

    // `main` was hidden and is not asked to be; `bens_dig` is asked to be and was not. What is
    // hidden of the other file in the same viewer is none of this file's business.
    expect(viewer.setSectionVisible.mock.calls).toEqual([
      [['Survey 2023', 'p8', 'bens_dig'], false],
      [['Survey 2023', 'p8', 'main'], true],
    ]);
  });

  it('asks for nothing when the viewer already shows what is wanted', () => {
    const viewer = viewerHiding(['p8', 'bens_dig']);

    applyHiddenSurveys(viewer, [], surveys, new Set([JSON.stringify(['p8', 'bens_dig'])]));

    expect(viewer.setSectionVisible).not.toHaveBeenCalled();
  });
});

describe('the viewer\'s own controls over a model that is being compared', () => {
  const all = ['stations', 'viewPlan', 'shadingMode', 'fullscreen'];

  it('lose fullscreen, and the shading chooser where the colours are the two surveys', () => {
    expect(compareToolbarButtons(all, 'sideBySide')).toEqual(['stations', 'viewPlan', 'shadingMode']);
    expect(compareToolbarButtons(all, 'overlaid')).toEqual(['stations', 'viewPlan']);
  });
});

// ---- the two colours ----
//
// Computed, because whether two colours stay apart for a reader who sees colour differently is not
// something an eye that sees them all can judge.

const channel = (hex: string, at: number) => parseInt(hex.slice(at, at + 2), 16) / 255;
const linear = (value: number) => (value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
const linearRgb = (hex: string): [number, number, number] => [
  linear(channel(hex, 1)),
  linear(channel(hex, 3)),
  linear(channel(hex, 5)),
];

/** How far a colour stands from black, as the contrast ratio accessibility guidance uses. */
const contrastWithBlack = (hex: string) => {
  const [r, g, b] = linearRgb(hex);
  return (0.2126 * r + 0.7152 * g + 0.0722 * b + 0.05) / 0.05;
};

// What a reader lacking one kind of cone sees, as a matrix over linear RGB (Machado, Oliveira and
// Fernandes, 2009, at full severity).
const SEEN_WITHOUT = {
  red: [
    [0.152286, 1.052583, -0.204868],
    [0.114503, 0.786281, 0.099216],
    [-0.003882, -0.048116, 1.051998],
  ],
  green: [
    [0.367322, 0.860646, -0.227968],
    [0.280085, 0.672501, 0.047413],
    [-0.01182, 0.04294, 0.968881],
  ],
  blue: [
    [1.255528, -0.076749, -0.178779],
    [-0.078411, 0.930809, 0.147602],
    [0.004733, 0.691367, 0.3039],
  ],
} as const;

/** A colour in OKLab, a space in which equal distances look about equally different. */
const oklab = ([r, g, b]: readonly number[]) => {
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ];
};

const apart = (a: readonly number[], b: readonly number[]) =>
  100 * Math.hypot(...oklab(a).map((value, index) => value - oklab(b)[index]));

const seenWithout = (matrix: readonly (readonly number[])[], rgb: readonly number[]) =>
  matrix.map((row) => Math.min(1, Math.max(0, row[0] * rgb[0] + row[1] * rgb[1] + row[2] * rgb[2])));

describe('the colours two overlaid surveys are drawn in', () => {
  const first = linearRgb(COMPARE_COLOURS.primary);
  const second = linearRgb(COMPARE_COLOURS.other);

  it('both stand clear of the black the viewer draws on', () => {
    // Lines a pixel wide: three to one is the least a graphic is asked for, and both are well past.
    expect(contrastWithBlack(COMPARE_COLOURS.primary)).toBeGreaterThan(4.5);
    expect(contrastWithBlack(COMPARE_COLOURS.other)).toBeGreaterThan(4.5);
  });

  it('stay apart for a reader who lacks any one kind of cone', () => {
    // Eight is where two colours side by side are comfortably told apart; these keep three times
    // that under each simulation, so a line one pixel wide is still one colour or the other.
    expect(apart(first, second)).toBeGreaterThan(24);
    for (const matrix of Object.values(SEEN_WITHOUT)) {
      expect(apart(seenWithout(matrix, first), seenWithout(matrix, second))).toBeGreaterThan(24);
    }
  });
});
