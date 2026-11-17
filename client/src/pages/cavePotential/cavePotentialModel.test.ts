// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  JUDGMENT_CODES,
  definitionOf,
  draftOf,
  emptyDraft,
  isFilledIn,
  judgmentCode,
  judgmentOf,
  judgmentValue,
  mapAddressOf,
  newLayerDraft,
  withJudgment,
  type EvidenceLayerInfo,
  type ModelDraft,
} from './cavePotentialModel.ts';

const slope: EvidenceLayerInfo = {
  code: 'slope',
  valueKind: 'continuous',
  source: 'terrain',
  usesPositions: false,
  unit: 'deg',
  transforms: ['classes', 'fuzzyLinear'],
  categories: [],
};
const aspect: EvidenceLayerInfo = { ...slope, code: 'aspect', valueKind: 'circular', transforms: ['classes'] };
const lithology: EvidenceLayerInfo = {
  code: 'lithology',
  valueKind: 'categorical',
  source: 'features',
  usesPositions: true,
  unit: null,
  transforms: ['categories'],
  categories: ['limestone', 'dolomite', 'granite'],
};
const infos = [slope, aspect, lithology];

function draft(over: Partial<ModelDraft>): ModelDraft {
  return { ...emptyDraft(), name: 'Model', ...over };
}

describe('pairwise judgments', () => {
  it('reads a pair the other way round as its reciprocal, and an unsaid pair as equal', () => {
    const judgments = withJudgment({}, 'slope', 'lithology', 5);
    expect(judgmentOf(judgments, 'slope', 'lithology')).toBe(5);
    expect(judgmentOf(judgments, 'lithology', 'slope')).toBeCloseTo(0.2);
    expect(judgmentOf(judgments, 'slope', 'aspect')).toBe(1);
  });

  it('keeps one entry for a pair however it is said', () => {
    const first = withJudgment({}, 'slope', 'lithology', 5);
    const second = withJudgment(first, 'lithology', 'slope', 3);
    expect(Object.keys(second)).toEqual(['lithology|slope']);
    expect(judgmentOf(second, 'slope', 'lithology')).toBeCloseTo(1 / 3);
  });

  it('finds the step of the scale a stored decimal stands on', () => {
    // A ninth is stored as a decimal and must come back as the ninth, not as the eighth.
    expect(judgmentCode(0.111)).toBe('1/9');
    expect(judgmentCode(1 / 3)).toBe('1/3');
    expect(judgmentCode(7)).toBe('7');
    for (const code of JUDGMENT_CODES) {
      expect(judgmentCode(judgmentValue(code))).toBe(code);
    }
  });
});

