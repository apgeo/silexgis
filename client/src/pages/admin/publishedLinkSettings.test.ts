// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { settingSpanOf } from './publishedLinkSettings.ts';

describe('a period set by an installation, as it is printed', () => {
  it('is said in the largest unit that holds it exactly', () => {
    expect(settingSpanOf(14 * 86_400)).toEqual({ unit: 'days', amount: 14 });
    expect(settingSpanOf(86_400)).toEqual({ unit: 'days', amount: 1 });
    expect(settingSpanOf(36 * 3_600)).toEqual({ unit: 'hours', amount: 36 });
    expect(settingSpanOf(90 * 60)).toEqual({ unit: 'minutes', amount: 90 });
    expect(settingSpanOf(60)).toEqual({ unit: 'minutes', amount: 1 });
  });

  it('never rounds: a period no larger unit divides is said in seconds', () => {
    expect(settingSpanOf(90)).toEqual({ unit: 'seconds', amount: 90 });
    expect(settingSpanOf(86_401)).toEqual({ unit: 'seconds', amount: 86_401 });
    expect(settingSpanOf(1)).toEqual({ unit: 'seconds', amount: 1 });
  });

  it('says nothing as zero of a larger unit', () => {
    expect(settingSpanOf(0)).toEqual({ unit: 'seconds', amount: 0 });
  });
});
