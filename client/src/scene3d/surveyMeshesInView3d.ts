// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  fetchCaveMeshesInView,
  type CaveMeshesInView,
  type CaveMeshInView,
  type MapConfig,
} from '../api/hooks.ts';
import { ANCHORED_TO_SURFACE, samePlacement, type Altitude3DPlacement } from './altitude3d.ts';
import { mapZoomFor } from './pseudoZoom.ts';
import type { Scene3DCamera, Scene3DModelAnchor, Scene3DModels } from './scene3dEngine.ts';
import { boundsToBbox } from './viewBounds3d.ts';

// The walls of every cave the camera is looking at, held within a budget.
//
// The walls of the selected cave are loaded because a viewer picked that cave, one at a time. This
// is the other way of asking for walls — by where the camera is — and it is a different product
// with a different risk: a wall mesh is a few hundred kilobytes in the ordinary case and tens of
// megabytes in the worst one, it is fetched and held whole, and it costs roughly twice its size in
// graphics memory for as long as it is held. A camera-driven layer that simply drew what was in
// view could therefore try to pull gigabytes on one bad view. So nothing here is unbounded, and
// the bounds are the installation's — or the person's own for this browser, which the
// installation in turn bounds:
//
//   * below a zoom floor it holds nothing at all, because a wide view is a district's worth of
//     caves and each would be a speck that cost its full size;
//   * above it, it takes meshes nearest the middle of the view first until a count or a byte
//     budget would be passed, and stops there;
//   * and whatever it stops holding it releases from the scene, immediately. Releasing matters as
//     much as loading does — a mesh that is merely hidden still holds every byte on the card, and
//     a loader that only ever added would reach the same gigabytes by panning.
//
// What it left out is counted and published, so the chrome can say "3 of 7" rather than draw a
// silent subset that reads as "these are the caves with walls".
//
// Which mesh stands for a cave, and which caves may be described at all, is the server's answer:
// one mesh per cave, already chosen, and only for caves this viewer may read and place exactly.
// Nothing here second-guesses either.
//
// The engine arrives as an argument rather than being reached for, so every decision here can be
// exercised against a plain object.

/** The parts of the engine this loader touches, named one by one. */
export interface SurveyMeshesInView3DEngine
  extends Pick<Scene3DCamera, 'getVisibleBounds' | 'getPseudoZoom' | 'onViewChanged'>,
    Pick<Scene3DModels, 'loadModel' | 'removeModel' | 'setModelPlacement'> {}

/**
 * Which caves' walls the scene draws: the one selected cave's, or those of every cave in view.
 *
 * Two modes of one layer rather than two layers, because they answer the same question — "show me
 * the walls" — and holding both at once would load the selected cave's mesh twice.
 */
export type SurveyWalls3DMode = 'selected' | 'inView';

/**
 * The limits in force for this mode: the installation's published ones, each replaced by the
 * person's own where they set one. There are no defaults here.
 */
export interface SurveyMeshesInView3DLimits {
  /** Below this map zoom nothing is held. */
  minZoom: number;
  /** The most caves held at once. */
  maxCaves: number;
  /** The most bytes of mesh held at once, counted in what the meshes are to fetch. */
  maxBytes: number;
}

/**
 * What the loader is doing, in the terms the chrome words.
 *
 * `looking` covers every moment between being switched on and having a first answer, including
 * the wait for the installation's limits: until they arrive there is no budget to spend, and
 * guessing one would be exactly the hard-coded limit this mode is not allowed to have.
 */
export type SurveyMeshesInView3DStatus = 'off' | 'zoomIn' | 'looking' | 'ready' | 'failed';

/** Which limit stopped the loader short of every cave in view. */
export type SurveyMeshesInView3DLimit = 'count' | 'bytes';

