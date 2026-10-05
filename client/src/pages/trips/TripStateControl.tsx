// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMoveTripLog, type ActivityState, type Visibility } from '../../api/hooks.ts';
import ActivityStateControl from '../../components/trips/ActivityStateControl.tsx';

interface Props {
  tripId: string;
  state: ActivityState;
  /** Who may read the trip, so the announcement can say who it is announcing to. */
  visibility: Visibility;
  /** The trip's own write permission, already decided by the page. */
  canEdit: boolean;
}

/**
 * Moving a trip through its lifecycle: the shared control, driven by the trip's own move.
 *
 * The wording is the control's default — the vocabulary is the trips' — and the one thing a
 * trip's announcement says that a camp's does not, that the people named on it are told, is in
 * that default.
 */
export default function TripStateControl({ tripId, state, visibility, canEdit }: Props) {
  const move = useMoveTripLog();

  return (
    <ActivityStateControl
      state={state}
      visibility={visibility}
      canEdit={canEdit}
      onMove={(to) => move.mutateAsync({ id: tripId, state: to })}
      pending={move.isPending}
      pendingState={move.variables?.state}
    />
  );
}