describe('the definition a form is written as', () => {
  it('writes weights and no comparisons for a model with weights given outright', () => {
    const ramp = { ...newLayerDraft(slope), low: 0, high: 30, weight: 3 };
    const rock = {
      ...newLayerDraft(lithology),
      categories: { limestone: 1, dolomite: 0.8, granite: null },
      weight: 1,
    };
    const definition = definitionOf(draft({ layers: [ramp, rock] }));

    expect(definition.pairwise).toBeUndefined();
    expect(definition.layers).toEqual([
      { layer: 'slope', transform: { kind: 'fuzzyLinear', low: 0, high: 30 }, weight: 3 },
      {
        layer: 'lithology',
        // The class with no score is left out; the server reads it as nought.
        transform: { kind: 'categories', categories: [{ code: 'limestone', score: 1 }, { code: 'dolomite', score: 0.8 }] },
        weight: 1,
      },
    ]);
  });

  it('writes the judgments above the diagonal and no weights for a model weighed by comparisons', () => {
    const layers = [newLayerDraft(slope), newLayerDraft(aspect), newLayerDraft(lithology)];
    let judgments = withJudgment({}, 'slope', 'aspect', 3);
    judgments = withJudgment(judgments, 'lithology', 'slope', 2);
    const definition = definitionOf(draft({ method: 'pairwiseComparison', layers, judgments }));

    // One row for every layer but the last: slope against the two after it, aspect against the one.
    expect(definition.pairwise).toEqual([[3, 0.5], [1]]);
    expect(definition.layers.every((layer) => !('weight' in layer))).toBe(true);
  });

  it('never writes an upper bound on the last row of a table', () => {
    const table = {
      ...newLayerDraft(aspect),
      classes: [{ upperBound: 90, score: 0.2 }, { upperBound: 270, score: 1 }, { upperBound: 300, score: 0.2 }],
    };
    expect(definitionOf(draft({ layers: [table] })).layers[0].transform.classes).toEqual([
      { upperBound: 90, score: 0.2 },
      { upperBound: 270, score: 1 },
      { upperBound: null, score: 0.2 },
    ]);
  });

  it('opens a stored model as the form that would write it back unchanged', () => {
    const stored = {
      layers: [
        { layer: 'slope', transform: { kind: 'fuzzyLinear' as const, low: 0, high: 30 } },
        { layer: 'lithology', transform: { kind: 'categories' as const, categories: [{ code: 'limestone', score: 1 }] } },
      ],
      pairwise: [[5]],
    };
    const opened = draftOf(
      {
        id: 'm-1',
        name: 'Stored',
        description: null,
        method: 'pairwiseComparison',
        definition: stored,
        weighting: null,
        usesPositions: true,
        cavingGroupId: 'g-1',
        visibility: 'cavingGroup',
        mine: true,
        authorName: null,
        mayEdit: true,
        mayDelete: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
      infos,
    );

    expect(opened.audience).toEqual({ visibility: 'cavingGroup', cavingGroupId: 'g-1' });
    expect(definitionOf(opened)).toEqual(stored);
  });
});

describe('whether a form has any blank left', () => {
  it('is not filled in until every layer has its numbers', () => {
    expect(isFilledIn(draft({ layers: [] }))).toBe(false);
    const blank = newLayerDraft(slope);
    expect(isFilledIn(draft({ layers: [blank] }))).toBe(false);
    expect(isFilledIn(draft({ layers: [{ ...blank, low: 0, high: 30 }] }))).toBe(true);
    // A ramp between a value and itself is no ramp.
    expect(isFilledIn(draft({ layers: [{ ...blank, low: 5, high: 5 }] }))).toBe(false);
  });

  it('asks a table for rising bounds and a score on every row', () => {
    const table = { ...newLayerDraft(aspect), classes: [{ upperBound: 180, score: 0.5 }, { upperBound: null, score: 1 }] };
    expect(isFilledIn(draft({ layers: [table] }))).toBe(true);
    const falling = { ...table, classes: [{ upperBound: 180, score: 0.5 }, { upperBound: 90, score: 0.5 }, { upperBound: null, score: 1 }] };
    expect(isFilledIn(draft({ layers: [falling] }))).toBe(false);
    const unscored = { ...table, classes: [{ upperBound: 180, score: null }, { upperBound: null, score: 1 }] };
    expect(isFilledIn(draft({ layers: [unscored] }))).toBe(false);
  });

  it('asks a layer of classes for at least one score, and a weight only where weights are given outright', () => {
    const rock = newLayerDraft(lithology);
    expect(isFilledIn(draft({ layers: [rock] }))).toBe(false);
    const scored = { ...rock, categories: { ...rock.categories, limestone: 1 } };
    expect(isFilledIn(draft({ layers: [scored] }))).toBe(true);
    expect(isFilledIn(draft({ layers: [{ ...scored, weight: null }] }))).toBe(false);
    expect(isFilledIn(draft({ method: 'pairwiseComparison', layers: [{ ...scored, weight: null }] }))).toBe(true);
  });
});

describe('the map address of a result', () => {
  it('looks at the middle of the grid, further out for a larger one', () => {
    const small = mapAddressOf({ west: 22.5, south: 46.5, east: 22.51, north: 46.51 });
    const large = mapAddressOf({ west: 22, south: 46, east: 23, north: 47 });
    expect(small).toMatch(/^\/map#\d+\.\d\d\/46\.50500\/22\.50500$/);
    const zoom = (address: string) => Number(address.slice('/map#'.length).split('/')[0]);
    expect(zoom(small)).toBeGreaterThan(zoom(large));
    expect(zoom(large)).toBeGreaterThanOrEqual(3);
  });

  it('is the plain map for a result with no grid yet', () => {
    expect(mapAddressOf({ west: null, south: null, east: null, north: null })).toBe('/map');
  });
});
