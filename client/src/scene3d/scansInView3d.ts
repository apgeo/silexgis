// SPDX-License-Identifier: AGPL-3.0-or-later
import { fetchCaveScansInView, type CaveScanInView, type CaveScansInView, type MapConfig } from '../api/hooks.ts';
import { handleMapFilterError, mapFilterParams, onMapFilterChange } from '../map/mapFilters.ts';
import { ANCHORED_TO_SURFACE, samePlacement, type Altitude3DPlacement } from './altitude3d.ts';
import { mapZoomFor } from './pseudoZoom.ts';
import type {
  Scene3DCamera,
  Scene3DModels,
  Scene3DPointCloudAnchor,
  Scene3DPointCloudDetail,
  Scene3DPointClouds,
} from './scene3dEngine.ts';
import type { SurveyTopLookup } from './surveyMeshesInView3d.ts';
import { boundsToBbox } from './viewBounds3d.ts';

// The placed point clouds of every cave the camera is looking at, held within a budget.
//
// This is the wall-mesh loader's shape applied to scans, and it keeps that shape for the same
// reasons: a tiled scan is streamed a tile at a time rather than fetched whole, but a tileset the
// scene holds still keeps a cache of tiles on the graphics card, and a camera-driven layer that
// simply drew every scan in view could still try to hold gigabytes on one wide view. So nothing
// here is unbounded: below a zoom floor nothing is held, above it scans are taken nearest the
// middle of the view first until a count or a byte budget would be passed, and whatever stops
// being wanted is released at once. The byte figure is the server's — the archive's size, the
// worst case — because only it knows what a tileset weighs before the scene has read any of it.
//
// The budgets are the scans' own, not the meshes': an archive of tiles is one to two orders of
// magnitude larger than a wall mesh, so the installation publishes a separate set of five numbers
// for this layer, and a person's own overrides for it are kept apart as well.
//
// Which scan stands for a cave, and which caves may be described at all, is the server's answer:
// one placed scan per cave, only for caves this viewer may read and place exactly. A scan that
// has no placement is not in the answer — where it would go is a statement about the cave's
// location nobody has made — and it is looked at from the cave's page instead, in its own frame.

/** The parts of the engine this loader touches, named one by one. */
export interface ScansInView3DEngine
  extends Pick<Scene3DCamera, 'getVisibleBounds' | 'getPseudoZoom' | 'onViewChanged'>,
    Pick<Scene3DPointClouds, 'loadPointCloud' | 'removePointCloud'>,
    Pick<Scene3DModels, 'setModelPlacement'> {}

export interface ScansInView3DLimits {
  /** Below this map zoom nothing is held. */
  minZoom: number;
  /** The most caves whose scans are held at once. */
  maxCaves: number;
  /** The most bytes of scan held at once, counted in what the server says each archive weighs. */
  maxBytes: number;
}

export type ScansInView3DStatus = 'off' | 'zoomIn' | 'looking' | 'ready' | 'failed';
export type ScansInView3DLimit = 'count' | 'bytes';

export interface ScansInView3DState {
  status: ScansInView3DStatus;
  /** True while a request for the caves in view is in the air. */
  listing: boolean;
  /** Caves whose scan is in the scene or on its way into it. */
  shown: number;
  /** Of those, how many are still on their way. */
  loading: number;
  /** Caves whose scan was within the budget and could not be read. */
  failed: number;
  /** Caves in view that have a placed scan this viewer may see, drawn or not. */
  inView: number;
  /** Caves in view whose scan was left out because a limit had been reached. */
  leftOut: number;
  /** Bytes of scan held or on their way, as the server states each one's size. */
  heldBytes: number;
  limitedBy?: ScansInView3DLimit;
  limits?: ScansInView3DLimits;
}

export const EMPTY_SCANS_IN_VIEW_3D_STATE: ScansInView3DState = {
  status: 'off',
  listing: false,
  shown: 0,
  loading: 0,
  failed: 0,
  inView: 0,
  leftOut: 0,
  heldBytes: 0,
};

