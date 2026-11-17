// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';

import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { LITHOLOGY_CODES, lithologyLabel, typedEnumLabel } from './lithology.ts';

const t = ((key: string) => `t:${key}`) as unknown as TFunction;

describe('lithology labels', () => {
  it('names a shipped code in the reader language and shows any other code as stored', () => {
    expect(lithologyLabel(t, 'limestone')).toBe('t:geology.lithology.limestone');
    expect(lithologyLabel(t, 'chalk')).toBe('chalk');
    expect(lithologyLabel(t, null)).toBe('t:geology.unclassified');
  });

  it('translates only the geological unit lithology among typed enum values', () => {
    expect(typedEnumLabel(t, 'geological_unit', 'lithology', 'marble')).toBe('t:geology.lithology.marble');
    expect(typedEnumLabel(t, 'geological_unit', 'source', 'marble')).toBe('marble');
    expect(typedEnumLabel(t, 'water_flow', 'flow_kind', 'spring')).toBe('spring');
  });

  it('has an English and a Romanian name for every shipped code', () => {
    for (const code of LITHOLOGY_CODES) {
      expect(en.geology.lithology[code]).toBeTruthy();
      expect(ro.geology.lithology[code]).toBeTruthy();
    }
    expect(Object.keys(en.geology.lithology)).toEqual([...LITHOLOGY_CODES]);
  });
});
