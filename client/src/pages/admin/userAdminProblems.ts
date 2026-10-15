// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals an operator can act on, each in words that say what to do about it.
 *
 * Literal keys on purpose: the translation guard reads the keys it can see in the source, and a
 * key composed from the code would be invisible to it. Anything not listed falls back to the
 * general failure, because a paraphrase of a code nobody wrote words for would be a guess.
 */
export function userAdminProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'permission_group.last_full_admin':
      return t('admin.users.problems.lastAdministrator');
    case 'user.cannot_lock_self':
      return t('admin.users.problems.cannotLockSelf');
    case 'user.cannot_delete_self':
      return t('admin.users.problems.cannotDeleteSelf');
    case 'user.email_taken':
      return t('admin.users.problems.emailTaken');
    case 'user.already_enterable':
      return t('admin.users.problems.alreadyEnterable');
    case 'user.cannot_reset_own_two_factor':
      return t('admin.users.problems.cannotResetOwnTwoFactor');
    case 'user.two_factor_not_enabled':
      return t('admin.users.problems.twoFactorNotEnabled');
    case 'access_entry.exceeds_own_rights':
      return t('admin.users.problems.fullAdministratorAccount');
    case 'user.not_found':
      return t('admin.users.problems.notFound');
    case 'validation.failed':
      return t('admin.users.problems.invalid');
    case 'access.create_forbidden':
    case 'acl.forbidden':
      return t('admin.users.problems.forbidden');
    default:
      return t('common.saveFailed');
  }
}
