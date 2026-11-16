// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { lithologyAppearance } from './lithologyAppearance.ts';
import { lithologyPalette } from './markerPalette.ts';

describe('lithologyAppearance', () => {
  it('fills a classified unit in its class colour and outlines karstifiable ground heavier', () => {
    const limestone = lithologyAppearance('limestone', true);
    const sandstone = lithologyAppearance('sandstone', false);
    expect(limestone.standing).toBe('karstifiable');
    expect(sandstone.standing).toBe('nonKarstifiable');
    expect(limestone.stroke).toBe(lithologyPalette.classes.limestone);
    expect(limestone.fill).toMatch(/^rgba\(/);
    expect(sandstone.fill).toMatch(/^rgba\(/);
    expect(limestone.width).toBeGreaterThan(sandstone.width);
    expect(limestone.lineDash).toBeNull();
  });

  it('draws an unclassified unit unfilled behind a dashed outline', () => {
    for (const code of [null, undefined, '']) {
      const look = lithologyAppearance(code, null);
      expect(look.standing).toBe('unclassified');
      expect(look.fill).toBeNull();
      expect(look.lineDash).not.toBeNull();
    }
  });

  it('tells an unrecognised class apart from an unclassified one', () => {
    const unknown = lithologyAppearance('chalk', null);
    const unclassified = lithologyAppearance(null, null);
    expect(unknown.standing).toBe('unrecognised');
    expect(unknown.stroke).not.toBe(unclassified.stroke);
    expect(unknown.lineDash).not.toEqual(unclassified.lineDash);
  });

  it('takes whether a class is karstifiable from the answer, never from the colour table', () => {
    // A shipped code the server did not flag is drawn as not recognised rather than guessed.
    expect(lithologyAppearance('limestone', null).standing).toBe('unrecognised');
  });

  it('gives every class its own colour', () => {
    const colours = Object.values(lithologyPalette.classes);
    expect(new Set(colours).size).toBe(colours.length);
  });
});
