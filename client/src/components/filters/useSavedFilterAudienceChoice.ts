// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { ApiError } from '../../api/client.ts';
import { useCapabilities, useCreateDoor } from '../../api/hooks/access.ts';
import { useCavingGroups } from '../../api/hooks/people.ts';
import { filterRefusalKey, isFilterRefusal } from '../../filters/refusal.ts';
import { effectiveSavedFilterAudience, type SavedFilterAudience } from '../../filters/savedFilterAudience.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/** One caving group a filter may be shared with, as the choice names it. */
export interface AudienceGroup {
  id: string;
  name: string;
}

/**
 * Who this member may offer a filter to: the caving groups the server accepts a saved filter of
 * theirs being bound to (their own clubs, as it decides it for a create), every other club of the
 * installation after them, and the audience a new filter starts with — their default from Settings,
 * narrowed to private when it names a group they can no longer use.
 *
 * The other clubs are offered because the owner asked for the whole list: a member may know which
 * club they mean before knowing whether they may share with it. Picking one the server does not
 * accept is refused by the server, and the save sites say so in words of their own.
 *
 * `groups` is undefined until the member's access has arrived, `otherGroups` until the list of
 * clubs has; the stored default is kept meanwhile.
 */
export function useSavedFilterAudienceChoice(): {
  groups: readonly AudienceGroup[] | undefined;
  otherGroups: readonly AudienceGroup[] | undefined;
  defaultAudience: SavedFilterAudience;
} {
  const capabilities = useCapabilities();
  const { cavingGroups } = useCreateDoor('mapViews');
  const { data: allGroups } = useCavingGroups();
  const stored = useUiPrefsStore((s) => s.savedFilterAudience);
  const groups = capabilities.data ? cavingGroups : undefined;
  const otherGroups = useMemo(() => {
    if (groups === undefined || allGroups === undefined) {
      return undefined;
    }
    const shareable = new Set(groups.map((group) => group.id));
    return allGroups
      .filter((group) => !shareable.has(group.id))
      .map((group) => ({ id: group.id, name: group.name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [groups, allGroups]);
  return { groups, otherGroups, defaultAudience: effectiveSavedFilterAudience(stored, groups) };
}

/** What the server answers when a filter is bound to a caving group its author may not share with. */
const GROUP_NOT_ALLOWED_CODE = 'access.caving_group_binding_forbidden';

/** What the server answers when the filter being updated was deleted, or is no longer the author's to change. */
const SAVED_FILTER_GONE_CODE = 'saved_filter.not_found';

/**
 * The message for a refused save of a saved filter, as the key of a sentence the author can act on:
 * a club the author may not share with is named as such — it is on offer, so picking it is an
 * ordinary mistake, not a failure — and so is a member without the right to save filters at all, a
 * filter holding a typed position (never stored, because it would travel with every share), and a
 * filter deleted or unshared elsewhere since it was opened (an update sends no conditions, so
 * telling the author to check them would send them in circles). A refusal the filter editor
 * already words by its code keeps that sentence; any other refusal of the filter itself is the
 * general "cannot save it as it stands". Only what is not a refusal — a dropped connection, a
 * server error — is the general failure.
 */
export function savedFilterSaveFailedKey(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'common.saveFailed';
  }
  switch (error.code) {
    case GROUP_NOT_ALLOWED_CODE:
      return 'savedFilters.groupNotAllowed';
    case 'access.create_forbidden':
      return 'savedFilters.saveRefused.forbidden';
    case 'saved_filter.anchor_refused':
      return 'savedFilters.saveRefused.holdsPosition';
    case SAVED_FILTER_GONE_CODE:
      return 'savedFilters.saveRefused.gone';
    default:
      return (
        filterRefusalKey(error.code) ??
        (isFilterRefusal(error) ? 'savedFilters.saveRefused.notSavable' : 'common.saveFailed')
      );
  }
}
