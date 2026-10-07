// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Spin } from 'antd';
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useSurveyModel } from '../../../api/hooks.ts';
import {
  caveViewerOptions,
  loadCaveView,
  type CaveViewer,
  type CaveViewUi,
  type Cv2Namespace,
} from '../../../caveview/loadCaveView.ts';
import { MOVIE_LABEL_SIZE_RANGE, type MovieSettings } from '../../../caveview/movie/movieSettings.ts';
import { applyMovieMarkerLabels, applyMovieView, turnToMovieView } from '../../../caveview/movie/movieView.ts';
import { releaseWebGlContext } from '../../../caveview/releaseWebGlContext.ts';
import { viewerFileName } from '../../../caveview/viewerFileName.ts';
import { previewBox, previewSurface } from './previewBox.ts';
import './MoviePreviewHost.css';

/**
 * The viewer a movie is previewed in and recorded from: its own, never the one on the page behind
 * the dialog, in a box the shape of the movie's frame.
 *
 * <b>Its own viewer, because a recording takes the viewer over.</b> For as long as an export runs
 * the viewer draws nothing but the movie's frames, with the movie's layers, labels and party on it.
 * Done on the tracking panel's viewer, the page somebody is following a live trip on would stop
 * showing the live trip. The cost is a second WebGL context while the dialog is open, which is why
 * everything is torn down the moment the host unmounts.
 *
 * <b>The box has the frame's shape, to the pixel.</b> A captured frame is what the box shows,
 * rendered at the frame's size, so the preview is only a preview if the two have one shape — and
 * the viewer refuses to capture from a box of another one. The box is the largest one of that shape
 * that fits the room it is given, in whole pixels.
 *
 * <b>The movie's view is set on the viewer, never saved as its defaults.</b> The viewer re-applies
 * a stored "save as default" view on every load, so the settings are applied after the model has
 * loaded and again whenever the reader changes one. The viewer's own settings panel is hidden
 * here: it is a second place to change the view, which the movie's settings would not remember and
 * whose "save as default" would carry a movie's choices into every model the reader opens later.
 *
 * <b>The model is read once.</b> Its signed address is refreshed every few minutes for as long as
 * anything shows the model; the preview takes the first address that is fresh enough, reads the
 * file from it and never again, so a refresh arriving mid-export cannot reload the model under it.
 */

export interface MoviePreviewHandle {
  viewer: CaveViewer;
  /** The namespace the viewer came from, whose constants the view settings are spelled in. */
  cv2: Cv2Namespace;
}

export interface MoviePreviewHostProps {
  surveyModelId: string;
  /** The movie's frame size in pixels. The preview takes its shape. */
  frame: { width: number; height: number };
  view: MovieSettings['view'];
  cavers: Pick<MovieSettings['cavers'], 'labels' | 'labelSize' | 'labelPlate'>;
  /** How tall the preview may grow; its width is whatever it is given. */
  maxHeight: number | string;
  /**
   * Called once the model is showing, with the viewer to draw on and record from, and with null
   * when it goes. Called again only for another viewer, never for a box that changed size.
   */
  onReady?: (handle: MoviePreviewHandle | null) => void;
  /** Drawn over the model, inside the box — the captions, where the dialog draws them. */
  overlay?: ReactNode;
  /**
   * True while a recording holds the viewer. The host then leaves it alone — no resize, no view or
   * label settings — since a recording sets its own and puts back what it found, and a write in
   * the middle of it would land in the movie's frames or be put back over. Whatever changed
   * meanwhile is applied when the recording lets go.
   */
  recording?: boolean;
  /**
   * Changed by the dialog when the reader asks for the starting view again, after turning or
   * zooming the preview away from it. Its value means nothing; only a change does.
   */
  viewRequest?: number;
}

type Status = 'loading' | 'ready' | 'error';

/**
 * How a caver's label is drawn in a movie, set on the preview's viewer when it is made — and so on
 * every frame recorded from it.
 *
 * The viewer's own labels derive their plate from the background: over the movie's black scene
 * that is a light grey plate at 60 % opacity with black writing on it, and the first line of each
 * label, drawn in the marker's own colour, is then orange or pink or teal on mid grey — hard to read
 * in a small file. With the plate turned off the writing stays black, and the names under a team's
 * heading vanish into the black scene. A dark plate with white writing reads either way: the
 * coloured first lines stand out on it, and without it white writing stands out on black.
 */
const MOVIE_LABEL_THEME = {
  liveMarkers: {
    labelBackground: '#141414',
    labelText: '#ffffff',
    labelBackgroundOpacity: 0.8,
  },
} as const;

