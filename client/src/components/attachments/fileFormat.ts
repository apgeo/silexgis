// SPDX-License-Identifier: AGPL-3.0-or-later

/** Human-readable file size: MB with one decimal above 1 MB, otherwise whole KB (min 1). */
export function formatSize(bytes: number): string {
  return bytes >= 1024 * 1024
    ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
