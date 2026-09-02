// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/**
 * The catalogue fails in several distinguishable ways and they lead to different people: an
 * unset key and a rejected key are an administrator's, an unreachable service is nobody's and
 * will pass, and a rejected request is this application's own defect.
 */
export function speologieErrorMessage(error: unknown, t: (key: string) => string): string | null {
  if (!error) {
    return null;
  }

  const code = error instanceof ApiError ? error.code : null;

  switch (code) {
    case 'speologie.not_configured':
      return t('speologie.errors.notConfigured');
    case 'speologie.unauthorized':
      return t('speologie.errors.unauthorized');
    case 'speologie.unavailable':
      return t('speologie.errors.unavailable');
    case 'speologie.rejected':
      return t('speologie.errors.rejected');
    case 'speologie.search_too_broad':
      return t('speologie.errors.searchTooBroad');
    case 'speologie.cave_not_found':
      return t('speologie.errors.caveNotFound');
    default:
      return t('speologie.errors.unavailable');
  }
}
