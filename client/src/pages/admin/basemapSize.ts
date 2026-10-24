// SPDX-License-Identifier: AGPL-3.0-or-later

/** A size the way the extracting tool reports it: decimal units, one decimal. */
export function formatBytes(bytes: number): string {
  if (bytes >= 1e9) {
    return `${(bytes / 1e9).toFixed(1)} GB`;
  }
  if (bytes >= 1e6) {
    return `${(bytes / 1e6).toFixed(1)} MB`;
  }
  return `${Math.max(1, Math.round(bytes / 1e3))} kB`;
}
