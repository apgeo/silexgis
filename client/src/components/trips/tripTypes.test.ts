// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import type { TripType } from '../../api/hooks.ts';
import { tripTypeLabel, tripTypeLabelOf } from './tripTypes.ts';

const t = i18n.t.bind(i18n);

function type(id: number, code: string, name: string, isSeeded: boolean): TripType {
  return { id, code, name, description: null, sortOrder: 0, isSeeded } as TripType;
}

const survey = type(1, 'survey', 'Survey / mapping', true);
const batCount = type(2, 'bat_count', 'Bat count', false);

describe('trip purpose wording', () => {
  it('translates a shipped purpose and shows a club purpose as it was written', () => {
    // Both halves together: the shipped list is translated, and anything an installation added
    // is shown exactly as whoever added it wrote it. A resolver that translated everything would
    // render a club's own row as a missing key, and one that translated nothing would leave the
    // shipped rows in English wherever the application is read in another language.
    expect(tripTypeLabel(survey, t)).toBe('Survey / mapping');
    expect(tripTypeLabel(batCount, t)).toBe('Bat count');

    // The shipped wording is this client's, not the row's: an installation that renamed the row
    // in its own database still reads the translation for a code the client ships wording for.
    expect(tripTypeLabel({ ...survey, name: 'Renamed on the server' }, t)).toBe('Survey / mapping');
  });

  it('says nothing at all for a trip with no purpose, or one the vocabulary has not arrived for', () => {
    expect(tripTypeLabelOf(null, [survey], t)).toBeNull();
    expect(tripTypeLabelOf(undefined, [survey], t)).toBeNull();
    // The list is still loading, or names a row this caller's copy does not have. An identity is
    // not a label, so nothing is shown rather than a raw number.
    expect(tripTypeLabelOf(1, undefined, t)).toBeNull();
    expect(tripTypeLabelOf(99, [survey], t)).toBeNull();
    expect(tripTypeLabelOf(2, [survey, batCount], t)).toBe('Bat count');
  });
});
