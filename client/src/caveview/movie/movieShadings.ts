// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Cv2Namespace } from '../loadCaveView.ts';

/**
 * The shadings a movie can be drawn in, by names of this application's own.
 *
 * <b>A remembered shading is one of these names, never the viewer's number for it.</b> The viewer
 * spells its shadings as numeric constants of its namespace, and nothing promises those numbers
 * hold from one version of the viewer to the next. A number kept in a browser's storage would then
 * mean another shading — or none — after an update, with nothing to say so. The name is translated
 * into the loaded viewer's own constant at the moment it is applied.
 */

export type MovieShadingId = 'height' | 'length' | 'inclination' | 'single' | 'survey' | 'depth' | 'depthCursor';

/** The names of the viewer's constants the offered shadings are. */
export type MovieShadingConstant = keyof Pick<
  Cv2Namespace,
  | 'SHADING_HEIGHT'
  | 'SHADING_LENGTH'
  | 'SHADING_INCLINATION'
  | 'SHADING_SINGLE'
  | 'SHADING_SURVEY'
  | 'SHADING_DEPTH'
  | 'SHADING_DEPTH_CURSOR'
>;

/**
 * The shadings offered, each with the name of the viewer's constant for it. Those that need the
 * pointer — the height cursor dragged along the model, the distance from a picked station, a route
 * being drawn — are left out: a movie has nobody at the pointer to set them.
 */
export const MOVIE_SHADINGS: readonly {
  id: MovieShadingId;
  constant: MovieShadingConstant;
  /** Whether it can be drawn only over real terrain. */
  terrain: boolean;
}[] = [
  { id: 'height', constant: 'SHADING_HEIGHT', terrain: false },
  { id: 'length', constant: 'SHADING_LENGTH', terrain: false },
  { id: 'inclination', constant: 'SHADING_INCLINATION', terrain: false },
  { id: 'single', constant: 'SHADING_SINGLE', terrain: false },
  { id: 'survey', constant: 'SHADING_SURVEY', terrain: false },
  { id: 'depth', constant: 'SHADING_DEPTH', terrain: true },
  { id: 'depthCursor', constant: 'SHADING_DEPTH_CURSOR', terrain: true },
];

/** A shading's name when the value is one, else null — whatever the value is. */
export function movieShadingId(value: unknown): MovieShadingId | null {
  return MOVIE_SHADINGS.find((shading) => shading.id === value)?.id ?? null;
}

/**
 * The loaded viewer's own number for a shading, or null when that viewer has no such constant.
 */
export function movieShadingNumber(
  id: MovieShadingId,
  constants: Partial<Record<MovieShadingConstant, number>>,
): number | null {
  const constant = MOVIE_SHADINGS.find((shading) => shading.id === id)?.constant;
  const value = constant === undefined ? undefined : constants[constant];
  return typeof value === 'number' ? value : null;
}

/**
 * The numbers a shading was remembered as before it was remembered by name: the viewer's constants
 * as they stood in every version of the viewer that setting was ever stored from. Written out here
 * rather than read from the loaded viewer on purpose — this says what a stored number meant when it
 * was written, which a later viewer that numbers its shadings differently could not.
 */
const SHADING_REMEMBERED_AS_NUMBER: Readonly<Record<number, MovieShadingId>> = {
  1: 'height',
  2: 'length',
  3: 'inclination',
  5: 'single',
  6: 'survey',
  9: 'depth',
  11: 'depthCursor',
};

/** The shading a number stored by an earlier version stood for, or null when it stood for none offered. */
export function movieShadingRememberedAs(value: unknown): MovieShadingId | null {
  return typeof value === 'number' && Object.hasOwn(SHADING_REMEMBERED_AS_NUMBER, value)
    ? SHADING_REMEMBERED_AS_NUMBER[value]
    : null;
}
