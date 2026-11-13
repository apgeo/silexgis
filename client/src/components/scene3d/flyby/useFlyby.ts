// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  caveQuery,
  fetchGeofileFeatureCollection,
  filterQueryOptions,
  geofileQuery,
  savedFiltersQuery,
  type FilterQueryBody,
} from '../../../api/hooks.ts';
import type { FilterDocument } from '../../../filters/types.ts';
import {
  FLYBY_FILTER_LIMIT,
  flybyProblems,
  type FlybyChange,
  type FlybyDocument,
  type FlybyFeatureLayer,
  type FlybyLook,
  type FlybyPresetId,
  type FlybyProblem,
  type FlybySection,
  type FlybySubject,
  type FlybyTexts,
} from '../../../flyby/flybyDocument.ts';
import { updateSection } from '../../../flyby/flybyEdit.ts';
import { expandFlyby, type FlybyExpanded } from '../../../flyby/flybyExpand.ts';
import { cavesOfFilterDocument, FLYBY_FEATURE_WORLD } from '../../../flyby/flybyFilters.ts';
import { createFlybyLookStage, type FlybyLookTarget } from '../../../flyby/flybyLookApply.ts';
import { createFlybyPlayer, type FlybyPlayer, type FlybyPlayerState, type FlybyStage } from '../../../flyby/flybyPlayer.ts';
import {
  FlybyFilterEmpty,
  isFlybyAbort,
  prepareFlyby,
  resolveFilterGroups,
  settleScene,
  type FlybyPrepareProgress,
  type FlybyPrepareStage,
} from '../../../flyby/flybyPrepare.ts';
import {
  buildPreset,
  DEFAULT_FLYBY_PRESET_OPTIONS,
  FLYBY_PRESET_SKY,
  FLYBY_VIEW_SECONDS,
  presetFits,
  presetFor,
  viewsOfDocument,
  type FlybyCapturedView,
  type FlybyPresetOptions,
} from '../../../flyby/flybyPresets.ts';
import {
  resolveSubject,
  shortestRoute,
  type FlybyResolvedSubject,
  type FlybyWorld,
} from '../../../flyby/flybySubjects.ts';
import type { FlybyTextPlan } from '../../../flyby/flybyTexts.ts';
import {
  compileFlyby,
  flybyLineFallbacks,
  FlybySubjectMissing,
  type FlybySayWords,
  type FlybyTimeline,
} from '../../../flyby/flybyTimeline.ts';
import { metersBetween, readCamera3D } from '../../../scene3d/camera3d.ts';
import type { CaveInView3D } from '../../../scene3d/centerlines3d.ts';
import { featuresOf, geoJsonBounds, lineStrings } from '../../../scene3d/geoJson3d.ts';
import { boundsToBbox } from '../../../scene3d/viewBounds3d.ts';
import type { Scene3DBounds, Scene3DCameraState, Scene3DCore } from '../../../scene3d/scene3dEngine.ts';
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
  /** One cave's survey lines as drawn now, at their drawn heights; what a descent follows. */
  caveLines?(caveId: string): { longitude: number; latitude: number; height: number }[][];
}

/** Where the subjects of a flyby being put together come from. */
export type FlybySource = 'selected' | 'inView' | 'filter' | 'view' | 'file';

/** What went wrong, in a form the panel puts into words. */
export type FlybyTrouble =
  | { kind: 'problems'; problems: FlybyProblem[] }
  | { kind: 'missing' }
  /** A saved filter the flyby is about keeps no cave for this reader, or cannot be run for them. */
  | { kind: 'emptyFilter' }
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

/** A cave's name, and its length and depth when they are asked for and known, in the reader's words. */
export function flybySayWords(t: (key: string, values?: Record<string, unknown>) => string, language: string): FlybySayWords {
  const metres = (value: number) => new Intl.NumberFormat(language).format(Math.round(value));
  return (about, what) => {
    if (!about.name) {
      return undefined;
    }
    if (what === 'name') {
      return about.name;
    }
    const size = [
      ...(about.lengthM ? [t('flyby.atCave.length', { metres: metres(about.lengthM) })] : []),
      ...(about.depthM ? [t('flyby.atCave.depth', { metres: metres(about.depthM) })] : []),
    ];
    return size.length > 0 ? `${about.name} — ${size.join(', ')}` : about.name;
  };
}

