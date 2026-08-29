// SPDX-License-Identifier: AGPL-3.0-or-later
import { fetchSurveyModels, type SurveyModelInfo } from '../api/hooks.ts';
import { ANCHORED_TO_SURFACE, samePlacement, type Altitude3DPlacement } from './altitude3d.ts';
import type { Scene3DModelAnchor, Scene3DModels } from './scene3dEngine.ts';

// Which cave's walls the scene is holding.
//
// Every other overlay in this scene is driven by the camera: it asks for what is inside the view
// and redraws whenever the view settles. This one deliberately is not. A converted wall mesh is
// hundreds of kilobytes in the ordinary case and tens of megabytes in the case this was built for,
// and it costs graphics memory for as long as it is held — so it is loaded because a viewer picked
// a cave, one at a time, and released the moment the selection moves on or the layer is turned
// off. Fetching one per cave in view would be a different product.
//
// The engine arrives as an argument rather than being reached for, so every decision here can be
// exercised against a plain object.

/** The part of the engine this loader touches, named member by member. */
export type SurveyMesh3DEngine = Pick<
  Scene3DModels,
  'loadModel' | 'removeModel' | 'setModelPlacement'
>;

/**
 * The id the wall mesh is held under, in the scene and in the viewer's saved layer settings.
 *
 * One id, not one per cave: the scene holds the walls of the selected cave and no other, so a
 * second id could only ever mean a second cave's mesh was left behind.
 */
export const SURVEY_MESH_LAYER_ID = 'survey-mesh';

/**
 * What the loader is doing, which the chrome states in words.
 *
 * `looking` and `loading` are separate because they cost different amounts of a viewer's patience:
 * the first is one small request, the second is the file itself, and only the second is worth
 * saying how big it is.
 */
export type SurveyMesh3DStatus =
  | 'off'
  | 'looking'
  | 'loading'
  | 'drawn'
  | 'converting'
  | 'unavailable'
  | 'failed';

export interface SurveyMesh3DState {
  status: SurveyMesh3DStatus;
  /** The cave the state is about; undefined when nothing is selected. */
  caveId?: string;
  /** Name of the model being read or drawn. */
  name?: string;
  /** How big the mesh is, as the only magnitude the server publishes about one. */
  triangleCount?: number;
  /**
   * The uploaded file had already lost precision before it arrived, so the survey is degraded.
   * Worth saying: re-exporting about a local origin gets the detail back.
   */
  precisionLost: boolean;
  /**
   * Why the mesh is not drawn, in the server's own words, when the server gave a reason. It is
   * the conversion's message, which is not translatable here and is shown as it was written.
   */
  message?: string;
}

export const EMPTY_SURVEY_MESH_3D_STATE: SurveyMesh3DState = {
  status: 'off',
  precisionLost: false,
};

export interface SurveyMesh3DHandle {
  /**
   * Says which cave the viewer is looking at, and how high the top of that cave's survey is.
   *
   * The top is what the anchored rendering hangs a cave from, and the mesh and the survey lines of
   * one cave have to hang from the same one or they separate vertically by the difference. It
   * arrives late — the lines are fetched by the camera loop, not by this one — so a top that turns
   * up after the mesh is already drawn moves what is loaded rather than reading it again.
   *
   * Leaving the top out means "I have nothing to tell you", not "the cave has no top": for the
   * cave already selected the last known top is kept, and only a change of cave clears it.
   */
  setCave(caveId: string | undefined, surveyTopAltitudeM?: number): void;
  /**
   * Draws the walls, or releases them. Turning them off is a real unload: a hidden mesh still
   * holds every byte of its geometry on the graphics card, and that memory is the whole reason a
   * viewer would turn one off.
   */
  setVisible(visible: boolean): void;
  /** Says how surveyed altitudes become scene heights. Moves what is loaded; never refetches. */
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  getState(): SurveyMesh3DState;
  /** Subscribes to state changes; returns an unsubscribe function. */
  subscribe(listener: (state: SurveyMesh3DState) => void): () => void;
  /** Stops loading and releases the mesh. */
  detach(): void;
}

/** Whether a model row carries a mesh that can actually be drawn, with somewhere to put it. */
export function isDrawableSurveyMesh(model: SurveyModelInfo): boolean {
  return (
    typeof model.meshUrl === 'string' &&
    model.meshUrl.length > 0 &&
    typeof model.anchorLongitude === 'number' &&
    typeof model.anchorLatitude === 'number' &&
    typeof model.anchorHeightM === 'number'
  );
}

/**
 * The one model of a cave whose walls are drawn.
 *
 * A cave may hold any number of models — several line plots, an older mesh and a newer one — and
 * nothing on the record says which is preferred, so the most recently uploaded drawable mesh wins.
 * That is the one an uploader who has just replaced a survey expects to see.
 *
 * A row that has an anchor but no mesh yet is not drawable: a file declared in local coordinates
 * is given its anchor at upload, before anything has been converted, so the anchor alone proves
 * nothing.
 */
export function drawableSurveyMesh(
  models: readonly SurveyModelInfo[] | undefined,
): SurveyModelInfo | undefined {
  let best: SurveyModelInfo | undefined;
  for (const model of models ?? []) {
    if (!isDrawableSurveyMesh(model)) {
      continue;
    }
    if (!best || model.createdAt > best.createdAt) {
      best = model;
    }
  }
  return best;
}

/**
 * The state to publish for a cave that has models but none of them drawable.
 *
 * Only the wall meshes are consulted. A line plot also has work done on it after it arrives — it
 * is read into its stations and shots — but no amount of that work ever produces walls, so
 * reporting "walls on the way" because a .lox is being read promises something that never comes.
 */