export interface ScansInView3DHandle {
  /** Starts or stops holding scans. Stopping releases every tileset this loader holds. */
  setActive(active: boolean): void;
  /** The selected cave's scan, when the view holds it, is taken before any other. */
  setSelectedCave(caveId: string | undefined): void;
  /** Applies the limits in force. Nothing is held until they have been given. */
  setLimits(limits: ScansInView3DLimits): void;
  /** Says how surveyed altitudes become scene heights. Moves what is loaded; never refetches. */
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  /** Re-reads the survey top of every cave whose scan is held, and moves the ones that changed. */
  refreshSurveyTops(): void;
  reload(): void;
  getState(): ScansInView3DState;
  subscribe(listener: (state: ScansInView3DState) => void): () => void;
  detach(): void;
}

/** The overlay key the scene keeps this layer's on/off under, beside the walls and the photos. */
export const SCANS_IN_VIEW_LAYER_ID = 'scans-in-view';

const SETTLE_MILLISECONDS = 250;

/** The id one cave's scan is held under in the scene. */
export function scanInViewId(caveId: string): string {
  return `scan-in-view:${caveId}`;
}

/** A person's own limits for the browser they are using. A value left out follows the installation. */
export interface PersonalScansInView3DLimits {
  minZoom?: number;
  maxCaves?: number;
  maxBytes?: number;
}

/** What the installation publishes about this layer: its three defaults and its two ceilings. */
export type ScansInViewMapConfig = Pick<
  MapConfig,
  | 'scansInViewMinZoom'
  | 'scansInViewMaxCaves'
  | 'scansInViewMaxBytes'
  | 'scansInViewMaxCavesLimit'
  | 'scansInViewMaxBytesLimit'
>;

export const SCANS_IN_VIEW_ZOOM_RANGE = { min: 1, max: 22 } as const;

/**
 * The smallest byte budget a person may set: below it an ordinary downsampled scan would be
 * refused, which reads on screen as the layer being broken rather than as a choice.
 */
export const SCANS_IN_VIEW_MIN_BYTES = 16 * 1024 * 1024;

/**
 * The limits in force for this viewer: the person's own value where set and in range, else the
 * installation's default. The same arithmetic as the meshes' limits, on the scans' own figures:
 * a stored value is held between a floor and the installation's ceiling, and a ceiling that did
 * not arrive sets the personal value aside rather than yielding a limit that compares false with
 * everything.
 */
export function scansInViewLimitsInForce(
  config: ScansInViewMapConfig,
  personal: PersonalScansInView3DLimits = {},
): ScansInView3DLimits {
  const inForce = (own: number | undefined, published: number, floor: number, ceiling: number) =>
    own === undefined || !Number.isFinite(own) || !Number.isFinite(ceiling)
      ? published
      : Math.min(Math.max(Math.round(own), floor), ceiling);

  return {
    minZoom: inForce(personal.minZoom, config.scansInViewMinZoom, SCANS_IN_VIEW_ZOOM_RANGE.min, SCANS_IN_VIEW_ZOOM_RANGE.max),
    maxCaves: inForce(personal.maxCaves, config.scansInViewMaxCaves, 1, config.scansInViewMaxCavesLimit),
    maxBytes: inForce(personal.maxBytes, config.scansInViewMaxBytes, SCANS_IN_VIEW_MIN_BYTES, config.scansInViewMaxBytesLimit),
  };
}

/**
 * Which of the scans in view are held, in loading order: nearest first until a limit would be
 * passed, stopping at the first that does not fit rather than skipping it for a smaller one
 * further out. The selected cave's scan goes first and is taken whatever its size, because
 * picking a cave is asking for it; it still spends the budget.
 */
export function chooseScansInView(
  answer: Pick<CaveScansInView, 'items' | 'total'>,
  limits: ScansInView3DLimits,
  selectedCaveId?: string,
): { taken: CaveScanInView[]; limitedBy?: ScansInView3DLimit } {
  const selected = answer.items.find((item) => item.caveId === selectedCaveId);
  const ordered = selected ? [selected, ...answer.items.filter((item) => item !== selected)] : answer.items;

  const taken: CaveScanInView[] = [];
  let bytes = 0;
  for (const item of ordered) {
    if (taken.length >= limits.maxCaves) {
      return { taken, limitedBy: 'count' };
    }
    if (item !== selected && bytes + item.sizeBytes > limits.maxBytes) {
      return { taken, limitedBy: 'bytes' };
    }
    taken.push(item);
    bytes += item.sizeBytes;
  }
  return answer.total > taken.length ? { taken, limitedBy: 'count' } : { taken };
}

