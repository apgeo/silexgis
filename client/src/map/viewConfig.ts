// SPDX-License-Identifier: AGPL-3.0-or-later
import { fromLonLat, toLonLat } from 'ol/proj';
import { getOverlayOrder, getWorkspaceMap } from './mapContext.ts';

/**
 * The saved-view config document (client-owned, versioned). Everything needed to
 * restore a workspace: camera, base layer, overlay toggles and filters.
 */
export interface ViewConfig {
  configVersion: 1;
  center: [number, number]; // lon/lat
  zoom: number;
  baseLayerId?: number;
  entrancesVisible: boolean;
  surfaceFeaturesVisible: boolean;
  /** Added after v1 shipped; older saved views omit it (treated as true). */
  centerlinesVisible?: boolean;
  geofileIds: string[];
  rasters: { id: string; opacity?: number }[];
  tagFilter: string | null;
  /** Added after v1 shipped; older saved views omit it (treated as fully opaque). */
  overlayOpacity?: Record<string, number>;
  /**
   * Overlay stacking, bottom→top, as layer ids (built-in ids plus `geofile:`/`raster:`
   * prefixed ones). Added after v1 shipped; older saved views omit it (default order).
   */
  overlayOrder?: string[];
}

/** UI state the map page owns; the camera lives on the OL map itself. */
export interface WorkspaceUiState {
  baseLayerId?: number;
  entrancesVisible: boolean;
  surfaceFeaturesVisible: boolean;
  centerlinesVisible: boolean;
  geofileIds: string[];
  rasters: { id: string; opacity?: number }[];
  tagFilter: string | null;
  overlayOpacity: Record<string, number>;
  overlayOrder: string[];
}

// The stacking order is read straight off the OL overlay group, so capture
// callers don't pass it.
export function captureViewConfig(ui: Omit<WorkspaceUiState, 'overlayOrder'>): ViewConfig {
  const view = getWorkspaceMap().getView();
  const center = toLonLat(view.getCenter() ?? [0, 0]);
  return {
    configVersion: 1,
    center: [Number(center[0].toFixed(6)), Number(center[1].toFixed(6))],
    zoom: Math.round((view.getZoom() ?? 8) * 100) / 100,
    baseLayerId: ui.baseLayerId,
    entrancesVisible: ui.entrancesVisible,
    surfaceFeaturesVisible: ui.surfaceFeaturesVisible,
    centerlinesVisible: ui.centerlinesVisible,
    geofileIds: ui.geofileIds,
    rasters: ui.rasters,
    tagFilter: ui.tagFilter,
    overlayOpacity: ui.overlayOpacity,
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
    centerlinesVisible: parsed.centerlinesVisible ?? true,
    geofileIds: parsed.geofileIds ?? [],
    rasters: parsed.rasters ?? [],
    tagFilter: parsed.tagFilter ?? null,
    overlayOpacity: parsed.overlayOpacity ?? {},
    overlayOrder: parsed.overlayOrder ?? [],
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
