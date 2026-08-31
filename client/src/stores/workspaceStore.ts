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

/** One member of a multi-selection: a reference, never a payload, like everything else here. */
export interface SelectedRef {
  kind: 'feature' | 'cave' | 'entrance';
  id: string;
}

interface WorkspaceState {
  selection: WorkspaceSelection | null;
  setSelection: (selection: WorkspaceSelection | null) => void;
  /**
   * Several objects picked at once — a modifier-click on the map, or ticking rows in the list.
   * Ids only, capped, because a panel showing what they have in common asks the server rather
   * than holding entities here.
   */
  selectionSet: SelectedRef[];
  setSelectionSet: (refs: SelectedRef[]) => void;
  toggleInSelectionSet: (ref: SelectedRef) => void;
  clearSelectionSet: () => void;
  /**
   * Recently selected objects, so following a link and coming back is one press.
   *
   * Recorded inside `setSelection` rather than by whoever calls it: map clicks, the bus, the
   * in-view list and deep links all set a selection, and a history that only some of them wrote
   * to would skip exactly the steps somebody wants to go back through.
   */
  selectionHistory: WorkspaceSelection[];
  selectionCursor: number;
  goBackSelection: () => void;
  goForwardSelection: () => void;
  canGoBackSelection: () => boolean;
  canGoForwardSelection: () => boolean;
  /** Geofile overlays currently shown on the map (ids only). */
  visibleGeofileIds: string[];
  setGeofileVisible: (id: string, visible: boolean) => void;
  /**
   * Tile overlays from the layer catalogue that are drawn on top of the basemap (catalogue ids),
   * with per-layer opacity. Several at once, unlike the basemap: hiking routes and ski routes over
   * one topographic map are three answers about one place, and picking between them defeats the
   * point of having them.
   */
  visibleTileOverlayIds: number[];
  setTileOverlayVisible: (id: number, visible: boolean) => void;
  tileOverlayOpacity: Record<number, number>;
  setTileOverlayOpacity: (id: number, opacity: number) => void;
  /**
   * Whether labels and markers that would land on top of each other are thinned out.
   *
   * On by default, because the case it answers is the ordinary one — a few thousand imported
   * waypoints, whose names at any zoom that shows the whole file are a grey smear. Off is a real
   * choice rather than a debugging aid: somebody checking that every station in a file arrived
   * needs every name on screen at once, however ugly, and a view that quietly drops some of them
   * cannot answer that question at all.
   */
  declutterLabels: boolean;
  setDeclutterLabels: (value: boolean) => void;
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

/**
 * How far back the panel can go. Long enough to retrace an afternoon's clicking, short enough
 * that the list is never something a person scrolls.
 */
const MAX_SELECTION_HISTORY = 50;

function sameSelection(a: WorkspaceSelection | null, b: WorkspaceSelection | null): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

export const useWorkspaceStore = create<WorkspaceState>((set, get) => ({
  selection: null,
  setSelection: (selection) =>
    set((state) => {
      if (sameSelection(state.selection, selection)) {
        return { selection };
      }

      if (selection === null) {
        // Deselecting is not a place to come back to; the history keeps its shape so that
        // pressing Escape and then Back returns to what was selected before, not to nothing.
        return { selection };
      }

      // Selecting something after going back drops the forward tail, which is what every
      // back/forward anybody has used does.
      const trimmed = state.selectionHistory.slice(0, state.selectionCursor + 1);
      const next = [...trimmed, selection].slice(-MAX_SELECTION_HISTORY);
      return { selection, selectionHistory: next, selectionCursor: next.length - 1 };
    }),
  selectionSet: [],
  setSelectionSet: (refs) => set({ selectionSet: refs.slice(0, MAX_SELECTION_SET) }),
  toggleInSelectionSet: (ref) =>
    set((state) => {
      const without = state.selectionSet.filter((x) => !(x.kind === ref.kind && x.id === ref.id));
      return {
        selectionSet:
          without.length === state.selectionSet.length
            ? [...state.selectionSet, ref].slice(0, MAX_SELECTION_SET)
            : without,
      };
    }),
  clearSelectionSet: () => set({ selectionSet: [] }),
  selectionHistory: [],
  selectionCursor: -1,
  goBackSelection: () =>
    set((state) => {
      const at = state.selectionCursor - 1;
      return at < 0
        ? {}
        : { selectionCursor: at, selection: state.selectionHistory[at] ?? null };
    }),
  goForwardSelection: () =>
    set((state) => {
      const at = state.selectionCursor + 1;
      return at >= state.selectionHistory.length
        ? {}
        : { selectionCursor: at, selection: state.selectionHistory[at] ?? null };
    }),
  canGoBackSelection: () => get().selectionCursor > 0,
  canGoForwardSelection: () => get().selectionCursor < get().selectionHistory.length - 1,
  visibleGeofileIds: [],
  setGeofileVisible: (id, visible) =>
    set((state) => ({
      visibleGeofileIds: visible
        ? [...new Set([...state.visibleGeofileIds, id])]
        : state.visibleGeofileIds.filter((x) => x !== id),
    })),
  visibleTileOverlayIds: [],
  setTileOverlayVisible: (id, visible) =>
    set((state) => ({
      visibleTileOverlayIds: visible
        ? [...new Set([...state.visibleTileOverlayIds, id])]
        : state.visibleTileOverlayIds.filter((x) => x !== id),
    })),
  tileOverlayOpacity: {},
  setTileOverlayOpacity: (id, opacity) =>
    set((state) => ({ tileOverlayOpacity: { ...state.tileOverlayOpacity, [id]: opacity } })),
  declutterLabels: true,
  setDeclutterLabels: (declutterLabels) => set({ declutterLabels }),
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

/**
 * A ceiling on how many objects can be picked at once. Bulk actions write per object through the
 * ordinary gates, so this is what keeps one gesture from becoming two hundred requests.
 */
const MAX_SELECTION_SET = 200;