function stateWithoutMesh(models: readonly SurveyModelInfo[]): Partial<SurveyMesh3DState> {
  const meshes = models.filter((model) => model.format === 'stl');
  const converting = meshes.find(
    (model) => model.status === 'pending' || model.status === 'processing',
  );
  if (converting) {
    return { status: 'converting', name: converting.name };
  }
  const failed = meshes.find((model) => model.status === 'failed');
  if (failed) {
    return { status: 'failed', name: failed.name, message: failed.processingError ?? undefined };
  }
  return { status: 'unavailable' };
}

export function attachSurveyMesh3d(engine: SurveyMesh3DEngine): SurveyMesh3DHandle {
  let caveId: string | undefined;
  let surveyTopAltitudeM: number | undefined;
  let visible = true;
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;
  let state: SurveyMesh3DState = { ...EMPTY_SURVEY_MESH_3D_STATE };
  const listeners = new Set<(next: SurveyMesh3DState) => void>();

  // Bumped by everything that makes an answer already in flight the wrong answer: a different
  // cave, the layer going off, the loader being taken down. A request compares the number it
  // started with and drops what it fetched rather than drawing it over a newer decision.
  let seq = 0;
  let detached = false;
  /** What is in the scene, or on its way there, kept so it can be moved without being reread. */
  let held: { url: string; anchor: Scene3DModelAnchor } | undefined;

  const publish = (next: Partial<SurveyMesh3DState>) => {
    state = { ...EMPTY_SURVEY_MESH_3D_STATE, caveId, ...next };
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  const release = () => {
    seq += 1;
    if (held) {
      held = undefined;
    }
    // Asked for unconditionally: a read still in the air is abandoned by the same call that frees
    // a mesh already on the card, and an id holding nothing is not an error.
    engine.removeModel(SURVEY_MESH_LAYER_ID);
  };

  const anchorFor = (model: SurveyModelInfo): Scene3DModelAnchor => ({
    longitude: model.anchorLongitude!,
    latitude: model.anchorLatitude!,
    altitudeM: model.anchorHeightM!,
    surveyTopAltitudeM,
  });

  const load = async () => {
    release();
    const mine = seq;
    if (!caveId || !visible) {
      publish({ status: 'off' });
      return;
    }
    publish({ status: 'looking' });

    let models: SurveyModelInfo[];
    try {
      models = await fetchSurveyModels(caveId);
    } catch {
      // The cave's models could not be listed at all. Nothing is drawn and nothing is claimed
      // about why, because a dropped request is not evidence that the cave has no walls.
      if (mine === seq && !detached) {
        publish({ status: 'failed' });
      }
      return;
    }
    if (mine !== seq || detached) {
      return;
    }

    const chosen = drawableSurveyMesh(models);
    if (!chosen) {
      publish(stateWithoutMesh(models));
      return;
    }

    const anchor = anchorFor(chosen);
    const url = chosen.meshUrl!;
    const describe = {
      name: chosen.name,
      triangleCount: chosen.triangleCount ?? undefined,
      precisionLost: chosen.sourcePrecisionLost,
    };
    held = { url, anchor };
    publish({ status: 'loading', ...describe });
    try {
      await engine.loadModel(SURVEY_MESH_LAYER_ID, { url, anchor, placement, visible: true });
    } catch {
      if (mine === seq && !detached) {
        held = undefined;
        publish({ status: 'failed', ...describe });
      }
      return;
    }
    if (mine !== seq || detached) {
      return;
    }
    publish({ status: 'drawn', ...describe });
  };

  return {
    setCave(nextCaveId, nextTop) {
      const sameCave = nextCaveId === caveId;
      // "No top" is not an answer about the cave, so the last real one is kept.
      //
      // The top comes from the camera-driven survey loader, and that loader answers for what is
      // inside the view: the very same cave stops being reported when it is panned off the screen,
      // when the zoom drops far enough that surveys arrive as flat outlines carrying no altitudes,
      // and when the survey lines are switched off altogether. None of those is news about where
      // the cave is. Taking each of them as "the top is unknown now" would re-place a mesh already
      // on the screen — hanging it from its own origin instead of the cave's top, which moves it by
      // the difference between the two and leaves it disagreeing with the very lines drawn inside
      // it. So a top, once known, survives until the selection moves to a different cave.
      const top = sameCave && nextTop === undefined ? surveyTopAltitudeM : nextTop;
      if (sameCave && top === surveyTopAltitudeM) {
        return;
      }
      surveyTopAltitudeM = top;
      if (sameCave && held) {
        // Only the height the cave hangs from changed. Asking for the same file under the same id
        // moves the mesh already on the graphics card; it is not a second download.
        caveId = nextCaveId;
        held = { url: held.url, anchor: { ...held.anchor, surveyTopAltitudeM: top } };
        void engine
          .loadModel(SURVEY_MESH_LAYER_ID, {
            url: held.url,
            anchor: held.anchor,
            placement,
            visible: true,
          })
          .catch(() => {
            // Re-placing something already loaded cannot fail for a reason the viewer can act on,
            // and the state it is in is still the truth.
          });
        return;
      }
      caveId = nextCaveId;
      void load();
    },
    setVisible(next) {
      if (next === visible) {
        return;
      }
      visible = next;
      void load();
    },
    setAltitudePlacement(next) {
      if (samePlacement(next, placement)) {
        return;
      }
      placement = next;
      // The ground changing under a mesh moves it and nothing else: where it belongs is arithmetic
      // on its anchor, not anything in the file, so what is on the graphics card stays there.
      engine.setModelPlacement(next);
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
      listeners.clear();
      release();
    },
  };
}
