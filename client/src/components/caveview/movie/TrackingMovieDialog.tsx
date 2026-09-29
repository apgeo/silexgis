// SPDX-License-Identifier: AGPL-3.0-or-later
import { CaretRightOutlined, PauseOutlined, VideoCameraOutlined } from '@ant-design/icons';
import { Alert, App, Button, Checkbox, Flex, Modal, Progress, Slider, Spin, Typography } from 'antd';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { saveBlob } from '../../../api/download.ts';
import { useCave, useSurveyModel, useSurveyModelTrackedTrips, type TrackedTrip } from '../../../api/hooks.ts';
import type {
  CaveViewer,
  CaveViewLabelText,
  CaveViewLiveMarker,
  Cv2Namespace,
} from '../../../caveview/loadCaveView.ts';
import { syncLiveMarkers, type DrawnMarker } from '../../../caveview/liveMarkerSync.ts';
import {
  MOVIE_EXTENSION,
  probeMovieFormats,
  type MovieFormatSupport,
} from '../../../caveview/movie/encode/movieEncoder.ts';
import {
  drawMovieCaptions,
  movieAutoTitle,
  movieCaptionsAt,
  movieClockText,
  movieTitle,
} from '../../../caveview/movie/movieCaptions.ts';
import {
  localIsoDate,
  MOVIE_GIF_SIZE_BUDGET,
  movieFileName,
  movieFileSizeEstimate,
} from '../../../caveview/movie/movieOutput.ts';
import { movieParty, type MovieParty } from '../../../caveview/movie/movieParty.ts';
import { isMovieAbort, recordMovie, type MovieProgress } from '../../../caveview/movie/movieRecorder.ts';
import {
  DEFAULT_MOVIE_SETTINGS,
  movieSize,
  normaliseMovieSettings,
  type MovieSettings,
  type MovieViewLayer,
} from '../../../caveview/movie/movieSettings.ts';
import { buildMovieTimeline, movieFrameCount, movieFrames } from '../../../caveview/movie/movieTimeline.ts';
import { movieLayersAvailable, settleCamera } from '../../../caveview/movie/movieView.ts';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import { formatTripDates } from '../../trips/tripDates.ts';
import { movieTripDays } from './movieDays.ts';
import MoviePreviewHost, { type MoviePreviewHandle } from './MoviePreviewHost.tsx';
import { MOVIE_SHADINGS, type MovieShadingConstant } from './movieChoices.ts';
import MovieSettingsForm from './MovieSettingsForm.tsx';
import { useMovieTrips } from './useMovieTrips.ts';
import './TrackingMovieDialog.css';

/**
 * The dialog a movie of a survey model and its tracked trips is set up, previewed and exported in.
 *
 * <b>The preview is the movie.</b> It is drawn by a viewer of its own, in a box the shape of the
 * frame, with the movie's layers, labels and captions on it; the slider shows any moment of the
 * movie, and playing it runs the movie in real time, camera turn included. The export is recorded
 * from that same viewer, starting from the view the reader framed in it.
 *
 * <b>An export can take minutes and can be called off.</b> Every frame is rendered on demand, in
 * software on some machines, so the dialog says how far it has got and how long it has left, and
 * Cancel — or closing the dialog — stops it with nothing reported: calling it off is not a failure.
 *
 * <b>A copy of what the reader could see.</b> Everything in the movie was sent to this reader under
 * their own rights; the file outlives that check, so the dialog says so, and the settings start
 * private (no heads-up display, grid or notes) until the reader turns them on.
 */

export interface TrackingMovieDialogProps {
  /** The model the movie is made on; null while the dialog is closed. */
  surveyModelId: string | null;
  /** Trips chosen when the dialog opens, in this order. */
  initialTripIds?: readonly string[];
  onClose: () => void;
}

