// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  fetchCenterlineFeatures,
  fetchEntranceFeatures,
  fetchMapFeatures,
  type MapConfig,
} from '../api/hooks.ts';
import { getMapTagFilter } from '../map/mapFilters.ts';
import { entranceMarkers, surfaceFeatureLines, surfaceFeatureMarkers } from './caveMarkers3d.ts';
import {
  centerlineLoadState,
  centerlinePolylines,
  EMPTY_CENTERLINE_LOAD_STATE,
  type CenterlineLoad3DState,
} from './centerlines3d.ts';
import { mapZoomFor } from './pseudoZoom.ts';
import { boundsToBbox } from './viewBounds3d.ts';
import type {
  Scene3DCamera,
  Scene3DMarker,
  Scene3DPolyline,
  Scene3DVectorSource,
  Scene3DVectorSources,
} from './scene3dEngine.ts';

// Keeping the scene's cave data in step with where the camera is looking.
//
// The flat map's overlays each own a copy of this loop — refetch when the view settles, throw away
// the answer if a newer request has already gone out, keep the previous frame when a request
// fails. The scene has one of it for all four of its overlays, because they are driven by one
// camera and share one box and one zoom: sending four requests derived from four separately
// sampled camera positions would let them disagree about which patch of ground is being shown.
//
// The engine arrives as an argument rather than being reached for, so everything here can be
// exercised against a plain object.

/** The parts of the engine this loader touches. */
export interface CaveData3DEngine extends Scene3DCamera, Scene3DVectorSources {}

/**
 * The installation's rendering limits, which the server publishes and a viewer may override.
 * Used until the real ones arrive; they match the server's own defaults.
 */
export interface CaveData3DLimits {
  /** From this zoom up the server sends full survey detail rather than the stored skeleton. */
  detailZoom: number;
  /** How many line components one request may serve across all caves in the view. */
  maxPaths: number;
}

const fallbackLimits: CaveData3DLimits = { detailZoom: 18, maxPaths: 25000 };

/**
 * How long the camera has to stay put before the data is refetched. The same quarter of a second
 * the flat map waits: long enough that dragging across a region is one request rather than twenty,
 * short enough that letting go feels like it loaded immediately.
 */
const SETTLE_MILLISECONDS = 250;

/** Source ids. They name what is in each batch and appear nowhere a user can see. */
export const CENTERLINE_SOURCE_ID = 'centerlines';
export const ENTRANCE_SOURCE_ID = 'entrances';
export const SURFACE_FEATURE_SOURCE_ID = 'surface-features';
export const SURFACE_FEATURE_LINE_SOURCE_ID = 'surface-feature-lines';

/** What the last load produced, for chrome that has to explain a partly-drawn view. */
export interface CaveData3DState extends CenterlineLoad3DState {
  /** True from the moment a load starts until every one of its requests has settled. */
  loading: boolean;
}

export const EMPTY_CAVE_DATA_3D_STATE: CaveData3DState = {
  ...EMPTY_CENTERLINE_LOAD_STATE,
  loading: false,
};

export interface CaveData3DHandle {
  /** Loads the current view now, without waiting for the camera to move. */
  reload(): void;
  /** Applies the installation's published limits; a partial update leaves the rest alone. */
  setLimits(limits: Partial<CaveData3DLimits>): void;
  getState(): CaveData3DState;
  /** Subscribes to load-state changes; returns an unsubscribe function. */
  subscribe(listener: (state: CaveData3DState) => void): () => void;
  /** Stops loading and takes every source out of the scene. */
  detach(): void;
}

/** Reads the limits out of the server's published map configuration. */
export function limitsFromMapConfig(
  config: Pick<MapConfig, 'centerlineDetailZoom' | 'centerlineMaxPaths'>,
): CaveData3DLimits {
  return { detailZoom: config.centerlineDetailZoom, maxPaths: config.centerlineMaxPaths };
}

/**
 * Puts the cave data into the scene and keeps it there. Starts one load immediately, because the
 * camera is already somewhere and waiting for it to move would open the view on an empty globe.
 */
