// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import { ApiError } from '../../../api/client.ts';

/**
 * The refusals worth naming, in the words of somebody who has to act on them.
 *
 * A code the server keeps stable so that a client can say something better than a status number.
 * Anything not listed falls back to the general failure sentence, because a refusal nobody has
 * written wording for is a refusal nobody can act on — a paraphrase of an unknown code would be
 * a guess presented as an explanation.
 *
 * Exported so the wording can be checked against both locales. These keys are looked up through a
 * variable rather than written out at the call, so the scan that catches a key nobody translated
 * cannot see a single one of them: every refusal on this page would fall back to showing its own
 * key, in the one place where being unable to read the answer matters most.
 */
export const TERRAIN_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'terrain_build.not_found': 'terrain.problems.notFound',
  'access.forbidden': 'terrain.problems.forbidden',
  'terrain_build.already_building': 'terrain.problems.alreadyBuilding',
  'terrain_build.no_sources': 'terrain.problems.noSources',
  'terrain_build.source_invalid': 'terrain.problems.sourceInvalid',
  'terrain_build.directory_unavailable': 'terrain.problems.directoryUnavailable',
  'import.no_roots_configured': 'terrain.problems.noRoots',
  'terrain_build.raster_unsupported': 'terrain.problems.rasterUnsupported',
  'terrain_build.raster_size_invalid': 'terrain.problems.rasterSizeInvalid',
  'terrain_build.raster_unplaceable': 'terrain.problems.rasterUnplaceable',
  'terrain_build.extent_invalid': 'terrain.problems.extentInvalid',
  'terrain_build.extent_too_large': 'terrain.problems.extentTooLarge',
  'terrain_build.depth_invalid': 'terrain.problems.depthInvalid',
  // The three the list's own actions run into: choosing a build that never published, and
  // deleting one that is either being drawn or still being written.
  'terrain_build.not_published': 'terrain.problems.notPublished',
  'terrain_build.base_not_found': 'terrain.problems.baseNotFound',
  'terrain_build.base_unchecked': 'terrain.problems.baseUnchecked',
  'terrain_build.base_not_published': 'terrain.problems.baseNotPublished',
  'terrain_build.base_depth_differs': 'terrain.problems.baseDepthDiffers',
  'terrain_build.base_datum_differs': 'terrain.problems.baseDatumDiffers',
  'terrain_build.being_extended': 'terrain.problems.beingExtended',
  'terrain_build.active': 'terrain.problems.active',
  'terrain_build.running': 'terrain.problems.running',
  // The pictures computed from a build: asking for one over a build that is gone, and removing
  // one that already is. The third is answered to a tile reader fetching a picture before it is
  // finished, which the register never does — worded all the same, because the code exists.
  'terrain_derivative.build_not_found': 'terrain.problems.derivativeBuildNotFound',
  'terrain_derivative.not_found': 'terrain.problems.derivativeNotFound',
  'terrain_derivative.not_ready': 'terrain.problems.derivativeNotReady',
};

export function terrainProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  if (!(error instanceof ApiError) || !error.code) {
    return t('common.saveFailed');
  }
  const key = TERRAIN_PROBLEM_MESSAGE_KEYS[error.code];
  return key ? t(key) : t('common.saveFailed');
}