export interface SurveyMeshesInView3DState {
  status: SurveyMeshesInView3DStatus;
  /** True while a request for the caves in view is in the air. */
  listing: boolean;
  /** Caves whose walls are in the scene or on their way into it. */
  shown: number;
  /** Of those, how many are still on their way. */
  loading: number;
  /** Caves whose mesh was within the budget and could not be read. */
  failed: number;
  /** Caves in view that have walls this viewer may see, drawn or not. */
  inView: number;
  /** Caves in view whose walls were left out because a limit had been reached. */
  leftOut: number;
  /** Bytes of mesh held or on their way, as the server states each one's size. */
  heldBytes: number;
  /** The limit that left caves out, when any were. */
  limitedBy?: SurveyMeshesInView3DLimit;
  /** The limits in force, for chrome that names one of them. */
  limits?: SurveyMeshesInView3DLimits;
}

export const EMPTY_SURVEY_MESHES_IN_VIEW_3D_STATE: SurveyMeshesInView3DState = {
  status: 'off',
  listing: false,
  shown: 0,
  loading: 0,
  failed: 0,
  inView: 0,
  leftOut: 0,
  heldBytes: 0,
};

export interface SurveyMeshesInView3DHandle {
  /**
   * Starts or stops holding walls. Stopping releases every mesh this loader holds, which is the
   * point of stopping: the memory is why a viewer turns walls off, and why switching back to the
   * selected cave's walls alone must not leave a dozen others behind.
   */
  setActive(active: boolean): void;
  /**
   * Says which cave the viewer has selected. Its mesh, when the view holds it, is taken before
   * any other regardless of how far from the middle of the view it is.
   */
  setSelectedCave(caveId: string | undefined): void;
  /**
   * Applies the limits in force. Nothing is held until they have been given.
   *
   * Given again with different numbers — a person changed their own — they take effect at once
   * rather than at the next camera move: what a lowered limit no longer covers is released
   * before this returns, and a raised one is asked about straight away.
   */
  setLimits(limits: SurveyMeshesInView3DLimits): void;
  /** Says how surveyed altitudes become scene heights. Moves what is loaded; never refetches. */
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  /**
   * Re-reads the survey top of every cave whose walls are held, and moves the ones that changed.
   *
   * The top is what an anchored cave hangs from, and it is learned by the loop that draws the
   * survey lines, after this one has usually already started a mesh. Called whenever that loop
   * has answered, this is what brings each cave's walls back onto its own lines.
   */
  refreshSurveyTops(): void;
  /** Asks about the current view now, without waiting for the camera to move. */
  reload(): void;
  getState(): SurveyMeshesInView3DState;
  /** Subscribes to state changes; returns an unsubscribe function. */
  subscribe(listener: (state: SurveyMeshesInView3DState) => void): () => void;
  /** Stops loading and releases every mesh held. */
  detach(): void;
}

/** How one cave's top is found; undefined while nothing has said what it is. */
export type SurveyTopLookup = (caveId: string) => number | undefined;

/** How long the camera has to stay put before the view is asked about; the cave data's own wait. */
const SETTLE_MILLISECONDS = 250;

/**
 * The id one cave's walls are held under in the scene.
 *
 * One per cave, and never the id the selected cave's walls use: the two modes are switched
 * between, and a shared id would let one mode's release take the other's mesh out.
 */
export function surveyMeshInViewModelId(caveId: string): string {
  return `survey-mesh-in-view:${caveId}`;
}

/** A person's own limits for the browser they are using. A value left out follows the installation. */
export interface PersonalMeshesInView3DLimits {
  minZoom?: number;
  maxCaves?: number;
  maxBytes?: number;
}

/** What the installation publishes about this mode: its three defaults and its two ceilings. */
export type MeshesInViewMapConfig = Pick<
  MapConfig,
  | 'meshesInViewMinZoom'
  | 'meshesInViewMaxCaves'
  | 'meshesInViewMaxBytes'
  | 'meshesInViewMaxCavesLimit'
  | 'meshesInViewMaxBytesLimit'
>;

