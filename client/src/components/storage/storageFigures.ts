// SPDX-License-Identifier: AGPL-3.0-or-later
import type { StorageLevel, StorageLimit } from '../../api/hooks.ts';

const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/**
 * A byte count as somebody reads a disk: one decimal from megabytes up, whole kilobytes below.
 * The attachment lists stop at megabytes, which is right for a photo and wrong for a store where
 * a single scan run takes gigabytes — "9216.0 MB" makes the reader do the division.
 */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return '0 B';
  }
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024;
    unit += 1;
  }
  if (unit === 0) {
    return `${Math.round(value)} B`;
  }
  return unit === 1 ? `${Math.max(1, Math.round(value))} KB` : `${value.toFixed(1)} ${UNITS[unit]}`;
}

/** How much of a cap is used, in whole percent; null without a cap. Never below 0; may pass 100. */
export function percentOfCap(used: number, cap: number | null | undefined): number | null {
  if (cap === null || cap === undefined || cap <= 0) {
    return null;
  }
  return Math.max(0, Math.round((used / cap) * 100));
}

/** The alert a level is shown with; `normal` is not shown at all. */
export function levelAlertType(level: string): 'info' | 'warning' | 'error' | null {
  switch (level as StorageLevel) {
    case 'notice':
      return 'info';
    case 'warning':
      return 'warning';
    case 'full':
      return 'error';
    default:
      return null;
  }
}

/** Every ceiling a shortfall can name, so each has a translated name. */
export const STORAGE_LIMITS: readonly StorageLimit[] = ['account', 'installation', 'scans', 'storeDisk', 'workerDisk'];

/** The translation key of a ceiling's name; an unknown one (a newer server) falls back to a general word. */
export function limitKey(limit: string): string {
  return (STORAGE_LIMITS as readonly string[]).includes(limit) ? `fileStorage.limits.${limit}` : 'fileStorage.limits.other';
}

/**
 * The translation key of a kind of stored file as the report spells it (`image`, `original`,
 * `point-cloud-tiles`, …). Kinds are appended on the server over time, so the page passes the
 * spelling itself as the fallback rather than failing on a kind it has no name for.
 */
export function kindKey(kind: string): string {
  return `fileStorage.kinds.${kind.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase())}`;
}

/** The groups a report can offer for deletion, in the order it lists them. */
export const RECLAIM_GROUPS = [
  'superseded-scan-products',
  'failed-run-work',
  'older-survey-versions',
  'expired-uploads',
] as const;

/** The translation keys of a deletable group's name and hint; an unknown one (a newer server) gets a general wording. */
export function reclaimGroupKeys(group: string): { title: string; hint: string } {
  const known: Record<string, string> = {
    'superseded-scan-products': 'supersededScanProducts',
    'failed-run-work': 'failedRunWork',
    'older-survey-versions': 'olderSurveyVersions',
    'expired-uploads': 'expiredUploads',
  };
  const stem = known[group] ?? 'other';
  return { title: `fileStorage.groups.${stem}.title`, hint: `fileStorage.groups.${stem}.hint` };
}

/**
 * The numbers a refusal for want of room carries beside its sentence, read from the problem's own
 * members rather than its English detail. Null when the problem is not that refusal or lacks them.
 */
export function insufficientFigures(
  problem: Readonly<Record<string, unknown>> | undefined,
): { limit: string; missingBytes: number } | null {
  if (!problem || problem.code !== 'storage.insufficient') {
    return null;
  }
  const missing = problem.missingBytes;
  const limit = problem.limit;
  return typeof missing === 'number' && typeof limit === 'string' ? { limit, missingBytes: missing } : null;
}

/**
 * How many times its input a tileset is taken to be when the estimate has no figure for it: the
 * top of the range tiles were measured at, as the server assumes when it checks a run.
 */
export const TILE_FACTOR_WITHOUT_ESTIMATE = 6;

/**
 * What a point cloud's processing run adds to the store, as the server estimates it before letting
 * the run start: the thinned copy (taken as large as the cloud it thins) when the run thins, plus
 * the tiles — the estimate's figure for the row chosen, else the upper factor over what is tiled.
 * Null when there is nothing to go by. Advice only: the server checks its own figure again.
 */
export function runAddsBytes(
  thins: boolean,
  originalBytes: number | null | undefined,
  estimatedTileBytes: number | null | undefined,
): number | null {
  const original = originalBytes ?? null;
  const tiles =
    estimatedTileBytes !== null && estimatedTileBytes !== undefined && estimatedTileBytes > 0
      ? estimatedTileBytes
      : original === null
        ? null
        : original * TILE_FACTOR_WITHOUT_ESTIMATE;
  if (tiles === null) {
    return null;
  }
  return thins && original !== null ? original + tiles : tiles;
}

/** The size of a point-cloud upload's original: what arrived, else what was declared. Null without one. */
export function originalBytesOf(
  files: readonly { role: string; receivedSize?: number | null; declaredSize?: number | null }[],
): number | null {
  const archive = files.find((file) => file.role === 'archive');
  return archive?.receivedSize ?? archive?.declaredSize ?? null;
}
