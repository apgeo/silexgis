// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { formatScale, parseScale } from './scale.ts';

describe('parseScale', () => {
  it.each([
    ['1:25000', 25000],
    ['25000', 25000],
    ['1 : 25 000', 25000],
    ['1:25.000', 25000],
    ['25,000', 25000],
    ['1/50000', 50000],
    ['1:25 000', 25000],
    ['  1:5000  ', 5000],
  ])('reads %j as 1:%d', (text, expected) => {
    expect(parseScale(text)).toBe(expected);
  });

  it.each([[''], ['abc'], ['1:'], ['1:0'], ['-5000'], ['1:25000x'], ['2:25000'], ['10'], ['1:9999999999']])(
    'refuses %j',
    (text) => {
      expect(parseScale(text)).toBeNull();
    },
  );
});

describe('formatScale', () => {
  it('groups the digits the reader’s way', () => {
    expect(formatScale(25000, 'en')).toBe('1:25,000');
    expect(formatScale(25000, 'ro')).toBe('1:25.000');
  });
});
