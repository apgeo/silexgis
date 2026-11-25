// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeAll, describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import type { CavePotentialDefinition } from '../../api/hooks/cavePotential.ts';
import { classLabel } from './cavePotentialNames.ts';

const t = i18n.t.bind(i18n);

const asRun: CavePotentialDefinition = {
  layers: [
    {
      layer: 'slope',
      transform: {
        kind: 'classes',
        classes: [{ upperBound: 10, score: null }, { upperBound: 25, score: null }, { upperBound: null, score: null }],
      },
    },
  ],
  training: { heldOutShare: 0.3, seed: 1 },
};

beforeAll(async () => {
  await i18n.changeLanguage('en');
});

describe('what a class of a weight table is called', () => {
  it('names a class of a measured layer by the bounds the model was run with', () => {
    expect(classLabel(t, 'slope', 0, asRun, { categories: [] })).toBe('below 10');
    // A value on a bound belongs to the class that starts there.
    expect(classLabel(t, 'slope', 1, asRun, { categories: [] })).toBe('10 up to 25');
    expect(classLabel(t, 'slope', 2, asRun, { categories: [] })).toBe('25 and above');
  });

  it('says only which class it is, in order, to a reader who is not shown the model', () => {
    expect(classLabel(t, 'slope', 1, null, { categories: [] })).toBe('Class 2');
    // Nor for a layer or a row the definition does not hold.
    expect(classLabel(t, 'aspect', 0, asRun, undefined)).toBe('Class 1');
    expect(classLabel(t, 'slope', 5, asRun, { categories: [] })).toBe('Class 6');
  });

  it('names a class of a layer that is classes already by its own word', () => {
    expect(classLabel(t, 'lithology', 1, null, { categories: ['limestone', 'dolomite'] })).toBe(
      i18n.t('geology.lithology.dolomite'),
    );
  });
});
