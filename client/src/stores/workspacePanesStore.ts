// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';

/** The panes of the map workspace that can be made to fill it. */
export type WorkspacePaneId = 'map' | 'layers' | 'details' | 'scene3d' | 'caveview' | 'table';

interface WorkspacePanesState {
  /**
   * Whether the 3D scene is open in this window. Kept for the session only and never with the
   * account — see the arrangement's own note for why a renderer is opened only when asked for.
   */
  scene3dOpen: boolean;
  setScene3dOpen: (open: boolean) => void;
  /**
   * Whether the survey-model pane is shown while no model is open in it: a preset for reviewing
   * surveys puts the pane up empty, saying how to open one, rather than waiting for a model.
   */
  caveViewPaneOpen: boolean;
  setCaveViewPaneOpen: (open: boolean) => void;
  /**
   * Which survey model is open beside the map, if any. Held as an id rather than as the model, so
   * the pane keeps asking for it: the answer carries a signed address with a ten-minute life, and
   * a pane left open outlives it.
   */
  caveViewModelId: string | null;
  setCaveViewModelId: (id: string | null) => void;
  /** The pane filling the workspace, or null when every pane is at its own size. */
  maximised: WorkspacePaneId | null;
  setMaximised: (pane: WorkspacePaneId | null) => void;
}

/**
 * The map workspace's panes as this window has them now. Session state, deliberately apart from
 * the stored preferences: a popped-out window has its own, and a reload starts every renderer shut.
 */
export const useWorkspacePanesStore = create<WorkspacePanesState>()((set) => ({
  scene3dOpen: false,
  setScene3dOpen: (open) => set({ scene3dOpen: open }),
  caveViewPaneOpen: false,
  setCaveViewPaneOpen: (open) => set({ caveViewPaneOpen: open }),
  caveViewModelId: null,
  setCaveViewModelId: (id) => set({ caveViewModelId: id }),
  maximised: null,
  setMaximised: (pane) => set({ maximised: pane }),
}));
