// SPDX-License-Identifier: AGPL-3.0-or-later
import { Modal, Spin } from 'antd';
import { Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatSize } from '../../attachments/fileFormat.ts';
import { lazyRetrying } from '../../../codeLoading.ts';
import { flybyMovieFrameSize } from '../../../flyby/flybyMovie.ts';
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import type { WorkspaceSelection } from '../../../stores/workspaceStore.ts';
import FlybyPanel from './FlybyPanel.tsx';
import FlybyTextsOverlay from './FlybyTextsOverlay.tsx';
import FlybyTransport from './FlybyTransport.tsx';
import { useFlyby, type FlybyScene } from './useFlyby.ts';
import { useFlybyFrameBox } from './useFlybyFrameBox.ts';
import './Scene3DFlyby.css';

// Its own chunk: the movie encoders are only fetched by somebody who makes a movie.
const FlybyMovieDialog = lazyRetrying(() => import('./FlybyMovieDialog.tsx'));

export interface Scene3DFlybyProps {
  scene: FlybyScene;
  selection: WorkspaceSelection | null;
  /**
   * Told whenever a flyby starts or stops having the camera: while it has, the view keeps the flat
   * map and the address bar out of it, and holds the controls that would fight it.
   */
  onActiveChange(active: boolean): void;
  onClose(): void;
}

/** The class the view wears while everything but the scene is off the screen. */
const CLEAN_CLASS = 'scene3d-wrap-clean';
/** Seconds counted down before a clean-screen flyby starts, so a recording can be started first. */
const COUNTDOWN_SECONDS = 3;

/**
 * The flyby tool, as the 3D view mounts it: the panel a flyby is put together in, the bar it is
 * played from, and the clean screen it is shown on for somebody recording their display.
 */
