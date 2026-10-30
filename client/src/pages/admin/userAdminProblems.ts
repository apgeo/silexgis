// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The kinds of record that can keep an account from being deleted, in words.
 *
 * Literal keys, one per kind the server can name: the translation guard reads the keys it can see.
 * A server test requires every kind the server names to have a label in both languages; the bare
 * name below is only a last resort for a client older than its server.
 */
function blockingLabel(kind: string, t: TFunction): string {
  switch (kind) {
    case 'albums':
      return t('admin.users.problems.deleteBlockedKinds.albums');
    case 'cave_level_bands':
      return t('admin.users.problems.deleteBlockedKinds.cave_level_bands');
    case 'cave_qr_publications':
      return t('admin.users.problems.deleteBlockedKinds.cave_qr_publications');
    case 'checklists':
      return t('admin.users.problems.deleteBlockedKinds.checklists');
    case 'documents':
      return t('admin.users.problems.deleteBlockedKinds.documents');
    case 'events':
      return t('admin.users.problems.deleteBlockedKinds.events');
    case 'expeditions':
      return t('admin.users.problems.deleteBlockedKinds.expeditions');
    case 'features':
      return t('admin.users.problems.deleteBlockedKinds.features');
    case 'feature_shares':
      return t('admin.users.problems.deleteBlockedKinds.feature_shares');
    case 'geofiles':
      return t('admin.users.problems.deleteBlockedKinds.geofiles');
    case 'georeferenced_maps':
      return t('admin.users.problems.deleteBlockedKinds.georeferenced_maps');
    case 'import_batches':
      return t('admin.users.problems.deleteBlockedKinds.import_batches');
    case 'raster_maps':
      return t('admin.users.problems.deleteBlockedKinds.raster_maps');
    case 'survey_uploads':
      return t('admin.users.problems.deleteBlockedKinds.survey_uploads');
    case 'survey_upload_public_links':
      return t('admin.users.problems.deleteBlockedKinds.survey_upload_public_links');
    case 'trips':
      return t('admin.users.problems.deleteBlockedKinds.trips');
    case 'trip_recordings':
      return t('admin.users.problems.deleteBlockedKinds.trip_recordings');
    case 'trip_tracking_shares':
      return t('admin.users.problems.deleteBlockedKinds.trip_tracking_shares');
    case 'upload_batches':
      return t('admin.users.problems.deleteBlockedKinds.upload_batches');
    default:
      return kind;
  }
}

/** "trips (3), albums (1)" from the refusal's `blocking` member, or undefined when it has none. */
function blockingList(error: ApiError, t: TFunction): string | undefined {
  const blocking = error.problem?.blocking;
  if (!Array.isArray(blocking)) {
    return undefined;
  }

  const parts = blocking.flatMap((entry: unknown) => {
    const { kind, count } = (entry ?? {}) as { kind?: unknown; count?: unknown };
    return typeof kind === 'string' && typeof count === 'number'
      ? [`${blockingLabel(kind, t)} (${count})`]
      : [];
  });
  return parts.length > 0 ? parts.join(', ') : undefined;
}

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
    case 'user.delete_blocked': {
      const owned = blockingList(error, t);
      return owned
        ? t('admin.users.problems.deleteBlocked', { owned })
        : t('common.saveFailed');
    }
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
