// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/** Why a group could not be made, in words a person filling in a form can act on. */
export function cavingGroupCreateProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('cavingGroupPicker.createFailed');
  }

  switch (error.code) {
    case 'caving_group.name_taken':
      return t('cavingGroupPicker.nameTaken');
    case 'caving_group.name_invalid':
      return t('cavingGroupPicker.nameInvalid');
    case 'access.create_forbidden':
    case 'acl.forbidden':
      return t('cavingGroupPicker.forbidden');
    case 'validation.failed':
      return t('cavingGroupPicker.nameTooLong');
    default:
      return t('cavingGroupPicker.createFailed');
  }
}
