// SPDX-License-Identifier: AGPL-3.0-or-later

// Passages drawn at an estimated size: the tubes a survey's reading builds, when asked to, around
// the legs the file gives no size for. They are guesses — one size for every such passage,
// whatever its real size — so they are built apart from the walls, in a colour of their own, and
// the scene can hide them or recolour them without touching the walls.

/** The name of the node in a built mesh that holds the estimated passages. */
export const ESTIMATED_NODE_NAME = 'estimated';

/**
 * The colour walls are drawn in, as the linear RGB their material states. The scene uses these
 * exact values to draw estimated passages "the same as the walls", so the two cannot drift apart
 * by a rounding step.
 */
export const WALL_LINEAR_RGB: readonly [number, number, number] = [0.72, 0.68, 0.62];

/** The colour an estimated passage is built in unless somebody chooses another. */
export const DEFAULT_ESTIMATED_COLOR = '#e8833a';

/** The default diameter of an estimated passage, in metres, and the range it may be set in. */
export const DEFAULT_ESTIMATED_DIAMETER_M = 1;
export const SMALLEST_ESTIMATED_DIAMETER_M = 0.2;
export const LARGEST_ESTIMATED_DIAMETER_M = 30;

/** Colours offered for estimated passages: each far enough from the walls' grey to be told from it. */
export const ESTIMATED_COLOR_PRESETS: readonly string[] = [
  DEFAULT_ESTIMATED_COLOR,
  '#d23f3f',
  '#3a7bd5',
  '#2fa36b',
  '#a855f7',
];

/**
 * How the scene colours estimated passages: as they were built, the same as the walls, or in a
 * chosen `#rrggbb`.
 */
export type EstimatedPassagesColor = 'built' | 'walls' | string;

/** What the scene does with estimated passages: whether they are drawn, and in what colour. */
export interface EstimatedPassagesDisplay {
  visible: boolean;
  color: EstimatedPassagesColor;
}

export const DEFAULT_ESTIMATED_PASSAGES_DISPLAY: EstimatedPassagesDisplay = {
  visible: true,
  color: 'built',
};

function srgbToLinear(channel: number): number {
  const c = channel / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function linearToSrgb(channel: number): number {
  const c = channel <= 0.0031308 ? channel * 12.92 : 1.055 * channel ** (1 / 2.4) - 0.055;
  return Math.round(Math.min(1, Math.max(0, c)) * 255);
}

/** `#rrggbb` as linear RGB, or undefined for anything else. */
export function linearFromHex(hex: string): [number, number, number] | undefined {
  const match = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!match) {
    return undefined;
  }
  const rgb = Number.parseInt(match[1]!, 16);
  return [srgbToLinear((rgb >> 16) & 0xff), srgbToLinear((rgb >> 8) & 0xff), srgbToLinear(rgb & 0xff)];
}

/** Linear RGB as the `#rrggbb` a colour swatch shows. */
export function hexFromLinear([r, g, b]: readonly [number, number, number]): string {
  return `#${[r, g, b].map((c) => linearToSrgb(c).toString(16).padStart(2, '0')).join('')}`;
}

/** The walls' colour as a swatch shows it. */
export const WALL_COLOR_HEX = hexFromLinear(WALL_LINEAR_RGB);

/**
 * The linear RGB the scene recolours estimated passages to, or undefined to leave them in the
 * colour they were built in.
 */
export function estimatedRecolour(color: EstimatedPassagesColor): readonly [number, number, number] | undefined {
  if (color === 'built') {
    return undefined;
  }
  if (color === 'walls') {
    return WALL_LINEAR_RGB;
  }
  return linearFromHex(color);
}
