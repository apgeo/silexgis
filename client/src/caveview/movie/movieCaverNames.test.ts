// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { initialsOf, movieCaverNames, type MovieCaverName } from './movieCaverNames.ts';

const person = (caverId: string, name: string): MovieCaverName => ({ caverId, name });
const named = (people: MovieCaverName[], mode: Parameters<typeof movieCaverNames>[1] = 'first') =>
  Object.fromEntries(movieCaverNames(people, mode));

describe('movieCaverNames', () => {
  it('names each person by the first word of their name', () => {
    expect(named([person('a', 'Ana Popescu'), person('b', 'Bogdan Ionescu')])).toEqual({ a: 'Ana', b: 'Bogdan' });
  });

  it('keeps a single-word name whole, and ignores runs of whitespace', () => {
    expect(named([person('a', 'Zamfir'), person('b', '  Ion \t  Popescu  ')])).toEqual({ a: 'Zamfir', b: 'Ion' });
    expect(named([person('b', '  Ion \t  Popescu  ')], 'full')).toEqual({ b: 'Ion Popescu' });
  });

  it('keeps the letters of a name as they are, diacritics included', () => {
    expect(named([person('a', 'Ștefan Țurcanu'), person('b', 'Ștefan Ursu')])).toEqual({
      a: 'Ștefan Ț.',
      b: 'Ștefan U.',
    });
  });

  it('adds the last word’s initial where two people share a first name, and only to them', () => {
    expect(
      named([person('a', 'Ion Popescu'), person('b', 'Ion Dima'), person('c', 'Maria Ionescu')]),
    ).toEqual({ a: 'Ion P.', b: 'Ion D.', c: 'Maria' });
  });

  it('uses the last word of a longer name, not the second', () => {
    expect(named([person('a', 'Ion Vasile Popescu'), person('b', 'Ion Dima')])).toEqual({ a: 'Ion P.', b: 'Ion D.' });
  });

  it('falls back to the full name where the initial still collides', () => {
    expect(
      named([person('a', 'Ion Popescu'), person('b', 'Ion Petrescu'), person('c', 'Ion Dima')]),
    ).toEqual({ a: 'Ion Popescu', b: 'Ion Petrescu', c: 'Ion D.' });
  });

  it('tells a one-word name from a longer one that begins with it', () => {
    expect(named([person('a', 'Ion'), person('b', 'Ion Popescu')])).toEqual({ a: 'Ion', b: 'Ion P.' });
  });

  it('counts names that differ only in capitals or in how an accent was typed as the same', () => {
    const decomposed = 'Ștefan Ursu'; // an S followed by a combining comma below
    expect(named([person('a', 'ion Popescu'), person('b', 'Ion Dima')])).toEqual({ a: 'ion P.', b: 'Ion D.' });
    expect(named([person('a', 'Ștefan Țurcanu'), person('b', decomposed)])).toEqual({
      a: 'Ștefan Ț.',
      b: 'Ștefan U.',
    });
  });

  it('counts collisions across every trip, and the same person on two trips as one', () => {
    // The rosters of two trips, one after the other: Ana is on both, and is nobody's namesake.
    const rosters = [person('ana', 'Ana Popescu'), person('ion', 'Ion Dima'), person('ana', 'Ana Popescu'), person('ion2', 'Ion Petrescu')];
    expect(named(rosters)).toEqual({ ana: 'Ana', ion: 'Ion D.', ion2: 'Ion P.' });
  });

  it('leaves two people with the same full name as they are, since nothing more can be said', () => {
    expect(named([person('a', 'Ion Popescu'), person('b', 'Ion Popescu')])).toEqual({
      a: 'Ion Popescu',
      b: 'Ion Popescu',
    });
  });

  it('answers full names, initials or nothing when asked', () => {
    const people = [person('a', 'Ana Popescu'), person('b', 'Ana Pavel')];
    expect(named(people, 'full')).toEqual({ a: 'Ana Popescu', b: 'Ana Pavel' });
    // Initials are not lengthened: the reader chose to say less.
    expect(named(people, 'initials')).toEqual({ a: 'AP', b: 'AP' });
    expect(named(people, 'off')).toEqual({ a: '', b: '' });
  });
});

describe('initialsOf', () => {
  it('takes the first letter of each word and hyphenated part, in any script', () => {
    expect(initialsOf('Ana Popescu')).toBe('AP');
    expect(initialsOf('Cora-Maria Dan')).toBe('CMD');
    expect(initialsOf('ștefan țurcanu')).toBe('ȘȚ');
    expect(initialsOf('  Ion   A. ')).toBe('IA');
    expect(initialsOf('3')).toBe('3');
  });
});
