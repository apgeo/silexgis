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

/** A low-zoom entrance cluster: the panel lists its members from the server. */
export interface ClusterSelection {
  kind: 'cluster';
  lon: number;
  lat: number;
  count: number;
  /** Map zoom at click time — determines the server's cluster cell size. */
  zoom: number;
}

export type WorkspaceSelection = EntranceSelection | FeatureSelection | CaveSelection | ClusterSelection;

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
  /**
   * Per-overlay opacity (0..1) for the built-in vector overlays (keyed by their layer id:
   * 'entrances', 'surface-features', 'centerlines') and for each geofile (keyed by geofile
   * id). A missing key means fully opaque. Rasters keep their own `rasterOpacity`.
   */
  overlayOpacity: Record<string, number>;
  setOverlayOpacity: (key: string, opacity: number) => void;
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
  overlayOpacity: {},
  setOverlayOpacity: (key, opacity) =>
    set((state) => ({ overlayOpacity: { ...state.overlayOpacity, [key]: opacity } })),
}));
