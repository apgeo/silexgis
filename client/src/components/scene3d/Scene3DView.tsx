// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import { LayoutOutlined } from '@ant-design/icons';
import { Alert, Button, Popover, Result, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useMapConfig, useMapLayers } from '../../api/hooks.ts';
import { baseImageryLayerId, syncBaseImagery } from '../../scene3d/baseImagery3d.ts';
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
import { geoJsonBounds } from '../../scene3d/geoJson3d.ts';
import { activePreset, presetCamera, type Camera3DPreset } from '../../scene3d/presets3d.ts';
import { claimSceneSurface, sceneSurfaceElement } from '../../scene3d/sceneSurface.ts';
import { pickPayload, selectionFromPick } from '../../scene3d/selection3d.ts';
import type { Scene3DCore, Scene3DProjection, Scene3DSurfaceState } from '../../scene3d/scene3dEngine.ts';
import type { Scene3DSession } from '../../scene3d/scene3dContext.ts';
import { attachScene3dHash } from '../../scene3d/urlHash3d.ts';
import { attachViewSync3d } from '../../scene3d/viewSync3d.ts';
import { supportsWebGl2 } from '../../scene3d/webglSupport.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { onSurfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera } from '../../workspace/viewCamera.ts';
import Scene3DCameraControls from './Scene3DCameraControls.tsx';
import Scene3DLayerPanel from './Scene3DLayerPanel.tsx';
import { cutawayPauseMessage } from './surfaceMessages.ts';
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
  const { t } = useTranslation();
  const slotRef = useRef<HTMLDivElement>(null);

  // Answered once, before anything is downloaded: the engine chunk is a megabyte that a browser
  // which cannot draw the scene should never be asked to fetch.
  const [webGl2] = useState(supportsWebGl2);

  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();
  // False while another mount in this window is showing the one scene there is.
  const [showingHere, setShowingHere] = useState(true);

  // The scene arrives asynchronously and is not React state; the counter is what tells the
  // effects below that the handle in the ref has changed.
  const engineRef = useRef<Scene3DCore | null>(null);
  const [engineVersion, setEngineVersion] = useState(0);

  useEffect(() => {
    if (!webGl2) {
      return;
    }
    let disposed = false;
    let session: Scene3DSession | null = null;
    let unsubscribeRenderError: (() => void) | undefined;
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
      engineRef.current = session.engine;
      setEngineVersion((version) => version + 1);
      setStatus('ready');
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
      engineRef.current = null;
      session?.release();
      session = null;
      releaseSurface?.();
    };
  }, [webGl2]);

  const { data: layers } = useMapLayers();
  const [activeBaseId, setActiveBaseId] = useState<number>();

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
  const [dataState, setDataState] = useState<CaveData3DState>(EMPTY_CAVE_DATA_3D_STATE);
  const [caveFramable, setCaveFramable] = useState(false);

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
    const unsubscribeState = data.subscribe((next) => {
      setDataState(next);
      // Whether there is a cave to frame changes with every load, and only the loader knows.
      setCaveFramable(data.caveBounds() !== undefined);
    });

    // Both views keep each other in step over the workspace bus, in references and degrees rather
    // than in cameras — the flat map must never be handed anything from a 3D engine, and this
    // scene must never be handed an OpenLayers view.
    const sync = attachViewSync3d(engine, {
      current: () => useWorkspaceStore.getState().selection,
      set: (selection) => useWorkspaceStore.getState().setSelection(selection),
    });

    // Selection is written straight into the workspace store, which is where the flat map writes
    // it too: both views describe what was picked as bare references, so the detail panel does not
    // know or care which one the viewer clicked in. It is announced as well as stored, because a
    // popped-out window has a store of its own that this one cannot reach.
    const unsubscribeClick = engine.onClick((pick) => {
      const selection = selectionFromPick(pick);
      useWorkspaceStore.getState().setSelection(selection);
      sync.publishSelection(selection);
    });

    // Hover only changes the cursor here. The scene throttles the hit test to one per drawn frame,
    // so this costs a pointer-shaped answer per frame and nothing else.
    const unsubscribeHover = engine.onHover((pick) => {
      surface.style.cursor = pickPayload(pick) ? 'pointer' : '';
    });

    // Editing or deleting a feature invalidates what is drawn here. The write happens in the
    // detail panel, which draws nothing itself and must not know which views exist, so it
    // announces and this view refetches the ground it is showing — otherwise a deleted marker
    // stays on screen, and stays clickable, until the camera next moves.
    const unsubscribeChanges = onSurfaceFeaturesChanged(() => data.reload());

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
      unsubscribeChanges();
      unsubscribeClick();
      unsubscribeHover();
      sync.detach();
      unsubscribeState();
      data.detach();
      dataRef.current = null;
      setDataState(EMPTY_CAVE_DATA_3D_STATE);
      setCaveFramable(false);
      setCamera(undefined);
      surface.style.cursor = '';
    };
  }, [engineVersion, syncUrlHash, showingHere]);

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
  const notices = [
    dataState.withheldCount > 0 ? t('map.centerlinesWithheld', { count: dataState.withheldCount }) : undefined,
    dataState.flatCount > 0 ? t('scene3d.centerlinesFlat', { count: dataState.flatCount }) : undefined,
    surfaceState?.pausedBy ? t(cutawayPauseMessage(surfaceState.pausedBy)) : undefined,
  ].filter((notice): notice is string => notice !== undefined);

  return (
    <div className="scene3d-wrap" style={{ height }}>
      {status === 'loading' && (
        <Spin style={{ position: 'absolute', inset: 0, marginTop: 48 }} data-testid="scene3d-loading" />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon message={t('scene3d.startFailed')} description={errorDetail} />
      )}
      {/* The window's drawing surface is moved into this element while this view is showing the
          scene; React never touches what is inside it. */}
      <div
        ref={slotRef}
        className="scene3d-canvas"
        data-testid="scene3d-container"
        style={{ display: status === 'error' ? 'none' : undefined }}
      />
      {!showingHere && status !== 'error' && (
        <div className="scene3d-elsewhere" data-testid="scene3d-elsewhere">
          <Typography.Text type="secondary">{t('scene3d.showingElsewhere')}</Typography.Text>
        </div>
      )}
      {status === 'ready' && showingHere && camera && (
        <Scene3DCameraControls
          activePreset={activePreset(camera)}
          onPreset={applyPreset}
          projection={camera.projection}
          onProjectionChange={changeProjection}
          onFitCave={fitCave}
          fitDisabled={!caveFramable}
        />
      )}
      {/* Gated on the scene alone. Only the basemap section of the panel is about the layer
          catalog; the layer switches, the fades and the surface mode are about the scene, and an
          installation whose catalog is unreadable — or merely slow — must not lose the controls
          this view is driven by along with it. */}
      {status === 'ready' && showingHere && (
        <div className="scene3d-controls">
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
                overlayVisible={overlayVisible}
                onOverlayVisibleChange={setOverlayVisible}
                overlayOpacity={overlayOpacity}
                onOverlayOpacityChange={setOverlayOpacity}
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
        data-loading={dataState.loading ? 'true' : 'false'}
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
