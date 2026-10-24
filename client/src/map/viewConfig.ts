// SPDX-License-Identifier: AGPL-3.0-or-later
import { fromLonLat, toLonLat } from 'ol/proj';
import { normalizeCamera3DState, type Camera3DState } from '../scene3d/camera3d.ts';
import { viewCamera3dState } from '../workspace/viewCamera.ts';
import { unreadableTileLayers } from './baseLayers.ts';
import {
  isKarstDensityBands,
  isKarstDensityMode,
  type KarstDensityBands,
  type KarstDensityMode,
} from './densityCells.ts';
import { getOverlayOrder, getWorkspaceMap } from './mapContext.ts';
import { readFilterDocument } from '../filters/url.ts';
import { holdsPosition } from '../filters/spatial.ts';
import type { FilterDocument } from '../filters/types.ts';
import { isSavedFilterId, tagChipFilter, tagOfMapFilter, type MapFilter } from './mapFilters.ts';
import { readFilterLayers, type FilterLayerSpec } from './filterLayerList.ts';

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
  /** The server's cave-density grid. Added after v1 shipped; older saved views omit it (off). */
  karstDensityVisible?: boolean;
  /**
   * Which reading fills the density cells: `density` (caves per km²), `kernel` (the smoothed
   * surface) or `hotSpot`. Added after v1 shipped; omitted or unrecognised reads as `density`.
   */
  karstDensityMode?: KarstDensityMode;
  /**
   * Whether the density bands are `relative` to the view or the installation's `fixed` ones.
   * Added after v1 shipped; omitted or unrecognised reads as `relative`, the colouring every view
   * saved before it had.
   */
  karstDensityBands?: KarstDensityBands;
  /**
   * The karstification-index choropleth over the areas in view. Added after v1 shipped; older
   * saved views omit it (off).
   */
  karstIndexVisible?: boolean;
  /** Added after v1 shipped; older saved views omit it (treated as false — an opt-in overlay). */
  photosVisible?: boolean;
  /** Added after v1 shipped; older saved views omit it (treated as false — an opt-in overlay). */
  tripsVisible?: boolean;
  /**
   * The day window the trip overlay asks with, inclusive, as `yyyy-MM-dd`. Added after v1 shipped;
   * older saved views omit either bound, and an absent bound is no bound rather than today — a
   * view saved before this existed asked about every trip, and reopening it must go on doing so.
   */
  tripsFrom?: string;
  tripsTo?: string;
  /**
   * Which photo libraries were switched on, named the way the server names each. Added after v1
   * shipped; older saved views omit it (treated as none — these are opt-in overlays).
   *
   * A list rather than a flag per product, because this document's version is never bumped and
   * every field added to it is permanent: pointing this installation at a different library, or at
   * a third one, must not need another field that can never be dropped. An entry naming a library
   * this installation no longer has is ignored, exactly as a saved view naming a deleted imported
   * file is.
   */
  libraryPhotoSources?: string[];
  geofileIds: string[];
  rasters: { id: string; opacity?: number }[];
  /**
   * The tag the view was narrowed to, when its filter is exactly the layer panel's tag chooser.
   *
   * Still written in this form, and only in this form, for that one case: a shared link reads this
   * key and no other, so a tag-only view keeps drawing through its link exactly as it always has.
   * Any other filter goes in `savedFilterId` or `filterDocument` with this left null, and a link to
   * such a view draws no data at all — narrower than the view, never wider.
   */
  tagFilter: string | null;
  /**
   * The saved filter the view's layers were drawn through. Added after v1 shipped; older views
   * omit it. Stored by id, never copied out, so reopening the view runs the filter as it now
   * stands and as the person reopening it may read it.
   */
  savedFilterId?: string;
  /**
   * The filter document, when the view's filter was not a saved one and not a plain tag. Added
   * after v1 shipped; older views omit it.
   */
  filterDocument?: FilterDocument;
  /**
   * The layers drawn from saved filters, bottom of the list first as they were added; their
   * stacking among the other overlays is in `overlayOrder` and their transparency in
   * `overlayOpacity`, both under each layer's `filter:<id>` layer id. Added after v1 shipped;
   * older views omit it (treated as none), and a view with none does not write it.
   *
   * A shared link reads none of this and draws none of these layers. Each one draws a subset of
   * what the main entrance and feature layers draw, so leaving them out of a link can only show
   * less than the view did, never more.
   */
  filterLayers?: FilterLayerSpec[];
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
  karstDensityVisible: boolean;
  karstDensityMode: KarstDensityMode;
  karstDensityBands: KarstDensityBands;
  karstIndexVisible: boolean;
  photosVisible: boolean;
  tripsVisible: boolean;
  tripsFrom?: string;
  tripsTo?: string;
  libraryPhotoSources: string[];
  geofileIds: string[];
  rasters: { id: string; opacity?: number }[];
  /** The filter the map's layers draw through, or null for none. */
  mapFilter: MapFilter | null;
  /** The layers drawn from saved filters, in the order they were added. */
  filterLayers: FilterLayerSpec[];
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
    karstDensityVisible: ui.karstDensityVisible,
    karstDensityMode: ui.karstDensityMode,
    karstDensityBands: ui.karstDensityBands,
    karstIndexVisible: ui.karstIndexVisible,
    photosVisible: ui.photosVisible,
    tripsVisible: ui.tripsVisible,
    tripsFrom: ui.tripsFrom,
    tripsTo: ui.tripsTo,
    libraryPhotoSources: ui.libraryPhotoSources,
    geofileIds: ui.geofileIds,
    rasters: ui.rasters,
    ...viewFilterFields(ui.mapFilter),
    ...(ui.filterLayers.length > 0 ? { filterLayers: ui.filterLayers.map((layer) => ({ ...layer })) } : {}),
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
    karstDensityVisible: parsed.karstDensityVisible ?? false,
    karstDensityMode: isKarstDensityMode(parsed.karstDensityMode) ? parsed.karstDensityMode : 'density',
    karstDensityBands: isKarstDensityBands(parsed.karstDensityBands) ? parsed.karstDensityBands : 'relative',
    karstIndexVisible: parsed.karstIndexVisible ?? false,
    photosVisible: parsed.photosVisible ?? false,
    tripsVisible: parsed.tripsVisible ?? false,
    tripsFrom: parsed.tripsFrom,
    tripsTo: parsed.tripsTo,
    libraryPhotoSources: parsed.libraryPhotoSources ?? [],
    geofileIds: parsed.geofileIds ?? [],
    rasters: parsed.rasters ?? [],
    mapFilter: viewFilterOf(parsed),
    filterLayers: readFilterLayers(parsed.filterLayers),
    overlayOpacity: parsed.overlayOpacity ?? {},
    baseOpacity: parsed.baseOpacity ?? {},
    overlayOrder: parsed.overlayOrder ?? [],
    // Checked field by field rather than trusted: the document is client-owned and the server
    // stores it without looking inside, so a hand-edited or older-shaped camera block has to read
    // as "this view says nothing about the scene" instead of taking a camera somewhere impossible.
    camera3d: normalizeCamera3DState(parsed.camera3d) ?? undefined,
  };
}

