// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import type { TerrainDerivativeCreate, TerrainDerivativeLayerInfo } from '../../../api/hooks.ts';

export type TerrainDerivativeKind = TerrainDerivativeLayerInfo['derivative'];
export type TerrainDerivativeStaleReason = NonNullable<TerrainDerivativeLayerInfo['staleReason']>;
export type TerrainHillshadeLighting = NonNullable<TerrainDerivativeCreate['lighting']>;
export type TerrainSurfaceFit = NonNullable<TerrainDerivativeCreate['surfaceFit']>;
export type TerrainSlopeUnit = NonNullable<TerrainDerivativeCreate['slopeUnit']>;
export type TerrainRuggednessFit = NonNullable<TerrainDerivativeCreate['ruggednessFit']>;

/**
 * The pictures of the ground this installation can compute, in the order they are offered.
 *
 * The order is also the numbering the server stores each kind under, and the same holds for the
 * three choice lists below: a stored settings record writes these as the number rather than the
 * word, so the position in each list is how a number is read back into a name. Appending is the
 * only safe change to any of them.
 */
export const TERRAIN_DERIVATIVE_KINDS: readonly TerrainDerivativeKind[] = [
  'hillshade',
  'slope',
  'aspect',
  'ruggednessIndex',
  'positionIndex',
  'roughness',
  'colourRelief',
  'contours',
];

export const HILLSHADE_LIGHTINGS: readonly TerrainHillshadeLighting[] = [
  'single',
  'multidirectional',
];
export const SURFACE_FITS: readonly TerrainSurfaceFit[] = ['horn', 'zevenbergenThorne'];
export const SLOPE_UNITS: readonly TerrainSlopeUnit[] = ['degrees', 'percent'];
export const RUGGEDNESS_FITS: readonly TerrainRuggednessFit[] = ['riley', 'wilson'];

/**
 * The light the server assumes when none is given: from the north-west, half-way up the sky.
 *
 * North-west is a convention rather than a physical fact — lit from where the sun actually stands
 * in the morning, most readers see the valleys as ridges — and it is the convention the server
 * holds too, so a form that opens on it asks for exactly what a request naming nothing would get.
 */
export const DEFAULT_AZIMUTH = 315;
export const DEFAULT_ALTITUDE = 45;
export const DEFAULT_Z_FACTOR = 1;

/**
 * How exaggerated a height may be asked to be, as the server bounds it.
 *
 * Past this a gentle landscape is a wall of saturated pixels: a picture that is finished, valid
 * and says nothing. Twenty times is already far beyond what anybody reads.
 */
export const MAX_Z_FACTOR = 100;

/**
 * How far apart contour lines may be asked to be, in metres of height, as the server bounds it —
 * and the spacing a request that names none is given.
 *
 * Twenty metres is what a map of mountain country at walking scale uses. Past a thousand the
 * spacing is wider than the relief of any ground, and the result is a finished, valid picture with
 * no line on it anywhere.
 */
export const DEFAULT_CONTOUR_INTERVAL = 20;
export const MIN_CONTOUR_INTERVAL = 1;
export const MAX_CONTOUR_INTERVAL = 1000;

/** The server's cap on a picture's name. */
export const MAX_NAME_LENGTH = 200;

/**
 * How many builds the form and the register read, which is a page of their own rather than the
 * build list's.
 *
 * The build list is paged at ten for reading; a picture may be drawn from any build that has
 * finished, and the one wanted is as likely to be an old one as the newest. The server caps a page
 * at five hundred, and an installation with a hundred terrain builds is not one this form was
 * drawn for. The two read the same page so naming a picture's build costs no request of its own.
 */
export const DERIVATIVE_BUILD_CHOICES = 100;

/*
 * Which settings each kind of picture reads. Everything else is sent as nothing and the server
 * fills in its default, which is also what it strips back to before deciding whether two requests
 * are the same picture — so a form that sent a slope unit along with a hillshade would be asking
 * for a file identical to one that already exists under a different fingerprint.
 */
export function readsLight(kind: TerrainDerivativeKind): boolean {
  return kind === 'hillshade';
}

export function readsSurfaceFit(kind: TerrainDerivativeKind): boolean {
  return kind === 'slope' || kind === 'aspect' || kind === 'hillshade';
}

export function readsSlopeUnit(kind: TerrainDerivativeKind): boolean {
  return kind === 'slope';
}

export function readsRuggednessFit(kind: TerrainDerivativeKind): boolean {
  return kind === 'ruggednessIndex';
}

