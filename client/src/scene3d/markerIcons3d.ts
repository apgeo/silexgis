// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  entrancePalette,
  featureSymbolUrl,
  surfaceFeaturePalette,
} from '../map/markerPalette.ts';

// Images for the markers the 3D scene draws.
//
// The flat map draws entrances and clusters as canvas circles with a text label; the scene draws
// screen-aligned images, and there is no image file for either symbol anywhere in the application
// because the flat map never needed one. Rather than commission a pair of PNGs that would then
// have to be kept in step with the colours by eye, the same circles are described as small inline
// SVG documents carrying the shared palette, and handed over as data URLs. That keeps the two
// renderings tied to one set of colours, adds no asset to fetch, and works on an installation
// serving no third-party content at all.
//
// Feature-type symbols are different: those already exist as image files the flat map points at,
// so the scene points at exactly the same URL rather than redrawing them.

/** An image plus the factor it should be drawn at, which is what a marker needs to be built. */
export interface MarkerIcon {
  image: string;
  scale: number;
}

/**
 * Radius, ring width and fill of the flat map's entrance symbol, in CSS pixels. The scene draws
 * the image at its natural size, so an SVG this many pixels across lands the same size on screen.
 */
const ENTRANCE_RADIUS = 7;
const APPROXIMATE_RADIUS = 8;
const RING_WIDTH = 2;

/** Cluster bubble sizing, copied from the flat map: it grows with the square root of the count. */
const CLUSTER_MIN_RADIUS = 12;
const CLUSTER_MAX_RADIUS = 24;
const CLUSTER_FONT_PIXELS = 12;

/** Fallback dot for a feature whose type carries no symbol file. */
const FEATURE_DOT_RADIUS = 6;

/**
 * The flat map draws the symbol files at half size, so the 64-pixel artwork lands at about 32
 * pixels. The scene follows, or the same cave's symbols would be twice the size in one view.
 */
const FEATURE_SYMBOL_SCALE = 0.5;

const entranceIcons = new Map<string, MarkerIcon>();
const clusterIcons = new Map<number, MarkerIcon>();

/** The pin for a single cave entrance; an approximate one is larger and ringed with a dashed line. */
export function entranceIcon(approximate: boolean): MarkerIcon {
  const key = approximate ? 'approximate' : 'exact';
  const cached = entranceIcons.get(key);
  if (cached) {
    return cached;
  }
  const radius = approximate ? APPROXIMATE_RADIUS : ENTRANCE_RADIUS;
  const fill = approximate ? entrancePalette.approximate : entrancePalette.point;
  const dashes = approximate ? " stroke-dasharray='2 2'" : '';
  const icon: MarkerIcon = {
    image: svgDataUrl(
      circleSvg(radius, `fill='${fill}' stroke='${entrancePalette.stroke}' stroke-width='${RING_WIDTH}'${dashes}`),
    ),
    scale: 1,
  };
  entranceIcons.set(key, icon);
  return icon;
}

/**
 * The bubble for a server-side cluster, with its member count printed inside. The count is baked
 * into the image rather than drawn as a separate label so a cluster stays one marker: a label
 * collection would be a second batch to keep aligned with the first through every camera move.
 */
export function clusterIcon(count: number): MarkerIcon {
  const safeCount = Number.isFinite(count) && count > 0 ? Math.round(count) : 0;
  const cached = clusterIcons.get(safeCount);
  if (cached) {
    return cached;
  }
  const radius = Math.min(CLUSTER_MAX_RADIUS, CLUSTER_MIN_RADIUS + Math.sqrt(safeCount));
  const size = iconSize(radius);
  const centre = size / 2;
  const svg =
    `<svg xmlns='http://www.w3.org/2000/svg' width='${size}' height='${size}' viewBox='0 0 ${size} ${size}'>` +
    `<circle cx='${centre}' cy='${centre}' r='${radius}' fill='${entrancePalette.cluster}'` +
    ` stroke='${entrancePalette.stroke}' stroke-width='${RING_WIDTH}'/>` +
    `<text x='${centre}' y='${centre}' text-anchor='middle' dominant-baseline='central'` +
    ` font-family='sans-serif' font-size='${CLUSTER_FONT_PIXELS}' font-weight='bold'` +
    ` fill='${entrancePalette.stroke}'>${safeCount}</text></svg>`;
  const icon: MarkerIcon = { image: svgDataUrl(svg), scale: 1 };
  clusterIcons.set(safeCount, icon);
  return icon;
}

/** The symbol a surface feature is drawn with, falling back to a plain dot for untyped rows. */
export function surfaceFeatureIcon(symbolFile: string | null | undefined): MarkerIcon {
  if (typeof symbolFile === 'string' && symbolFile.length > 0) {
    return { image: featureSymbolUrl(symbolFile), scale: FEATURE_SYMBOL_SCALE };
  }
  return featureDotIcon();
}

let featureDot: MarkerIcon | undefined;

function featureDotIcon(): MarkerIcon {
  featureDot ??= {
    image: svgDataUrl(
      circleSvg(
        FEATURE_DOT_RADIUS,
        `fill='${surfaceFeaturePalette.point}' stroke='${surfaceFeaturePalette.stroke}' stroke-width='${RING_WIDTH}'`,
      ),
    ),
    scale: 1,
  };
  return featureDot;
}

/** Enough room for the circle plus the ring drawn half in and half out of its edge. */
function iconSize(radius: number): number {
  return Math.ceil(2 * (radius + RING_WIDTH));
}

function circleSvg(radius: number, attributes: string): string {
  const size = iconSize(radius);
  const centre = size / 2;
  return (
    `<svg xmlns='http://www.w3.org/2000/svg' width='${size}' height='${size}' viewBox='0 0 ${size} ${size}'>` +
    `<circle cx='${centre}' cy='${centre}' r='${radius}' ${attributes}/></svg>`
  );
}

// Percent-encoded rather than base64: the documents are tiny, the encoded form stays readable in
// a debugger, and base64 would make every one of them a third larger.
function svgDataUrl(svg: string): string {
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}
