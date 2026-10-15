// SPDX-License-Identifier: AGPL-3.0-or-later
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

// The shadings are named where the settings that remember one are; the dialog offers that list.
export { MOVIE_SHADINGS, type MovieShadingConstant, type MovieShadingId } from '../../../caveview/movie/movieShadings.ts';

export const MOVIE_FORMATS: readonly MovieFormat[] = ['gif', 'webm', 'mp4'];
export const MOVIE_QUALITIES: readonly MovieQuality[] = ['low', 'medium', 'high'];
export const MOVIE_CAVER_LABELS: readonly MovieCaverLabels[] = ['first', 'full', 'initials', 'off'];
export const MOVIE_COLOUR_BY: readonly MovieColourBy[] = ['auto', 'trip', 'team', 'single'];
/** The frame shapes the sizes are grouped by, named by their ratio. */
export const MOVIE_ASPECTS: readonly { id: '16x9' | '9x16' | '1x1' | '4x5' | '3x4' | '4x3'; ratio: number }[] = [
  { id: '16x9', ratio: 16 / 9 },
  { id: '9x16', ratio: 9 / 16 },
  { id: '1x1', ratio: 1 },
  { id: '4x5', ratio: 4 / 5 },
  { id: '3x4', ratio: 3 / 4 },
  { id: '4x3', ratio: 4 / 3 },
];

export type MovieSettingsGroup = 'trips' | 'output' | 'motion' | 'cavers' | 'view' | 'captions' | 'pictures';

/** The ids the settings' labels are looked up by, for the test that holds both locales to them. */
export const MOVIE_SETTINGS_GROUPS: readonly MovieSettingsGroup[] = [
  'trips',
  'output',
  'motion',
  'cavers',
  'view',
  'captions',
  'pictures',
];
