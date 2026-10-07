// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { MOVIE_SHADINGS, movieShadingId, movieShadingNumber, movieShadingRememberedAs } from './movieShadings.ts';

describe('the shadings a movie can be drawn in', () => {
  it('are named once each, each by a constant of its own', () => {
    expect(new Set(MOVIE_SHADINGS.map(({ id }) => id)).size).toBe(MOVIE_SHADINGS.length);
    expect(new Set(MOVIE_SHADINGS.map(({ constant }) => constant)).size).toBe(MOVIE_SHADINGS.length);
  });

  it('are recognised by name only', () => {
    expect(movieShadingId('depthCursor')).toBe('depthCursor');
    for (const value of ['Depth', 'SHADING_DEPTH', '', 9, null, undefined, {}]) {
      expect(movieShadingId(value)).toBeNull();
    }
  });

  it('are turned into the number the loaded viewer has for them', () => {
    expect(movieShadingNumber('height', { SHADING_HEIGHT: 31 })).toBe(31);
    expect(movieShadingNumber('depthCursor', { SHADING_HEIGHT: 31, SHADING_DEPTH_CURSOR: 0 })).toBe(0);
    // A viewer without the constant has no number for it.
    expect(movieShadingNumber('survey', { SHADING_HEIGHT: 31 })).toBeNull();
  });

  it('read back from the numbers they were once remembered as, each to a shading still offered', () => {
    expect(
      [1, 2, 3, 5, 6, 9, 11].map(movieShadingRememberedAs),
    ).toEqual(['height', 'length', 'inclination', 'single', 'survey', 'depth', 'depthCursor']);
    // Every shading offered could be remembered that way, and no two numbers stood for the same one.
    expect(new Set([1, 2, 3, 5, 6, 9, 11].map(movieShadingRememberedAs))).toEqual(
      new Set(MOVIE_SHADINGS.map(({ id }) => id)),
    );
  });

  it('read nothing from a number that stood for a shading a movie never offered, or from anything else', () => {
    for (const value of [0, 4, 7, 8, 10, 12, 13, 2.5, -1, Number.NaN, '1', 'height', null, undefined]) {
      expect(movieShadingRememberedAs(value), String(value)).toBeNull();
    }
  });
});
