// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { SurveyMesh3DState } from '../../scene3d/surveyMesh3d.ts';
import type { SurveyMeshesInView3DState } from '../../scene3d/surveyMeshesInView3d.ts';
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

/**
 * The sentence that says whose walls are drawn when the scene is asked for every cave in view.
 *
 * Shared by the notice over the scene and by the hint under the switch for the same reason the
 * sentence above is: one state, one wording. What it has to get across is that the walls on screen
 * are a subset whenever they are — a view drawing three caves' walls out of seven looks, with
 * nothing said, exactly like a view of three caves that have walls and four that do not. So the
 * two counts are always stated together, and what kept the rest out is named with its number.
 *
 * Walls still arriving and walls that could not be read each get a sentence of their own after
 * it, because they are different news from a budget: one is a wait, the other is a file somebody
 * should look at.
 */
export function meshesInViewMessage(state: SurveyMeshesInView3DState, t: TFunction): string {
  switch (state.status) {
    case 'off':
      return t('scene3d.meshOff');
    case 'zoomIn':
      return t('scene3d.meshesInViewZoomIn');
    case 'looking':
      return t('scene3d.meshesInViewLooking');
    case 'failed':
      return t('scene3d.meshesInViewFailed');
    case 'ready':
      break;
  }
  if (state.inView === 0) {
    return t('scene3d.meshesInViewNone');
  }
  const counts = { shown: state.shown, total: state.inView };
  const sentences = [
    state.limitedBy === 'bytes' && state.limits
      ? t('scene3d.meshesInViewOverBytes', { ...counts, limit: formatSize(state.limits.maxBytes) })
      : state.limitedBy === 'count' && state.limits
        ? t('scene3d.meshesInViewOverCount', { ...counts, limit: state.limits.maxCaves })
        : t('scene3d.meshesInViewShown', { ...counts, size: formatSize(state.heldBytes) }),
  ];
  if (state.loading > 0) {
    sentences.push(t('scene3d.meshesInViewLoading', { count: state.loading }));
  }
  if (state.failed > 0) {
    sentences.push(t('scene3d.meshesInViewUnreadable', { count: state.failed }));
  }
  return sentences.join(' ');
}

/**
 * Whether that sentence is worth saying over the scene itself, where a viewer sees it without
 * opening anything.
 *
 * It is whenever what is on screen is less than "the walls of the caves in view": nothing because
 * the view is too wide, a subset because a limit was reached, walls still arriving, or a mesh that
 * could not be read. A view that holds every cave's walls, all landed, needs no notice — the panel
 * still says how many and how big.
 */
export function meshesInViewNeedsNotice(state: SurveyMeshesInView3DState): boolean {
  switch (state.status) {
    case 'off':
      return false;
    case 'zoomIn':
    case 'looking':
    case 'failed':
      return true;
    case 'ready':
      return state.leftOut > 0 || state.loading > 0 || state.failed > 0;
  }
}
