// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  WALL_COLOR_HEX,
  WALL_LINEAR_RGB,
  estimatedRecolour,
  hexFromLinear,
  linearFromHex,
} from './estimatedPassages.ts';

describe('colours of passages of estimated size', () => {
  it('shows the walls swatch in the colour the walls are drawn in', () => {
    // The walls' material states linear values; a swatch is sRGB. Converted back, the swatch is
    // the walls' colour to within one step of eight bits.
    const back = linearFromHex(WALL_COLOR_HEX)!;
    back.forEach((channel, i) => expect(Math.abs(channel - WALL_LINEAR_RGB[i]!)).toBeLessThan(0.005));
  });

  it('turns a chosen colour into linear values and back', () => {
    expect(hexFromLinear(linearFromHex('#3a7bd5')!)).toBe('#3a7bd5');
    expect(linearFromHex('#ffffff')).toEqual([1, 1, 1]);
    expect(linearFromHex('blue')).toBeUndefined();
  });

  it('recolours nothing for the built colour, and uses the walls exact values for theirs', () => {
    expect(estimatedRecolour('built')).toBeUndefined();
    expect(estimatedRecolour('walls')).toBe(WALL_LINEAR_RGB);
    expect(estimatedRecolour('#000000')).toEqual([0, 0, 0]);
  });
});
