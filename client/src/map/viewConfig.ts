// SPDX-License-Identifier: AGPL-3.0-or-later
import { fromLonLat, toLonLat } from 'ol/proj';
import { normalizeCamera3DState, type Camera3DState } from '../scene3d/camera3d.ts';
import { viewCamera3dState } from '../workspace/viewCamera.ts';
import { getOverlayOrder, getWorkspaceMap } from './mapContext.ts';

/**
 * The saved-view config document (client-owned, versioned). Everything needed to
 * restore a workspace: camera, base layer, overlay toggles and filters.
 */
export interface ViewConfig {
  /**
   * Never bumped. Everything added since the first release is an optional field with a documented
   * default, and the reader rejects any other version outright — so raising this number would make
   * every view saved from that moment unopenable by anything already deployed, and every view
   * saved before it unopenable by the new code. The version is there to reject a document that is
   * not one of these at all, not to track additions.
   */
  configVersion: 1;
  center: [number, number]; // lon/lat
  zoom: number;
  baseLayerId?: number;
  entrancesVisible: boolean;
  surfaceFeaturesVisible: boolean;
  /** Added after v1 shipped; older saved views omit it (treated as off — an opt-in overlay). */
  centerlinesVisible?: boolean;
  /** Added after v1 shipped; older saved views omit it (treated as off). */
  heatmapVisible?: boolean;
  /** Added after v1 shipped; older saved views omit it (treated as false — an opt-in overlay). */
  photosVisible?: boolean;
  geofileIds: string[];
  rasters: { id: string; opacity?: number }[];
  tagFilter: string | null;
  /** Added after v1 shipped; older saved views omit it (treated as fully opaque). */
  overlayOpacity?: Record<string, number>;
  /** Per-base-layer opacity keyed by catalog id. Older saved views omit it (opaque). */
  baseOpacity?: Record<number, number>;
  /**
   * Overlay stacking, bottom→top, as layer ids (built-in ids plus `geofile:`/`raster:`
   * prefixed ones). Added after v1 shipped; older saved views omit it (default order).
   */
  overlayOrder?: string[];
  /**
   * Where the 3D scene's camera stood, in degrees and metres. Added after v1 shipped; older saved
   * views omit it, and so does any view saved while no 3D view was on screen — both mean "this
   * view says nothing about the scene", and opening one leaves the scene's camera alone.
   *
   * Written down rather than stored as whatever the renderer holds, for the same reason the
   * document has a version at all: a saved view outlives the code that saved it.
   */
  camera3d?: Camera3DState;
}

/** UI state the map page owns; the camera lives on the OL map itself. */
export interface WorkspaceUiState {
  baseLayerId?: number;
  entrancesVisible: boolean;
  surfaceFeaturesVisible: boolean;
  centerlinesVisible: boolean;
  heatmapVisible: boolean;
  photosVisible: boolean;
  geofileIds: string[];
  rasters: { id: string; opacity?: number }[];
  tagFilter: string | null;
  overlayOpacity: Record<string, number>;
  baseOpacity: Record<number, number>;
  overlayOrder: string[];
  /** The 3D camera the view carried, if any; applied to the 3D view on screen, if there is one. */
  camera3d?: Camera3DState;
}

// The stacking order is read straight off the OL overlay group, and the 3D camera off whichever
// view is on screen, so capture callers pass neither. The 3D camera in particular must not be
// passed in: the page assembling this state is the flat map's, which has no way of knowing whether
// a scene is mounted beside it, and a stale camera saved from a scene closed ten minutes ago would
// be restored as confidently as a live one.
export function captureViewConfig(
  ui: Omit<WorkspaceUiState, 'overlayOrder' | 'camera3d'>,
): ViewConfig {
  const view = getWorkspaceMap().getView();
  const center = toLonLat(view.getCenter() ?? [0, 0]);
  const camera3d = viewCamera3dState();
  return {
    configVersion: 1,
    ...(camera3d ? { camera3d } : {}),
    center: [Number(center[0].toFixed(6)), Number(center[1].toFixed(6))],
    zoom: Math.round((view.getZoom() ?? 8) * 100) / 100,
    baseLayerId: ui.baseLayerId,
    entrancesVisible: ui.entrancesVisible,
    surfaceFeaturesVisible: ui.surfaceFeaturesVisible,
    centerlinesVisible: ui.centerlinesVisible,
    heatmapVisible: ui.heatmapVisible,
    photosVisible: ui.photosVisible,
    geofileIds: ui.geofileIds,
    rasters: ui.rasters,
    tagFilter: ui.tagFilter,
    overlayOpacity: ui.overlayOpacity,
    baseOpacity: ui.baseOpacity,
    overlayOrder: getOverlayOrder(),
  };
}

/** Restores the camera; returns the UI state for the page to apply. Unknown versions no-op. */
export function applyViewConfig(config: unknown): WorkspaceUiState | null {
  const parsed = config as Partial<ViewConfig> | null;
  if (!parsed || parsed.configVersion !== 1 || !Array.isArray(parsed.center)) {
    return null;
  }

  const view = getWorkspaceMap().getView();
  view.animate({ center: fromLonLat(parsed.center), zoom: parsed.zoom ?? 12, duration: 400 });

  return {
    baseLayerId: parsed.baseLayerId,
    entrancesVisible: parsed.entrancesVisible ?? true,
    surfaceFeaturesVisible: parsed.surfaceFeaturesVisible ?? true,
    centerlinesVisible: parsed.centerlinesVisible ?? false,
    heatmapVisible: parsed.heatmapVisible ?? false,
    photosVisible: parsed.photosVisible ?? false,
    geofileIds: parsed.geofileIds ?? [],
    rasters: parsed.rasters ?? [],
    tagFilter: parsed.tagFilter ?? null,
    overlayOpacity: parsed.overlayOpacity ?? {},
    baseOpacity: parsed.baseOpacity ?? {},
    overlayOrder: parsed.overlayOrder ?? [],
    // Checked field by field rather than trusted: the document is client-owned and the server
    // stores it without looking inside, so a hand-edited or older-shaped camera block has to read
    // as "this view says nothing about the scene" instead of taking a camera somewhere impossible.
    camera3d: normalizeCamera3DState(parsed.camera3d) ?? undefined,
  };
}

/** Renders the current map canvas composite to a PNG blob (attribution included). */
export function exportMapImage(): Promise<Blob | null> {
  const map = getWorkspaceMap();
  return new Promise((resolve) => {
    map.once('rendercomplete', () => {
      const size = map.getSize()!;
      const target = document.createElement('canvas');
      target.width = size[0];
      target.height = size[1];
      const context = target.getContext('2d')!;
      // Compose every layer canvas honoring per-layer opacity/transforms.
      map.getTargetElement()
        .querySelectorAll<HTMLCanvasElement>('.ol-layer canvas, canvas.ol-layer')
        .forEach((canvas) => {
          if (canvas.width > 0) {
            const opacity = (canvas.parentNode as HTMLElement)?.style.opacity || canvas.style.opacity;
            context.globalAlpha = opacity === '' ? 1 : Number(opacity);
            const transform = canvas.style.transform;
            const match = /^matrix\(([^(]*)\)$/.exec(transform);
            if (match) {
              context.setTransform(...(match[1].split(',').map(Number) as [number, number, number, number, number, number]));
            } else {
              context.setTransform(1, 0, 0, 1, 0, 0);
            }
            context.drawImage(canvas, 0, 0);
          }
        });
      context.globalAlpha = 1;
      context.setTransform(1, 0, 0, 1, 0, 0);
      target.toBlob(resolve, 'image/png');
    });
    map.renderSync();
  });
}
