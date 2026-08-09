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
 * would be a defect leaking into the interface.
 * </p>
 */
export function uploadReasonKey(code: string | undefined, status: UploadItemStatus): string {
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
    default:
      return 'uploads.reason.failed';
  }
}
