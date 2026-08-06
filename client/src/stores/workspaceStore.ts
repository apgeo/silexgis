// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import type { Scene3DSurfaceMode } from '../scene3d/scene3dEngine.ts';

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
  /**
   * Whether each built-in vector overlay is drawn, keyed exactly as `overlayOpacity` is. A
   * missing key means shown, so nothing has to enumerate the overlays to start from a sane view.
   */
  overlayVisible: Record<string, boolean>;
  setOverlayVisible: (key: string, visible: boolean) => void;
  /**
   * How the 3D scene draws the ground over a cave: 'overlay' shows the survey through it,
   * 'cutaway' removes the ground above the cave instead. Session state rather than a stored
   * preference — it belongs to the view being worked in, not to the browser.
   */
  scene3dSurfaceMode: Scene3DSurfaceMode;
  setScene3dSurfaceMode: (mode: Scene3DSurfaceMode) => void;
  /**
   * Per-base-layer opacity (0..1), keyed by catalog id. Only the active base is visible
   * at a time, but each base remembers its own value so switching restores it. A missing
   * key means fully opaque.
   */
  baseOpacity: Record<number, number>;
  setBaseOpacity: (id: number, opacity: number) => void;
  /**
   * Replaces the whole base-opacity map at once — used when restoring a saved view, so a
   * base the view doesn't mention reverts to opaque instead of keeping an earlier manual
   * dim (a plain per-key merge would leave stale values in place).
   */
  resetBaseOpacity: (opacities: Record<number, number>) => void;
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
  overlayVisible: {},
  setOverlayVisible: (key, visible) =>
    set((state) => ({ overlayVisible: { ...state.overlayVisible, [key]: visible } })),
  scene3dSurfaceMode: 'overlay',
  setScene3dSurfaceMode: (scene3dSurfaceMode) => set({ scene3dSurfaceMode }),
  baseOpacity: {},
  setBaseOpacity: (id, opacity) =>
    set((state) => ({ baseOpacity: { ...state.baseOpacity, [id]: opacity } })),
  resetBaseOpacity: (opacities) => set({ baseOpacity: opacities }),
}));
