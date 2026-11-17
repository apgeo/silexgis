// SPDX-License-Identifier: AGPL-3.0-or-later
import type { UploadItemStatus } from './uploadPlan.ts';

/**
 * The translation key for why a file did not land.
 *
 * <p>
 * The server answers with stable codes rather than sentences precisely so this mapping can live
 * on the client and be translated. Two spellings of several reasons are accepted because the
 * same refusal arrives from two places — as an HTTP problem code on an upload the person is
 * watching, and as a line of a batch's report written by work that ran in the background.
 * </p>
 * <p>
 * An unknown code falls back to a general failure rather than showing the code itself, which
 * would be a defect leaking into the interface. A failure with no code is worded from its HTTP
 * status where one is given: 0 for a connection that failed outright, a 5xx for a fault on the
 * server's side (a full or failing disk while a file was arriving is answered that way).
 * </p>
 */
export function uploadReasonKey(
  code: string | undefined,
  status: UploadItemStatus,
  httpStatus?: number,
): string {
  if (status === 'skipped' && !code) {
    return 'uploads.reason.duplicate';
  }

  switch (code) {
    case 'file.duplicate':
    case 'upload.duplicate':
      return 'uploads.reason.duplicate';
    case 'file.too_large':
    case 'upload.too_large':
      return 'uploads.reason.tooLarge';
    case 'file.type_not_accepted':
    case 'upload.type_not_accepted':
      return 'uploads.reason.typeNotAccepted';
    case 'file.quota_exceeded':
    case 'upload.quota_exceeded':
    case 'storage.insufficient':
      return 'uploads.reason.quotaExceeded';
    case 'file.empty':
    case 'upload.empty':
      return 'uploads.reason.empty';
    case 'upload.path_refused':
      return 'uploads.reason.pathRefused';
    case 'document.filing_forbidden':
    case 'upload.filing_refused':
      return 'uploads.reason.filingRefused';
    case 'upload.unreadable':
      return 'uploads.reason.unreadable';
    case 'upload.length_required':
      return 'uploads.reason.lengthRequired';
    case 'upload.session_not_found':
      return 'uploads.reason.sessionEnded';
    case 'upload.chunk_out_of_order':
    case 'upload.size_mismatch':
    case 'upload.incomplete':
      return 'uploads.reason.outOfStep';
    // Why a whole batch failed, recorded on the batch by the background work that ran it.
    case 'archive.unreadable':
      return 'uploads.reason.archiveUnreadable';
    case 'archive.too_many_entries':
      return 'uploads.reason.archiveTooManyEntries';
    case 'archive.too_large':
      return 'uploads.reason.archiveTooLarge';
    case 'archive.suspicious_compression':
      return 'uploads.reason.archiveSuspicious';
    case 'import.directory_not_found':
      return 'uploads.reason.importDirectoryNotFound';
    case 'import.path_not_allowed':
      return 'uploads.reason.importPathNotAllowed';
    case 'import.no_roots_configured':
      return 'uploads.reason.importNoRoots';
    default:
      if (code === undefined && httpStatus === 0) {
        return 'uploads.reason.connectionLost';
      }
      if (code === undefined && httpStatus !== undefined && httpStatus >= 500) {
        return 'uploads.reason.serverFault';
      }
      return 'uploads.reason.failed';
  }
}
