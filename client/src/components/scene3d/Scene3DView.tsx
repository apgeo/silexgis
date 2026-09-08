// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { LayoutOutlined } from '@ant-design/icons';
import { Alert, Button, Popover, Result, Spin, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useFeatureTypes, useMapConfig, useMapLayers, useRasterMaps } from '../../api/hooks.ts';
import {
  onFeatureTypeCatalogChanged,
  setFeatureTypeCatalog,
} from '../../map/featureTypeCatalog.ts';
import { ANCHORED_TO_SURFACE, type Altitude3DPlacement } from '../../scene3d/altitude3d.ts';
import { baseImageryLayerId, syncBaseImagery, syncTileOverlayImagery } from '../../scene3d/baseImagery3d.ts';
import { syncRasterImagery } from '../../scene3d/rasterOverlay3d.ts';
import {
  applyCamera3D,
  readCamera3D,
  toSceneCamera,
  type Camera3DState,
} from '../../scene3d/camera3d.ts';
import {
  attachCaveData3d,
  limitsFromMapConfig,
  CAVE_DATA_3D_LAYERS,
  EMPTY_CAVE_DATA_3D_STATE,
  type CaveData3DHandle,
  type CaveData3DState,
} from '../../scene3d/caveData3d.ts';
import {
  attachClosestApproach3d,
  type ClosestApproach3DHandle,
} from '../../scene3d/closestApproach3d.ts';
import {
  attachOverburdenHighlight3d,
  type OverburdenHighlight3DHandle,
} from '../../scene3d/overburdenHighlight3d.ts';
import { geoJsonBounds } from '../../scene3d/geoJson3d.ts';
import type { OverlayRect } from '../../scene3d/overlayPlacement.ts';
import { activePreset, presetCamera, type Camera3DPreset } from '../../scene3d/presets3d.ts';
import { claimSceneSurface, sceneSurfaceElement } from '../../scene3d/sceneSurface.ts';
import {
  pickMatchesSelection,
  pickPayload,
  selectionFromPick,
  type Scene3DPickPayload,
} from '../../scene3d/selection3d.ts';
import type {
  Scene3DAnchor,
  Scene3DCore,
  Scene3DProjection,
  Scene3DScreenPosition,
  Scene3DSurfaceState,
} from '../../scene3d/scene3dEngine.ts';
import {
  checkTerrainSource,
  type TerrainSourceProblem,
} from '../../scene3d/terrainSource3d.ts';
import type { Scene3DSession } from '../../scene3d/scene3dContext.ts';
import {
  attachSurveyMesh3d,
  EMPTY_SURVEY_MESH_3D_STATE,
  SURVEY_MESH_LAYER_ID,
  type SurveyMesh3DHandle,
  type SurveyMesh3DState,
} from '../../scene3d/surveyMesh3d.ts';
import { attachScene3dHash } from '../../scene3d/urlHash3d.ts';
import { attachViewSync3d, type ViewSync3dHandle } from '../../scene3d/viewSync3d.ts';
import { supportsWebGl2 } from '../../scene3d/webglSupport.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { onSurfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera } from '../../workspace/viewCamera.ts';
import { APPROXIMATE_SPAN_DEGREES, geometryFor, isGeographic } from '../../viewlinks/geoTargets.ts';
import { useViewControl } from '../../viewlinks/useViewControl.ts';
import Scene3DCameraControls from './Scene3DCameraControls.tsx';
import Scene3DLayerPanel from './Scene3DLayerPanel.tsx';
import Scene3DOverlay from './Scene3DOverlay.tsx';
import { cutawayPauseMessage, terrainProblemMessage } from './surfaceMessages.ts';
import './Scene3DView.css';

export interface Scene3DViewProps {
  /** CSS height of the scene surface. */
  height?: number | string;
  /**
   * Whether this mount owns the browser's URL hash.
   *
   * There is one hash per window and the flat map already writes to it, so only a view that fills
   * the window claims it — the 3D route does, a 3D panel sitting beside the map does not. Two
   * writers would overwrite each other on every camera move and the last one to settle would win,
   * which is not a shareable position at all.
   */
  syncUrlHash?: boolean;
}

/**
 * React shell around the 3D scene: it owns the element the scene draws into and the states the
 * user can see (starting, running, unsupported, failed), and nothing else. The scene itself lives
 * outside React in its own module — a drawing surface with GPU resources behind it cannot be
 * rebuilt every render — so this component never holds engine objects in state; it holds a handle
 * and asks React to re-run the effects that talk to it.
 */
