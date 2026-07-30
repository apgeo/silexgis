// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where a built-in avatar's artwork lives. The id is the value stored on the account and
 * published by the API, so the two sides need no shared list beyond the id itself.
 */
export function avatarPresetUrl(id: string): string {
  return `/avatars/preset-${id}.svg`;
}