/**
 * What a shared link to a stored view leaves out, for the share dialog to say so. A link runs no
 * filter — nobody behind it has an account to run one as — so a view narrowed by anything more than
 * a tag is shared with neither of its data layers (the server draws them for no such view), and a
 * layer drawn from a saved filter is never drawn through a link at all.
 */
export function viewLinkLeavesOut(config: unknown): { dataLayers: boolean; filterLayers: boolean } {
  const parsed = (config !== null && typeof config === 'object' ? config : {}) as Partial<ViewConfig>;
  const present = (value: unknown) => value !== undefined && value !== null;
  return {
    dataLayers: present(parsed.savedFilterId) || present(parsed.filterDocument),
    filterLayers: Array.isArray(parsed.filterLayers) && parsed.filterLayers.length > 0,
  };
}

/**
 * Whether a captured view's filter holds a place of its own: a typed point, a pasted outline, or
 * the place nearest first is measured from.
 *
 * A view is shown to whoever it is shared with, so it never stores one — the place belongs to the
 * request that ran with it, which is why the map's address may carry such a filter and a saved
 * view may not. The server refuses the view; this lets the panel say why before asking. A filter
 * naming an object or an area, or a saved filter (stored by id), is kept as it always was.
 */
export function viewHoldsPosition(config: Pick<ViewConfig, 'filterDocument'>): boolean {
  return config.filterDocument !== undefined && holdsPosition(config.filterDocument);
}

/** The filter as a view stores it: a tag in its old key, a saved filter by id, anything else whole. */
function viewFilterFields(
  filter: MapFilter | null,
): Pick<ViewConfig, 'tagFilter' | 'savedFilterId' | 'filterDocument'> {
  const tag = tagOfMapFilter(filter);
  if (filter === null || tag !== null) {
    return { tagFilter: tag };
  }
  return filter.kind === 'saved'
    ? { tagFilter: null, savedFilterId: filter.savedFilterId }
    : { tagFilter: null, filterDocument: filter.document };
}

/**
 * The filter a stored view names. Each form is checked rather than trusted — the server stores the
 * document without looking inside — and a saved filter outranks a document, which outranks a tag.
 * A filter the view holds in a form this version cannot read restores no filter rather than a
 * guess; the server, not this, is what refuses a saved filter the reader may no longer use.
 */
function viewFilterOf(parsed: Partial<ViewConfig>): MapFilter | null {
  if (isSavedFilterId(parsed.savedFilterId)) {
    return { kind: 'saved', savedFilterId: parsed.savedFilterId };
  }
  const document = readFilterDocument(parsed.filterDocument);
  if (document !== null) {
    return { kind: 'document', document };
  }
  return typeof parsed.tagFilter === 'string' ? tagChipFilter(parsed.tagFilter) : null;
}

/**
 * What exporting the map came to: the picture, or why there is none. `unreadable` is a canvas the
 * browser will not let be read back — a tile on it was drawn from a source that gave no
 * cross-origin leave — which is a fact about the layers shown, not a failure to retry; `layers`
 * names the ones known to cause it (none, when the browser refused for a reason not known here).
 */
export type MapImageExport = { image: Blob } | { refused: 'unreadable'; layers: string[] } | { refused: 'failed' };

/** Renders the current map canvas composite to a PNG blob (attribution included). */
export function exportMapImage(): Promise<MapImageExport> {
  const map = getWorkspaceMap();
  // Known before drawing anything: a layer whose tiles cannot be read back makes the whole
  // picture unreadable.
  const unreadable = unreadableTileLayers(map);
  if (unreadable.length > 0) {
    return Promise.resolve({ refused: 'unreadable', layers: unreadable });
  }
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
      try {
        target.toBlob((blob) => resolve(blob ? { image: blob } : { refused: 'failed' }), 'image/png');
      } catch (error) {
        // Thrown, not reported through the callback, when the canvas holds a tile the browser
        // treats as another site's: left uncaught, the export never answered and the error landed
        // in the console instead of in front of the person who pressed the button.
        resolve(
          error instanceof DOMException && error.name === 'SecurityError'
            ? { refused: 'unreadable', layers: unreadableTileLayers(map) }
            : { refused: 'failed' },
        );
      }
    });
    map.renderSync();
  });
}
