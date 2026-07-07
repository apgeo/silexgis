// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';

// Workspace UI state (04-frontend-spec.md §8): serializable, carries references (ids),
// never entity payloads — panels fetch their own data through TanStack Query.
export interface EntranceSelection {
  entranceId: string;
  caveId: string;
}

interface WorkspaceState {
  selection: EntranceSelection | null;
  setSelection: (selection: EntranceSelection | null) => void;
}

export const useWorkspaceStore = create<WorkspaceState>((set) => ({
  selection: null,
  setSelection: (selection) => set({ selection }),
}));
