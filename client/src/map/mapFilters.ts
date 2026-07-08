// SPDX-License-Identifier: AGPL-3.0-or-later

// Cross-layer map filters. Loaders read the current value on every fetch, so
// setting a filter just needs a reload of the affected layers.

let tagFilter: string | null = null;

export function getMapTagFilter(): string | null {
  return tagFilter;
}

export function setMapTagFilter(slug: string | null): void {
  tagFilter = slug;
}
