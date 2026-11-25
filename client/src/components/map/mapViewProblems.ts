// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/**
 * Why the server refused to save, share or delete a saved map view, as a sentence the person can
 * act on: a member without the right to create views, a view somebody else deleted meanwhile and a
 * filter that holds a typed position each need a different next step, and "the operation failed"
 * names none of them. An answer this list does not know falls back to the plain failure sentence.
 */
export function mapViewProblem(
  error: unknown,
  t: (key: string) => string,
  fallbackKey: 'common.saveFailed' | 'common.deleteFailed' = 'common.saveFailed',
): string {
  if (!(error instanceof ApiError)) {
    return t(fallbackKey);
  }
  switch (error.code) {
    case 'access.create_forbidden':
      return t('views.problems.createForbidden');
    case 'access.caving_group_binding_forbidden':
      return t('views.problems.groupForbidden');
    case 'saved_filter.anchor_refused':
      return t('views.filterHoldsPosition');
    case 'map_view.not_found':
      return t('views.problems.notFound');
    case 'validation.failed':
      return t('views.problems.invalid');
    default:
      break;
  }
  if (error.status === 403) {
    return t('views.problems.forbidden');
  }
  if (error.status === 404) {
    return t('views.problems.notFound');
  }
  return t(fallbackKey);
}