export default function Scene3DView({ height = '100%', syncUrlHash = false }: Scene3DViewProps) {
  const { t, i18n } = useTranslation();
  const slotRef = useRef<HTMLDivElement>(null);

  // Answered once, before anything is downloaded: the engine chunk is a megabyte that a browser
  // which cannot draw the scene should never be asked to fetch.
  const [webGl2] = useState(supportsWebGl2);

  const [status, setStatus] = useState<'loading' | 'ready' | 'error' | 'contextLost'>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();
  // Incremented to build the scene again after the browser has taken its graphics context away.
  // It is a dependency of the effect that owns the scene, so changing it is the rebuild.
  const [rebuildToken, setRebuildToken] = useState(0);
  // False while another mount in this window is showing the one scene there is.
  const [showingHere, setShowingHere] = useState(true);

  // The scene arrives asynchronously and is not React state; the counter is what tells the
  // effects below that the handle in the ref has changed.
  const engineRef = useRef<Scene3DCore | null>(null);
  const [engineVersion, setEngineVersion] = useState(0);
  const queryClient = useQueryClient();

  // Somewhere a hyperlink in a text panel can be sent, for as long as there is a scene to send
  // it to. Off while another mount in this window holds the one scene, and off before the scene
  // has arrived: a control that accepted a reveal it could not draw would look, to the reader,
  // exactly like a link that does not work.
  useViewControl({
    id: 'scene3d',
    kind: 'scene3d',
    labelKey: 'viewLinks.controls.scene3d',
    enabled: showingHere && engineVersion > 0,
    canReveal: isGeographic,
    reveal: (ref) => {
      void geometryFor(queryClient, ref).then((target) => {
        const engine = engineRef.current;
        const bounds = target === null ? undefined : geoJsonBounds(target.geometry);
        if (engine !== null && bounds) {
          // A snapped point's box is a single position, so the camera would drop to the same
          // height it uses for a surveyed station over a spot that can be kilometres out. Widening
          // to about the grid it was snapped to is what makes the picture say how well the place
          // is actually known.
          const [west, south, east, north] = bounds;
          const pad = target?.approximate ? APPROXIMATE_SPAN_DEGREES / 2 : 0;
          engine.fitBounds([west - pad, south - pad, east + pad, north + pad], { animate: true });
        }
      });
    },
  });

  useEffect(() => {
    if (!webGl2) {
      return;
    }
    let disposed = false;
    let session: Scene3DSession | null = null;
    let unsubscribeRenderError: (() => void) | undefined;
    let unsubscribeContextLoss: (() => void) | undefined;
    setStatus('loading');
    setErrorDetail(undefined);

    // The scene is built inside the window's one drawing surface, which this borrows for as long
    // as it is mounted. Borrowing rather than supplying an element is what makes a second view of
    // the scene join this one instead of asking for a second graphics context: whoever builds the
    // scene is always handed the same element, so there is never a second one to build it in.
    const releaseSurface = slotRef.current
      ? claimSceneSurface(slotRef.current, setShowingHere)
      : undefined;

    (async () => {
      // Imported here rather than at the top of the file so the engine and its runtime assets
      // are fetched by the sessions that open a 3D view and by no others.
      const { acquireScene3D } = await import('../../scene3d/scene3dContext.ts');
      if (disposed) {
        return;
      }
      session = acquireScene3D(sceneSurfaceElement());
      unsubscribeRenderError = session.engine.subscribeRenderError((message) => {
        if (!disposed) {
          setStatus('error');
          setErrorDetail(message);
        }
      });
      // The browser can take the graphics context away for reasons that have nothing to do with
      // this application — a phone backgrounded and returned to, a driver reset, memory pressure.
      // The GPU resources do not survive it, so the first answer is to build the scene again;
      // bumping the token re-runs this whole effect, which is exactly that. Where the camera was
      // looking is held as plain degrees and metres outside the scene, so the rebuilt one opens
      // where the old one was and a viewer sees a flicker rather than a loss.
      unsubscribeContextLoss = session.engine.subscribeContextLoss((state) => {
        if (disposed) {
          return;
        }
        if (state === 'recovering') {
          setRebuildToken((token) => token + 1);
        } else {
          setStatus('contextLost');
        }
      });
      engineRef.current = session.engine;
      setEngineVersion((version) => version + 1);
      setStatus('ready');
      // Arms the next recovery. Without it a scene that came back cleanly would refuse to come
      // back from a later, unrelated loss: the guard that stops a rebuild loop cannot tell a
      // repeat from a new fault until one rebuild has been seen to work.
      session.engine.reportContextRecovered();
    })().catch((error: unknown) => {
      if (disposed) {
        return;
      }
      setStatus('error');
      setErrorDetail(error instanceof Error ? error.message : String(error));
    });

    return () => {
      disposed = true;
      unsubscribeRenderError?.();
      unsubscribeContextLoss?.();
      engineRef.current = null;
      session?.release();
      session = null;
      releaseSurface?.();
    };
  }, [webGl2, rebuildToken]);

  const { data: layers } = useMapLayers();
  const [activeBaseId, setActiveBaseId] = useState<number>();

  // What each kind of surface feature is called, which is how a feature drawn here gets named in
  // a tooltip. The flat map fills the same catalog when it opens; this view has to fill it as
  // well, because a session that goes straight to the 3D route never opens the flat map and would
  // otherwise show every feature as unnamed.
  const { data: featureTypes } = useFeatureTypes();
  useEffect(() => {
    if (featureTypes) {
      setFeatureTypeCatalog(featureTypes);
    }
  }, [featureTypes]);

  useEffect(() => {
    if (layers && activeBaseId === undefined) {
      const initial = layers.find((l) => l.isDefault && l.isBase) ?? layers.find((l) => l.isBase);
      if (initial) {
        setActiveBaseId(Number(initial.id));
      }
    }
  }, [layers, activeBaseId]);

  useEffect(() => {
    const engine = engineRef.current;
    if (!engine || !layers || activeBaseId === undefined) {
      return;
    }
    syncBaseImagery(engine, layers, activeBaseId);
    // Nothing moved the camera, and the scene only draws when asked to.
    engine.requestRender();
  }, [layers, activeBaseId, engineVersion]);

  // ---- catalogue overlays and this installation's own georeferenced maps ----

  const scene3dCoupledToMap = useWorkspaceStore((s) => s.scene3dCoupledToMap);
  const setScene3dCoupledToMap = useWorkspaceStore((s) => s.setScene3dCoupledToMap);
  const visibleTileOverlayIds = useWorkspaceStore((s) => s.visibleTileOverlayIds);
  const setTileOverlayVisible = useWorkspaceStore((s) => s.setTileOverlayVisible);
  const tileOverlayOpacity = useWorkspaceStore((s) => s.tileOverlayOpacity);
  const setTileOverlayOpacity = useWorkspaceStore((s) => s.setTileOverlayOpacity);
  const visibleRasterIds = useWorkspaceStore((s) => s.visibleRasterIds);
  const setRasterVisible = useWorkspaceStore((s) => s.setRasterVisible);
  const rasterOpacity = useWorkspaceStore((s) => s.rasterOpacity);
  // The same page size the flat map asks for, so the two views offer the same list.
  const { data: rasterPage } = useRasterMaps({ pageSize: 100 });
  const rasters = useMemo(() => rasterPage?.items ?? [], [rasterPage]);

  // After the basemap effect above and never before it, because a globe composites its imagery in
  // the order the layers were added: an overlay created first would be drawn under an opaque
  // picture of the ground, which nothing reports and nothing on screen explains.
  useEffect(() => {
    const engine = engineRef.current;
    if (!engine || !layers) {
      return;
    }
    syncTileOverlayImagery(engine, layers, new Set(visibleTileOverlayIds), tileOverlayOpacity);
    engine.requestRender();
  }, [layers, visibleTileOverlayIds, tileOverlayOpacity, engineVersion, activeBaseId]);

  useEffect(() => {
    const engine = engineRef.current;
    if (!engine) {
      return;
    }
    // Flattening a raster takes seconds and touches a second drawing context, so this is the one
    // imagery effect that is asynchronous. Nothing is awaited by the caller: the scene is usable
    // throughout and each sheet appears when it is ready.
    void syncRasterImagery(engine, rasters, new Set(visibleRasterIds), rasterOpacity).then(() =>
      engineRef.current?.requestRender(),
    );
  }, [rasters, visibleRasterIds, rasterOpacity, engineVersion]);

  // ---- what a viewer can turn off, fade and cut into ----

  const baseOpacity = useWorkspaceStore((s) => s.baseOpacity);
  const setBaseOpacity = useWorkspaceStore((s) => s.setBaseOpacity);
  const overlayVisible = useWorkspaceStore((s) => s.overlayVisible);
  const setOverlayVisible = useWorkspaceStore((s) => s.setOverlayVisible);
  const overlayOpacity = useWorkspaceStore((s) => s.overlayOpacity);
  const setOverlayOpacity = useWorkspaceStore((s) => s.setOverlayOpacity);
  const surfaceMode = useWorkspaceStore((s) => s.scene3dSurfaceMode);
  const setSurfaceMode = useWorkspaceStore((s) => s.setScene3dSurfaceMode);
  const [surfaceState, setSurfaceState] = useState<Scene3DSurfaceState>();

  useEffect(() => {
    const engine = engineRef.current;
    if (!engine || !layers) {
      return;
    }
    // Every base keeps its own value and only one of them is visible at a time, so this is
    // applied to all of them rather than to the active one: switching basemap then shows that
    // layer at the transparency it was left at.
    for (const layer of layers) {
      if (layer.isBase) {
        const id = Number(layer.id);
        engine.setImageryLayerOpacity(baseImageryLayerId(id), baseOpacity[id] ?? 1);
      }
    }
    engine.requestRender();
  }, [layers, baseOpacity, activeBaseId, engineVersion]);

  // ---- cave data, picking and selection ----

  const dataRef = useRef<CaveData3DHandle | null>(null);
  // Where two caves come closest, if a pair has been measured. Held apart from the loader above
  // for the same reason the walls are: nothing about it is camera-driven — it is drawn because
  // somebody asked a question on a cave's page, and it stays until they ask a different one.
  const approachRef = useRef<ClosestApproach3DHandle | null>(null);
  // The place on a passage a reading from the overburden curve came from. Held apart for the same
  // reason: it is drawn because somebody pressed a point on a chart on a cave's page, and it stays
  // until they press a different one.
  const overburdenRef = useRef<OverburdenHighlight3DHandle | null>(null);
  const [dataState, setDataState] = useState<CaveData3DState>(EMPTY_CAVE_DATA_3D_STATE);
  const [caveFramable, setCaveFramable] = useState(false);

  // The walls of one cave, held apart from the camera-driven loader above because nothing about
  // them is camera-driven: they are read because a viewer picked a cave, and released again the
  // moment the pick moves on.
  const meshRef = useRef<SurveyMesh3DHandle | null>(null);
  const [meshState, setMeshState] = useState<SurveyMesh3DState>(EMPTY_SURVEY_MESH_3D_STATE);

  // The view exchange, held in a ref for the same reason as the handles above: the effect that
  // owns it must not re-run when coupling is switched, so the switch reaches it from outside.
  const syncRef = useRef<ViewSync3dHandle | null>(null);

  // What the camera is doing, read back from the scene so the preset buttons describe the camera
  // rather than the last button pressed. Undefined until the scene exists.
  const [camera, setCamera] = useState<Camera3DState>();

  useEffect(() => {
    const engine = engineRef.current;
    // Only the mount actually showing the scene drives it. Two mounts share one scene, and if both
    // loaded data into it they would build two batches under each source id and take each other's
    // out of the scene; two of them registering as "the view on screen" would likewise leave the
    // detail panel's buttons moving a scene nobody is looking at. The displaced mount is inert
    // until it gets the surface back.
    if (!engine || !showingHere) {
      return;
    }
    // The element the scene draws into, captured now: it is the one this subscription belongs to,
    // and the cleanup has to reset the cursor on that element rather than on whatever is current
    // by the time it runs.
    const surface = sceneSurfaceElement();
    const data = attachCaveData3d(engine);
    dataRef.current = data;
    const mesh = attachSurveyMesh3d(engine);
    meshRef.current = mesh;
    // The tops are read through the loader rather than copied out of it: they change with every
    // load, and a line hung from the tops of a moment ago would drift away from the surveys it
    // joins.
    const approach = attachClosestApproach3d(engine, (caveId) => data.caveSurveyTop(caveId));
    approachRef.current = approach;
    const overburden = attachOverburdenHighlight3d(engine, (caveId) => data.caveSurveyTop(caveId));
    overburdenRef.current = overburden;
    const unsubscribeMesh = mesh.subscribe(setMeshState);
    const unsubscribeState = data.subscribe((next) => {
      setDataState(next);
      // A load is where the survey tops are learned, and both ends of the measured line hang from
      // them. Redrawing here is what keeps the line joined to the surveys rather than floating.
      approach.refresh();
      // The mark on a passage hangs from the same tops for the same reason, and drifts off the
      // passage it belongs to if it is not redrawn with them.
      overburden.refresh();
      // Whether there is a cave to frame changes with every load, and only the loader knows.
      setCaveFramable(data.caveBounds() !== undefined);
      // The callout holds what it was handed when the thing was clicked, and a load replaces every
      // one of those with a freshly composed name at wherever the thing now is. Without this, a
      // feature renamed or moved from the panel beside the scene leaves the callout stating the
      // old name at the old place while the panel next to it states the new one. Kept rather than
      // dropped when the thing is no longer among what is drawn: that is what the camera moving
      // away from it looks like, and it is not a reason to take a label down.
      setPicked((current) => (current ? (data.currentPick(current) ?? current) : current));
    });

    // Both views keep each other in step over the workspace bus, in references and degrees rather
    // than in cameras — the flat map must never be handed anything from a 3D engine, and this
    // scene must never be handed an OpenLayers view.
    const sync = attachViewSync3d(
      engine,
      {
        current: () => useWorkspaceStore.getState().selection,
        set: (selection) => useWorkspaceStore.getState().setSelection(selection),
      },
      // Read from the store at the moment of each message rather than captured, so that switching
      // coupling never appears in this effect's dependencies. A re-run here detaches the wall mesh
      // and reloads the cave — tens of megabytes of graphics memory released and fetched again —
      // which is far too much to spend on a button press, and would look like the scene breaking.
      { followsExtent: () => useWorkspaceStore.getState().scene3dCoupledToMap },
    );
    syncRef.current = sync;

    // Selection is written straight into the workspace store, which is where the flat map writes
    // it too: both views describe what was picked as bare references, so the detail panel does not
    // know or care which one the viewer clicked in. It is announced as well as stored, because a
    // popped-out window has a store of its own that this one cannot reach.
    const unsubscribeClick = engine.onClick((pick) => {
      const selection = selectionFromPick(pick);
      useWorkspaceStore.getState().setSelection(selection);
      sync.publishSelection(selection);
      // Kept alongside the selection rather than derived from it: the callout names and points at
      // the thing that was clicked, and the store holds a bare reference with no name and no
      // position in it. A click on empty ground takes the callout down, which is what clicking
      // away means everywhere else.
      setPicked(pickPayload(pick));
    });

    // Hover changes the cursor and names what is under the pointer. The scene throttles the hit
    // test to one per drawn frame and answers nothing at all on a touch device, so this costs one
    // shallow state write per frame while the pointer is over something and nothing otherwise.
    const unsubscribeHover = engine.onHover((pick) => {
      const payload = pickPayload(pick);
      surface.style.cursor = payload ? 'pointer' : '';
      setHovered(payload && pick?.screen ? { payload, screen: pick.screen } : undefined);
    });

    // Editing or deleting a feature invalidates what is drawn here. The write happens in the
    // detail panel, which draws nothing itself and must not know which views exist, so it
    // announces and this view refetches the ground it is showing — otherwise a deleted marker
    // stays on screen, and stays clickable, until the camera next moves.
    const unsubscribeChanges = onSurfaceFeaturesChanged(() => data.reload());

    // What each kind of feature is called is composed into the drawn items as they are built, so
    // features drawn before the catalog answered carry no type name at all — an unnamed sinkhole
    // ends up called "surface features" in every label about it, and stays that way until the
    // camera happens to move. The catalog is a separate request that can answer after the features
    // do, so this reloads them when it lands. The flat map has no equivalent because it composes
    // the same label at the moment the pointer stops on something, by which time the catalog is
    // there; here the label has to be attached to the item, because a hit test runs once per drawn
    // frame and cannot go looking things up.
    const unsubscribeCatalog = onFeatureTypeCatalogChanged(() => data.reload());

    // The same panel's "zoom to" buttons: while this view is on screen, they move this camera.
    // The two camera members are what lets a saved view remember, and restore, a place underground
    // — the flat map leaves them off, because it has no direction or tilt to save.
    const detachCamera = setActiveViewCamera({
      flyTo: (longitude, latitude, zoom) =>
        engine.flyToZoom(longitude, latitude, zoom, { animate: true }),
      fitGeometry: (geometry) => {
        const bounds = geoJsonBounds(geometry);
        if (bounds) {
          engine.fitBounds(bounds, { animate: true });
        }
      },
      getCamera3D: () => readCamera3D(engine),
      setCamera3D: (state) => {
        // The document being opened says where this camera stands and where the flat map stands,
        // and each is put back by the code reading it — so this move is not the viewer looking
        // somewhere new, and announcing it would send the map off to frame this camera's box
        // instead of the position the same document just gave it.
        sync.muteUntilSettled();
        // Not animated: restoring a saved view is a jump to somewhere else, and an orthographic
        // view restored mid-flight would be sized from where the flight started.
        applyCamera3D(engine, state);
        setCamera(readCamera3D(engine));
      },
    });

    const refreshCamera = () => setCamera(readCamera3D(engine));
    const unsubscribeView = engine.onViewChanged(refreshCamera);
    refreshCamera();

    const detachHash = syncUrlHash ? attachScene3dHash(engine) : undefined;

    return () => {
      detachHash?.();
      unsubscribeView();
      detachCamera();
      unsubscribeCatalog();
      unsubscribeChanges();
      unsubscribeClick();
      unsubscribeHover();
      sync.detach();
      syncRef.current = null;
      unsubscribeState();
      unsubscribeMesh();
      approach.detach();
      approachRef.current = null;
      overburden.detach();
      overburdenRef.current = null;
      data.detach();
      dataRef.current = null;
      // Releases the graphics memory the walls hold; a scene handed back with a mesh still on it
      // would keep tens of megabytes for a view nobody is looking at.
      mesh.detach();
      meshRef.current = null;
      setMeshState(EMPTY_SURVEY_MESH_3D_STATE);
      setDataState(EMPTY_CAVE_DATA_3D_STATE);
      setCaveFramable(false);
      setCamera(undefined);
      setHovered(undefined);
      setPicked(undefined);
      surface.style.cursor = '';
    };
  }, [engineVersion, syncUrlHash, showingHere]);

  // ---- what the chrome over the scene names ----

  /** What the pointer rests on and where it is; both change together or not at all. */
  const [hovered, setHovered] = useState<{
    payload: Scene3DPickPayload;
    screen: Scene3DScreenPosition;
  }>();
  /** What was picked in this scene, until it is dismissed or the selection moves on. */
  const [picked, setPicked] = useState<Scene3DPickPayload>();
  const selection = useWorkspaceStore((s) => s.selection);

  useEffect(() => {
    // The callout comes down when the selection is no longer what it is about — which covers
    // selecting something on the flat map, in a table, or in another window, none of which this
    // view hears about any other way. Dismissing it by hand does not clear the selection: the
    // detail panel beside the scene is still showing the thing, and closing a label is not
    // deselecting.
    setPicked((current) => (pickMatchesSelection(current, selection) ? current : undefined));
  }, [selection]);

  // Both read the scene through the ref rather than closing over it, so they never go stale and
  // never change identity. What has to notice a scene being rebuilt is the *subscription*, and
  // that is handled by remounting the overlay on the counter below rather than by rebuilding
  // these — a subscription taken on a scene that no longer exists would otherwise be held for
  // ever and no frame of the new one would reach the overlay.
  const project = useCallback((position: Scene3DAnchor) => {
    const engine = engineRef.current;
    if (!engine) {
      return undefined;
    }
    // An anchor for something drawn on the ground has no height of its own — nothing that built
    // it could know one — so the scene is asked where the ground is now. Asked on every frame
    // rather than once, because "now" changes: elevation tiles refine after the camera arrives,
    // and an anchor resolved against the coarse surface would sit hundreds of metres off the one
    // that is finally drawn.
    const resolved = position.onGround
      ? { ...position, height: engine.groundHeight(position.longitude, position.latitude) }
      : position;
    return engine.positionToScreen(resolved);
  }, []);

  const subscribeFrames = useCallback(
    (listener: () => void) => engineRef.current?.onBeforeRender(listener) ?? (() => {}),
    [],
  );

  const dismissPicked = useCallback(() => setPicked(undefined), []);

  // ---- keeping the chrome out of the chrome's way ----

  const layerControlsRef = useRef<HTMLDivElement>(null);
  const cameraControlsRef = useRef<HTMLDivElement>(null);

  /**
   * The patch of the view the scene's own controls stand in, so a callout pinned to a cave near
   * that corner is put somewhere else rather than over the buttons.
   *
   * Measured rather than written down: the two groups change size and corner between a mouse and a
   * finger, and a copy of those numbers here would go stale the first time either stylesheet moved.
   * Both are positioned against the same box the overlay covers, so what they report is already in
   * the coordinates the placement works in.
   */
  const controlsArea = useCallback(
    () => boundingRectOf([layerControlsRef.current, cameraControlsRef.current]),
    [],
  );

  const applyPreset = useCallback((preset: Camera3DPreset) => {
    const engine = engineRef.current;
    if (!engine) {
      return;
    }
    // Read, turn, write: the preset is computed from where the camera already is, so it changes
    // the direction the cave is seen from and leaves the viewer where they were standing.
    engine.setCamera(toSceneCamera(presetCamera(preset, readCamera3D(engine))), { animate: true });
    setCamera(readCamera3D(engine));
  }, []);

  /**
   * Switching whether this scene moves with the flat map.
   *
   * Re-coupling brings the SCENE to where the map is, not the other way about. The two have been
   * moving independently and now disagree, and they do not converge on their own — the exchange's
   * existing rule decides it: whoever was already there answers, whoever has just arrived listens,
   * and a camera that went off underground on its own is the one arriving. The store is written
   * first so that the rejoin, which asks the store whether it is allowed to follow, finds the
   * answer it needs.
   */
  const changeCoupling = useCallback(
    (next: boolean) => {
      setScene3dCoupledToMap(next);
      if (next) {
        syncRef.current?.rejoin();
      }
    },
    [setScene3dCoupledToMap],
  );

  const changeProjection = useCallback((projection: Scene3DProjection) => {
    const engine = engineRef.current;
    if (!engine) {
      return;
    }
    engine.setProjection(projection);
    setCamera(readCamera3D(engine));
  }, []);

  const fitCave = useCallback(() => {
    const engine = engineRef.current;
    const bounds = dataRef.current?.caveBounds();
    if (engine && bounds) {
      engine.fitBounds(bounds, { animate: true });
      setCamera(readCamera3D(engine));
    }
  }, []);

  const { data: mapConfig } = useMapConfig();

  useEffect(() => {
    if (mapConfig) {
      dataRef.current?.setLimits(limitsFromMapConfig(mapConfig));
    }
  }, [mapConfig, engineVersion]);

  // ---- the ground's elevation ----

  /** Why the configured elevation model is not being drawn, when one is configured and is not. */
  /**
   * What is wrong with the elevation source this installation named, and whether a second source
   * was drawn in its place. Two facts rather than one because they answer different people: the
   * problem is for whoever repairs the configuration, the fallback is for whoever is looking at
   * the screen and needs to know whether the ground under the caves is real.
   */
  const [terrainProblem, setTerrainProblem] = useState<{
    problem: TerrainSourceProblem;
    fellBack: boolean;
  }>();
  /**
   * Where surveyed altitudes go. It follows what is actually drawn rather than what is configured:
   * a cave placed at its real altitude over a globe that turned out to have no relief on it would
   * hang a kilometre above the surface, which is the failure the anchoring exists to prevent.
   */
  const [placement, setPlacement] = useState<Altitude3DPlacement>(ANCHORED_TO_SURFACE);

  const terrain = mapConfig?.terrain;
  const terrainFallback = mapConfig?.terrainFallback;

  useEffect(() => {
    const engine = engineRef.current;
    if (!engine) {
      return;
    }
    let cancelled = false;
    setTerrainProblem(undefined);

    if (!terrain) {
      void engine.setTerrainSource(undefined);
      setPlacement(ANCHORED_TO_SURFACE);
      return;
    }

    // Refusing an elevation model has to take any model already attached down with it, not merely
    // decline to attach this one. The two states this component holds are a description of what is
    // actually drawn: saying "the ground is a smooth globe" and placing every cave on a smooth
    // globe while the globe still has a hillside on it is the one combination that is wrong in
    // both directions at once — the caves end up a hillside below their own entrance markers, and
    // the sentence on screen sends the operator to look at the wrong thing entirely.
    const refuse = (problem: TerrainSourceProblem) => {
      void engine.setTerrainSource(undefined);
      setTerrainProblem({ problem, fellBack: false });
      setPlacement(ANCHORED_TO_SURFACE);
    };

    // In preference order: what this installation named, then whatever whole pyramid it has of
    // its own. The second is almost always absent — it exists only when configuration named an
    // address AND a checked build sits at a different one, which is the shape of a stale
    // configuration line outliving the setup it described.
    const candidates = [terrain, ...(terrainFallback ? [terrainFallback] : [])];

    void (async () => {
      // Looked at before the engine is handed it, and this order is the whole point. A pyramid
      // served in a form the engine cannot parse produces a globe with no ground, every tile
      // answering 200 and not one error anywhere — so the only way anybody finds out is to check
      // first and refuse. Refusing leaves the ordinary smooth globe, which works.
      //
      // Every candidate is checked the same way, and the verdict reported is the FIRST one's.
      // That is deliberate: falling back keeps the scene usable, but the thing an operator has to
      // repair is the source they configured, and reporting the fallback's verdict — or none at
      // all, because the fallback worked — is how a broken configuration line survives for months.
      let firstProblem: TerrainSourceProblem | undefined;
      for (const candidate of candidates) {
        const problem = await checkTerrainSource(candidate.url);
        if (cancelled) {
          return;
        }
        if (problem) {
          firstProblem ??= problem;
          continue;
        }
        try {
          await engine.setTerrainSource({
            url: candidate.url,
            ...(candidate.attribution ? { attribution: candidate.attribution } : {}),
          });
        } catch {
          if (cancelled) {
            return;
          }
          firstProblem ??= 'unreachable';
          continue;
        }
        if (!cancelled) {
          // Only now, and only because the ground is really there: the correction comes from the
          // server, which resolved it from what THIS source says its heights mean. Taking it from
          // the configured source while drawing the fallback's ground is the forty-metre error
          // this whole chain of offsets exists to prevent.
          setPlacement({ absolute: true, offsetM: candidate.surveyHeightOffsetM });
          // A source that was fallen back to is still a source that failed. The notice stays up,
          // naming what went wrong with the one the operator has to fix.
          setTerrainProblem(firstProblem ? { problem: firstProblem, fellBack: true } : undefined);
        }
        return;
      }
      if (!cancelled && firstProblem) {
        refuse(firstProblem);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [terrain, terrainFallback, engineVersion]);

  // Applied through its own effect rather than from the one above, so that a scene rebuilt — or a
  // second view taking the surface over — comes back with the caves where the ground is instead of
  // where the ellipsoid is.
  useEffect(() => {
    dataRef.current?.setAltitudePlacement(placement);
    // The walls follow the same rule as the lines drawn inside them, and follow it at the same
    // moment: a mesh left at the height the bare ellipsoid put it, under ground that has since
    // gained relief, would be buried under its own hillside. Moving it is arithmetic on its
    // anchor, so nothing is fetched again.
    meshRef.current?.setAltitudePlacement(placement);
    // And the measured line, whose two ends hang from the tops of two different caves under the
    // one rule: left behind, it would join two surveys that had both moved out from under it.
    approachRef.current?.setAltitudePlacement(placement);
    // And the mark on a passage, which hangs from one cave's top under the same rule.
    overburdenRef.current?.setAltitudePlacement(placement);
  }, [placement, engineVersion, showingHere]);

  // Which cave's walls are held. The selection names a cave both when a cave was picked and when
  // one of its entrances was; anything else selects no cave and releases what was held.
  //
  // The top of the cave's survey is passed with it because that is what an anchored rendering
  // hangs a cave from, and the walls and the survey lines of one cave have to hang from the same
  // one. It comes from the camera-driven loader, which is the only thing here that is told it, so
  // it can arrive after the mesh is already drawn — which moves the mesh rather than reloading it.
  const selectedCaveId =
    selection?.kind === 'cave' || selection?.kind === 'entrance' ? selection.caveId : undefined;
  const meshVisible = overlayVisible[SURVEY_MESH_LAYER_ID] ?? true;
  useEffect(() => {
    const mesh = meshRef.current;
    if (!mesh) {
      return;
    }
    mesh.setVisible(meshVisible);
    mesh.setCave(
      selectedCaveId,
      selectedCaveId ? dataRef.current?.caveSurveyTop(selectedCaveId) : undefined,
    );
  }, [selectedCaveId, meshVisible, dataState, engineVersion, showingHere]);

  // Declared after the loader is attached, and keyed on the same counter, so a scene that is
  // rebuilt comes back with the settings the viewer had rather than with the defaults.
  useEffect(() => {
    const data = dataRef.current;
    if (!data) {
      return;
    }
    for (const layer of CAVE_DATA_3D_LAYERS) {
      data.setLayerVisible(layer, overlayVisible[layer] ?? true);
      data.setLayerOpacity(layer, overlayOpacity[layer] ?? 1);
    }
  }, [overlayVisible, overlayOpacity, engineVersion]);

  useEffect(() => {
    const engine = engineRef.current;
    if (!engine) {
      return;
    }
    engine.setSurfaceMode(surfaceMode);
    // The scene decides for itself whether the cutaway is worth drawing from where the camera is
    // standing, and changes its mind as the camera moves, so what it is doing is subscribed to
    // rather than assumed from what was asked for.
    setSurfaceState(engine.getSurfaceState());
    return engine.onSurfaceStateChanged(setSurfaceState);
  }, [surfaceMode, engineVersion]);

  if (!webGl2) {
    return (
      <div className="scene3d-wrap" style={{ height }} data-testid="scene3d-unsupported">
        <Result
          status="warning"
          title={t('scene3d.unsupportedTitle')}
          subTitle={t('scene3d.unsupportedHint')}
          extra={<Link to="/map">{t('scene3d.openFlatMap')}</Link>}
        />
      </div>
    );
  }

  // Two things the viewer would otherwise have to guess at: caves the server refused to send at
  // this zoom, and caves it could only send as flat outlines. Both are ordinary answers rather
  // than failures, so they are stated quietly rather than raised as errors.
  // A cutaway the camera has taken away is the third: the viewer asked for it, the control still
  // says so, and without a word here the scene would simply look as if the setting had been
  // ignored. Which word depends on why, because the way out of one reason is not the way out of
  // the other. The panel explains the reasons a cutaway was never drawn at all.

  // The walls of the selected cave are the fifth, and the only one that is about waiting. They are
  // the one thing this scene draws that a viewer waits for, and the wait belongs where it can be
  // seen without being looked for: the full status line lives in the layer panel, but that panel
  // opens on a click, so a viewer who picks a cave with a fifty-megabyte mesh would otherwise
  // watch an unchanged scene for several seconds with nothing to say why — and a mesh that failed
  // to arrive would look exactly like a cave nobody has surveyed walls for. Only the two states
  // worth interrupting a viewer for are said out here; the panel says the rest.
  const meshNotice = () => {
    const triangles = meshState.triangleCount?.toLocaleString(i18n.language);
    switch (meshState.status) {
      case 'looking':
        return t('scene3d.meshLooking');
      case 'loading':
        return triangles ? t('scene3d.meshLoadingSized', { triangles }) : t('scene3d.meshLoading');
      case 'failed':
        return meshState.message
          ? t('scene3d.meshFailedBecause', { reason: meshState.message })
          : t('scene3d.meshFailed');
      default:
        return undefined;
    }
  };
  /** True while the walls are still on their way, which is a wait like any other data wait. */
  const meshLoading = meshState.status === 'looking' || meshState.status === 'loading';

  const notices = [
    meshNotice(),
    dataState.withheldCount > 0 ? t('map.centerlinesWithheld', { count: dataState.withheldCount }) : undefined,
    dataState.flatCount > 0 ? t('scene3d.centerlinesFlat', { count: dataState.flatCount }) : undefined,
    surfaceState?.pausedBy ? t(cutawayPauseMessage(surfaceState.pausedBy)) : undefined,
    // The fourth is not about the data at all: this installation was configured with an elevation
    // model that cannot be drawn, and the globe a viewer is looking at is the smooth one. Said
    // here because there is nowhere else it would ever show up.
    terrainProblem
      ? t(terrainProblemMessage(terrainProblem.problem, terrainProblem.fellBack))
      : undefined,
  ].filter((notice): notice is string => notice !== undefined);

  return (
    <div className="scene3d-wrap" style={{ height }}>
      {status === 'loading' && (
        <Spin style={{ position: 'absolute', inset: 0, marginTop: 48 }} data-testid="scene3d-loading" />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon title={t('scene3d.startFailed')} description={errorDetail} />
      )}
      {/* The scene has already been rebuilt once for this and lost the context again, so the view
          stops rather than flickering: a rebuild loop looks like a frozen application and empties
          a phone's battery. Reloading is offered as a control because the alternative is a viewer
          working out for themselves that a page which otherwise looks alive needs it. */}
      {status === 'contextLost' && (
        <Alert
          type="warning"
          showIcon
          data-testid="scene3d-context-lost"
          title={t('scene3d.contextLostTitle')}
          description={t('scene3d.contextLostHint')}
          action={
            <Button size="small" onClick={() => window.location.reload()}>
              {t('scene3d.contextLostReload')}
            </Button>
          }
        />
      )}
      {/* The window's drawing surface is moved into this element while this view is showing the
          scene; React never touches what is inside it. */}
      <div
        ref={slotRef}
        className="scene3d-canvas"
        data-testid="scene3d-container"
        style={{ display: status === 'error' || status === 'contextLost' ? 'none' : undefined }}
      />
      {!showingHere && status !== 'error' && (
        <div className="scene3d-elsewhere" data-testid="scene3d-elsewhere">
          <Typography.Text type="secondary">{t('scene3d.showingElsewhere')}</Typography.Text>
        </div>
      )}
      {/* Over the scene, under nothing: what the pointer is on and what was picked, drawn as
          elements rather than into the canvas so they are translated, themed and readable. */}
      {status === 'ready' && showingHere && (
        <Scene3DOverlay
          key={engineVersion}
          hovered={hovered?.payload}
          hoveredAt={hovered?.screen}
          selected={picked}
          project={project}
          subscribeFrames={subscribeFrames}
          onDismissSelection={dismissPicked}
          controlsArea={controlsArea}
        />
      )}
      {status === 'ready' && showingHere && camera && (
        <Scene3DCameraControls
          containerRef={cameraControlsRef}
          activePreset={activePreset(camera)}
          onPreset={applyPreset}
          projection={camera.projection}
          onProjectionChange={changeProjection}
          onFitCave={fitCave}
          fitDisabled={!caveFramable}
          coupled={scene3dCoupledToMap}
          onCoupledChange={changeCoupling}
        />
      )}
      {/* Gated on the scene alone. Only the basemap section of the panel is about the layer
          catalog; the layer switches, the fades and the surface mode are about the scene, and an
          installation whose catalog is unreadable — or merely slow — must not lose the controls
          this view is driven by along with it. */}
      {status === 'ready' && showingHere && (
        <div className="scene3d-controls" ref={layerControlsRef}>
          <Popover
            trigger="click"
            placement="bottomRight"
            title={t('map.layersTitle')}
            content={
              <Scene3DLayerPanel
                layers={layers ?? []}
                activeBaseId={activeBaseId}
                onBaseChange={setActiveBaseId}
                baseOpacity={baseOpacity}
                onBaseOpacityChange={setBaseOpacity}
                visibleTileOverlayIds={visibleTileOverlayIds}
                onTileOverlayVisibleChange={setTileOverlayVisible}
                tileOverlayOpacity={tileOverlayOpacity}
                onTileOverlayOpacityChange={setTileOverlayOpacity}
                rasters={rasters}
                visibleRasterIds={visibleRasterIds}
                onRasterVisibleChange={setRasterVisible}
                overlayVisible={overlayVisible}
                onOverlayVisibleChange={setOverlayVisible}
                overlayOpacity={overlayOpacity}
                onOverlayOpacityChange={setOverlayOpacity}
                meshVisible={meshVisible}
                onMeshVisibleChange={(visible) => setOverlayVisible(SURVEY_MESH_LAYER_ID, visible)}
                meshState={meshState}
                surfaceMode={surfaceMode}
                onSurfaceModeChange={setSurfaceMode}
                surfaceState={surfaceState}
              />
            }
          >
            <Button
              size="small"
              icon={<LayoutOutlined />}
              aria-label={t('map.layersTitle')}
              data-testid="scene3d-layers-trigger"
            />
          </Popover>
        </div>
      )}
      {/* Settles to "idle" once every request of a load has answered, which is the only signal
          from outside that the view has finished filling itself in. */}
      <div
        className="scene3d-data-state"
        data-testid="scene3d-data"
        data-loading={dataState.loading || meshLoading ? 'true' : 'false'}
      >
        {notices.map((notice) => (
          <span key={notice} className="scene3d-notice">
            {notice}
          </span>
        ))}
      </div>
    </div>
  );
}

/**
 * The smallest box holding all of these elements, in the coordinates of the box they are
 * positioned against, or nothing when none of them is on screen.
 *
 * Offsets rather than a client rectangle: everything here is positioned against the same element
 * the overlay covers, so its offsets are already measured from the corner the overlay measures
 * from, and reading them costs no extra conversion and no knowledge of where the page has been
 * scrolled to.
 */
function boundingRectOf(elements: readonly (HTMLElement | null)[]): OverlayRect | undefined {
  let left = Number.POSITIVE_INFINITY;
  let top = Number.POSITIVE_INFINITY;
  let right = Number.NEGATIVE_INFINITY;
  let bottom = Number.NEGATIVE_INFINITY;
  for (const element of elements) {
    if (!element || element.offsetWidth === 0 || element.offsetHeight === 0) {
      continue;
    }
    left = Math.min(left, element.offsetLeft);
    top = Math.min(top, element.offsetTop);
    right = Math.max(right, element.offsetLeft + element.offsetWidth);
    bottom = Math.max(bottom, element.offsetTop + element.offsetHeight);
  }
  return Number.isFinite(left)
    ? { leftPixels: left, topPixels: top, widthPixels: right - left, heightPixels: bottom - top }
    : undefined;
}
