// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Cv2Namespace } from '../../../caveview/loadCaveView.ts';
import type {
  MovieCaverLabels,
  MovieColourBy,
  MovieFormat,
  MovieQuality,
} from '../../../caveview/movie/movieSettings.ts';

/**
 * The choices the movie dialog offers, as lists: the order each control shows them in, and the ids
 * each one's label is looked up by — which is why they are listed rather than spelled out at each
 * control, so the test holding both languages to them reads the same lists the dialog does.
 */

export type MovieShadingId = 'height' | 'length' | 'inclination' | 'single' | 'survey' | 'depth' | 'depthCursor';

/**
 * The shadings offered, each with the viewer's constant for it. Those that need the pointer — the
 * height cursor dragged along the model, the distance from a picked station, a route being drawn —
 * are left out: a movie has nobody at the pointer to set them.
 */
export const MOVIE_SHADINGS: readonly {
  id: MovieShadingId;
  constant: keyof Pick<
    Cv2Namespace,
    | 'SHADING_HEIGHT'
    | 'SHADING_LENGTH'
    | 'SHADING_INCLINATION'
    | 'SHADING_SINGLE'
    | 'SHADING_SURVEY'
    | 'SHADING_DEPTH'
    | 'SHADING_DEPTH_CURSOR'
  >;
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

export const MOVIE_FORMATS: readonly MovieFormat[] = ['gif', 'webm', 'mp4'];
export const MOVIE_QUALITIES: readonly MovieQuality[] = ['low', 'medium', 'high'];
export const MOVIE_CAVER_LABELS: readonly MovieCaverLabels[] = ['first', 'full', 'initials', 'off'];
export const MOVIE_COLOUR_BY: readonly MovieColourBy[] = ['auto', 'trip', 'team', 'single'];
/** The frame shapes the sizes are grouped by, named by their ratio. */
export const MOVIE_ASPECTS: readonly { id: '16x9' | '4x3'; ratio: number }[] = [
  { id: '16x9', ratio: 16 / 9 },
  { id: '4x3', ratio: 4 / 3 },
];

/** The constants of the viewer the offered shadings are. */
export type MovieShadingConstant = (typeof MOVIE_SHADINGS)[number]['constant'];

export type MovieSettingsGroup = 'trips' | 'output' | 'motion' | 'cavers' | 'view' | 'captions';

/** The ids the settings' labels are looked up by, for the test that holds both locales to them. */
export const MOVIE_SETTINGS_GROUPS: readonly MovieSettingsGroup[] = [
  'trips',
  'output',
  'motion',
  'cavers',
  'view',
  'captions',
];