/** What is found of the subjects only when a flyby is prepared: the lines a section follows, and what a section says of one. */
function followedSubjects(document: FlybyDocument): string[] {
  return [
    ...new Set(
      document.sections.flatMap((section) => [
        ...(section.kind === 'descent' ? [`line:${section.subject}`] : []),
        // What a section says about its subject is read when the flyby is prepared, too.
        ...(section.say ? [`about:${section.subject}`] : []),
      ]),
    ),
  ].sort();
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
    document.features,
  ]);
}

export function useFlyby(scene: FlybyScene, selection: WorkspaceSelection | null, loadRoute: boolean) {
  const queryClient = useQueryClient();
  const { t, i18n } = useTranslation();
  const say = useMemo(() => flybySayWords(t, i18n.language), [t, i18n.language]);

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
  /** The imported file a flyby along a line follows, when that is what it is about. */
  const [fileId, setFileIdState] = useState<string>();
  /** The saved filter whose caves a flyby is about, and how many of them at most. */
  const [savedFilterId, setSavedFilterIdState] = useState<string>();
  const [filterLimit, setFilterLimitState] = useState<number>(FLYBY_FILTER_LIMIT.initial);
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
  /** The layers of features the flyby draws itself. */
  const [features, setFeatures] = useState<FlybyFeatureLayer[]>([]);
  /** The camera views captured for a flyby through them, in the order they are flown. */
  const [views, setViewsState] = useState<FlybyCapturedView[]>([]);

  const subjects = useMemo<FlybySubject[]>(() => {
    switch (source) {
      case 'selected':
        return selectedSubject ? [selectedSubject] : [];
      case 'inView':
        return checked.map((caveId) => ({ kind: 'cave', caveId }));
      case 'view':
        return [{ kind: 'view' }];
      case 'file':
        return fileId ? [{ kind: 'file', geofileId: fileId }] : [];
      case 'filter':
        return savedFilterId ? [{ kind: 'filter', savedFilterId, limit: filterLimit }] : [];
    }
  }, [source, selectedSubject, checked, fileId, savedFilterId, filterLimit]);

  const preset = presetChoice && presetFits(presetChoice, subjects) ? presetChoice : presetFor(subjects);

  const document = useMemo<FlybyDocument | null>(() => {
    const base =
      edited ??
      (preset === 'myViews'
        ? views.length > 0
          ? buildPreset(preset, [], options, '', views)
          : null
        : subjects.length > 0
          ? buildPreset(preset, subjects, options)
          : null);
    if (!base) {
      return null;
    }
    // Written only when they say something, so a flyby without them is the plain document it was.
    const built: FlybyDocument = { ...base, speed, loop, name };
    delete built.look;
    delete built.changes;
    delete built.texts;
    delete built.features;
    return {
      ...built,
      ...(Object.keys(look).length > 0 ? { look } : {}),
      ...(changes.length > 0 ? { changes } : {}),
      ...(Object.keys(texts).length > 0 ? { texts } : {}),
      ...(features.length > 0 ? { features } : {}),
    };
  }, [edited, subjects, preset, options, speed, loop, name, look, changes, texts, features, views]);

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
  const setFileId = useCallback((next: string | undefined) => {
    setEdited(null);
    setFileIdState(next);
  }, []);
  const setCheckedCaves = useCallback((next: string[]) => {
    setEdited(null);
    setChecked(next);
  }, []);
  /** A cave found by its name, ticked at the end of the list — whether or not it is in view. */
  const addCave = useCallback((caveId: string) => {
    setEdited(null);
    setChecked((current) => (current.includes(caveId) ? current : [...current, caveId]));
  }, []);
  const setSavedFilterId = useCallback((next: string | undefined) => {
    setEdited(null);
    setSavedFilterIdState(next);
  }, []);
  const setFilterLimit = useCallback((next: number) => {
    setEdited(null);
    setFilterLimitState(Math.round(Math.min(FLYBY_FILTER_LIMIT.max, Math.max(FLYBY_FILTER_LIMIT.min, next))));
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

  /** The views of a flyby through captured views, changed: the flyby is built from them again. */
  const setViews = useCallback((next: FlybyCapturedView[]) => {
    setEdited(null);
    setViewsState(next);
  }, []);

  /**
   * The scene's camera as a view to fly through: the point the middle of the picture shows, how far
   * the eye is from it and which way it looks. Undefined when the middle of the picture is sky, which
   * has no point to fly to.
   */
  const captureView = useCallback((): FlybyCapturedView | undefined => {
    const engine = scene.engine();
    if (!engine) {
      return undefined;
    }
    const camera = readCamera3D(engine);
    if (!camera.target) {
      return undefined;
    }
    return {
      target: { longitude: camera.target.lon, latitude: camera.target.lat, height: camera.target.height },
      distance: Math.max(metersBetween(camera.eye, camera.target), 1),
      heading: camera.heading,
      pitch: camera.pitch,
      underground: camera.eye.height < engine.groundHeight(camera.eye.lon, camera.eye.lat),
      seconds: FLYBY_VIEW_SECONDS,
    };
  }, [scene]);

  const open = useCallback((opened: FlybyDocument) => {
    const openedViews = viewsOfDocument(opened);
    if (openedViews) {
      setViewsState(openedViews);
      setPresetChoice('myViews');
    }
    setEdited(opened);
    setSpeed(opened.speed);
    setLoop(opened.loop);
    setName(opened.name);
    setLook(opened.look ?? {});
    setChanges(opened.changes ?? []);
    setTexts(opened.texts ?? {});
    setFeatures(opened.features ?? []);
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
          name: cave.name,
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
      ...(scene.caveLines ? { caveLines: (caveId: string) => scene.caveLines!(caveId) } : {}),
      fileLines: async (geofileId) => {
        // A file this reader may not read answers with a refusal, which here is simply "not found".
        const file = await queryClient.fetchQuery({ ...geofileQuery(geofileId), staleTime: 30_000 }).catch(() => undefined);
        const box = file ? geoJsonBounds(file.bbox) : undefined;
        if (!box) {
          return undefined;
        }
        // A hair wider than the file's own box, so the rounding of the request leaves no end of a line out.
        const pad = 0.001;
        const asked: Scene3DBounds = [box[0] - pad, box[1] - pad, box[2] + pad, box[3] + pad];
        const collection = await fetchGeofileFeatureCollection(geofileId, boundsToBbox(asked)).catch(() => undefined);
        if (!collection) {
          return undefined;
        }
        const lines = featuresOf(collection).flatMap((feature) =>
          lineStrings(feature).map((line) => line.map(({ longitude, latitude }) => ({ longitude, latitude }))),
        );
        return { bounds: box, lines };
      },
      filterCaves: async (savedFilterId, limit) => {
        // The filter as this reader may read it: one they may not, or one that can no longer be
        // run, keeps nothing — never the caves it kept for somebody else.
        const filters = await queryClient.fetchQuery({ ...savedFiltersQuery(), staleTime: 30_000 }).catch(() => undefined);
        const saved = filters?.find((filter) => filter.id.toLowerCase() === savedFilterId.toLowerCase());
        const asked = saved?.state === 'ready' && saved.document ? cavesOfFilterDocument(saved.document as unknown as FilterDocument) : undefined;
        if (!asked) {
          return undefined;
        }
        const body: FilterQueryBody = {
          document: asked as unknown as FilterQueryBody['document'],
          page: 1,
          pageSize: limit,
          count: false,
        };
        const answer = await queryClient.fetchQuery({ ...filterQueryOptions(body), staleTime: 30_000 }).catch(() => undefined);
        return answer?.worlds.find((world) => world.world === FLYBY_FEATURE_WORLD)?.hits.map((hit) => hit.id).slice(0, limit);
      },
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
  /**
   * The subjects as last found, kept so a change of timing does not send the camera visiting again:
   * with the caves each saved filter kept, which the document is written out for again.
   */
  const foundRef = useRef<{
    key: string;
    resolved: (FlybyResolvedSubject | undefined)[];
    filterCaves: Map<number, string[]>;
  } | null>(null);

  const abortRef = useRef<AbortController | null>(null);
  const [playerState, setPlayerState] = useState<FlybyPlayerState>({ status: 'idle', t: 0, section: 0 });
  const [length, setLength] = useState(0);
  const [preparing, setPreparing] = useState<FlybyPrepareProgress | true | null>(null);
  const [trouble, setTrouble] = useState<FlybyTrouble>();
  const [unsettled, setUnsettled] = useState(0);
  /** Sections that were to follow a line and had none to follow, as last prepared. */
  const [lineFallbacks, setLineFallbacks] = useState<string[]>([]);
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
    // The subjects, and which of them a section follows the survey of: a way down a cave is found
    // when the flyby is prepared, so a descent added since is a reason to prepare it again.
    const subjectsKey = JSON.stringify([document.subjects, followedSubjects(document)]);
    const controller = new AbortController();
    abortRef.current = controller;
    setPreparing(true);
    try {
      let timeline;
      let expanded: FlybyExpanded;
      if (foundRef.current?.key === subjectsKey) {
        // Same subjects, different timing: the path is rebuilt from where they were found.
        expanded = expandFlyby(document, foundRef.current.filterCaves);
        const resolved = [...foundRef.current.resolved];
        resolveFilterGroups(expanded, resolved);
        timeline = compileFlyby(expanded.document, resolved, say);
      } else {
        const prepared = await prepareFlyby(document, world, stage, {
          loadRoute,
          say,
          signal: controller.signal,
          onProgress: setPreparing,
        });
        foundRef.current = { key: subjectsKey, resolved: prepared.resolved, filterCaves: prepared.filterCaves };
        expanded = prepared.expanded;
        timeline = prepared.timeline;
        setUnsettled(prepared.unsettled);
      }
      const sectionOf = expanded.sectionOf;
      // Said of the sections as written: one flown for each cave is named once, whichever cave it failed at.
      setLineFallbacks([
        ...new Set(
          flybyLineFallbacks(expanded.document, foundRef.current?.resolved ?? []).flatMap((id) => {
            const at = expanded.document.sections.findIndex((section) => section.id === id);
            const original = document.sections[sectionOf[at] ?? -1];
            return original ? [original.id] : [];
          }),
        ),
      ]);
      const player = createFlybyPlayer(timeline, stage, { loop: document.loop, home });
      let shown = Number.NEGATIVE_INFINITY;
      const texts = timeline.texts;
      const unsubscribe = player.subscribe((played) => {
        tellMoment({ status: played.status, t: played.t, texts });
        // The panel lists the sections as written: one flown for each cave is the one written once.
        const next = { ...played, section: sectionOf[played.section] ?? played.section };
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
            : error instanceof FlybyFilterEmpty
              ? { kind: 'emptyFilter' }
              : { kind: 'failed', detail: error instanceof Error ? error.message : String(error) },
        );
      }
      return undefined;
    } finally {
      abortRef.current = null;
      setPreparing(null);
    }
  }, [document, scene, world, stage, loadRoute, say, dropPlayer, tellMoment]);

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
    fileId,
    setFileId,
    addCave,
    savedFilterId,
    setSavedFilterId,
    filterLimit,
    setFilterLimit,
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
    views,
    setViews,
    captureView,
    changeSection,
    open,
    // how it looks, and what it says
    look,
    setLook,
    changes,
    setChanges,
    texts,
    setTexts,
    features,
    setFeatures,
    // playing
    playerState,
    length,
    preparing,
    trouble,
    setTrouble,
    unsettled,
    lineFallbacks,
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
