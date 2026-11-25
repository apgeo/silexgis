// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TFunction } from 'i18next';
import { typedEnumLabel, typedFieldLabel, typedValueText } from './lithology.ts';
import {
  MEASUREMENT_TYPES,
  STRUCTURAL_MEASUREMENT_TYPE_CODE,
  measurementTypeLabel,
  withOrientationEntry,
} from './structural.ts';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';

const t = ((key: string) => `t:${key}`) as unknown as TFunction;

describe('structural readings', () => {
  it('sends a strike under its own key and drops the old dip direction', () => {
    const typed = { measurement_type: 'bedding', dip_direction: 30, dip: 40, note: 'n' };
    expect(withOrientationEntry(typed, 'strike')).toEqual({
      measurement_type: 'bedding',
      strike: 30,
      dip: 40,
      note: 'n',
    });
    // Entered as dip direction, the document goes as typed.
    expect(withOrientationEntry(typed, 'dipDirection')).toBe(typed);
    // Nothing typed: no strike is invented, and the old dip direction still goes.
    expect(withOrientationEntry({ dip: 10 }, 'strike')).toEqual({ dip: 10 });
  });

  it('names a shipped plane kind in the reader language and any other code as stored', () => {
    expect(measurementTypeLabel(t, 'joint')).toBe('t:structural.types.joint');
    expect(measurementTypeLabel(t, 'lineation')).toBe('lineation');
    expect(typedEnumLabel(t, STRUCTURAL_MEASUREMENT_TYPE_CODE, 'measurement_type', 'fault')).toBe(
      't:structural.types.fault',
    );
    // Another kind's enum with the same key is not translated by this vocabulary.
    expect(typedEnumLabel(t, 'sinkhole', 'measurement_type', 'fault')).toBe('fault');
  });

  it('labels the shipped fields in the reader language and an added field by its title', () => {
    expect(typedFieldLabel(t, STRUCTURAL_MEASUREMENT_TYPE_CODE, 'dip', 'Dip (°)')).toBe('t:structural.fields.dip');
    expect(typedFieldLabel(t, STRUCTURAL_MEASUREMENT_TYPE_CODE, 'roughness', 'Roughness')).toBe('Roughness');
    expect(typedFieldLabel(t, 'sinkhole', 'dip', 'Dip')).toBe('Dip');
    expect(typedValueText(t, STRUCTURAL_MEASUREMENT_TYPE_CODE, 'dip', 40)).toBe('40');
  });

  it('ships words for every plane kind in both languages', () => {
    for (const type of MEASUREMENT_TYPES) {
      expect(en.structural.types[type]).toBeTruthy();
      expect(ro.structural.types[type]).toBeTruthy();
    }
  });
});
