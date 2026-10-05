// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { SurveyMesh3DState } from '../../scene3d/surveyMesh3d.ts';
import { formatSize } from '../attachments/fileFormat.ts';

/**
 * The sentence that says the walls of the selected cave are on their way, or there, and how much
 * of them there is.
 *
 * Shared by the notice over the scene and by the hint under the switch in the layer panel, so
 * the two never describe one load in two different measures. The measure is the thing: a wall
 * mesh is the one download in this scene a viewer waits for, the ordinary export is a few hundred
 * kilobytes and the case this was built for is fifty megabytes, and the number that tells those
 * apart before either arrives is the size. The server has only recently begun measuring it, so
 * a mesh converted before that is still described by its triangle count, which is the magnitude
 * the server has always published — and once the walls are drawn both are said, because the
 * count is what a modeller comparing exports asks about.
 *
 * Undefined for every state that is not about magnitude; the callers word those themselves.
 */
export function meshProgressMessage(
  meshState: SurveyMesh3DState,
  language: string,
  t: TFunction,
): string | undefined {
  const size = meshState.meshSizeBytes === undefined ? undefined : formatSize(meshState.meshSizeBytes);
  const triangles = meshState.triangleCount?.toLocaleString(language);
  switch (meshState.status) {
    case 'loading':
      if (size) {
        return t('scene3d.meshLoadingBytes', { size });
      }
      return triangles ? t('scene3d.meshLoadingSized', { triangles }) : t('scene3d.meshLoading');
    case 'drawn':
      if (size) {
        return triangles
          ? t('scene3d.meshDrawnBytesTriangles', { size, triangles })
          : t('scene3d.meshDrawnBytes', { size });
      }
      return triangles ? t('scene3d.meshDrawnSized', { triangles }) : t('scene3d.meshDrawn');
    default:
      return undefined;
  }
}
