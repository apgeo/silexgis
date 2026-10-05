// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMoveExpedition, type ActivityState, type Visibility } from '../../api/hooks.ts';
import ActivityStateControl from '../../components/trips/ActivityStateControl.tsx';

interface Props {
  expeditionId: string;
  state: ActivityState;
  /** Who may read the camp, so the announcement can say who it is announcing to. */
  visibility: Visibility;
  /** The camp's own write permission, already decided by the page. */
  canEdit: boolean;
}

/**
 * Moving a camp through its lifecycle: the shared control, driven by the camp's own move.
 *
 * A camp holds the trips' whole vocabulary and makes the same moves, so the buttons read as a
 * trip's do. What differs is said differently: announcing a camp notifies nobody — nothing
 * notifies on camps, and a confirmation that promised otherwise would be a lie — so its question
 * and its success say only that the camp is now announced and that who may read it is unchanged.
 * Calling it off names a camp, in the one language where the button names its subject.
 */
export default function ExpeditionStateControl({ expeditionId, state, visibility, canEdit }: Props) {
  const move = useMoveExpedition();

  return (
    <ActivityStateControl
      state={state}
      visibility={visibility}
      canEdit={canEdit}
      onMove={(to) => move.mutateAsync({ id: expeditionId, state: to })}
      pending={move.isPending}
      pendingState={move.variables?.state}
      wording={{
        publishConfirm: 'expeditions.publishConfirm',
        publishSuccess: 'expeditions.publishSuccess',
        callOff: 'expeditions.callOff',
        callOffConfirm: 'expeditions.callOffConfirm',
      }}
    />
  );
}
