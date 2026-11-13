// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { caveQuery } from '../../../api/hooks.ts';
import {
  flybyProblems,
  type FlybyChange,
  type FlybyDocument,
  type FlybyLook,
  type FlybyPresetId,
  type FlybyProblem,
  type FlybySection,
  type FlybySubject,
  type FlybyTexts,
} from '../../../flyby/flybyDocument.ts';
import { updateSection } from '../../../flyby/flybyEdit.ts';
import { createFlybyLookStage, type FlybyLookTarget } from '../../../flyby/flybyLookApply.ts';
import { createFlybyPlayer, type FlybyPlayer, type FlybyPlayerState, type FlybyStage } from '../../../flyby/flybyPlayer.ts';
import {
  isFlybyAbort,
  prepareFlyby,
  settleScene,
  type FlybyPrepareProgress,
  type FlybyPrepareStage,
} from '../../../flyby/flybyPrepare.ts';
import {
  buildPreset,
  DEFAULT_FLYBY_PRESET_OPTIONS,
  FLYBY_PRESET_SKY,
  presetFits,
  presetFor,
  type FlybyPresetOptions,
} from '../../../flyby/flybyPresets.ts';
import {
  resolveSubject,
  shortestRoute,
  type FlybyResolvedSubject,
  type FlybyWorld,
} from '../../../flyby/flybySubjects.ts';
import type { FlybyTextPlan } from '../../../flyby/flybyTexts.ts';
import { compileFlyby, FlybySubjectMissing, type FlybyTimeline } from '../../../flyby/flybyTimeline.ts';
import { metersBetween, readCamera3D } from '../../../scene3d/camera3d.ts';
import type { CaveInView3D } from '../../../scene3d/centerlines3d.ts';
import { geoJsonBounds } from '../../../scene3d/geoJson3d.ts';
import type { Scene3DCameraState, Scene3DCore } from '../../../scene3d/scene3dEngine.ts';
import type { WorkspaceSelection } from '../../../stores/workspaceStore.ts';
import { APPROXIMATE_SPAN_DEGREES, geometryFor } from '../../../viewlinks/geoTargets.ts';

/** What the flyby tool asks of the view it is mounted in. Every member reads the view as it is now. */
export interface FlybyScene {
  /** The scene, or null while there is none — before it has arrived, and while it is being rebuilt. */
  engine(): Scene3DCore | null;
  cavesInView(): CaveInView3D[];
  /** Has every loader of the view read what the camera now shows. */
  refreshData(): void;
  /** True while any loader of the view is still reading. */
  dataLoading(): boolean;
  /** The element the whole view is inside: what is shown alone on a clean screen. */
  wrap(): HTMLElement | null;
  /** The element the scene is drawn into: a press there means the viewer is taking the camera. */
  surface(): HTMLElement | null;
  /** What a flyby's look is put on the scene through; left out, a flyby moves the camera only. */
  look?: FlybyLookTarget;
}

/** Where the subjects of a flyby being put together come from. */
export type FlybySource = 'selected' | 'inView' | 'view';

/** What went wrong, in a form the panel puts into words. */
export type FlybyTrouble =
  | { kind: 'problems'; problems: FlybyProblem[] }
  | { kind: 'missing' }
  | { kind: 'noScene' }
  | { kind: 'badFile' }
  | { kind: 'failed'; detail: string };

/** Where a flyby is, as told on every frame it is placed at: what is written over the scene follows it. */
export interface FlybyMoment {
  status: FlybyPlayerState['status'];
  t: number;
  texts: FlybyTextPlan;
}

/** How often the position shown is brought up to date while a flyby plays, in milliseconds. */
const POSITION_UPDATE_MILLISECONDS = 100;
/** How many caves in view are ticked when the list is first shown: enough for a tour, not a marathon. */
const DEFAULT_TOUR_CAVES = 6;

const NO_CAMERA: Scene3DCameraState = { longitude: 0, latitude: 0, height: 0, heading: 0, pitch: -90, roll: 0 };