/** The zooms a person may start this mode from: the range a map of this kind is ever drawn at. */
export const MESHES_IN_VIEW_ZOOM_RANGE = { min: 1, max: 22 } as const;

/**
 * The smallest byte budget a person may set. Lower would be a budget that refuses an ordinary
 * cave's walls, which reads on screen as the mode being broken rather than as a choice.
 */
export const MESHES_IN_VIEW_MIN_BYTES = 1024 * 1024;

/**
 * The limits in force for this viewer: for each of the three, the person's own value when they
 * have set one and the installation's default when they have not.
 *
 * A personal value is held to what may be asked for before it is used — one cave up to the
 * installation's ceiling, a megabyte up to the installation's ceiling, a zoom a map can be at —
 * because it was typed once and is kept in this browser, while a ceiling is the operator's and
 * can be lowered afterwards. Where a ceiling sits below a floor the ceiling wins: the operator
 * keeps the last word. The installation's defaults are taken as published; they are the
 * operator's own numbers, and the server has already held each to its ceiling.
 *
 * A ceiling that did not arrive — an answer from a server that does not publish one — is not
 * leave to go without: the personal value is set aside and the default stands. Arithmetic on a
 * missing ceiling would yield a limit that compares false with everything, which is no limit.
 */
export function meshesInViewLimitsInForce(
  config: MeshesInViewMapConfig,
  personal: PersonalMeshesInView3DLimits = {},
): SurveyMeshesInView3DLimits {
  const inForce = (own: number | undefined, published: number, floor: number, ceiling: number) =>
    // A value that is not a number is nobody's choice — a stored blob can hold anything.
    own === undefined || !Number.isFinite(own) || !Number.isFinite(ceiling)
      ? published
      : Math.min(Math.max(Math.round(own), floor), ceiling);

  return {
    minZoom: inForce(
      personal.minZoom,
      config.meshesInViewMinZoom,
      MESHES_IN_VIEW_ZOOM_RANGE.min,
      MESHES_IN_VIEW_ZOOM_RANGE.max,
    ),
    maxCaves: inForce(
      personal.maxCaves,
      config.meshesInViewMaxCaves,
      1,
      config.meshesInViewMaxCavesLimit,
    ),
    maxBytes: inForce(
      personal.maxBytes,
      config.meshesInViewMaxBytes,
      MESHES_IN_VIEW_MIN_BYTES,
      config.meshesInViewMaxBytesLimit,
    ),
  };
}

/**
 * Which of the meshes in view are held, in the order they are loaded.
 *
 * The answer arrives nearest first and is taken in that order until a limit would be passed, and
 * it stops at the first mesh that does not fit rather than skipping it for a smaller one further
 * out. Skipping would fill the budget better and draw a worse picture: the walls of a cave at the
 * edge of the view with a hole where the one in the middle should be.
 *
 * The selected cave's mesh goes first, and it alone is taken even when it is bigger than the whole
 * byte budget. The other mode draws the selected cave's walls whatever their size, because picking
 * a cave is asking for them; this mode showing less of that one cave than the other does would
 * make choosing "every cave" the way to lose the walls being looked at. It still spends the
 * budget, so a very large selected cave leaves little or nothing for its neighbours — which the
 * count published beside the result says.
 */
export function chooseMeshesInView(
  answer: Pick<CaveMeshesInView, 'items' | 'total'>,
  limits: SurveyMeshesInView3DLimits,
  selectedCaveId?: string,
): { taken: CaveMeshInView[]; limitedBy?: SurveyMeshesInView3DLimit } {
  const selected = answer.items.find((item) => item.caveId === selectedCaveId);
  const ordered = selected
    ? [selected, ...answer.items.filter((item) => item !== selected)]
    : answer.items;

  const taken: CaveMeshInView[] = [];
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
  // Everything the answer described was taken, but the answer itself stops at the same count:
  // caves beyond it were counted by the server and not described.
  return answer.total > taken.length ? { taken, limitedBy: 'count' } : { taken };
}

