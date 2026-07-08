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

/** Cave picked without a specific entrance (e.g. from a popped-out registry). */
export interface CaveSelection {
  kind: 'cave';
  caveId: string;
}

export type WorkspaceSelection = EntranceSelection | FeatureSelection | CaveSelection;

interface WorkspaceState {
  selection: WorkspaceSelection | null;
  setSelection: (selection: WorkspaceSelection | null) => void;
  /** Geofile overlays currently shown on the map (ids only). */
  visibleGeofileIds: string[];
  setGeofileVisible: (id: string, visible: boolean) => void;
  /** Georeferenced raster overlays shown on the map, with per-map opacity overrides. */
  visibleRasterIds: string[];
  setRasterVisible: (id: string, visible: boolean) => void;
  rasterOpacity: Record<string, number>;
  setRasterOpacity: (id: string, opacity: number) => void;
}

export const useWorkspaceStore = create<WorkspaceState>((set) => ({
  selection: null,
  setSelection: (selection) => set({ selection }),
  visibleGeofileIds: [],
  setGeofileVisible: (id, visible) =>
    set((state) => ({
      visibleGeofileIds: visible
        ? [...new Set([...state.visibleGeofileIds, id])]
        : state.visibleGeofileIds.filter((x) => x !== id),
    })),
  visibleRasterIds: [],
  setRasterVisible: (id, visible) =>
    set((state) => ({
      visibleRasterIds: visible
        ? [...new Set([...state.visibleRasterIds, id])]
        : state.visibleRasterIds.filter((x) => x !== id),
    })),
  rasterOpacity: {},
  setRasterOpacity: (id, opacity) =>
    set((state) => ({ rasterOpacity: { ...state.rasterOpacity, [id]: opacity } })),
}));
