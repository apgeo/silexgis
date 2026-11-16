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

/**
 * Why a write of access rules or of a group's members was refused — the rules of a permission
 * group, an object's own rules, or a trustee added to or removed from a group — in words the
 * administrator can act on.
 *
 * The same refusal reads the same on every screen that can meet it; anything not listed falls
 * back to the general failure, because a paraphrase of a code nobody has worded would be a guess.
 * The keys are literal on purpose: the check that every key used exists in both languages can
 * only see literal keys.
 */
export function accessRuleProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'access_entry.exceeds_own_rights':
      return t('permissionGroups.exceedsOwnRights');
    case 'permission_group.last_full_admin':
      return t('permissionGroups.lastFullAdmin');
    case 'access_entry.deny_full_administrators':
    case 'permission_group.deny_full_administrators':
      return t('permissionGroups.denyFullAdministrators');
    case 'access.subject_unknown':
    case 'permission_group.member_unknown':
      return t('permissionGroups.subjectUnknown');
    case 'access_entry.anchor_not_found':
      return t('permissionGroups.anchorNotFound');
    case 'access_entry.scope_invalid':
      return t('permissionGroups.scopeInvalid');
    case 'access.forbidden':
    case 'acl.forbidden':
      return t('permissionGroups.forbidden');
    default:
      return t('common.saveFailed');
  }
}