/** One cave's walls, in the scene or on their way there. */
interface HeldMesh {
  caveId: string;
  /** Which model this is, which is what says whether a later answer names the same mesh. */
  surveyModelId: string;
  /** The address it was read from, kept so it can be moved without being read again. */
  url: string;
  anchor: Scene3DModelAnchor;
  sizeBytes: number;
  status: 'loading' | 'drawn' | 'failed';
}

/**
 * Holds the walls of the caves in view, within the budget in force. Nothing is held until it has
 * been switched on and told the limits.
 *
 * `surveyTopOf` is how each cave's top is found, read at the moment a mesh is placed rather than
 * copied: every cave hangs from its own top, the same one its survey lines hang from, and those
 * are learned by another loop and change with every answer it gets.
 */
export function attachSurveyMeshesInView3d(
  engine: SurveyMeshesInView3DEngine,
  surveyTopOf: SurveyTopLookup,
): SurveyMeshesInView3DHandle {
  let active = false;
  let selectedCaveId: string | undefined;
  let limits: SurveyMeshesInView3DLimits | undefined;
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;
  let state: SurveyMeshesInView3DState = { ...EMPTY_SURVEY_MESHES_IN_VIEW_3D_STATE };
  const listeners = new Set<(next: SurveyMeshesInView3DState) => void>();

  // Bumped by every request and by everything that makes a request in the air the wrong answer:
  // being switched off, dropping below the zoom floor, being taken down.
  let requestSeq = 0;
  let settleTimer: number | undefined;
  let detached = false;
  /** The last answer, kept so a change of selection or of top is settled without asking again. */
  let lastAnswer: CaveMeshesInView | undefined;
  let limitedBy: SurveyMeshesInView3DLimit | undefined;
  const held = new Map<string, HeldMesh>();

  const publish = (next: SurveyMeshesInView3DState) => {
    state = next;
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  /** The numbers, counted from what is actually held rather than from what was asked for. */
  const counted = (
    status: SurveyMeshesInView3DStatus,
    listing: boolean,
  ): SurveyMeshesInView3DState => {
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

  const release = (entry: HeldMesh) => {
    held.delete(entry.caveId);
    // A real unload, and asked for whatever state the mesh is in: a read still in the air is
    // abandoned by the same call that frees one already on the card.
    engine.removeModel(surveyMeshInViewModelId(entry.caveId));
  };

  const releaseAll = () => {
    for (const entry of [...held.values()]) {
      release(entry);
    }
  };

  const start = (item: CaveMeshInView) => {
    const entry: HeldMesh = {
      caveId: item.caveId,
      surveyModelId: item.surveyModelId,
      url: item.meshUrl,
      anchor: {
        longitude: item.anchorLongitude,
        latitude: item.anchorLatitude,
        altitudeM: item.anchorHeightM,
        surveyTopAltitudeM: surveyTopOf(item.caveId),
      },
      sizeBytes: item.sizeBytes,
      status: 'loading',
    };
    held.set(item.caveId, entry);
    const settled = (status: 'drawn' | 'failed') => {
      // Only while this is still the mesh held for the cave: one released while it was in the air
      // belongs to nobody, and counting it would report walls that are not in the scene.
      if (detached || held.get(item.caveId) !== entry) {
        return;
      }
      entry.status = status;
      publish(counted(state.status, state.listing));
    };
    engine
      .loadModel(surveyMeshInViewModelId(item.caveId), {
        url: entry.url,
        anchor: entry.anchor,
        placement,
        visible: true,
      })
      .then(
        () => settled('drawn'),
        // Kept as a failed entry rather than dropped: dropping it would have the next settled
        // view ask for the same unreadable file again, and again on every pan after that.
        () => settled('failed'),
      );
  };

  /** Brings what is held into line with one answer: releases first, then loads what is missing. */
  const apply = (answer: CaveMeshesInView, activeLimits: SurveyMeshesInView3DLimits) => {
    const chosen = chooseMeshesInView(answer, activeLimits, selectedCaveId);
    limitedBy = chosen.limitedBy;
    const wanted = new Map(chosen.taken.map((item) => [item.caveId, item]));

    // Released before anything new is asked for, so the memory a view no longer needs is back
    // before the memory the new view needs is taken.
    for (const entry of [...held.values()]) {
      const item = wanted.get(entry.caveId);
      // Compared by model and never by address: the address is signed afresh in every answer, so
      // the same mesh has a different one each time and would be read again on every pan.
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
      return; // The camera is not looking at the globe; there is no box to ask about.
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
    // A view that already has an answer keeps saying it while the next one is fetched: the walls
    // on screen are still on screen, and blanking the count for a quarter of a second on every
    // pan would be a flicker with no information in it.
    publish(counted(lastAnswer ? 'ready' : 'looking', true));

    let answer: CaveMeshesInView;
    try {
      // The count travels with the question, because the answer stops at it: asked without one,
      // a person who raised theirs would be told about the installation's default and no more.
      answer = await fetchCaveMeshesInView(boundsToBbox(bounds), activeLimits.maxCaves);
    } catch {
      if (seq === requestSeq && !detached) {
        // Keep what is already drawn: a dropped request is usually a network hiccup, and it says
        // nothing about whether the walls on screen still belong there.
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
      // The view has not moved, so the answer already held is still the answer; only which mesh
      // goes first changed, and that is settled here without another request.
      if (active && lastAnswer && limits && !detached) {
        apply(lastAnswer, limits);
        publish(counted(state.status, state.listing));
      }
    },
    setLimits(next) {
      if (
        limits &&
        limits.minZoom === next.minZoom &&
        limits.maxCaves === next.maxCaves &&
        limits.maxBytes === next.maxBytes
      ) {
        return;
      }
      limits = next;
      // The answer already held says what this view contains, so it is measured against the new
      // limits here, before anything is asked: a person who lowered a budget did it to get
      // memory back, and waiting for a round trip to release it would hold the bytes they had
      // just said they could not afford. Not when the view is now below the floor — there the
      // request below releases everything, and starting a read first would be a download
      // abandoned in the same breath.
      if (
        active &&
        lastAnswer &&
        !detached &&
        mapZoomFor(engine.getPseudoZoom()) >= next.minZoom
      ) {
        apply(lastAnswer, next);
        publish(counted(state.status, state.listing));
      }
      // And asked again in any case: the answer held stopped at the old count, so a raised one
      // has caves in it that were only ever counted.
      void load();
    },
    setAltitudePlacement(next) {
      if (samePlacement(next, placement)) {
        return;
      }
      placement = next;
      // The ground changing under a mesh moves it and nothing else: where it belongs is arithmetic
      // on its anchor, so everything already on the graphics card stays there.
      engine.setModelPlacement(next);
    },
    refreshSurveyTops() {
      if (detached) {
        return;
      }
      for (const entry of held.values()) {
        const top = surveyTopOf(entry.caveId);
        // "No top" is not news about the cave. The loop that reports tops answers for what it has
        // drawn: a cave panned to the edge, served flat at this zoom, or with its lines switched
        // off stops being reported without having moved, and re-hanging its walls from their own
        // origin would pull them off the lines they were drawn around. A top, once known, is kept
        // for as long as the mesh is.
        if (entry.status === 'failed' || top === undefined || top === entry.anchor.surveyTopAltitudeM) {
          continue;
        }
        entry.anchor = { ...entry.anchor, surveyTopAltitudeM: top };
        // The same file under the same id is a move, not a second download — including while the
        // first read is still in the air, where it changes where the mesh will land.
        void engine
          .loadModel(surveyMeshInViewModelId(entry.caveId), {
            url: entry.url,
            anchor: entry.anchor,
            placement,
            visible: true,
          })
          .catch(() => {
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
      listeners.clear();
      requestSeq += 1;
      releaseAll();
    },
  };
}
