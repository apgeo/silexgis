// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DEFAULT_MODEL_LABEL_SCALE, MODEL_LABEL_SCALES, modelLabelScale } from './labelScale.ts';

describe('the size a model\'s station names are written at', () => {
  it('is the viewer\'s own until somebody chooses another', () => {
    expect(modelLabelScale(undefined)).toBe(DEFAULT_MODEL_LABEL_SCALE);
    expect(MODEL_LABEL_SCALES).toContain(DEFAULT_MODEL_LABEL_SCALE);
  });

  it('is what was chosen, where that is one of the sizes offered', () => {
    for (const scale of MODEL_LABEL_SCALES) {
      expect(modelLabelScale(scale)).toBe(scale);
    }
  });

  it('is never something the viewer would refuse or that nobody was offered', () => {
    // The viewer warns on every model opened for a value that is not a number above zero, and a
    // size nobody was offered cannot be chosen again once it is changed.
    for (const stored of [0, -1, Number.NaN, '0.6', null, true, 0.55, 12]) {
      expect(modelLabelScale(stored)).toBe(DEFAULT_MODEL_LABEL_SCALE);
    }
  });
});
