// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';

// Workspace UI state: serializable, carries references (ids),
// never entity payloads — panels fetch their own data through TanStack Query.
export interface EntranceSelection {
  kind: 'entrance';
  entranceId: string;
  caveId: string;
}

export interface FeatureSelection {
  kind: 'feature';
  featureId: string;
}

export type WorkspaceSelection = EntranceSelection | FeatureSelection;

interface WorkspaceState {
  selection: WorkspaceSelection | null;
  setSelection: (selection: WorkspaceSelection | null) => void;
}

export const useWorkspaceStore = create<WorkspaceState>((set) => ({
  selection: null,
  setSelection: (selection) => set({ selection }),
}));
