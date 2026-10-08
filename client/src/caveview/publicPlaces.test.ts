// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { placeLabelFor, type PublishedPlace } from './publicPlaces.ts';

const places: PublishedPlace[] = [
  { station: 'cave.ent.0', depthM: 20, label: 'Balconul' },
  { station: 'cave.upper.2', depthM: 50, label: 'Sala Mare' },
  { station: 'cave.upper.2', depthM: 55, label: 'Sala Mare, podea' },
  { station: 'cave.deep.3', depthM: 120, label: 'Sifonul' },
];

describe('placeLabelFor', () => {
  it('answers what the cave calls the station somebody was reported at', () => {
    expect(placeLabelFor('cave.deep.3', places)).toBe('Sifonul');
    expect(placeLabelFor('cave.ent.0', places)).toBe('Balconul');
  });

  it('answers the shallower name where one station was given two', () => {
    expect(placeLabelFor('cave.upper.2', places)).toBe('Sala Mare');
  });

  it('answers nothing for a station the cave has not named', () => {
    expect(placeLabelFor('cave.mid.1', places)).toBeNull();
  });

  it('matches the station as written, and no near spelling of it', () => {
    expect(placeLabelFor('CAVE.DEEP.3', places)).toBeNull();
    expect(placeLabelFor('deep.3', places)).toBeNull();
    expect(placeLabelFor(' cave.deep.3', places)).toBeNull();
  });

  it('answers nothing where nobody was placed at a station', () => {
    expect(placeLabelFor(null, places)).toBeNull();
    expect(placeLabelFor(undefined, places)).toBeNull();
    expect(placeLabelFor('', places)).toBeNull();
  });

  it('answers nothing from an empty list, which is what an installation that publishes none sends', () => {
    // The positive case beside it: the same station is named once the list holds it.
    expect(placeLabelFor('cave.deep.3', [])).toBeNull();
    expect(placeLabelFor('cave.deep.3', places)).toBe('Sifonul');
  });

  it('reads an answer that has no such list at all as one that names nothing', () => {
    expect(placeLabelFor('cave.deep.3', undefined)).toBeNull();
    expect(placeLabelFor('cave.deep.3', null)).toBeNull();
  });

  it('passes over a name that is empty rather than printing nothing as a name', () => {
    expect(placeLabelFor('cave.deep.3', [{ station: 'cave.deep.3', depthM: 120, label: '' }])).toBeNull();
  });
});