export function attachCaveData3d(engine: CaveData3DEngine): CaveData3DHandle {
  const centerlines: Scene3DVectorSource<Scene3DPolyline> =
    engine.createPolylineSource(CENTERLINE_SOURCE_ID);
  const featureLines: Scene3DVectorSource<Scene3DPolyline> = engine.createPolylineSource(
    SURFACE_FEATURE_LINE_SOURCE_ID,
  );
  // Markers are created after the lines so they are added to the scene on top of them: an
  // entrance pin sitting on the survey line that starts at it must stay the thing that gets hit.
  const entrances: Scene3DVectorSource<Scene3DMarker> = engine.createMarkerSource(ENTRANCE_SOURCE_ID);
  const features: Scene3DVectorSource<Scene3DMarker> = engine.createMarkerSource(
    SURFACE_FEATURE_SOURCE_ID,
  );

  let limits: CaveData3DLimits = { ...fallbackLimits };
  let state: CaveData3DState = { ...EMPTY_CAVE_DATA_3D_STATE };
  const listeners = new Set<(next: CaveData3DState) => void>();

  let requestSeq = 0;
  let settleTimer: number | undefined;
  let detached = false;

  const publish = (next: Partial<CaveData3DState>) => {
    state = { ...state, ...next };
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  const loadCenterlines = async (bbox: string, zoom: number, seq: number) => {
    try {
      // Altitudes are the whole point of drawing a survey in three dimensions, so this is the one
      // caller in the application that asks for them.
      const collection = await fetchCenterlineFeatures(
        bbox,
        zoom,
        limits.detailZoom,
        limits.maxPaths,
        true,
      );
      if (seq !== requestSeq || detached) {
        return;
      }
      centerlines.replace(centerlinePolylines(collection));
      publish(centerlineLoadState(collection));
    } catch {
      // Keep what is already drawn: a dropped request is usually a network hiccup, and blanking
      // the survey the viewer is reading would be a worse answer than showing it a moment stale.
    }
  };

  const loadEntrances = async (bbox: string, zoom: number, seq: number) => {
    try {
      const collection = await fetchEntranceFeatures(bbox, zoom, getMapTagFilter() ?? undefined);
      if (seq !== requestSeq || detached) {
        return;
      }
      entrances.replace(entranceMarkers(collection, zoom));
    } catch {
      // As above.
    }
  };

  const loadFeatures = async (bbox: string, seq: number) => {
    try {
      const collection = await fetchMapFeatures(bbox, { tag: getMapTagFilter() ?? undefined });
      if (seq !== requestSeq || detached) {
        return;
      }
      features.replace(surfaceFeatureMarkers(collection));
      featureLines.replace(surfaceFeatureLines(collection));
    } catch {
      // As above.
    }
  };

  const load = async () => {
    if (detached) {
      return;
    }
    const bounds = engine.getVisibleBounds();
    if (!bounds) {
      return; // The camera is not looking at the globe; there is no box to ask about.
    }
    const bbox = boundsToBbox(bounds);
    // One zoom for every request in this load. It decides both what the server clusters entrances
    // into and whether it sends survey detail, and the cluster it answers with can only be opened
    // again at the zoom it was summed at.
    const zoom = mapZoomFor(engine.getPseudoZoom());
    const seq = ++requestSeq;
    publish({ loading: true });
    await Promise.all([
      loadCenterlines(bbox, zoom, seq),
      loadEntrances(bbox, zoom, seq),
      loadFeatures(bbox, seq),
    ]);
    if (seq === requestSeq && !detached) {
      publish({ loading: false });
    }
  };

  const onViewChanged = () => {
    window.clearTimeout(settleTimer);
    settleTimer = window.setTimeout(() => void load(), SETTLE_MILLISECONDS);
  };

  const unsubscribeView = engine.onViewChanged(onViewChanged);
  void load();

  return {
    reload() {
      void load();
    },
    setLimits(next) {
      const merged = { ...limits, ...next };
      if (merged.detailZoom === limits.detailZoom && merged.maxPaths === limits.maxPaths) {
        return;
      }
      limits = merged;
      void load();
    },
    getState() {
      return state;
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    detach() {
      detached = true;
      window.clearTimeout(settleTimer);
      unsubscribeView();
      listeners.clear();
      centerlines.remove();
      featureLines.remove();
      entrances.remove();
      features.remove();
    },
  };
}
