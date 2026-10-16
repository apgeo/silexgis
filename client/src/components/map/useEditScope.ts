// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import type { ObjectRights } from '../../api/hooks/objectRights.ts';
import type { MapEditController } from '../../map/mapEdit.ts';

export interface EditScopeInput {
  controller: MapEditController | null | undefined;
  /** The feature selected on the map, if a feature is what is selected. */
  selectedFeatureId: string | undefined;
  /** That feature's own rights (the installation-wide stand-in until it has answered). */
  selectedRights: ObjectRights;
  mayWriteFeatures: boolean;
  mayCreateFeatures: boolean;
  /** Edits drawn or made on the map and not saved yet. */
  unsavedEdits: number;
}

export interface EditScope {
  /** Whether this member may edit anything on the map — what the map's menu offers by. */
  canEdit: boolean;
  /** Whether the edit toolbar is shown. */
  toolbarShown: boolean;
  /** Whether the reshaping tools may take hold of saved features at all. */
  mayReshapeSaved: boolean;
}

/**
 * What the map's edit tools may act on, kept on the edit controller itself — so the tools never
 * take hold of a feature this member may not change, rather than only having their buttons
 * disabled — and no tool left armed once the toolbar that shows it and puts it down is gone.
 *
 * With a feature selected, the reshaping tools take hold of that feature alone, and only once its
 * own rights say it may be changed; with none selected, of every feature, for a member who may
 * write them all. Features drawn here and not saved yet are always the member's to reshape (the
 * controller sees to that).
 */
export function useEditScope({
  controller,
  selectedFeatureId,
  selectedRights,
  mayWriteFeatures,
  mayCreateFeatures,
  unsavedEdits,
}: EditScopeInput): EditScope {
  const mayReshapeSaved =
    selectedFeatureId !== undefined ? selectedRights.settled && selectedRights.canEdit : mayWriteFeatures;
  const canEdit = mayWriteFeatures || mayCreateFeatures || selectedRights.canEdit;
  // Shown for those rights — and while there are unsaved edits, whose save and discard live on it.
  const toolbarShown = canEdit || unsavedEdits > 0;

  useEffect(() => {
    controller?.setReshapable(
      selectedFeatureId !== undefined
        ? (feature) => mayReshapeSaved && feature.get('id') === selectedFeatureId
        : mayReshapeSaved
          ? undefined
          : () => false,
    );
  }, [controller, selectedFeatureId, mayReshapeSaved]);

  useEffect(() => {
    if (!toolbarShown) {
      controller?.setMode('none');
    }
  }, [toolbarShown, controller]);

  return { canEdit, toolbarShown, mayReshapeSaved };
}
