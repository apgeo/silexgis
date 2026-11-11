// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../../api/client.ts';

/** Why a permission group could not be created or renamed, in words the form can show. */
export function permissionGroupNameProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'permission_group.name_taken':
      return t('permissionGroups.nameTaken');
    case 'permission_group.name_invalid':
      return t('permissionGroups.nameInvalid');
    default:
      return t('common.saveFailed');
  }
}
