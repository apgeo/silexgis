// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * Why a roster write — a merge, a person deleted, somebody enrolled in a group or taken out of
 * one — was refused, in words the person who asked can act on. A roster write can move rights,
 * because a caving group can be a trustee of a permission group, so several of these refusals are
 * about rights and not about the roster at all.
 */
export function rosterWriteProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'caver.grants_full_admin':
      return t('cavers.rosterProblems.grantsFullAdmin');
    case 'caver.revokes_full_admin':
      return t('cavers.rosterProblems.revokesFullAdmin');
    case 'access_entry.exceeds_own_rights':
      return t('cavers.rosterProblems.exceedsOwnRights');
    case 'caving_group.member_raced':
      return t('cavers.rosterProblems.memberRaced');
    case 'permission_group.last_full_admin':
      return t('cavers.rosterProblems.lastFullAdmin');
    default:
      return t('common.saveFailed');
  }
}
