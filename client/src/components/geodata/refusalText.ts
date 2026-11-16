// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';
import { DownloadError } from '../../api/download.ts';

/**
 * The refusals an action on uploaded geodata or on an import review can meet — editing, deleting
 * or exporting a file or a raster map, reading or confirming a review — each with a sentence of
 * its own. Uploading a file has its own wording (it speaks of sizes and formats); this is for
 * everything done to a file once it is there.
 */
const BY_CODE: Readonly<Record<string, string>> = {
  'geofile.import_in_progress': 'geodata.refusals.busy',
  'georeferenced_map.busy': 'geodata.refusals.busy',
  'access.caving_group_binding_forbidden': 'geodata.refusals.groupNotAllowed',
  'import.file_not_ready': 'geodata.refusals.fileNotReady',
  'import.selection_too_large': 'geodata.refusals.selectionTooLarge',
  'photo_import.selection_too_large': 'geodata.refusals.selectionTooLarge',
  'import.nothing_created': 'geodata.refusals.nothingCreated',
  'photo_import.nothing_created': 'geodata.refusals.nothingCreated',
  'import.review_required': 'geodata.refusals.reviewRequired',
  'import.too_large_without_review': 'geodata.refusals.tooLargeWithoutReview',
  'photo_import.no_files': 'geodata.refusals.noFiles',
  'term_rules.not_found': 'geodata.refusals.ruleSetGone',
};

/**
 * Why an action on geodata or an import review did not go through, in a sentence the person who
 * pressed the button can act on.
 *
 * A code this screen knows has its own wording. Otherwise the status decides: a request that got
 * no answer, a signed-out session, a refused permission, something gone, a version that moved and
 * a server fault each lead somewhere different. Anything left is said with the caller's own
 * sentence for the action, never as "not found" or as silence.
 */
export function refusalText(error: unknown, t: TFunction, fallbackKey: string): string {
  // A refused export arrives as a download's own error, with the same status and code.
  if (!(error instanceof ApiError || error instanceof DownloadError)) {
    return t(fallbackKey);
  }
  const known = error.code ? BY_CODE[error.code] : undefined;
  if (known) {
    return t(known);
  }
  if (isConcurrencyConflict(error)) {
    return t('common.conflict');
  }
  switch (error.status) {
    case 0:
      return t('geodata.refusals.connection');
    case 401:
      return t('geodata.refusals.signedOut');
    case 403:
      return t('geodata.refusals.forbidden');
    case 404:
      return t('geodata.refusals.gone');
  }
  if (error.status >= 500) {
    return t('geodata.refusals.server', { status: error.status });
  }
  return t(fallbackKey);
}