export default function Scene3DFlyby({ scene, selection, onActiveChange, onClose }: Scene3DFlybyProps) {
  const { t } = useTranslation();
  const loadRoute = useFlybyStore((state) => state.loadRoute);
  const flyby = useFlyby(scene, selection, loadRoute);
  const { active, playerState, pause, play, ensurePlayer } = flyby;

  // ---- the movie ----

  const [movieOpen, setMovieOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const movieSize = flybyMovieFrameSize({ size: useFlybyStore((state) => state.movie.size) });
  // State rather than a ref: the panel arrives in a chunk of its own, after this has rendered, and
  // the box has to be fitted again beside it once it is there.
  const [movieElement, setMovieElement] = useState<HTMLDivElement | null>(null);
  const transportRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    onActiveChange(active || exporting);
  }, [active, exporting, onActiveChange]);
  // And when the tool is closed with a flyby in flight, which no change of `active` reports.
  useEffect(() => () => onActiveChange(false), [onActiveChange]);

  // ---- the clean screen ----

  const [clean, setClean] = useState(false);
  const [countdown, setCountdown] = useState<number | null>(null);
  const countdownTimer = useRef<number | undefined>(undefined);

  // While a movie is being set up, and on the clean screen, the scene is boxed to the movie's frame:
  // what is seen is what the file will hold, or what an outside recorder should keep.
  useFlybyFrameBox(
    scene.wrap,
    movieOpen || clean ? { width: movieSize.width, height: movieSize.height } : null,
    () => (clean ? [] : [movieElement, transportRef.current?.firstElementChild ?? null]),
    exporting,
  );

  const leaveClean = useCallback(() => {
    window.clearTimeout(countdownTimer.current);
    setCountdown(null);
    setClean(false);
    pause();
    scene.wrap()?.classList.remove(CLEAN_CLASS);
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => undefined);
    }
  }, [pause, scene]);

  const enterClean = useCallback(async () => {
    const wrap = scene.wrap();
    if (!wrap) {
      return;
    }
    // Asked for before anything is awaited: a browser grants the whole screen only in direct
    // answer to a press. Refused or unavailable, the flyby still plays with the view's own chrome
    // off — which is what an outside recorder pointed at the window needs.
    if (wrap.requestFullscreen) {
      void wrap.requestFullscreen().catch(() => undefined);
    }
    wrap.classList.add(CLEAN_CLASS);
    setClean(true);
    const player = await ensurePlayer();
    if (!player) {
      leaveClean();
      return;
    }
    // From the start, whatever moment the flyby had been left at: a recording is of all of it.
    player.seek(0);
    let left = COUNTDOWN_SECONDS;
    setCountdown(left);
    const tick = () => {
      left -= 1;
      if (left > 0) {
        setCountdown(left);
        countdownTimer.current = window.setTimeout(tick, 1000);
      } else {
        setCountdown(null);
        player.play();
      }
    };
    countdownTimer.current = window.setTimeout(tick, 1000);
  }, [scene, ensurePlayer, leaveClean]);

  useEffect(() => {
    if (!clean) {
      return;
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        leaveClean();
      } else if (event.key === ' ' && countdown === null) {
        event.preventDefault();
        if (playerState.status === 'playing') {
          pause();
        } else {
          void play();
        }
      }
    };
    // Leaving the whole screen by the browser's own means — its key, its gesture — leaves this too.
    let wasFullscreen = document.fullscreenElement !== null;
    const onFullscreen = () => {
      if (document.fullscreenElement) {
        wasFullscreen = true;
      } else if (wasFullscreen) {
        leaveClean();
      }
    };
    window.addEventListener('keydown', onKey);
    document.addEventListener('fullscreenchange', onFullscreen);
    return () => {
      window.removeEventListener('keydown', onKey);
      document.removeEventListener('fullscreenchange', onFullscreen);
    };
  }, [clean, countdown, playerState.status, pause, play, leaveClean]);

  // A view that goes away mid-flight must not leave its element dressed for a clean screen.
  useEffect(
    () => () => {
      window.clearTimeout(countdownTimer.current);
      scene.wrap()?.classList.remove(CLEAN_CLASS);
    },
    [scene],
  );

  // ---- a press on the scene takes the camera back ----

  const playing = playerState.status === 'playing';
  useEffect(() => {
    // Not on the clean screen: there the viewer is recording, and a stray press must not end it.
    if (!playing || clean) {
      return;
    }
    const surface = scene.surface();
    if (!surface) {
      return;
    }
    surface.addEventListener('pointerdown', pause);
    surface.addEventListener('wheel', pause, { passive: true });
    return () => {
      surface.removeEventListener('pointerdown', pause);
      surface.removeEventListener('wheel', pause);
    };
  }, [playing, clean, pause, scene]);

  return (
    <>
      {/* Under the panel and the bar, over the scene; shown on the clean screen too, where it is
          part of what is being recorded. */}
      <FlybyTextsOverlay subscribeMoment={flyby.subscribeMoment} />
      {!clean && !movieOpen && <FlybyPanel flyby={flyby} onClose={onClose} />}
      {!clean && movieOpen && (
        <Suspense fallback={<Spin className="scene3d-flyby-panel scene3d-flyby-movie" />}>
          <FlybyMovieDialog
            ref={setMovieElement}
            flyby={flyby}
            scene={scene}
            onExportingChange={setExporting}
            onClose={() => setMovieOpen(false)}
          />
        </Suspense>
      )}
      {!clean && (
        // A plain element: the bar is placed against the view, not against this.
        <div ref={transportRef} className={exporting ? 'scene3d-flyby-held' : undefined} inert={exporting}>
          <FlybyTransport
            flyby={flyby}
            onCleanScreen={() => void enterClean()}
            onMakeMovie={() => setMovieOpen(true)}
          />
        </div>
      )}
      {/* Asked once before the flyby plays, for every cave on its route whose walls wait for a yes. */}
      <Modal
        open={flyby.wallsQuestion !== undefined}
        title={t('flyby.walls.title')}
        okText={t('flyby.walls.download')}
        cancelText={t('flyby.walls.without')}
        onOk={() => flyby.answerWalls(true)}
        onCancel={() => flyby.answerWalls(false)}
        okButtonProps={{ 'data-testid': 'flyby-walls-download' } as Record<string, string>}
        cancelButtonProps={{ 'data-testid': 'flyby-walls-without' } as Record<string, string>}
        destroyOnHidden
      >
        {flyby.wallsQuestion &&
          t('flyby.walls.question', { count: flyby.wallsQuestion.caveIds.length, size: formatSize(flyby.wallsQuestion.bytes) })}
      </Modal>
      {clean && countdown !== null && (
        <div className="scene3d-flyby-countdown" data-testid="flyby-countdown" role="status" aria-live="assertive">
          {countdown}
        </div>
      )}
      {clean && (
        // Off the picture, for a screen reader and for a test: how to get back.
        <span className="scene3d-flyby-clean-hint" data-testid="flyby-clean-hint">
          {t('flyby.cleanScreenLeave')}
        </span>
      )}
    </>
  );
}