interface HeldScan {
  caveId: string;
  surveyModelId: string;
  url: string;
  anchor: Scene3DPointCloudAnchor;
  sizeBytes: number;
  status: 'loading' | 'drawn' | 'failed';
}

/**
 * Holds the placed scans of the caves in view, within the budget in force. Nothing is held until
 * it has been switched on and told the limits. `surveyTopOf` is read at the moment a scan is
 * placed, so a scan hangs from the same top as its cave's survey lines and walls; `detailOf` is
 * the person's point-detail setting as it stands when a tileset is asked for.
 */
export function attachScansInView3d(
  engine: ScansInView3DEngine,
  surveyTopOf: SurveyTopLookup,
  detailOf: () => Scene3DPointCloudDetail,
): ScansInView3DHandle {
  let active = false;
  let selectedCaveId: string | undefined;
  let limits: ScansInView3DLimits | undefined;
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;
  let state: ScansInView3DState = { ...EMPTY_SCANS_IN_VIEW_3D_STATE };
  const listeners = new Set<(next: ScansInView3DState) => void>();

  let requestSeq = 0;
  let settleTimer: number | undefined;
  let detached = false;
  let lastAnswer: CaveScansInView | undefined;
  let limitedBy: ScansInView3DLimit | undefined;
  const held = new Map<string, HeldScan>();

  const publish = (next: ScansInView3DState) => {
    state = next;
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  const counted = (status: ScansInView3DStatus, listing: boolean): ScansInView3DState => {
    const entries = [...held.values()];
    const alive = entries.filter((entry) => entry.status !== 'failed');
    const inView = lastAnswer?.total ?? 0;
    return {
      status,
      listing,
      shown: alive.length,
      loading: alive.filter((entry) => entry.status === 'loading').length,
      failed: entries.length - alive.length,
      inView,
      leftOut: Math.max(0, inView - entries.length),
      heldBytes: alive.reduce((sum, entry) => sum + entry.sizeBytes, 0),
      ...(limitedBy && inView > entries.length ? { limitedBy } : {}),
      ...(limits ? { limits } : {}),
    };
  };

  const release = (entry: HeldScan) => {
    held.delete(entry.caveId);
    engine.removePointCloud(scanInViewId(entry.caveId));
  };

  const releaseAll = () => {
    for (const entry of [...held.values()]) {
      release(entry);
    }
  };

  const optionsOf = (entry: HeldScan) => ({
    url: entry.url,
    anchor: entry.anchor,
    placement,
    // Read at the moment of each load rather than copied: the engine takes the detail handed
    // with a load as the budget in force for every cloud, so a stale figure here would undo a
    // setting the person changed while the scene was open.
    detail: detailOf(),
    visible: true,
  });

  const start = (item: CaveScanInView) => {
    const entry: HeldScan = {
      caveId: item.caveId,
      surveyModelId: item.surveyModelId,
      url: item.tilesetUrl,
      anchor: {
        longitude: item.anchorLongitude,
        latitude: item.anchorLatitude,
        altitudeM: item.anchorHeightM,
        surveyTopAltitudeM: surveyTopOf(item.caveId),
        headingDeg: item.headingDeg,
        pitchDeg: item.pitchDeg,
        rollDeg: item.rollDeg,
      },
      sizeBytes: item.sizeBytes,
      status: 'loading',
    };
    held.set(item.caveId, entry);
    const settled = (status: 'drawn' | 'failed') => {
      if (detached || held.get(item.caveId) !== entry) {
        return;
      }
      entry.status = status;
      publish(counted(state.status, state.listing));
    };
    engine.loadPointCloud(scanInViewId(item.caveId), optionsOf(entry)).then(
      () => settled('drawn'),
      () => settled('failed'),
    );
  };

  const apply = (answer: CaveScansInView, activeLimits: ScansInView3DLimits) => {
    const chosen = chooseScansInView(answer, activeLimits, selectedCaveId);
    limitedBy = chosen.limitedBy;
    const wanted = new Map(chosen.taken.map((item) => [item.caveId, item]));
    for (const entry of [...held.values()]) {
      const item = wanted.get(entry.caveId);
      // Compared by model and never by address: the address is signed afresh in every answer.
      if (!item || item.surveyModelId !== entry.surveyModelId) {
        release(entry);
      }
    }
    for (const item of chosen.taken) {
      if (!held.has(item.caveId)) {
        start(item);
      }
    }
  };

  const load = async () => {
    if (detached || !active || !limits) {
      return;
    }
    const bounds = engine.getVisibleBounds();
    if (!bounds) {
      return;
    }
    if (mapZoomFor(engine.getPseudoZoom()) < limits.minZoom) {
      requestSeq += 1;
      lastAnswer = undefined;
      limitedBy = undefined;
      releaseAll();
      publish(counted('zoomIn', false));
      return;
    }

    const seq = ++requestSeq;
    const activeLimits = limits;
    publish(counted(lastAnswer ? 'ready' : 'looking', true));

    let answer: CaveScansInView;
    try {
      answer = await fetchCaveScansInView(boundsToBbox(bounds), activeLimits.maxCaves, mapFilterParams());
    } catch (error) {
      if (seq === requestSeq && !detached) {
        handleMapFilterError(error, () => {
          lastAnswer = undefined;
          releaseAll();
        });
        publish(counted(lastAnswer ? 'ready' : 'failed', false));
      }
      return;
    }
    if (seq !== requestSeq || detached) {
      return;
    }
    lastAnswer = answer;
    apply(answer, activeLimits);
    publish(counted('ready', false));
  };

  const onViewChanged = () => {
    window.clearTimeout(settleTimer);
    if (!active) {
      return;
    }
    settleTimer = window.setTimeout(() => void load(), SETTLE_MILLISECONDS);
  };

  const unsubscribeView = engine.onViewChanged(onViewChanged);
  const unsubscribeFilter = onMapFilterChange(() => void load());

  return {
    setActive(next) {
      if (next === active || detached) {
        return;
      }
      active = next;
      if (!active) {
        window.clearTimeout(settleTimer);
        requestSeq += 1;
        lastAnswer = undefined;
        limitedBy = undefined;
        releaseAll();
        publish(counted('off', false));
        return;
      }
      publish(counted('looking', false));
      void load();
    },
    setSelectedCave(caveId) {
      if (caveId === selectedCaveId) {
        return;
      }
      selectedCaveId = caveId;
      if (active && lastAnswer && limits && !detached) {
        apply(lastAnswer, limits);
        publish(counted(state.status, state.listing));
      }
    },
    setLimits(next) {
      if (limits && limits.minZoom === next.minZoom && limits.maxCaves === next.maxCaves && limits.maxBytes === next.maxBytes) {
        return;
      }
      limits = next;
      if (active && lastAnswer && !detached && mapZoomFor(engine.getPseudoZoom()) >= next.minZoom) {
        apply(lastAnswer, next);
        publish(counted(state.status, state.listing));
      }
      void load();
    },
    setAltitudePlacement(next) {
      if (samePlacement(next, placement)) {
        return;
      }
      placement = next;
      // Re-places models and point clouds alike: the engine keeps one placement rule for both.
      engine.setModelPlacement(next);
    },
    refreshSurveyTops() {
      if (detached) {
        return;
      }
      for (const entry of held.values()) {
        const top = surveyTopOf(entry.caveId);
        if (entry.status === 'failed' || top === undefined || top === entry.anchor.surveyTopAltitudeM) {
          continue;
        }
        entry.anchor = { ...entry.anchor, surveyTopAltitudeM: top };
        // The same tileset under the same id is a move, not a second read.
        void engine.loadPointCloud(scanInViewId(entry.caveId), optionsOf(entry)).catch(() => {
          // Moving something already loaded cannot fail for a reason the viewer can act on.
        });
      }
    },
    reload() {
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
      unsubscribeFilter();
      listeners.clear();
      requestSeq += 1;
      releaseAll();
    },
  };
}
