// SPDX-License-Identifier: AGPL-3.0-or-later
import type { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/client.ts';
import { geometryFor, type GeoTarget } from './geoTargets.ts';
import type { ResourceRef } from './resourceRef.ts';

/**
 * The sentences for a link that could not move a view. Exported so the wording can be checked
 * against both locales: the keys are chosen through a variable, so the scan that catches an
 * untranslated key at a `t('…')` call cannot see them.
 */
export const REVEAL_PROBLEM_KEYS = {
  gone: 'viewLinks.revealGone',
  forbidden: 'viewLinks.revealForbidden',
  failed: 'viewLinks.revealFailed',
} as const;

/**
 * Why a link could not move a view, as the key of a sentence a reader can act on.
 *
 * A link to a feature is followed by reading where the feature is, and that read can be refused:
 * the feature was deleted after the link was written, or it is one the reader is not allowed to
 * see. Either way the view stays where it was, and a link that does nothing reads exactly like
 * one that is broken — so what kept it from moving is said, without the server's raw code.
 */
export function revealProblemKey(error: unknown): string {
  const status = error instanceof ApiError ? error.status : undefined;
  if (status === 404 || status === 410) {
    return REVEAL_PROBLEM_KEYS.gone;
  }
  if (status === 401 || status === 403) {
    return REVEAL_PROBLEM_KEYS.forbidden;
  }
  return REVEAL_PROBLEM_KEYS.failed;
}

/**
 * Follows a link into a view: reads where the reference is and hands it to the view's own camera,
 * or hands the view the sentence for why it could not.
 *
 * Every view that answers links goes through here, so none of them can forget the second half —
 * a read refused for a deleted or hidden feature would otherwise be a rejection nobody catches.
 * `frame` receives null for a reference whose position this reader may not be told, which is an
 * answer rather than a failure: the view stays put and says nothing.
 */
export function followReveal(
  queryClient: QueryClient,
  ref: ResourceRef,
  frame: (target: GeoTarget | null) => void,
  problem: (key: string) => void,
): void {
  geometryFor(queryClient, ref).then(frame, (error: unknown) => problem(revealProblemKey(error)));
}
