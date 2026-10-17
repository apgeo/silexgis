// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCapabilities, useCreateDoor } from '../../api/hooks/access.ts';
import { effectiveSavedFilterAudience, type SavedFilterAudience } from '../../filters/savedFilterAudience.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/** One caving group a filter may be shared with, as the choice names it. */
export interface AudienceGroup {
  id: string;
  name: string;
}

/**
 * The caving groups this member may share a filter with — their own groups a saved filter may be
 * bound to, as the server decides it for a create — and the audience a new filter starts with:
 * their default from Settings, narrowed to private when it names a group they can no longer use.
 *
 * `groups` is undefined until the member's access has arrived; the stored default is kept meanwhile.
 */
export function useSavedFilterAudienceChoice(): {
  groups: readonly AudienceGroup[] | undefined;
  defaultAudience: SavedFilterAudience;
} {
  const capabilities = useCapabilities();
  const { cavingGroups } = useCreateDoor('mapViews');
  const stored = useUiPrefsStore((s) => s.savedFilterAudience);
  const groups = capabilities.data ? cavingGroups : undefined;
  return { groups, defaultAudience: effectiveSavedFilterAudience(stored, groups) };
}