export function readsColourRamp(kind: TerrainDerivativeKind): boolean {
  return kind === 'colourRelief';
}

export function readsContourInterval(kind: TerrainDerivativeKind): boolean {
  return kind === 'contours';
}

/**
 * Whether the kind has an outermost ring of cells to compute or leave blank.
 *
 * Contour lines are traced through the heights rather than computed from a cell's neighbours, so
 * there is no such ring, and the choice is neither offered nor sent for them.
 */
export function readsEdges(kind: TerrainDerivativeKind): boolean {
  return kind !== 'contours';
}

/*
 * The bounds the server checks a request against, mirrored so the form refuses before sending
 * rather than after. Each bound is the server's exactly, including which end is open: a light
 * standing at 0° is on the horizon and lights nothing, and 360° is 0° written twice.
 */
export function azimuthValid(degrees: number): boolean {
  return Number.isFinite(degrees) && degrees >= 0 && degrees < 360;
}

export function altitudeValid(degrees: number): boolean {
  return Number.isFinite(degrees) && degrees > 0 && degrees <= 90;
}

export function zFactorValid(factor: number): boolean {
  return Number.isFinite(factor) && factor > 0 && factor <= MAX_Z_FACTOR;
}

export function contourIntervalValid(metres: number): boolean {
  return (
    Number.isFinite(metres) && metres >= MIN_CONTOUR_INTERVAL && metres <= MAX_CONTOUR_INTERVAL
  );
}

/** One stop of a colour ramp as the form holds it: a height and a `#rrggbb` colour. */
export interface ColourStopDraft {
  elevation: number | null | undefined;
  colour: string;
}

export type ColourRampProblem = 'tooShort' | 'heightMissing' | 'duplicate';

/**
 * Why a ramp cannot be computed from, or null when it can.
 *
 * The three refusals the server has for a ramp, in the order it makes them: fewer than two
 * stops leave nothing to put colours between, a stop with no height sits nowhere, and two stops
 * at one height are two answers to the same question decided by the order they happen to be
 * written in.
 */
export function colourRampProblem(stops: readonly ColourStopDraft[]): ColourRampProblem | null {
  if (stops.length < 2) {
    return 'tooShort';
  }
  const heights = stops.map((stop) => stop.elevation);
  if (heights.some((height) => height === null || height === undefined || !Number.isFinite(height))) {
    return 'heightMissing';
  }
  if (new Set(heights).size !== heights.length) {
    return 'duplicate';
  }
  return null;
}

/** A `#rrggbb` colour as the three bytes the server stores a stop as. */
export function hexToRgb(hex: string): { red: number; green: number; blue: number } {
  const digits = hex.replace('#', '');
  const wide = digits.length === 3 ? [...digits].map((d) => d + d).join('') : digits;
  const value = Number.parseInt(wide.slice(0, 6), 16);
  return { red: (value >> 16) & 255, green: (value >> 8) & 255, blue: value & 255 };
}

/** A sensible starting ramp over Carpathian relief: valley floor, mid-slope, bare summit. */
export const DEFAULT_COLOUR_RAMP: readonly ColourStopDraft[] = [
  { elevation: 200, colour: '#2f855a' },
  { elevation: 1000, colour: '#e9c46a' },
  { elevation: 2500, colour: '#ffffff' },
];

/**
 * Which sentence explains why a picture is out of date.
 *
 * The two reasons have two remedies — a picture over the elevation now served, or the same picture
 * asked for again — so the hint under the badge has to be the one for the reason given. A listing
 * that names no reason is read as replaced elevation, which was the only reason there was before
 * the second was told apart.
 */
export function staleHintKey(
  reason: TerrainDerivativeLayerInfo['staleReason'] | undefined,
): `terrain.derivatives.staleHints.${TerrainDerivativeStaleReason}` {
  return `terrain.derivatives.staleHints.${reason ?? 'elevationReplaced'}`;
}

/** The name a picture opens with: what its kind is called. */
export function defaultDerivativeName(
  kind: TerrainDerivativeKind,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  return t(`terrain.derivatives.kinds.${kind}`);
}

/** The settings a picture was computed with, as far as the register is able to read them. */
export interface StoredDerivativeSettings {
  lighting?: TerrainHillshadeLighting;
  azimuthDegrees?: number;
  altitudeDegrees?: number;
  zFactor?: number;
  surfaceFit?: TerrainSurfaceFit;
  slopeUnit?: TerrainSlopeUnit;
  ruggednessFit?: TerrainRuggednessFit;
  computeEdges?: boolean;
  colourRamp?: unknown[];
  contourIntervalMetres?: number;
}

