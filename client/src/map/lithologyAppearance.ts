// SPDX-License-Identifier: AGPL-3.0-or-later
import { lithologyPalette } from './markerPalette.ts';

/** The kind a mapped geological unit carries — the code the server marks such a row with. */
export const GEOLOGICAL_UNIT_TYPE_CODE = 'geological_unit';

/** Which of the four ways a geological unit can be drawn applies to it. */
export type LithologyStanding = 'karstifiable' | 'nonKarstifiable' | 'unclassified' | 'unrecognised';

/** How one geological unit is drawn: free of any drawing library so a legend can read it too. */
export interface LithologyAppearance {
  standing: LithologyStanding;
  /** Outline colour. */
  stroke: string;
  /** Fill colour with its opacity, or null for a unit drawn as an outline only. */
  fill: string | null;
  width: number;
  /** Dash pattern of the outline, or null for a solid one. */
  lineDash: number[] | null;
}

function withAlpha(hex: string, alpha: number): string {
  const value = Number.parseInt(hex.slice(1), 16);
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`;
}

/**
 * A geological unit's appearance from what the map answer says about it: its stored lithology
 * code and whether the vocabulary calls that class karstifiable. The server sends the flag, so the
 * browser never decides which rock is karstifiable; it only chooses the colours.
 *
 * Karstifiable ground is outlined heavier than the rest, so the difference survives a colour-blind
 * reader and a basemap that swallows the fills.
 */
export function lithologyAppearance(lithology: unknown, karstifiable: unknown): LithologyAppearance {
  if (typeof lithology !== 'string' || lithology === '') {
    return {
      standing: 'unclassified',
      stroke: lithologyPalette.unclassified,
      fill: null,
      width: 1.5,
      lineDash: [6, 4],
    };
  }
  const colour = lithologyPalette.classes[lithology];
  if (colour === undefined || typeof karstifiable !== 'boolean') {
    return {
      standing: 'unrecognised',
      stroke: lithologyPalette.unrecognised,
      fill: null,
      width: 2,
      lineDash: [2, 4],
    };
  }
  return {
    standing: karstifiable ? 'karstifiable' : 'nonKarstifiable',
    stroke: colour,
    fill: withAlpha(colour, lithologyPalette.fillAlpha),
    width: karstifiable ? 2.5 : 1.25,
    lineDash: null,
  };
}
