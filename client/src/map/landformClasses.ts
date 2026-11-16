// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The landform classes by their stored number less one — 1 flat through 10 pit, the order the
 * reference implementations write — and a colour for each, chosen so the convex forms (peak,
 * ridge, shoulder, spur) read warm, the concave ones (hollow, footslope, valley, pit) cool, and
 * the two neutral forms (flat, slope) pale.
 */
export const LANDFORM_CLASSES = [
  'flat',
  'peak',
  'ridge',
  'shoulder',
  'spur',
  'slope',
  'hollow',
  'footslope',
  'valley',
  'pit',
] as const;
export type LandformClass = (typeof LANDFORM_CLASSES)[number];
export const LANDFORM_COLOURS: Readonly<Record<LandformClass, string>> = {
  flat: '#dcdcdc',
  peak: '#7f0000',
  ridge: '#d7301f',
  shoulder: '#fc8d59',
  spur: '#fdcc8a',
  slope: '#f7f7d4',
  hollow: '#c6dbef',
  footslope: '#6baed6',
  valley: '#2171b5',
  pit: '#08306b',
};
