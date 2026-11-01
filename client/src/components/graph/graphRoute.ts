// SPDX-License-Identifier: AGPL-3.0-or-later

/** The address of the relations graph seeded on one item. */
export function graphRoute(type: string, id: string): string {
  return `/graph?${new URLSearchParams({ type, id }).toString()}`;
}
