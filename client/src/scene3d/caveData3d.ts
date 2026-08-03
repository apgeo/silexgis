// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  fetchCenterlineFeatures,
  fetchEntranceFeatures,
  fetchMapFeatures,
  type MapConfig,
} from '../api/hooks.ts';
import { getMapTagFilter } from '../map/mapFilters.ts';
import { entranceMarkers, surfaceFeatureLines, surfaceFeatureMarkers } from './caveMarkers3d.ts';
import { cameraFloorFor, caveFootprint } from './caveFootprint3d.ts';
import {
  centerlineLoadState,
  centerlinePolylines,
  EMPTY_CENTERLINE_LOAD_STATE,
  nearestCaveCenterlines,
  type CenterlineLoad3DState,
} from './centerlines3d.ts';
import { mapZoomFor } from './pseudoZoom.ts';
import { boundsToBbox } from './viewBounds3d.ts';
import type {
  Scene3DCamera,
  Scene3DMarker,
  Scene3DPolyline,
  Scene3DSurface,
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
export interface CaveData3DEngine extends Scene3DCamera, Scene3DVectorSources, Scene3DSurface {}

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

/**
 * The layers a viewer can turn off and fade, which are not one-to-one with the batches above:
 * the surface-feature layer is drawn as two of them, its symbols and the lines and outlines that
 * belong to the same features, and turning off half of a feature would be nonsense.
 *
 * These names are the flat map's overlay ids, unchanged, so that a viewer who dims the survey
 * lines in one view finds them dimmed in the other rather than meeting two independent settings
 * for one thing.
 */
export type CaveData3DLayer =
  | typeof CENTERLINE_SOURCE_ID
  | typeof ENTRANCE_SOURCE_ID
  | typeof SURFACE_FEATURE_SOURCE_ID;

export const CAVE_DATA_3D_LAYERS: readonly CaveData3DLayer[] = [
  CENTERLINE_SOURCE_ID,
  ENTRANCE_SOURCE_ID,
  SURFACE_FEATURE_SOURCE_ID,
];

/** The part of a batch's handle a layer control uses; it never gets the batch itself. */
type LayerControl = Pick<Scene3DVectorSource<unknown>, 'setVisible' | 'setOpacity'>;

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
  /**
   * Draws or stops drawing one layer. A layer that is off is also not fetched: the requests it
   * would make are the expensive part of it, and a viewer who turned it off is not waiting for
   * them. Turning it back on loads the view it missed.
   */
  setLayerVisible(layer: CaveData3DLayer, visible: boolean): void;
  /** Fades one layer, 0..1. It survives reloading, because the viewer asked for it. */
  setLayerOpacity(layer: CaveData3DLayer, opacity: number): void;
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

  const layerSources: Record<CaveData3DLayer, LayerControl[]> = {
    [CENTERLINE_SOURCE_ID]: [centerlines],
    [ENTRANCE_SOURCE_ID]: [entrances],
    [SURFACE_FEATURE_SOURCE_ID]: [features, featureLines],
  };
  const layerVisible: Record<CaveData3DLayer, boolean> = {
    [CENTERLINE_SOURCE_ID]: true,
    [ENTRANCE_SOURCE_ID]: true,
    [SURFACE_FEATURE_SOURCE_ID]: true,
  };
  const layerOpacity: Record<CaveData3DLayer, number> = {
    [CENTERLINE_SOURCE_ID]: 1,
    [ENTRANCE_SOURCE_ID]: 1,
    [SURFACE_FEATURE_SOURCE_ID]: 1,
  };

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

  const loadCenterlines = async (
    bbox: string,
    zoom: number,
    seq: number,
    center: { longitude: number; latitude: number },
  ) => {
    if (!layerVisible[CENTERLINE_SOURCE_ID]) {
      return;
    }
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
      // The layer can be turned off while its request is in the air, and turning it off clears
      // the notices about it on purpose. Answering afterwards would put them back over a view the
      // viewer has just emptied, and nothing would take them down again — the next load stops at
      // the guard above without publishing anything.
      if (seq !== requestSeq || detached || !layerVisible[CENTERLINE_SOURCE_ID]) {
        return;
      }
      const polylines = centerlinePolylines(collection);
      centerlines.replace(polylines);
      publish(centerlineLoadState(collection));
      // The survey is what says where the ground may be cut away and how far down a viewer may
      // go, so both are derived from what was just drawn rather than configured anywhere. The
      // opening belongs to one cave — the one the view is centred on — because it is sized to the
      // survey it reveals, and one drawn around every cave a regional view holds would take the
      // whole basemap off the screen.
      const footprint = caveFootprint(nearestCaveCenterlines(polylines, center));
      engine.setCutawayFootprint(footprint);
      engine.setCameraFloorHeight(cameraFloorFor(footprint));
    } catch {
      // Keep what is already drawn: a dropped request is usually a network hiccup, and blanking
      // the survey the viewer is reading would be a worse answer than showing it a moment stale.
    }
  };

  const loadEntrances = async (bbox: string, zoom: number, seq: number) => {
    if (!layerVisible[ENTRANCE_SOURCE_ID]) {
      return;
    }
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
    if (!layerVisible[SURFACE_FEATURE_SOURCE_ID]) {
      return;
    }
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
    // The middle of the box, which is the ground the middle of the screen is showing — the box is
    // built around exactly that point — and so the place to ask which cave is being looked at.
    const center = {
      longitude: (bounds[0] + bounds[2]) / 2,
      latitude: (bounds[1] + bounds[3]) / 2,
    };
    // One zoom for every request in this load. It decides both what the server clusters entrances
    // into and whether it sends survey detail, and the cluster it answers with can only be opened
    // again at the zoom it was summed at.
    const zoom = mapZoomFor(engine.getPseudoZoom());
    const seq = ++requestSeq;
    publish({ loading: true });
    await Promise.all([
      loadCenterlines(bbox, zoom, seq, center),
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
    setLayerVisible(layer, visible) {
      if (layerVisible[layer] === visible) {
        return;
      }
      layerVisible[layer] = visible;
      for (const source of layerSources[layer]) {
        source.setVisible(visible);
      }
      if (!visible) {
        if (layer === CENTERLINE_SOURCE_ID) {
          // The notices explain what the survey layer could not show. With the layer off there is
          // nothing on screen for them to be about, and a viewer reading "some caves are not shown
          // at this zoom" over a view they themselves emptied would be told the wrong thing.
          publish(EMPTY_CENTERLINE_LOAD_STATE);
          // The ground goes back too. An excavation only makes sense around a survey that is being
          // drawn: left cut, it is an opening with nothing in it, and because a hidden layer is
          // not fetched either, nothing would ever move it or take it away again — it would sit
          // over the last cave the viewer looked at however far they travelled from it. Handing it
          // back also lets the chrome say the honest thing, which is that there is no survey to
          // cut around.
          //
          // The descent limit is deliberately not handed back with it. It only ever lets a viewer
          // go deeper than the standing default, so a stale one cannot fence anybody out of a
          // cave, while resetting it would haul a camera that is already down there back up to the
          // default the moment a layer was switched off.
          engine.setCutawayFootprint(undefined);
        }
        return;
      }
      // What the view missed while the layer was off. The geometry is kept rather than thrown
      // away when a layer is hidden, so this is a catch-up rather than a cold start.
      void load();
    },
    setLayerOpacity(layer, opacity) {
      if (layerOpacity[layer] === opacity) {
        return;
      }
      layerOpacity[layer] = opacity;
      for (const source of layerSources[layer]) {
        source.setOpacity(opacity);
      }
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
      // The scene outlives this loader — it is shared and reference counted — so the ground it
      // was told to cut away, and the depth it was told a viewer may descend to, both have to be
      // handed back with the geometry they were derived from.
      engine.setCutawayFootprint(undefined);
      engine.setCameraFloorHeight(cameraFloorFor(undefined));
      centerlines.remove();
      featureLines.remove();
      entrances.remove();
      features.remove();
    },
  };
}