/** What the viewer showed when its model loaded, kept off React state: an engine is not a value. */
interface Loaded {
  viewer: CaveViewer;
  cv2: Cv2Namespace;
  /** The shading the viewer drew the model in on its own, which "as the viewer draws it" means. */
  viewerShading: number | null;
}

export default function MoviePreviewHost({
  surveyModelId,
  frame,
  view,
  cavers,
  maxHeight,
  onReady,
  overlay,
  recording = false,
  viewRequest = 0,
}: MoviePreviewHostProps) {
  const { t, i18n } = useTranslation();
  // Read off a ref at the moment the viewer is built: a language switched while the dialog is open
  // is not a reason to build the viewer and read the model again.
  const languageRef = useRef(i18n.language);
  languageRef.current = i18n.language;
  const containerId = `movie-preview-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const roomRef = useRef<HTMLDivElement | null>(null);
  const [room, setRoom] = useState({ width: 0, height: 0 });
  const [status, setStatus] = useState<Status>('loading');
  const [errorDetail, setErrorDetail] = useState<string | null>(null);
  // The viewer lives in a ref; React state holds only a count of the viewers that have loaded, so
  // the effects that set things on one run again for the next — and are never keyed on the engine.
  const loadedRef = useRef<Loaded | null>(null);
  const [loads, setLoads] = useState(0);
  const onReadyRef = useRef(onReady);
  onReadyRef.current = onReady;

  // ---- the model's file, from the first address fresh enough to read it from ----
  const model = useSurveyModel(surveyModelId);
  const [source, setSource] = useState<{ url: string; fileName: string } | null>(null);
  const fresh = model.data !== undefined && (!model.isStale || model.isFetchedAfterMount);
  useEffect(() => {
    if (source === null && fresh && model.data !== undefined) {
      setSource({ url: model.data.modelUrl, fileName: viewerFileName(model.data) });
    }
  }, [source, fresh, model.data]);
  useEffect(() => {
    if (source === null && model.error !== null) {
      setStatus('error');
      setErrorDetail(model.error instanceof Error ? model.error.message : String(model.error));
    }
  }, [source, model.error]);

  // ---- the room the box is fitted into ----
  useLayoutEffect(() => {
    const element = roomRef.current;
    if (element === null) {
      return;
    }
    // The room's own laid-out size, not its rectangle on the page: the dialog is scaled while it
    // zooms open, and a rectangle measured mid-zoom would size the box to a fraction of the room,
    // with nothing to measure it again once the zoom has ended.
    const measure = () => {
      const width = element.clientWidth;
      const height = element.clientHeight;
      setRoom((before) => (before.width === width && before.height === height ? before : { width, height }));
    };
    measure();
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const box = useMemo(() => previewBox(room, { width: frame.width, height: frame.height }), [room, frame.width, frame.height]);

  // ---- the viewer ----
  useEffect(() => {
    if (source === null) {
      return;
    }
    let disposed = false;
    let ui: CaveViewUi | null = null;
    let created: CaveViewer | null = null;
    const download = new AbortController();
    // Held from the start: by the time an unmount's cleanup runs, the page no longer has it.
    const container = document.getElementById(containerId);
    setStatus('loading');
    setErrorDetail(null);
    (async () => {
      const cv2 = await loadCaveView();
      const response = await fetch(source.url, { signal: download.signal });
      if (!response.ok) throw new Error(`survey file request failed (${response.status})`);
      const blob = await response.blob();
      if (disposed) return;
      // Built with what the viewer on the page behind the dialog was built with, and the movie's
      // labels on top. The language is not this viewer's alone: all the viewers on a page share
      // one, and a preview built without it would put the other into the browser's.
      const viewer = new cv2.CaveViewer(containerId, {
        ...caveViewerOptions(languageRef.current),
        theme: MOVIE_LABEL_THEME,
      });
      created = viewer;
      viewer.addEventListener('newCave', (event) => {
        if (disposed) return;
        // The viewer says `newCave` for a model that has loaded, carrying the survey, and also,
        // carrying none, when a key switches how its controls behave. Only a model's arrival is a
        // load: counted as one, the key would turn the preview back to its starting view — throwing
        // away the reader's framing — and read the movie's own shading as the viewer's.
        if ((event as { survey?: unknown } | null)?.survey === undefined) return;
        // Read before any of the movie's settings is written: this is the viewer's own.
        const shading = viewer.shadingMode;
        loadedRef.current = {
          viewer,
          cv2,
          viewerShading: typeof shading === 'number' ? shading : null,
        };
        setLoads((count) => count + 1);
        setStatus('ready');
        onReadyRef.current?.({ viewer, cv2 });
      });
      ui = new cv2.CaveViewUI(viewer);
      ui.loadCave(new File([blob], source.fileName));
    })().catch((error: unknown) => {
      if (disposed) return;
      setStatus('error');
      setErrorDetail(error instanceof Error ? error.message : String(error));
    });
    return () => {
      disposed = true;
      // A model still on its way is not wanted any more: a large file would otherwise go on
      // downloading for a dialog that has closed.
      download.abort();
      const hadLoaded = loadedRef.current !== null;
      loadedRef.current = null;
      if (hadLoaded) {
        onReadyRef.current?.(null);
      }
      // The canvas is found before the viewer is disposed, which may take it out of its container.
      // Given back at once: on the tracking tab the oldest context alive is the live trip's own
      // viewer, made before any movie dialog was opened, and it is the one a browser would take.
      const canvas = container?.querySelector('canvas') ?? null;
      // A recording still holding the viewer is let go of first: ending a session is safe when
      // none is open, and a viewer disposed in the middle of one would leave its state behind.
      (created as CaveViewer | null)?.endCapture();
      (ui as CaveViewUi | null)?.dispose();
      releaseWebGlContext(canvas);
      ui = null;
      created = null;
    };
  }, [source, containerId]);

  // How large the viewer's surface is laid out inside the box: smaller than the box, and scaled up
  // to it, where the box has more device pixels than the labels can be drawn in proportion at.
  const devicePixelRatio = typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1;
  const surface = previewSurface(
    box,
    frame,
    cavers.labels === 'off' ? null : cavers.labelSize,
    devicePixelRatio,
    MOVIE_LABEL_SIZE_RANGE.max,
  );

  // The viewer is told when its surface changes size; it reads the size off the surface itself.
  useLayoutEffect(() => {
    if (!recording) {
      loadedRef.current?.viewer.resize();
    }
  }, [loads, surface.width, surface.height, recording]);

  // The movie's view, after the model has loaded and whenever a setting changes.
  useEffect(() => {
    const loaded = loadedRef.current;
    if (loaded !== null && !recording) {
      applyMovieView(loaded.viewer, view, loaded.cv2, loaded.viewerShading);
    }
  }, [loads, view, recording]);

  // The starting view: when a model has loaded, when the choice changes, and when the reader asks
  // for it again — and on no other change, since every turn reframes the model and would undo the
  // reader's own turning and zooming. After the view settings above, so a camera change is in place
  // before the model is framed. A recording is let alone, and ending one turns nothing: the choice
  // cannot change while it runs, so the key is the one already applied.
  const turnedRef = useRef<string | null>(null);
  useEffect(() => {
    const loaded = loadedRef.current;
    if (loaded === null || recording) {
      return;
    }
    const key = `${loads}:${view.direction}:${viewRequest}`;
    if (turnedRef.current !== key) {
      turnedRef.current = key;
      turnToMovieView(loaded.viewer, view.direction, loaded.cv2);
    }
  }, [loads, view.direction, viewRequest, recording]);

  // The labels at the share of the picture the movie will give them: a label of so many frame
  // pixels is drawn at the surface's device pixels per frame pixel.
  useEffect(() => {
    const loaded = loadedRef.current;
    if (loaded === null || surface.width === 0 || recording) {
      return;
    }
    applyMovieMarkerLabels(loaded.viewer, cavers, (surface.width * devicePixelRatio) / frame.width);
  }, [loads, cavers, surface.width, devicePixelRatio, frame.width, recording]);

  const reduced = surface.width !== box.width;
  const surfaceStyle = reduced
    ? ({
        '--movie-surface-width': `${surface.width}px`,
        '--movie-surface-height': `${surface.height}px`,
        '--movie-surface-scale': String(box.width / surface.width),
      } as CSSProperties)
    : undefined;

  return (
    <div
      ref={roomRef}
      className="movie-preview-room"
      // As tall as a frame of the room's width, up to the limit: a room of a fixed height left a
      // wide frame floating in a band of empty space, with the controls under it pushed away.
      style={{ maxHeight, aspectRatio: `${frame.width} / ${frame.height}` }}
      data-testid="movie-preview"
      data-status={status}
    >
      {status === 'error' ? (
        <Alert type="error" showIcon title={t('caveview.movie.previewError')} description={errorDetail} />
      ) : (
        <div className="movie-preview-box" style={{ width: box.width, height: box.height }}>
          <div id={containerId} className="movie-preview-surface" style={surfaceStyle} data-reduced={reduced || undefined} />
          {status === 'ready' && overlay}
          {status === 'loading' && (
            <Spin className="movie-preview-loading" description={t('caveview.movie.previewLoading')} />
          )}
        </div>
      )}
    </div>
  );
}
