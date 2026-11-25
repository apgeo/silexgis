// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * Why an album's share link could not be made or taken back, in words the reader can act on.
 *
 * Anything not listed falls back to the general failure. The keys are literal on purpose: the
 * check that every key used exists in both languages can only see literal keys.
 */
export function albumShareProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'album.share_forbidden':
      return t('gallery.shareForbidden');
    default:
      return t('common.saveFailed');
  }
}

/**
 * Why a new album could not be made. An album is documents-domain content, so the right it needs
 * is the right to write documents; and one made about a trip or a cave needs that subject to be
 * there and visible to the maker. Anything else falls back to the general failure.
 */
export function albumCreateProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'access.create_forbidden':
      return t('gallery.createForbidden');
    case 'album.subject_not_found':
      return t('gallery.subjectGone');
    default:
      return t('common.saveFailed');
  }
}
