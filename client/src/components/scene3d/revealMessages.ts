// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/**
 * Why a link could not move the scene, as the key of a sentence a reader can act on.
 *
 * A link to a feature is followed by reading where the feature is, and that read can be refused:
 * the feature was deleted after the link was written, or it is one the reader is not allowed to
 * see. Either way the camera stays where it was, and a link that does nothing reads exactly like
 * one that is broken — so what kept it from moving is said, without the server's raw code.
 */
export function revealProblemKey(error: unknown): string {
  const status = error instanceof ApiError ? error.status : undefined;
  if (status === 404 || status === 410) {
    return 'scene3d.revealGone';
  }
  if (status === 401 || status === 403) {
    return 'scene3d.revealForbidden';
  }
  return 'scene3d.revealFailed';
}