/** The subject a selection names, if it names one a camera can fly around. */
export function subjectOfSelection(selection: WorkspaceSelection | null): FlybySubject | undefined {
  switch (selection?.kind) {
    case 'cave':
    case 'entrance':
      return { kind: 'cave', caveId: selection.caveId };
    case 'feature':
      return { kind: 'feature', featureId: selection.featureId };
    default:
      return undefined;
  }
}

/** What of a document decides what is played: everything but what it is called and whether it repeats. */
function pathKey(document: FlybyDocument): string {
  return JSON.stringify([
    document.subjects,
    document.start,
    document.sections,
    document.speed,
    document.look,
    document.changes,
    document.texts,
  ]);
}

export function useFlyby(scene: FlybyScene, selection: WorkspaceSelection | null, loadRoute: boolean) {
  const queryClient = useQueryClient();

  // ---- what the flyby is about, and how it is built ----

  const selectedSubject = useMemo(() => subjectOfSelection(selection), [selection]);
  const [source, setSourceState] = useState<FlybySource>(() => (subjectOfSelection(selection) ? 'selected' : 'view'));
  const [caves, setCaves] = useState<CaveInView3D[]>(() => scene.cavesInView());
  const [checked, setChecked] = useState<string[]>(() =>
    scene
      .cavesInView()
      .slice(0, DEFAULT_TOUR_CAVES)
      .map((cave) => cave.caveId),
  );
  const [presetChoice, setPresetChoice] = useState<FlybyPresetId>();
  const [options, setOptionsState] = useState<FlybyPresetOptions>(DEFAULT_FLYBY_PRESET_OPTIONS);
  const [speed, setSpeed] = useState(1);
  const [loop, setLoop] = useState(false);
  const [name, setName] = useState('');
  /** A document opened from the kept ones or a file, or one whose sections were changed by hand. */
  const [edited, setEdited] = useState<FlybyDocument | null>(null);
  // How the scene looks, its timed changes and the texts over it: kept apart from the sections, so
  // building the sections again from another preset or option keeps what was set here.
  const [look, setLook] = useState<FlybyLook>({ sky: FLYBY_PRESET_SKY });
  const [changes, setChanges] = useState<FlybyChange[]>([]);
  const [texts, setTexts] = useState<FlybyTexts>({});

  const subjects = useMemo<FlybySubject[]>(() => {
    switch (source) {
      case 'selected':
        return selectedSubject ? [selectedSubject] : [];
      case 'inView':
        return checked.map((caveId) => ({ kind: 'cave', caveId }));
      case 'view':
        return [{ kind: 'view' }];
    }
  }, [source, selectedSubject, checked]);

  const preset = presetChoice && presetFits(presetChoice, subjects) ? presetChoice : presetFor(subjects);

  const document = useMemo<FlybyDocument | null>(() => {
    const base = edited ?? (subjects.length > 0 ? buildPreset(preset, subjects, options) : null);
    if (!base) {
      return null;
    }
    // Written only when they say something, so a flyby without them is the plain document it was.
    const built: FlybyDocument = { ...base, speed, loop, name };
    delete built.look;
    delete built.changes;
    delete built.texts;
    return {
      ...built,
      ...(Object.keys(look).length > 0 ? { look } : {}),
      ...(changes.length > 0 ? { changes } : {}),
      ...(Object.keys(texts).length > 0 ? { texts } : {}),
    };
  }, [edited, subjects, preset, options, speed, loop, name, look, changes, texts]);

  const refreshCaves = useCallback(() => setCaves(scene.cavesInView()), [scene]);

  // Anything that changes what the flyby is built from puts the built one back in force: sections
  // changed by hand belong to the flyby they were changed in, not to the next one.
  const setSource = useCallback(
    (next: FlybySource) => {
      setEdited(null);
      setSourceState(next);
      if (next === 'inView') {
        refreshCaves();
      }
    },
    [refreshCaves],
  );
  const setCheckedCaves = useCallback((next: string[]) => {
    setEdited(null);
    setChecked(next);
  }, []);
  const setPreset = useCallback((next: FlybyPresetId) => {
    setEdited(null);
    setPresetChoice(next);
  }, []);
  const setOptions = useCallback((next: Partial<FlybyPresetOptions>) => {
    setEdited(null);
    setOptionsState((current) => ({ ...current, ...next }));
  }, []);

  /**
   * Makes an edited document the flyby: its sections as they now are, and the changes and the
   * texts that went with them — an edit that takes a section out takes its changes and captions too.
   */
  const edit = useCallback((next: FlybyDocument) => {
    setEdited(next);
    setChanges(next.changes ?? []);
    setTexts(next.texts ?? {});
  }, []);

  const changeSection = useCallback(
    (id: string, change: Partial<Pick<FlybySection, 'seconds' | 'speed'>>) => {
      if (document) {
        edit(updateSection(document, id, change));
      }
    },
    [document, edit],
  );

  const open = useCallback((opened: FlybyDocument) => {
    setEdited(opened);
    setSpeed(opened.speed);
    setLoop(opened.loop);
    setName(opened.name);
    setLook(opened.look ?? {});
    setChanges(opened.changes ?? []);
    setTexts(opened.texts ?? {});
  }, []);

  // ---- the world the subjects are found in ----

  const world = useMemo<FlybyWorld>(
    () => ({
      caveInView: (caveId) => scene.cavesInView().find((cave) => cave.caveId === caveId),
      caveFacts: async (caveId) => {
        // A cave this reader may not read answers with a refusal, which here is simply "not found".
        const cave = await queryClient.fetchQuery({ ...caveQuery(caveId), staleTime: 30_000 }).catch(() => undefined);
        const point = cave?.geom?.coordinates;
        if (!point || point.length < 2) {
          return undefined;
        }
        return {
          longitude: Number(point[0]),
          latitude: Number(point[1]),
          depthM: cave.depth ?? undefined,
          lengthM: cave.surveyedLength ?? undefined,
        };
      },
      featureBounds: async (featureId) => {
        const target = await geometryFor(queryClient, { targetType: 'feature', targetId: featureId }).catch(
          () => null,
        );
        const bounds = target ? geoJsonBounds(target.geometry) : undefined;
        if (!bounds) {
          return undefined;
        }
        // A place given only roughly is flown around as the patch it could be anywhere in, the
        // same widening the view uses when it is asked to show one.
        const pad = target?.approximate ? APPROXIMATE_SPAN_DEGREES / 2 : 0;
        return [bounds[0] - pad, bounds[1] - pad, bounds[2] + pad, bounds[3] + pad];
      },
      groundHeight: (longitude, latitude) => scene.engine()?.groundHeight(longitude, latitude) ?? 0,
      viewTarget: () => {
        const engine = scene.engine();
        if (!engine) {
          return undefined;
        }
        const camera = readCamera3D(engine);
        return camera.target
          ? { target: camera.target, distance: metersBetween(camera.eye, camera.target) }
          : undefined;
      },
    }),
    [scene, queryClient],
  );

  const stage = useMemo<FlybyStage & FlybyPrepareStage>(
    () => ({
      getCamera: () => scene.engine()?.getCamera() ?? NO_CAMERA,
      setCamera: (camera) => {
        const engine = scene.engine();
        if (engine) {
          engine.setCamera(camera, { animate: false });
          // Nothing else asks: the scene draws only on request, and a camera placed by a script
          // is not a gesture the engine saw.
          engine.requestRender();
        }
      },
      groundHeight: (longitude, latitude) => scene.engine()?.groundHeight(longitude, latitude) ?? 0,
      refreshData: () => scene.refreshData(),
      now: () => performance.now(),
      requestFrame: (callback) => window.requestAnimationFrame(callback),
      cancelFrame: (handle) => window.cancelAnimationFrame(handle),
      ...(scene.look ? { look: createFlybyLookStage(scene.look) } : {}),
      settle: (timeoutMs) =>
        settleScene(
          {
            isSettled: () => scene.engine()?.isSettled() ?? true,
            dataLoading: () => scene.dataLoading(),
            isDestroyed: () => scene.engine()?.isDestroyed() ?? true,
            requestRender: () => scene.engine()?.requestRender(),
            nextFrame: () => new Promise((resolve) => window.requestAnimationFrame(() => resolve())),
            now: () => performance.now(),
          },
          timeoutMs,
        ),
    }),
    [scene],
  );

  // ---- playing ----

  const playerRef = useRef<{ player: FlybyPlayer; key: string; timeline: FlybyTimeline; unsubscribe(): void } | null>(null);
  /** The subjects as last found, kept so a change of timing does not send the camera visiting again. */
  const foundRef = useRef<{ key: string; resolved: (FlybyResolvedSubject | undefined)[] } | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const [playerState, setPlayerState] = useState<FlybyPlayerState>({ status: 'idle', t: 0, section: 0 });
  const [length, setLength] = useState(0);
  const [preparing, setPreparing] = useState<FlybyPrepareProgress | true | null>(null);
  const [trouble, setTrouble] = useState<FlybyTrouble>();
  const [unsettled, setUnsettled] = useState(0);
  // Told of every moment, unthrottled and outside React state: what is drawn over the scene must
  // keep step with the scene frame by frame, which the throttled state above does not.
  const momentListeners = useRef(new Set<(moment: FlybyMoment | null) => void>());
  const tellMoment = useCallback((moment: FlybyMoment | null) => {
    for (const listener of [...momentListeners.current]) {
      listener(moment);
    }
  }, []);
  const subscribeMoment = useCallback((listener: (moment: FlybyMoment | null) => void) => {
    momentListeners.current.add(listener);
    return () => {
      momentListeners.current.delete(listener);
    };
  }, []);

  const dropPlayer = useCallback(() => {
    const held = playerRef.current;
    if (held) {
      held.unsubscribe();
      // Stopped rather than only let go of, so the camera goes back to where the flyby found it.
      held.player.stop();
      held.player.destroy();
      playerRef.current = null;
      setPlayerState({ status: 'idle', t: 0, section: 0 });
      tellMoment(null);
    }
  }, [tellMoment]);

  /** The player for the document as it stands, prepared if it has to be; undefined when it cannot be. */
  const ensurePlayer = useCallback(async (): Promise<FlybyPlayer | undefined> => {
    if (!document) {
      return undefined;
    }
    const problems = flybyProblems(document);
    if (problems.length > 0) {
      setTrouble({ kind: 'problems', problems });
      return undefined;
    }
    const engine = scene.engine();
    if (!engine) {
      setTrouble({ kind: 'noScene' });
      return undefined;
    }
    const key = pathKey(document);
    if (playerRef.current?.key === key) {
      return playerRef.current.player;
    }
    dropPlayer();
    setTrouble(undefined);
    const home = engine.getCamera();
    const subjectsKey = JSON.stringify(document.subjects);
    const controller = new AbortController();
    abortRef.current = controller;
    setPreparing(true);
    try {
      let timeline;
      if (foundRef.current?.key === subjectsKey) {
        // Same subjects, different timing: the path is rebuilt from where they were found.
        timeline = compileFlyby(document, foundRef.current.resolved);
      } else {
        const prepared = await prepareFlyby(document, world, stage, {
          loadRoute,
          signal: controller.signal,
          onProgress: setPreparing,
        });
        foundRef.current = { key: subjectsKey, resolved: prepared.resolved };
        timeline = prepared.timeline;
        setUnsettled(prepared.unsettled);
      }
      const player = createFlybyPlayer(timeline, stage, { loop: document.loop, home });
      let shown = Number.NEGATIVE_INFINITY;
      const texts = timeline.texts;
      const unsubscribe = player.subscribe((next) => {
        tellMoment({ status: next.status, t: next.t, texts });
        // A playing flyby reports sixty times a second; the slider and the clock need a tenth of
        // that. Only while it plays, though: a moment it was put at by hand, or left at by a pause,
        // is the last word until the next one, and one that was skipped would leave the clock
        // showing where the flyby no longer is.
        const now = performance.now();
        setPlayerState((current) => {
          if (
            next.status === 'playing' &&
            current.status === 'playing' &&
            current.section === next.section &&
            now - shown < POSITION_UPDATE_MILLISECONDS
          ) {
            return current;
          }
          shown = now;
          return next;
        });
      });
      playerRef.current = { player, key, timeline, unsubscribe };
      setLength(timeline.length);
      return player;
    } catch (error) {
      // The visits moved the camera; a flyby that is not going to be played gives it back.
      stage.setCamera(home);
      stage.refreshData();
      if (!isFlybyAbort(error)) {
        setTrouble(
          error instanceof FlybySubjectMissing
            ? { kind: 'missing' }
            : { kind: 'failed', detail: error instanceof Error ? error.message : String(error) },
        );
      }
      return undefined;
    } finally {
      abortRef.current = null;
      setPreparing(null);
    }
  }, [document, scene, world, stage, loadRoute, dropPlayer, tellMoment]);

  /** The path of the document as it stands, prepared if it has to be, for whatever films it. */
  const ensureTimeline = useCallback(async (): Promise<FlybyTimeline | undefined> => {
    const player = await ensurePlayer();
    return player && playerRef.current?.player === player ? playerRef.current.timeline : undefined;
  }, [ensurePlayer]);

  const play = useCallback(async () => {
    const player = await ensurePlayer();
    player?.play();
  }, [ensurePlayer]);

  const pause = useCallback(() => playerRef.current?.player.pause(), []);
  const seek = useCallback((t: number) => playerRef.current?.player.seek(t), []);

  const stop = useCallback(() => {
    abortRef.current?.abort();
    playerRef.current?.player.stop();
  }, []);

  // A document whose path has changed is not the one being played: the flyby in progress ends, and
  // the camera goes back, rather than carrying on along a path the panel no longer shows.
  const key = document ? pathKey(document) : '';
  useEffect(() => {
    if (playerRef.current && playerRef.current.key !== key) {
      dropPlayer();
    }
  }, [key, dropPlayer]);

  useEffect(() => {
    playerRef.current?.player.setLoop(loop);
  }, [loop]);

  // Whoever closes the tool, or takes the view away, gets their camera back.
  useEffect(
    () => () => {
      abortRef.current?.abort();
      const held = playerRef.current;
      if (held) {
        held.unsubscribe();
        held.player.stop();
        held.player.destroy();
        playerRef.current = null;
      }
    },
    [],
  );

  /** Puts the ticked caves in the order that makes the camera travel least. */
  const orderByRoute = useCallback(async () => {
    if (source !== 'inView' || checked.length < 3) {
      return;
    }
    const found = await Promise.all(
      checked.map((caveId) => resolveSubject({ kind: 'cave', caveId }, world).catch(() => undefined)),
    );
    const known = checked.filter((_, index) => found[index] !== undefined);
    const order = shortestRoute(found.filter((subject): subject is FlybyResolvedSubject => subject !== undefined));
    // Caves that could not be found keep their place at the end rather than being dropped unsaid.
    const unknown = checked.filter((_, index) => found[index] === undefined);
    setCheckedCaves([...order.map((index) => known[index]), ...unknown]);
  }, [source, checked, world, setCheckedCaves]);

  return {
    // what it is about
    source,
    setSource,
    selectedSubject,
    caves,
    refreshCaves,
    checked,
    setCheckedCaves,
    orderByRoute,
    // how it is built
    preset,
    setPreset,
    options,
    setOptions,
    speed,
    setSpeed,
    loop,
    setLoop,
    name,
    setName,
    document,
    edited: edited !== null,
    edit,
    changeSection,
    open,
    // how it looks, and what it says
    look,
    setLook,
    changes,
    setChanges,
    texts,
    setTexts,
    // playing
    playerState,
    length,
    preparing,
    trouble,
    setTrouble,
    unsettled,
    ensurePlayer,
    ensureTimeline,
    stage,
    play,
    pause,
    seek,
    stop,
    subscribeMoment,
    active: preparing !== null || playerState.status !== 'idle',
  };
}

export type FlybyController = ReturnType<typeof useFlyby>;