/**
 * A choice read back out of a stored settings record, whichever way it was written.
 *
 * The server writes the record with its own serializer, which spells a choice as its number; the
 * same choice crosses the API as its name. Both are accepted so the register reads the record
 * whether or not that serializer is ever changed to agree with the API.
 */
function choice<T extends string>(value: unknown, names: readonly T[]): T | undefined {
  if (typeof value === 'number') {
    return names[value];
  }
  return typeof value === 'string' && (names as readonly string[]).includes(value)
    ? (value as T)
    : undefined;
}

function finite(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

/**
 * The settings a picture was computed with, read out of the record stored beside it.
 *
 * Tolerant on purpose. The record is the server's own description of a request and this reads it
 * for display only, so a field it cannot make sense of is left out rather than failing the row —
 * a register that showed nothing for a picture because one setting was spelt unexpectedly would be
 * withholding the six it could read.
 */
export function parseDerivativeSettings(settings: string): StoredDerivativeSettings {
  let raw: unknown;
  try {
    raw = JSON.parse(settings);
  } catch {
    return {};
  }
  if (typeof raw !== 'object' || raw === null) {
    return {};
  }
  const record = raw as Record<string, unknown>;
  return {
    lighting: choice(record.lighting, HILLSHADE_LIGHTINGS),
    azimuthDegrees: finite(record.azimuthDegrees),
    altitudeDegrees: finite(record.altitudeDegrees),
    zFactor: finite(record.zFactor),
    surfaceFit: choice(record.surfaceFit, SURFACE_FITS),
    slopeUnit: choice(record.slopeUnit, SLOPE_UNITS),
    ruggednessFit: choice(record.ruggednessFit, RUGGEDNESS_FITS),
    computeEdges: typeof record.computeEdges === 'boolean' ? record.computeEdges : undefined,
    colourRamp: Array.isArray(record.colourRamp) ? record.colourRamp : undefined,
    contourIntervalMetres: finite(record.contourIntervalMetres),
  };
}

/**
 * The settings of a picture in one line, saying only what that kind of picture reads.
 *
 * The stored record carries every setting whether or not the picture used it, each at its
 * default, so a line that printed the whole record would tell a reader a slope map was lit from
 * the north-west. Only what the kind reads is said, and the defaults the server assumes are left
 * unsaid where saying them would be noise: a hillshade with no height exaggeration is the ordinary
 * hillshade, and edges are computed unless somebody asked otherwise.
 */
export function describeDerivativeSettings(
  kind: TerrainDerivativeKind,
  settings: string,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  const stored = parseDerivativeSettings(settings);
  const parts: string[] = [];
  if (readsLight(kind)) {
    if (stored.lighting !== undefined) {
      parts.push(t(`terrain.derivativeForm.lightings.${stored.lighting}`));
    }
    if (stored.lighting !== 'multidirectional' && stored.azimuthDegrees !== undefined) {
      parts.push(t('terrain.derivativeList.params.azimuth', { degrees: stored.azimuthDegrees }));
    }
    if (stored.altitudeDegrees !== undefined) {
      parts.push(t('terrain.derivativeList.params.altitude', { degrees: stored.altitudeDegrees }));
    }
    if (stored.zFactor !== undefined && stored.zFactor !== DEFAULT_Z_FACTOR) {
      parts.push(t('terrain.derivativeList.params.zFactor', { factor: stored.zFactor }));
    }
  }
  if (readsSurfaceFit(kind) && stored.surfaceFit !== undefined) {
    parts.push(t(`terrain.derivativeForm.surfaceFits.${stored.surfaceFit}`));
  }
  if (readsSlopeUnit(kind) && stored.slopeUnit !== undefined) {
    parts.push(t(`terrain.derivativeForm.slopeUnits.${stored.slopeUnit}`));
  }
  if (readsRuggednessFit(kind) && stored.ruggednessFit !== undefined) {
    parts.push(t(`terrain.derivativeForm.ruggednessFits.${stored.ruggednessFit}`));
  }
  if (readsColourRamp(kind) && stored.colourRamp !== undefined) {
    parts.push(t('terrain.derivativeList.params.stops', { count: stored.colourRamp.length }));
  }
  if (readsContourInterval(kind) && stored.contourIntervalMetres !== undefined) {
    parts.push(t('terrain.derivativeList.params.interval', { metres: stored.contourIntervalMetres }));
  }
  if (readsEdges(kind) && stored.computeEdges === false) {
    parts.push(t('terrain.derivativeList.params.edgesOff'));
  }
  return parts.join(' · ');
}
