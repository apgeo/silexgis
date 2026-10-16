// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import type { ObjectRights } from '../../api/hooks/objectRights.ts';
import type { MapEditController, ReshapeScope } from '../../map/mapEdit.ts';

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
  /** Whether a reshaping tool may be armed for saved features now. */
  mayReshapeSaved: boolean;
  /** Whether the selected feature's own rights are still on their way. */
  rightsPending: boolean;
}

/**
 * Which saved features the reshaping tools may take hold of, by whose rights decide.
 *
 * A member who may write every feature reshapes any of them — except the selected one once its own
 * answer withholds it. A member without that right reshapes only the selected feature, and only
 * once its own answer allows it: their right is on that feature alone, and nothing else on the map
 * is theirs to drag about.
 */
export function reshapeScopeFor(
  selectedFeatureId: string | undefined,
  selectedRights: ObjectRights,
  mayWriteFeatures: boolean,
): ReshapeScope {
  const answered = selectedFeatureId !== undefined && selectedRights.settled;
  if (mayWriteFeatures) {
    return answered && !selectedRights.canEdit ? { kind: 'every', except: selectedFeatureId } : { kind: 'every' };
  }
  return answered && selectedRights.canEdit ? { kind: 'only', id: selectedFeatureId } : { kind: 'none' };
}

/**
 * What the map's edit tools may act on, kept on the edit controller itself — so the tools never
 * take hold of a feature this member may not change, rather than only having their buttons
 * disabled — and no tool left armed once the toolbar that shows it and puts it down is gone.
 * Features drawn here and not saved yet are always the member's to reshape (the controller sees to
 * that).
 */
export function useEditScope({
  controller,
  selectedFeatureId,
  selectedRights,
  mayWriteFeatures,
  mayCreateFeatures,
  unsavedEdits,
}: EditScopeInput): EditScope {
  const rightsPending = selectedFeatureId !== undefined && !selectedRights.settled;
  const mayReshapeSaved =
    selectedFeatureId !== undefined ? selectedRights.settled && selectedRights.canEdit : mayWriteFeatures;
  const canEdit = mayWriteFeatures || mayCreateFeatures || selectedRights.canEdit;
  // Shown for those rights — and while there are unsaved edits, whose save and discard live on it.
  const toolbarShown = canEdit || unsavedEdits > 0;

  const scope = reshapeScopeFor(selectedFeatureId, selectedRights, mayWriteFeatures);
  const scopeKey = JSON.stringify(scope);
  useEffect(() => {
    // Left as it was until the selected feature has answered. A tap on a vertex with a reshaping
    // tool armed is also what selects that feature; changing the scope in the moment before its
    // answer would re-attach the tool and drop the vertex the tap just took hold of.
    if (!rightsPending) {
      controller?.setReshapeScope(JSON.parse(scopeKey) as ReshapeScope);
    }
  }, [controller, scopeKey, rightsPending]);

  useEffect(() => {
    if (!toolbarShown) {
      controller?.setMode('none');
    }
  }, [toolbarShown, controller]);

  return { canEdit, toolbarShown, mayReshapeSaved, rightsPending };
}
