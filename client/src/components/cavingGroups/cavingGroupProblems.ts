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

/**
 * Why an announcement to a caving group was refused. The wait between two announcements and the
 * day's spending ceiling refuse the same send however often it is pressed, so they are said as
 * what they are rather than as a failure worth repeating.
 */
export function announcementProblem(error: unknown, t: TFunction): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'caving_group.announcement_too_soon':
        return t('cavingGroups.announce.tooSoon');
      case 'caving_group.announcement_paid_cap_reached':
        return t('cavingGroups.announce.paidCapReached');
    }
  }
  return t('cavingGroups.announce.failed');
}
