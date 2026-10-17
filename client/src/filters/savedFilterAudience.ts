// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Who a saved filter is shared with, as a member chooses it: only themselves, the members of one
 * of their caving groups, or everyone signed in to this installation. Never anonymous — no route
 * that runs a filter answers anyone who is not signed in, and the server refuses `public`.
 *
 * Whatever the audience, a filter grants nothing: each person who runs it is answered with only
 * what they could find by hand. The audience decides who sees the filter's wording in their list.
 */
export type SavedFilterAudience =
  | { visibility: 'private' }
  | { visibility: 'cavingGroup'; cavingGroupId: string }
  | { visibility: 'authenticated' };

/** What a member who has chosen nothing shares a new filter with: nobody. */
export const DEFAULT_SAVED_FILTER_AUDIENCE: SavedFilterAudience = { visibility: 'private' };

/** The anchor of the Settings section that holds the default, for a "change your default" link. */
export const SAVED_FILTER_AUDIENCE_ANCHOR = 'default-audience';

/** Where that link goes: the section, scrolled to and focused on the choice. */
export const SAVED_FILTER_AUDIENCE_SETTINGS_PATH = `/settings/filters#${SAVED_FILTER_AUDIENCE_ANCHOR}`;

/**
 * Whether a stored value is an audience this client can use. The account's preferences are written
 * by whichever client version the member last used, so anything of another shape is dropped and
 * the default applies — and a `public` written by anything at all never becomes a default.
 */
export function isSavedFilterAudience(value: unknown): value is SavedFilterAudience {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }
  const { visibility, cavingGroupId } = value as Record<string, unknown>;
  if (visibility === 'private' || visibility === 'authenticated') {
    return cavingGroupId === undefined;
  }
  return visibility === 'cavingGroup' && typeof cavingGroupId === 'string' && cavingGroupId.length > 0;
}

/**
 * The audience a new filter starts with: the member's stored default, unless it names a caving
 * group they can no longer share with (they left it, or it was removed) — then private. A stale
 * default only ever narrows, never widens to something the member did not choose.
 *
 * `groups` is undefined while the member's groups are still being fetched; the stored choice is
 * kept meanwhile, because the server refuses a group the member may not share with anyway.
 */
export function effectiveSavedFilterAudience(
  stored: SavedFilterAudience,
  groups: readonly { id: string }[] | undefined,
): SavedFilterAudience {
  if (stored.visibility !== 'cavingGroup' || groups === undefined) {
    return stored;
  }
  return groups.some((group) => group.id === stored.cavingGroupId) ? stored : DEFAULT_SAVED_FILTER_AUDIENCE;
}

/** The two fields a saved-filter write carries for an audience. */
export function audienceFields(audience: SavedFilterAudience): {
  visibility: SavedFilterAudience['visibility'];
  cavingGroupId: string | null;
} {
  return audience.visibility === 'cavingGroup'
    ? { visibility: 'cavingGroup', cavingGroupId: audience.cavingGroupId }
    : { visibility: audience.visibility, cavingGroupId: null };
}

/**
 * One string per audience, for a single choice control (a select's value): `private`,
 * `authenticated`, or `group:<id>`.
 */
export function audienceKey(audience: SavedFilterAudience): string {
  return audience.visibility === 'cavingGroup' ? `group:${audience.cavingGroupId}` : audience.visibility;
}

/** The audience a choice control's value stands for; private for anything it does not recognise. */
export function audienceFromKey(key: string): SavedFilterAudience {
  if (key === 'authenticated') {
    return { visibility: 'authenticated' };
  }
  if (key.startsWith('group:') && key.length > 'group:'.length) {
    return { visibility: 'cavingGroup', cavingGroupId: key.slice('group:'.length) };
  }
  return DEFAULT_SAVED_FILTER_AUDIENCE;
}
