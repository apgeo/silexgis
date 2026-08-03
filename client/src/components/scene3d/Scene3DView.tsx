// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Result, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useMapConfig, useMapLayers } from '../../api/hooks.ts';
import { syncBaseImagery } from '../../scene3d/baseImagery3d.ts';
import {
  attachCaveData3d,
  limitsFromMapConfig,
  EMPTY_CAVE_DATA_3D_STATE,
  type CaveData3DHandle,
  type CaveData3DState,
} from '../../scene3d/caveData3d.ts';
import { geoJsonBounds } from '../../scene3d/geoJson3d.ts';
import { pickPayload, selectionFromPick } from '../../scene3d/selection3d.ts';
import type { Scene3DCore } from '../../scene3d/scene3dEngine.ts';
import type { Scene3DSession } from '../../scene3d/scene3dContext.ts';
import { supportsWebGl2 } from '../../scene3d/webglSupport.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { onSurfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera } from '../../workspace/viewCamera.ts';
import './Scene3DView.css';

export interface Scene3DViewProps {
  /** CSS height of the scene surface. */
  height?: number | string;
}

/**
 * React shell around the 3D scene: it owns the element the scene draws into and the states the
 * user can see (starting, running, unsupported, failed), and nothing else. The scene itself lives
 * outside React in its own module — a drawing surface with GPU resources behind it cannot be
 * rebuilt every render — so this component never holds engine objects in state; it holds a handle
 * and asks React to re-run the effects that talk to it.
 */
export default function Scene3DView({ height = '100%' }: Scene3DViewProps) {
  const { t } = useTranslation();
  const containerRef = useRef<HTMLDivElement>(null);

  // Answered once, before anything is downloaded: the engine chunk is a megabyte that a browser
  // which cannot draw the scene should never be asked to fetch.
  const [webGl2] = useState(supportsWebGl2);

  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();

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

    (async () => {
      // Imported here rather than at the top of the file so the engine and its runtime assets
      // are fetched by the sessions that open a 3D view and by no others.
      const { acquireScene3D } = await import('../../scene3d/scene3dContext.ts');
      if (disposed || !containerRef.current) {
        return;
      }
      session = acquireScene3D(containerRef.current);
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

  // ---- cave data, picking and selection ----

  const dataRef = useRef<CaveData3DHandle | null>(null);
  const [dataState, setDataState] = useState<CaveData3DState>(EMPTY_CAVE_DATA_3D_STATE);

  useEffect(() => {
    const engine = engineRef.current;
    if (!engine) {
      return;
    }
    // The element the scene draws into, captured now: it is the one this subscription belongs to,
    // and the cleanup has to reset the cursor on that element rather than on whatever is current
    // by the time it runs.
    const surface = containerRef.current;
    const data = attachCaveData3d(engine);
    dataRef.current = data;
    const unsubscribeState = data.subscribe(setDataState);

    // Selection is written straight into the workspace store, which is where the flat map writes
    // it too: both views describe what was picked as bare references, so the detail panel does not
    // know or care which one the viewer clicked in.
    const unsubscribeClick = engine.onClick((pick) => {
      useWorkspaceStore.getState().setSelection(selectionFromPick(pick));
    });

    // Hover only changes the cursor here. The scene throttles the hit test to one per drawn frame,
    // so this costs a pointer-shaped answer per frame and nothing else.
    const unsubscribeHover = engine.onHover((pick) => {
      if (surface) {
        surface.style.cursor = pickPayload(pick) ? 'pointer' : '';
      }
    });

    // Editing or deleting a feature invalidates what is drawn here. The write happens in the
    // detail panel, which draws nothing itself and must not know which views exist, so it
    // announces and this view refetches the ground it is showing — otherwise a deleted marker
    // stays on screen, and stays clickable, until the camera next moves.
    const unsubscribeChanges = onSurfaceFeaturesChanged(() => data.reload());

    // The same panel's "zoom to" buttons: while this view is on screen, they move this camera.
    const detachCamera = setActiveViewCamera({
      flyTo: (longitude, latitude, zoom) =>
        engine.flyToZoom(longitude, latitude, zoom, { animate: true }),
      fitGeometry: (geometry) => {
        const bounds = geoJsonBounds(geometry);
        if (bounds) {
          engine.fitBounds(bounds, { animate: true });
        }
      },
    });

    return () => {
      detachCamera();
      unsubscribeChanges();
      unsubscribeClick();
      unsubscribeHover();
      unsubscribeState();
      data.detach();
      dataRef.current = null;
      setDataState(EMPTY_CAVE_DATA_3D_STATE);
      if (surface) {
        surface.style.cursor = '';
      }
    };
  }, [engineVersion]);

  const { data: mapConfig } = useMapConfig();

  useEffect(() => {
    if (mapConfig) {
      dataRef.current?.setLimits(limitsFromMapConfig(mapConfig));
    }
  }, [mapConfig, engineVersion]);

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
  const notices = [
    dataState.withheldCount > 0 ? t('map.centerlinesWithheld', { count: dataState.withheldCount }) : undefined,
    dataState.flatCount > 0 ? t('scene3d.centerlinesFlat', { count: dataState.flatCount }) : undefined,
  ].filter((notice): notice is string => notice !== undefined);

  return (
    <div className="scene3d-wrap" style={{ height }}>
      {status === 'loading' && (
        <Spin style={{ position: 'absolute', inset: 0, marginTop: 48 }} data-testid="scene3d-loading" />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon message={t('scene3d.startFailed')} description={errorDetail} />
      )}
      {/* The scene owns everything inside this element — React never touches its contents. */}
      <div
        ref={containerRef}
        className="scene3d-canvas"
        data-testid="scene3d-container"
        style={{ display: status === 'error' ? 'none' : undefined }}
      />
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
