// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { placeLabelFor, type PublishedPlace } from './publicPlaces.ts';

const place = (station: string, depthM: number | null, label: string): PublishedPlace => ({
  station,
  depthM,
  label,
  note: null,
  landmark: false,
});

const places: PublishedPlace[] = [
  place('cave.ent.0', 20, 'Balconul'),
  place('cave.upper.2', 50, 'Sala Mare'),
  place('cave.upper.2', 55, 'Sala Mare, podea'),
  place('cave.deep.3', 120, 'Sifonul'),
  // Named without a depth, so after every depth: a station named both ways answers to its depth.
  place('cave.deep.3', null, 'Sifonul de jos'),
  place('cave.mid.1', null, 'Joncțiunea'),
];

describe('placeLabelFor', () => {
  it('answers what the cave calls the station somebody was reported at', () => {
    expect(placeLabelFor('cave.deep.3', places)).toBe('Sifonul');
    expect(placeLabelFor('cave.ent.0', places)).toBe('Balconul');
  });

  it('answers the shallower name where one station was given two', () => {
    expect(placeLabelFor('cave.upper.2', places)).toBe('Sala Mare');
  });

  it('answers the name of a place named without a depth', () => {
    expect(placeLabelFor('cave.mid.1', places)).toBe('Joncțiunea');
  });

  it('answers the depth\'s name where one station was named both with a depth and without one', () => {
    expect(placeLabelFor('cave.deep.3', places)).toBe('Sifonul');
  });

  it('answers nothing for a station the cave has not named', () => {
    expect(placeLabelFor('cave.side.8', places)).toBeNull();
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
    expect(placeLabelFor('cave.deep.3', [place('cave.deep.3', 120, '')])).toBeNull();
  });
});