export default function TrackingMovieDialog({ surveyModelId, initialTripIds, onClose }: TrackingMovieDialogProps) {
  const { t } = useTranslation();
  const model = useSurveyModel(surveyModelId ?? undefined);
  return (
    <Modal
      title={
        <Flex gap="small" align="center">
          <VideoCameraOutlined />
          {model.data?.name === undefined
            ? t('caveview.movie.title')
            : t('caveview.movie.titleOf', { model: model.data.name })}
        </Flex>
      }
      open={surveyModelId !== null}
      onCancel={onClose}
      footer={null}
      width="min(1320px, 96vw)"
      // Near the top of the window: the dialog is tall, and its export button is at the bottom.
      style={{ top: 24 }}
      // A second WebGL context is held for as long as the preview exists, so the whole body goes
      // with the dialog — and with it any export still running, which unmounting cancels.
      destroyOnHidden
      // A stray click beside the dialog would throw away the settings and any export under way.
      mask={{ closable: false }}
      data-testid="movie-dialog"
    >
      {surveyModelId !== null && (
        // Keyed on the model, so a dialog handed another model starts again: the preview reads its
        // model once, and a movie folded against one model and drawn on another would be neither.
        <MovieDialogBody
          key={surveyModelId}
          surveyModelId={surveyModelId}
          initialTripIds={initialTripIds ?? []}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

interface ExportRun {
  startedAt: number;
  progress: MovieProgress | null;
  now: number;
}

const PREVIEW_TRAIL_PREFIX = 'movie-preview:';

/**
 * Past this many pixels rendered — frames times the frame's area — a video export is warned about
 * before it starts: about 1,100 frames at 1280 × 720, or 36 seconds of it at 30 frames a second.
 */
const MOVIE_LONG_EXPORT_PIXELS = 1_000_000_000;

/** What the dialog knows of the preview's viewer, as values: which viewer, and what it can show. */
interface PreviewFacts {
  /** Counts the viewers the preview has had, so effects that draw on one run again for the next. */
  generation: number;
  layers: ReadonlySet<MovieViewLayer>;
  terrain: boolean;
  shadings: Record<MovieShadingConstant, number>;
}

function shadingConstants(cv2: Cv2Namespace): Record<MovieShadingConstant, number> {
  return Object.fromEntries(MOVIE_SHADINGS.map(({ constant }) => [constant, cv2[constant]])) as Record<
    MovieShadingConstant,
    number
  >;
}

/** The settings without a written title, which belongs to one movie and is never remembered. */
function withoutTitleText(settings: MovieSettings): MovieSettings {
  return settings.captions.titleText === ''
    ? settings
    : { ...settings, captions: { ...settings.captions, titleText: '' } };
}

function MovieDialogBody({
  surveyModelId,
  initialTripIds,
  onClose,
}: {
  surveyModelId: string;
  initialTripIds: readonly string[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const model = useSurveyModel(surveyModelId).data;
  // The place a movie of several trips is called by: the cave, which is what people know the survey
  // by — its model's own name while the cave has not been read.
  const caveName = useCave(model?.caveId).data?.name;
  const place = caveName ?? model?.name ?? '';

  // ---- settings, remembered per browser; the trips and the title are this movie's own ----
  const remembered = useUiPrefsStore((state) => state.movieSettings);
  const remember = useUiPrefsStore((state) => state.setMovieSettings);
  const [settings, setSettings] = useState<MovieSettings>(() =>
    withoutTitleText(remembered === undefined ? DEFAULT_MOVIE_SETTINGS : normaliseMovieSettings(remembered)),
  );
  // Playing the preview holds the camera: each of its frames puts the angles back from where play
  // began. A turn to another starting view made under it would be undone on the next frame, so play
  // stops first. Reached through refs because play is set up further down.
  const stopPlayingRef = useRef<() => void>(() => {});
  const directionRef = useRef(settings.view.direction);
  directionRef.current = settings.view.direction;
  const changeSettings = useCallback(
    (next: MovieSettings) => {
      if (next.view.direction !== directionRef.current) {
        stopPlayingRef.current();
      }
      setSettings(next);
      // Whether a title is drawn is a preference; what it says is about this movie. Remembered, a
      // title written for one cave's trip would caption and name the next movie, of any cave.
      remember(withoutTitleText(next));
    },
    [remember],
  );
  const [tripIds, setTripIds] = useState<readonly string[]>(initialTripIds);

  // ---- what the movie is made of ----
  const [openedAt] = useState(() => Date.now());
  const tracked = useSurveyModelTrackedTrips(surveyModelId);
  // Only trips the list offers are in the movie. The trip a dialog was opened for is ticked before
  // the list has arrived, so its reads start at once; if the list then leaves it out — its watch was
  // never armed on this model, or everything tying it here is withheld from this reader — it has no
  // box to untick, and must not go on shaping a movie from behind the list.
  const listed = tracked.data;
  const chosenIds = useMemo(
    () => (listed === undefined ? tripIds : tripIds.filter((id) => listed.some((trip) => trip.tripLogId === id))),
    [listed, tripIds],
  );
  const movie = useMovieTrips(surveyModelId, chosenIds, openedAt);
  const timeline = useMemo(
    () =>
      movie.spans.length === 0
        ? null
        : buildMovieTimeline(movie.spans, {
            mode: settings.timeline.mode,
            quietGapMs: settings.timeline.shortenQuiet ? settings.timeline.quietGapMin * 60_000 : null,
          }),
    [movie.spans, settings.timeline],
  );
  const frames = useMemo(() => (timeline === null ? null : movieFrames(timeline, settings)), [timeline, settings]);
  const frameCount = movieFrameCount(settings).count;
  const frameSize = movieSize(settings);
  const { width, height } = frameSize;
  const words = useMemo(
    () => ({
      t,
      language: i18n.language,
      today: new Date().toLocaleDateString(i18n.language),
    }),
    [t, i18n.language],
  );
  const readyIds = new Set(movie.trips.map((trip) => trip.tripLogId));
  const days = movieTripDays(
    (tracked.data ?? []).filter((trip) => readyIds.has(trip.tripLogId)),
    i18n.language,
  );
  const autoTitle = movieAutoTitle(
    movie.trips.map((trip) => trip.title),
    place,
    days,
  );
  const title = movieTitle(settings.captions, autoTitle);

  // ---- what this browser can write at this size and rate ----
  const [formats, setFormats] = useState<MovieFormatSupport[] | null>(null);
  useEffect(() => {
    let current = true;
    setFormats(null);
    probeMovieFormats({ width, height }, settings.fps).then(
      (answer) => current && setFormats(answer),
      // A probe that cannot even be asked is a browser that writes GIF only.
      () =>
        current &&
        setFormats([
          { format: 'gif', supported: true, codec: null },
          { format: 'webm', supported: false, codec: null },
          { format: 'mp4', supported: false, codec: null },
        ]),
    );
    return () => {
      current = false;
    };
  }, [width, height, settings.fps]);
  const formatWritable =
    settings.format === 'gif' || formats?.find((entry) => entry.format === settings.format)?.supported === true;

  // ---- the preview ----
  // The viewer and its namespace are kept in a ref, as every viewer on these pages is; what the
  // form needs of them is read once, as plain values, when a viewer arrives.
  const handleRef = useRef<MoviePreviewHandle | null>(null);
  const [preview, setPreview] = useState<PreviewFacts | null>(null);
  const onPreviewReady = useCallback((next: MoviePreviewHandle | null) => {
    if (handleRef.current?.viewer === next?.viewer) {
      return;
    }
    handleRef.current = next;
    setPreview((before) =>
      next === null
        ? null
        : {
            generation: (before?.generation ?? 0) + 1,
            layers: movieLayersAvailable(next.viewer),
            terrain: next.viewer.hasRealTerrain === true,
            shadings: shadingConstants(next.cv2),
          },
    );
  }, []);
  const generation = preview?.generation ?? 0;
  const previewReady = preview !== null;
  // Counted up when the reader asks the preview to turn to the starting view again.
  const [viewRequest, setViewRequest] = useState(0);
  const viewAgain = useCallback(() => {
    stopPlayingRef.current();
    setViewRequest((count) => count + 1);
  }, []);
  const [position, setPosition] = useState(0);
  const index = frames === null ? 0 : Math.min(position, frames.count - 1);
  const [run, setRun] = useState<ExportRun | null>(null);
  const recording = run !== null;
  const [failure, setFailure] = useState<string | null>(null);

  const partyRef = useRef<MovieParty | null>(null);
  const drawnRef = useRef(new Map<string, DrawnMarker>());
  const trailsRef = useRef(new Map<string, string>());
  const captionsRef = useRef<HTMLCanvasElement | null>(null);
  // One labeller for the preview's grouped markers, reading whichever party is on it now — and
  // the one an export hands back when it lets go of the viewer.
  const clusterLabel = useCallback(
    (markers: readonly CaveViewLiveMarker[]): CaveViewLabelText | null =>
      partyRef.current?.clusterLabel(markers.map((marker) => marker.id)) ?? null,
    [],
  );

  // ---- playing the preview in real time ----
  const [playing, setPlaying] = useState(false);
  const playRef = useRef<{
    frame: number;
    viewer: CaveViewer;
    start: { azimuth: number; polar: number };
  } | null>(null);
  const framesRef = useRef(frames);
  framesRef.current = frames;
  const positionRef = useRef(index);
  positionRef.current = index;

  /** Stops playing and puts the camera back where the movie starts from — at once, not on a render. */
  const stopPlaying = useCallback(() => {
    const play = playRef.current;
    playRef.current = null;
    if (play !== null) {
      cancelAnimationFrame(play.frame);
      try {
        play.viewer.setCameraAngles(play.start);
      } catch {
        // A viewer already being taken down has no camera to put back.
      }
    }
    setPlaying(false);
  }, []);
  stopPlayingRef.current = stopPlaying;

  const startPlaying = () => {
    const schedule = framesRef.current;
    const viewer = handleRef.current?.viewer ?? null;
    if (viewer === null || schedule === null || playRef.current !== null) {
      return;
    }
    const from = positionRef.current >= schedule.count - 1 ? 0 : positionRef.current;
    const began = performance.now();
    // Read after any turn still under way has reached its view, or play would begin — and stop,
    // putting the camera back — between two views.
    settleCamera(viewer);
    const start = viewer.getCameraAngles();
    const tick = (now: number) => {
      const current = framesRef.current;
      const play = playRef.current;
      if (current === null || play === null) {
        return;
      }
      // A frame's timestamp is when the browser began drawing it, which can be before the moment
      // play was pressed; counted from there the first frame would land before the start.
      const at = from + Math.floor((Math.max(0, now - began) * current.fps) / 1000);
      if (at >= current.count) {
        setPosition(current.count - 1);
        stopPlaying();
        return;
      }
      const frame = current.frame(at);
      viewer.setCameraAngles({
        azimuth: start.azimuth + frame.azimuthOffset,
        polar: start.polar,
      });
      setPosition(at);
      play.frame = requestAnimationFrame(tick);
    };
    playRef.current = { frame: requestAnimationFrame(tick), viewer, start };
    setPlaying(true);
  };
  useEffect(() => stopPlaying, [stopPlaying]);

  // ---- the party and the captions at the preview's moment ----
  useEffect(() => {
    const viewer = handleRef.current?.viewer ?? null;
    if (viewer === null || recording) {
      return;
    }
    const party =
      timeline === null || frames === null
        ? null
        : movieParty(movie.trips, timeline.instants(frames.frame(index).position), surveyModelId, {
            settings,
            ...words,
          });
    partyRef.current = party;
    drawnRef.current = syncLiveMarkers(viewer, drawnRef.current, party?.markers ?? new Map(), {
      duration: playing ? settings.cavers.transitionS * 1000 : 0,
    });
    // The route each caver walked, drawn as the export draws it.
    const trails = party?.trails ?? new Map<string, { stations: string[]; color: string }>();
    for (const [id, trail] of trails) {
      const key = `${trail.color}\n${trail.stations.join('\n')}`;
      const before = trailsRef.current.get(id);
      if (before === undefined) {
        viewer.addTrail(PREVIEW_TRAIL_PREFIX + id, trail.stations, {
          color: trail.color,
        });
      } else if (before !== key) {
        viewer.updateTrail(PREVIEW_TRAIL_PREFIX + id, trail.stations, {
          color: trail.color,
        });
      }
      trailsRef.current.set(id, key);
    }
    for (const id of [...trailsRef.current.keys()]) {
      if (!trails.has(id)) {
        viewer.removeTrail(PREVIEW_TRAIL_PREFIX + id);
        trailsRef.current.delete(id);
      }
    }

    const canvas = captionsRef.current;
    const context = canvas?.getContext('2d') ?? null;
    if (canvas !== null && context !== null) {
      context.clearRect(0, 0, canvas.width, canvas.height);
      if (party !== null && timeline !== null && frames !== null) {
        const frame = frames.frame(index);
        drawMovieCaptions(
          context,
          canvas.width,
          canvas.height,
          movieCaptionsAt(settings, title, party, timeline.clock(frame.position), frame.progress, words),
        );
      }
    }
  }, [
    generation,
    recording,
    timeline,
    frames,
    index,
    movie.trips,
    surveyModelId,
    settings,
    words,
    title,
    playing,
    clusterLabel,
  ]);

  // A new viewer starts with nothing on it, and names its grouped markers by the preview's party.
  useEffect(() => {
    drawnRef.current = new Map();
    trailsRef.current = new Map();
    handleRef.current?.viewer.setLiveMarkerClusterLabel(clusterLabel);
  }, [generation, clusterLabel]);

  // While an export runs, the preview shows the frame just recorded; the captions over it are
  // that frame's, so the clock, the legend and the bar do not stand still while the cavers move.
  // Between frames — choosing a GIF's colours, writing the file — they are taken off.
  const recordedFrame = run?.progress?.stage === 'rendering' ? run.progress.step - 1 : null;
  useEffect(() => {
    if (!recording) {
      return;
    }
    const canvas = captionsRef.current;
    const context = canvas?.getContext('2d') ?? null;
    if (canvas === null || context === null) {
      return;
    }
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (recordedFrame === null || timeline === null || frames === null || recordedFrame >= frames.count) {
      return;
    }
    const frame = frames.frame(recordedFrame);
    const party = movieParty(movie.trips, timeline.instants(frame.position), surveyModelId, {
      settings,
      ...words,
    });
    drawMovieCaptions(
      context,
      canvas.width,
      canvas.height,
      movieCaptionsAt(settings, title, party, timeline.clock(frame.position), frame.progress, words),
    );
  }, [recording, recordedFrame, timeline, frames, movie.trips, surveyModelId, settings, words, title]);

  // ---- exporting ----
  const abortRef = useRef<AbortController | null>(null);
  useEffect(() => () => abortRef.current?.abort(), []);
  // The elapsed time moves on between frames too: one frame can take seconds in software.
  useEffect(() => {
    if (!recording) {
      return;
    }
    const timer = window.setInterval(() => setRun((before) => before && { ...before, now: performance.now() }), 1000);
    return () => window.clearInterval(timer);
  }, [recording]);

  const exportMovie = async () => {
    const handle = handleRef.current;
    if (handle === null || timeline === null || movie.trips.length === 0) {
      return;
    }
    stopPlaying();
    const controller = new AbortController();
    abortRef.current = controller;
    const startedAt = performance.now();
    setFailure(null);
    setRun({ startedAt, progress: null, now: startedAt });
    try {
      const file = await recordMovie({
        viewer: handle.viewer,
        constants: handle.cv2,
        settings,
        trips: movie.trips,
        timeline,
        surveyModelId,
        title,
        words,
        clusterLabelAfter: clusterLabel,
        signal: controller.signal,
        onProgress: (progress) => setRun((before) => before && { ...before, progress, now: performance.now() }),
      });
      // Named for what the movie shows: the title written over it when there is one, and otherwise
      // what it is of — the trip, or the cave. A title written but not drawn does not name it. The
      // days an automatic title of several trips ends in are left off: the name already ends in the
      // day the file was made, and a second date beside it, in the reader's own order (9-29-2026,
      // 29-09-2026), reads as neither.
      const written = settings.captions.title ? settings.captions.titleText.trim() : '';
      const name = movieFileName(
        written ||
          movieAutoTitle(
            movie.trips.map((trip) => trip.title),
            place,
            null,
          ),
        localIsoDate(),
        MOVIE_EXTENSION[settings.format],
      );
      saveBlob(file, name);
      message.success(t('caveview.movie.saved', { name }));
    } catch (error) {
      if (!isMovieAbort(error)) {
        setFailure(error instanceof Error ? error.message : String(error));
      }
    } finally {
      if (abortRef.current === controller) {
        abortRef.current = null;
      }
      setRun(null);
    }
  };
  const cancelExport = () => abortRef.current?.abort();

  // ---- what the file will be ----
  const estimate = movieFileSizeEstimate(settings, frameCount);
  const tooLarge = settings.format === 'gif' && estimate > MOVIE_GIF_SIZE_BUDGET;
  // Every frame is rendered, in software where there is no graphics card; a video's length, rate
  // and size can add up to thousands of large frames, and an export of an hour or more.
  const tooLong =
    settings.format !== 'gif' && frameCount * frameSize.width * frameSize.height > MOVIE_LONG_EXPORT_PIXELS;
  const summary = (
    <Flex vertical gap="small">
      <Typography.Text strong data-testid="movie-summary">
        {t('caveview.movie.summary', {
          frames: frameCount,
          size: fileSizeText(estimate, i18n.language),
        })}
      </Typography.Text>
      {tooLarge && (
        <Alert
          type="warning"
          showIcon
          title={t('caveview.movie.gifTooLarge', {
            size: fileSizeText(estimate, i18n.language),
          })}
          data-testid="movie-too-large"
        />
      )}
      {tooLong && (
        <Alert type="warning" showIcon title={t('caveview.movie.videoTooLong')} data-testid="movie-too-long" />
      )}
    </Flex>
  );

  const nothingChosen = chosenIds.length === 0;
  // A chosen trip that could not be read holds the export back rather than dropping out of it: a
  // movie quietly missing one of the trips somebody ticked would be believed.
  const readFailed = movie.failed.length > 0;
  const blocker = nothingChosen
    ? t('caveview.movie.chooseTrip')
    : movie.loading
      ? t('caveview.movie.tripsLoading')
      : readFailed
        ? t('caveview.movie.tripsFailedHint')
        : movie.trips.length === 0
          ? t('trips.tracking.replay.nothingToReplay')
          : null;
  const canExport =
    previewReady
    && timeline !== null
    && movie.trips.length > 0
    && !readFailed
    && formatWritable
    && !recording
    && !movie.loading;

  // While an export runs, the slider and the moment beside it follow the frame being recorded, as the
  // preview and its captions do.
  const shownFrame = recording && recordedFrame !== null ? recordedFrame : index;
  const clockAt = (at: number) =>
    timeline === null || frames === null
      ? ''
      : movieClockText(timeline.clock(frames.frame(at).position), t, i18n.language);

  return (
    <div className="movie-dialog">
      <div className="movie-dialog-body">
        <Flex vertical gap="small" className="movie-dialog-preview">
          <MoviePreviewHost
            surveyModelId={surveyModelId}
            frame={frameSize}
            view={settings.view}
            cavers={settings.cavers}
            maxHeight="max(200px, min(calc(100vh - 400px), 640px))"
            onReady={onPreviewReady}
            recording={recording}
            viewRequest={viewRequest}
            overlay={
              <canvas
                ref={captionsRef}
                width={frameSize.width}
                height={frameSize.height}
                className="movie-preview-captions"
                aria-hidden
              />
            }
          />
          <Flex gap="small" align="center">
            <Button
              icon={playing ? <PauseOutlined /> : <CaretRightOutlined />}
              onClick={playing ? stopPlaying : startPlaying}
              disabled={frames === null || !previewReady || recording}
              aria-label={playing ? t('caveview.movie.stopPreview') : t('caveview.movie.playPreview')}
              data-testid="movie-play"
            />
            {/* The slider hands neither a test id nor a label to anything it draws: the handle is
                named through its own prop, and the id goes on a box around it. */}
            <div className="movie-dialog-position" data-testid="movie-position" data-frame={shownFrame}>
              <Slider
                min={0}
                max={Math.max(0, (frames?.count ?? 1) - 1)}
                value={shownFrame}
                disabled={frames === null || recording}
                onChange={(value: number) => {
                  stopPlaying();
                  setPosition(value);
                }}
                // The moment is written beside the slider instead of in a tooltip over its handle.
                // A tooltip follows the handle on every step, and one kept open on a focused handle
                // while the value steps quickly re-renders its popup container in a loop until
                // React gives up and the page shows an error.
                tooltip={{ formatter: null }}
                ariaLabelForHandle={t('caveview.movie.position')}
                ariaValueTextFormatterForHandle={(value) => (value === undefined ? '' : clockAt(value))}
              />
            </div>
            <Typography.Text className="movie-dialog-moment" data-testid="movie-moment">
              {clockAt(shownFrame)}
            </Typography.Text>
          </Flex>
          <Typography.Text type="secondary">{blocker ?? t('caveview.movie.previewHint')}</Typography.Text>
        </Flex>
        <div className="movie-dialog-settings">
          <MovieSettingsForm
            settings={settings}
            onChange={changeSettings}
            disabled={recording}
            formats={formats}
            videoEncoding={typeof VideoEncoder !== 'undefined'}
            layers={preview?.layers ?? null}
            constants={preview?.shadings ?? null}
            terrain={preview?.terrain === true}
            autoTitle={autoTitle}
            summary={summary}
            onViewAgain={previewReady ? viewAgain : null}
            trips={
              <MovieTripPicker
                tracked={tracked.data}
                loading={tracked.isPending}
                listFailed={tracked.error !== null}
                chosen={chosenIds}
                empty={movie.empty}
                tripsFailed={movie.failed}
                logFailed={movie.logFailed}
                disabled={recording}
                onChange={setTripIds}
              />
            }
          />
        </div>
      </div>

      <Flex vertical gap="small" className="movie-dialog-footer">
        {movie.logFailed.length > 0 ? (
          // The trip's own replay refuses a partial log in these words; the movie refuses it for the
          // same reason and says the same thing.
          <Alert
            type="error"
            showIcon
            title={t('trips.tracking.replay.logUnavailable')}
            description={t('trips.tracking.replay.logUnavailableBody')}
            data-testid="movie-log-unavailable"
          />
        ) : (
          readFailed && (
            <Alert type="error" showIcon title={t('caveview.movie.tripsLoadError')} data-testid="movie-trips-failed" />
          )
        )}
        {failure !== null && (
          <Alert
            type="error"
            showIcon
            closable={{ onClose: () => setFailure(null) }}
            title={t('caveview.movie.exportFailed')}
            description={failure}
            data-testid="movie-export-failed"
          />
        )}
        {run !== null && <ExportProgress run={run} />}
        <Alert type="info" showIcon title={t('caveview.movie.privacy')} data-testid="movie-privacy" />
        <Flex justify="flex-end" gap="small">
          {recording ? (
            <Button onClick={cancelExport} data-testid="movie-cancel">
              {t('caveview.movie.cancelExport')}
            </Button>
          ) : (
            <Button onClick={onClose}>{t('common.close')}</Button>
          )}
          <Button
            type="primary"
            icon={<VideoCameraOutlined />}
            loading={recording}
            disabled={!canExport}
            onClick={() => void exportMovie()}
            data-testid="movie-export"
          >
            {t('caveview.movie.export', {
              format: t(`caveview.movie.formats.${settings.format}`),
            })}
          </Button>
        </Flex>
      </Flex>
    </div>
  );
}

/** A byte count as a reader says it: kilobytes below a megabyte, megabytes with one decimal above. */
function fileSizeText(bytes: number, language: string): string {
  if (bytes < 1024 * 1024) {
    return `${Math.max(1, Math.round(bytes / 1024)).toLocaleString(language)} kB`;
  }
  return `${(bytes / (1024 * 1024)).toLocaleString(language, { maximumFractionDigits: 1 })} MB`;
}

/** Minutes and seconds, as a clock shows them. */
function durationText(ms: number): string {
  const seconds = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function ExportProgress({ run }: { run: ExportRun }) {
  const { t } = useTranslation();
  const { progress } = run;
  const elapsed = run.now - run.startedAt;
  // The bar and the time left go by the whole of the work; the words by the stage's own count, so
  // a GIF of 20 frames is said to be at frame 1 of 20, not 17 of 36 after its colour samples.
  const done = progress?.done ?? 0;
  const total = progress?.total ?? 0;
  const percent = total === 0 ? 0 : Math.floor((done / total) * 100);
  // Remaining time is only said once there is a rate to say it from.
  const remaining = done > 0 && total > done ? (elapsed / done) * (total - done) : null;
  const stage =
    progress === null
      ? t('caveview.movie.progress.starting')
      : t(`caveview.movie.progress.${progress.stage}`, { done: progress.step, total: progress.steps });
  return (
    <div data-testid="movie-progress">
      <Progress percent={percent} status="active" />
      <Typography.Text type="secondary">
        {stage}
        {' · '}
        {remaining === null
          ? t('caveview.movie.progressElapsed', {
              elapsed: durationText(elapsed),
            })
          : t('caveview.movie.progressTimes', {
              elapsed: durationText(elapsed),
              remaining: durationText(remaining),
            })}
      </Typography.Text>
    </div>
  );
}

/**
 * The trips tracked on the model, to tick into the movie. A trip whose watch was never armed, or
 * whose reports cover no stretch of time, is listed and cannot be ticked, saying why; a ticked trip
 * that could not be read says so under its name, and can be unticked.
 */
function MovieTripPicker({
  tracked,
  loading,
  listFailed,
  chosen,
  empty,
  tripsFailed,
  logFailed,
  disabled,
  onChange,
}: {
  tracked: TrackedTrip[] | undefined;
  loading: boolean;
  listFailed: boolean;
  chosen: readonly string[];
  empty: readonly string[];
  tripsFailed: readonly string[];
  logFailed: readonly string[];
  disabled: boolean;
  onChange: (ids: readonly string[]) => void;
}) {
  const { t, i18n } = useTranslation();
  if (listFailed) {
    return <Alert type="error" showIcon title={t('caveview.movie.trackedLoadError')} />;
  }
  if (loading || tracked === undefined) {
    return <Spin size="small" />;
  }
  if (tracked.length === 0) {
    return <Typography.Text type="secondary">{t('caveview.movie.noTrackedTrips')}</Typography.Text>;
  }
  return (
    <Flex vertical gap="small" data-testid="movie-trips">
      <Typography.Text type="secondary">{t('caveview.movie.tripsHint')}</Typography.Text>
      {tracked.map((trip) => {
        const nothing = trip.armedAt === null || empty.includes(trip.tripLogId);
        const checked = chosen.includes(trip.tripLogId);
        const help = logFailed.includes(trip.tripLogId)
          ? t('trips.tracking.replay.logUnavailable')
          : tripsFailed.includes(trip.tripLogId)
            ? t('caveview.movie.tripLoadError')
            : nothing
              ? t('trips.tracking.replay.nothingToReplay')
              : [
                  trip.tripDate === null ? null : formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.language),
                  t('caveview.movie.tripReports', { reports: trip.reportCount }),
                ]
                  .filter((part) => part !== null)
                  .join(' · ');
        return (
          <div key={trip.tripLogId} data-testid={`movie-trip-${trip.tripLogId}`}>
            <Checkbox
              checked={checked}
              disabled={disabled || (nothing && !checked)}
              onChange={(event) =>
                onChange(
                  event.target.checked ? [...chosen, trip.tripLogId] : chosen.filter((id) => id !== trip.tripLogId),
                )
              }
            >
              <Flex vertical>
                <Typography.Text>{trip.title}</Typography.Text>
                <Typography.Text
                  type={tripsFailed.includes(trip.tripLogId) ? 'danger' : 'secondary'}
                  className="movie-setting-help"
                >
                  {help}
                </Typography.Text>
              </Flex>
            </Checkbox>
          </div>
        );
      })}
    </Flex>
  );
}
