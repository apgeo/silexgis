// SPDX-License-Identifier: AGPL-3.0-or-later

/** Where one file in a drop has got to. */
export type UploadItemStatus = 'pending' | 'uploading' | 'stored' | 'skipped' | 'failed';

/**
 * One file of a drop, and everything the list beside it is drawn from.
 *
 * `relativePath` is what makes a folder drop a folder drop: the browser reports the path a
 * file had inside the folder that was dropped, and the server mirrors those folders as
 * cabinets. For an ordinary multi-file selection it is just the file name.
 */
export interface UploadItem {
  /** Stable across retries, so a row keeps its identity while its status changes. */
  id: string;
  file: File;
  relativePath: string;
  status: UploadItemStatus;
  /** 0–1. Meaningful for a resumable upload; a single-request one jumps from 0 to 1. */
  progress: number;
  /** Stable problem code, translated where it is shown. */
  errorCode?: string;
  documentId?: string;
  /** Set when the file was skipped because this content is already here. */
  duplicateOfDocumentId?: string;
}

/** How a drop ended, as the summary line states it. */
export interface UploadSummary {
  total: number;
  stored: number;
  skipped: number;
  failed: number;
  pending: number;
  /** Whether anything is still in flight, which is what keeps the dialog from claiming it is done. */
  running: boolean;
}

/**
 * The path a browser reports for a dropped file.
 *
 * `webkitRelativePath` is populated for a directory selection and empty for everything else;
 * it is non-standard in name only — every browser this application supports sets it. Falling
 * back to the bare name is what makes a mixed drop work: files picked individually have no
 * folder and must not acquire one.
 */
export function relativePathOf(file: File): string {
  const withPath = file as File & { webkitRelativePath?: string };
  const reported = withPath.webkitRelativePath;
  return reported !== undefined && reported.length > 0 ? reported : file.name;
}

/**
 * Turns a set of chosen files into the queue.
 *
 * Ids are positional rather than derived from the name, because a folder drop routinely
 * contains two files with the same name in different folders — and a list whose rows share a
 * key updates the wrong row.
 */
export function planUpload(files: readonly File[], idPrefix = 'u'): UploadItem[] {
  return files.map((file, index) => ({
    id: `${idPrefix}-${index}`,
    file,
    relativePath: relativePathOf(file),
    status: 'pending' as const,
    progress: 0,
  }));
}

/** Applies a change to one row, leaving every other row identical. */
export function withItem(
  items: readonly UploadItem[],
  id: string,
  change: Partial<UploadItem>,
): UploadItem[] {
  return items.map((item) => (item.id === id ? { ...item, ...change } : item));
}

export function summarise(items: readonly UploadItem[]): UploadSummary {
  const count = (status: UploadItemStatus) => items.filter((i) => i.status === status).length;
  const pending = count('pending');
  const uploading = count('uploading');
  return {
    total: items.length,
    stored: count('stored'),
    skipped: count('skipped'),
    failed: count('failed'),
    pending,
    running: pending + uploading > 0,
  };
}

/**
 * The files a retry should send: the ones that failed, and nothing else.
 *
 * Retrying the whole drop is the wrong shape and the reason this exists — re-sending four
 * hundred files because twelve failed is both slow and, for the ones that succeeded, refused
 * as duplicates.
 */
export function retryable(items: readonly UploadItem[]): UploadItem[] {
  return items.filter((item) => item.status === 'failed');
}

/** Puts failed rows back in the queue, untouched otherwise. */
export function resetForRetry(items: readonly UploadItem[]): UploadItem[] {
  return items.map((item) =>
    item.status === 'failed'
      ? { ...item, status: 'pending' as const, progress: 0, errorCode: undefined }
      : item,
  );
}

/**
 * Whether a file is large enough to be worth a resumable session.
 *
 * Below the threshold the extra round trips cost more than the resumption is worth: a 4 MB
 * photograph that fails is re-sent in seconds, and a 400 MB scan is not.
 */
export function shouldResume(sizeBytes: number, thresholdBytes: number): boolean {
  return thresholdBytes > 0 && sizeBytes > thresholdBytes;
}

/**
 * The byte ranges a resumable upload sends, starting from what the server already holds.
 *
 * Starting from `receivedBytes` rather than from zero is the whole of resuming: a client that
 * came back after losing its connection sends the remainder, not the file.
 */
export function chunkRanges(
  sizeBytes: number,
  chunkBytes: number,
  receivedBytes = 0,
): { start: number; end: number }[] {
  if (sizeBytes <= 0 || chunkBytes <= 0 || receivedBytes >= sizeBytes) {
    return [];
  }

  const ranges: { start: number; end: number }[] = [];
  for (let start = Math.max(0, receivedBytes); start < sizeBytes; start += chunkBytes) {
    ranges.push({ start, end: Math.min(start + chunkBytes, sizeBytes) });
  }
  return ranges;
}

/**
 * How many uploads run at once.
 *
 * Small on purpose. The server writes each upload to disk and hashes it, and a browser opening
 * twenty connections to one host mostly queues them anyway — while a drop whose first file is
 * a 400 MB scan should not hold the other nineteen behind it.
 */
export const UploadConcurrency = 3;
