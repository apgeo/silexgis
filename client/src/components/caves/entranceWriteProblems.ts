// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';
import { featureProblemMessage } from '../features/featureProblems.ts';

/**
 * Why the server refused to create or change a cave entrance, as a sentence the editor can act on.
 *
 * Only the refusals that belong to entrances are worded here. An entrance is stored as a map
 * feature, so a bad shape, a missing right or somebody else's newer change is refused exactly as it
 * is for any feature, and that wording has its own home rather than a second copy here.
 */
export function entranceWriteProblem(
  error: unknown,
  t: (key: string) => string,
  fallbackKey: 'common.saveFailed' | 'common.deleteFailed' = 'common.saveFailed',
): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'entrance.location_forbidden':
        return t('entrances.problems.locationForbidden');
      case 'entrance.not_found':
        return t('entrances.problems.notFound');
      case 'cave.not_found':
        return t('entrances.problems.caveNotFound');
      default:
        break;
    }
  }
  return featureProblemMessage(error, t, fallbackKey);
}
