// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  CameraOutlined,
  CaretRightOutlined,
  CloseOutlined,
  DownOutlined,
  PauseOutlined,
  RightOutlined,
  VideoCameraOutlined,
} from '@ant-design/icons';
import { Alert, App, Button, Checkbox, Flex, Modal, Progress, Slider, Spin, Typography } from 'antd';
import { memo, useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { saveBlob } from '../../../api/download.ts';
import { useCave, useSurveyModel, useSurveyModelTrackedTrips, type TrackedTrip } from '../../../api/hooks.ts';
import type { CaveViewer, CaveViewLabelText, CaveViewLiveMarker } from '../../../caveview/loadCaveView.ts';
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
  movieEstimateIsCalibrated,
  movieExportName,
  movieFileSizeEstimate,
  movieGifCalibrationFrom,
  normaliseMovieGifCalibration,
} from '../../../caveview/movie/movieOutput.ts';
import {
  movieMarkerId,
  movieMarkerIsOfTrip,
  movieParty,
  type MovieParty,
  type MovieTripData,
} from '../../../caveview/movie/movieParty.ts';
import { moviePresetOutput, withMovieOutput, type MoviePresetId } from '../../../caveview/movie/moviePresets.ts';
import type { MovieSink } from '../../../caveview/movie/encode/movieSink.ts';
import { chooseMovieFile, movieGoesToDisk, type MovieFileChoice } from '../../../caveview/movie/movieFileSink.ts';
import {
  isMovieAbort,
  recordMovie,
  recordMovieToSink,
  recordMovieStill,
  MovieFrameUncomposedError,
  MovieStillUnwrittenError,
  type MovieProgress,
  type MovieRecording,
} from '../../../caveview/movie/movieRecorder.ts';
import {
  DEFAULT_MOVIE_SETTINGS,
  movieSettingsNeedRewriting,
  movieSize,
  normaliseMovieSettings,
  type MovieSettings,
  type MovieViewLayer,
} from '../../../caveview/movie/movieSettings.ts';
import {
  buildMovieTimeline,
  movieFrameCount,
  movieFrames,
  movieSpansAt,
  movieSpansShowing,
  movieTripIsLive,
  type MovieTimeline,
  type MovieTripSpan,
} from '../../../caveview/movie/movieTimeline.ts';
import { movieLayersAvailable, settleCamera } from '../../../caveview/movie/movieView.ts';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import { formatTripDates } from '../../trips/tripDates.ts';
import { movieTripDays } from './movieDays.ts';
import MoviePreviewHost, { type MoviePreviewHandle } from './MoviePreviewHost.tsx';
import MovieSettingsForm from './MovieSettingsForm.tsx';
import { useMovieTrips } from './useMovieTrips.ts';
import './TrackingMovieDialog.css';

/**
 * The settings form, drawn again only when what it is given changes.
 *
 * An export reports its progress after every frame, and the progress is state of the dialog, so
 * without this the whole form — dozens of antd controls and the list of trips — was drawn again for
 * each of a long export's frames: on a movie of twenty trips that was a large share of the export's
 * time, taken from the frames themselves. Nothing the form shows can change while an export runs.
 */
const SettingsForm = memo(MovieSettingsForm);

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
 * Cancel stops it with nothing reported: calling it off is not a failure.
 *
 * <b>Closing the dialog is not how an export is called off, so it asks first.</b> Cancel export is
 * a button that says what it does. Escape and the X in the corner say only "close", are pressed by
 * habit, and during an export would throw away minutes of rendering without a word; so while one
 * runs they ask whether to stop it, and the answers that need no thought — Escape again, or
 * Enter on the button the question opens on — keep it going. A click beside the dialog does
 * nothing at all.
 *
 * <b>The preview takes a few keys of its own, and only where nothing else wants them.</b> With the
 * preview or the moment slider in focus, Space plays and pauses and Home and End go to the two ends
 * of the movie. On a button Space presses that button, and in a text box it types a space, as it
 * must — so the keys are read where the preview's column hears them and passed over whenever the
 * key was pressed on a control of that kind.
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
  const { modal } = App.useApp();
  const model = useSurveyModel(surveyModelId ?? undefined);

  // What stops the export under way, handed up by the body for as long as one runs; null otherwise.
  // A ref, not state: it is read when a key is pressed, and an export starting or ending must not
  // draw the dialog's frame again.
  const stopExportRef = useRef<(() => void) | null>(null);
  const questionRef = useRef<{ destroy: () => void } | null>(null);
  const onExportRunning = useCallback((stop: (() => void) | null) => {
    stopExportRef.current = stop;
    if (stop === null) {
      // The export ended — finished, failed, cancelled or unmounted — while the question was still
      // up. There is nothing left to stop, so the question goes; the dialog stays as it is.
      questionRef.current?.destroy();
      questionRef.current = null;
    }
  }, []);
  // Escape and the X arrive here; the mask does not, and the footer's own buttons are the body's.
  const requestClose = () => {
    if (stopExportRef.current === null) {
      onClose();
      return;
    }
    if (questionRef.current !== null) {
      return;
    }
    questionRef.current = modal.confirm({
      title: t('caveview.movie.closeWhileExporting.title'),
      content: t('caveview.movie.closeWhileExporting.body'),
      okText: t('caveview.movie.closeWhileExporting.stop'),
      okButtonProps: { danger: true },
      cancelText: t('caveview.movie.closeWhileExporting.keep'),
      // The question opens on "keep going": Enter pressed by habit after Escape must not be what
      // throws the export away.
      focusable: { autoFocusButton: 'cancel' },
      onOk: () => {
        questionRef.current = null;
        // Stopped here rather than left to the body going away with the dialog: the body is only
        // taken down once the dialog has finished closing, and frames would go on being rendered
        // for a movie nobody wants until then.
        stopExportRef.current?.();
        onClose();
      },
      onCancel: () => {
        questionRef.current = null;
      },
    });
  };
  // A question still up when the dialog itself goes has nothing left to ask about.
  useEffect(
    () => () => {
      questionRef.current?.destroy();
      questionRef.current = null;
    },
    [],
  );

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
      onCancel={requestClose}
      footer={null}
      width="min(1320px, 96vw)"
      // Near the top of the window: the dialog is tall, and its export button is at the bottom.
      style={{ top: 24 }}
      // A second WebGL context is held for as long as the preview exists, so the whole body goes
      // with the dialog — and with it any export still running, which unmounting cancels.
      destroyOnHidden
      // A stray click beside the dialog would throw away the settings and any export under way; it
      // is not asked about either, since nobody meant anything by it.
      mask={{ closable: false }}
      // Round the whole dialog, not only what is drawn in its body: the X in the corner and the
      // dialog's own frame — which is where the focus is put on opening — take keys too, and the
      // viewer behind the preview would cancel a Tab or an Enter pressed on either.
      wrapProps={{ onKeyDown: keepKeysFromViewer }}
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
          onExportRunning={onExportRunning}
        />
      )}
    </Modal>
  );
}

/** What could not be made, and why, as the reader is told it. */
interface Failure {
  what: 'movie' | 'still';
  /** Null where the heading says all there is to say. */
  detail: string | null;
}

/**
 * The controls a key belongs to before it belongs to the preview: Space on a button presses it, in a
 * text box it types, and on a link it follows it.
 */
const TAKES_ITS_OWN_KEYS = 'button, a[href], input, textarea, select, [contenteditable="true"]';

interface ExportRun {
  /** The name the file is saved under, fixed when the export starts. */
  name: string;
  startedAt: number;
  progress: MovieProgress | null;
  now: number;
}

const PREVIEW_TRAIL_PREFIX = 'movie-preview:';

/** Nobody left out: what every movie starts with. */
const NOBODY: ReadonlySet<string> = new Set();

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
}

/** The movie's clock over these trips' spans, as the reader set it; null when there is no trip. */
function timelineOf(spans: readonly MovieTripSpan[], timeline: MovieSettings['timeline']): MovieTimeline | null {
  return spans.length === 0
    ? null
    : buildMovieTimeline(spans, {
        mode: timeline.mode,
        quietGapMs: timeline.shortenQuiet ? timeline.quietGapMin * 60_000 : null,
      });
}

/**
 * The spans the movie's clock is built from: the trips' own, cut only at the reports of the people
 * who appear. One function for the preview and the export, so the file is paced as the preview was.
 */
function spansShowing(
  spans: readonly MovieTripSpan[],
  trips: readonly MovieTripData[],
  surveyModelId: string,
  excluded: ReadonlySet<string>,
): readonly MovieTripSpan[] {
  return excluded.size === 0
    ? spans
    : movieSpansShowing(spans, trips, surveyModelId, (tripLogId, caverId) => !excluded.has(movieMarkerId(tripLogId, caverId)));
}

/** The settings without a written title, which belongs to one movie and is never remembered. */
function withoutTitleText(settings: MovieSettings): MovieSettings {
  return settings.captions.titleText === ''
    ? settings
    : { ...settings, captions: { ...settings.captions, titleText: '' } };
}

/**
 * Keeps a key pressed anywhere on the dialog from the viewer's own shortcuts.
 *
 * The viewer listens for keys on the whole document and, whenever the pointer is resting over it,
 * takes every key for itself and cancels it — so a title typed with the pointer left over the
 * preview wrote nothing, and the shortcuts turned the preview under a reader whose keys were meant
 * for a text box. A key pressed on the dialog is the dialog's — on its settings, on its buttons, on
 * the X in its corner and on its frame, which holds the focus until the first Tab.
 *
 * <b>Only Escape goes on past it.</b> The modal closes on Escape from a listener on the window,
 * which a key stopped here never reaches; the viewer cancels it on the way and has no use for it.
 * Tab is stopped with the rest. The modal keeps the focus inside the dialog by watching where the
 * focus goes, and sees the key on its way down before it ever gets here — so nothing of the
 * modal's is lost — while a Tab left to travel on was cancelled by the viewer whenever the pointer
 * rested on the preview and the focus was on a button, a switch or the slider: the focus stayed
 * where it was, with no sign of why.
 */
function keepKeysFromViewer(event: { key: string; stopPropagation(): void }): void {
  if (event.key !== 'Escape') {
    event.stopPropagation();
  }
}

function MovieDialogBody({
  surveyModelId,
  initialTripIds,
  onClose,
  onExportRunning,
}: {
  surveyModelId: string;
  initialTripIds: readonly string[];
  onClose: () => void;
  /**
   * Told how to stop the export for as long as one runs, and null when none does — so whatever
   * closes the dialog from outside this body knows there is an export to ask about, and can stop it.
   */
  onExportRunning: (stop: (() => void) | null) => void;
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
  // Settings remembered in a shape no longer written are written back once, repaired, so the old
  // shape does not outlive the first opening after an update — even for a reader who changes nothing.
  useEffect(() => {
    if (remembered !== undefined && movieSettingsNeedRewriting(remembered)) {
      remember(withoutTitleText(normaliseMovieSettings(remembered)));
    }
  }, [remembered, remember]);
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
  // ---- presets, and putting everything back ----
  // Read through refs: one preset asks the browser what it can write before it changes anything,
  // and its answer must be applied to the settings as they are when it arrives. The handlers stay
  // the same functions throughout, which is what keeps the form from being drawn again for them.
  const settingsRef = useRef(settings);
  settingsRef.current = settings;
  const recordingRef = useRef(false);
  const aliveRef = useRef(true);
  useEffect(() => {
    aliveRef.current = true;
    return () => {
      aliveRef.current = false;
    };
  }, []);
  const applyPreset = useCallback(
    (preset: MoviePresetId) => {
      void moviePresetOutput(preset).then((output) => {
        // An export started, or the dialog closed, while the browser was being asked: nothing may
        // change under the one, and there is nothing to change after the other.
        if (!aliveRef.current || recordingRef.current) {
          return;
        }
        if (output === null) {
          message.warning(t('caveview.movie.presetHdUnavailable'));
          return;
        }
        // Only the file's own settings: who is shown, the view and the captions are what the
        // reader chose to disclose, and a preset has no say in them.
        changeSettings(withMovieOutput(settingsRef.current, output));
      });
    },
    [changeSettings, message, t],
  );
  // Every setting, the written title included. The trips ticked and the people left out are not
  // settings and stay: somebody unticked on purpose must not come back because the frame size was
  // put back to its default.
  const resetSettings = useCallback(() => changeSettings(DEFAULT_MOVIE_SETTINGS), [changeSettings]);
  const [tripIds, setTripIds] = useState<readonly string[]>(initialTripIds);
  // What the GIFs made in this browser came to, which the size estimated for the next one goes by.
  const storedGifCalibration = useUiPrefsStore((state) => state.movieGifCalibration);
  const rememberGifCalibration = useUiPrefsStore((state) => state.setMovieGifCalibration);
  const gifCalibration = useMemo(() => normaliseMovieGifCalibration(storedGifCalibration), [storedGifCalibration]);

  // ---- what the movie is made of ----
  // Where a trip still under way ends in the movie. Fixed while the reader looks, so the slider
  // does not creep under the hand that drags it; moved once, to the moment an export starts, so the
  // file runs up to when it was made however long the dialog stood open before.
  const [windowEnd, setWindowEnd] = useState(() => Date.now());
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
  const [run, setRun] = useState<ExportRun | null>(null);
  const recording = run !== null;
  recordingRef.current = recording;
  // The logs of trips still under way are read again as their watch answers, except under an
  // export: the frames being recorded, and the captions drawn over the preview meanwhile, are of
  // the movie as it was when the export began.
  const movie = useMovieTrips(surveyModelId, chosenIds, windowEnd, recording);
  // (The movie's clock is worked out below, once it is known who appears: it is cut at the reports
  // of the people shown, and of nobody else.)
  // Each trip still under way says on its row how many reports have come in since the dialog
  // opened: the party goes on while it stands open, and the reader should see that the movie they
  // are about to make is of more than the one they opened.
  const newReports = movie.newReports;
  // An export of a trip under way reads its log again first; when that read cannot reach the log's
  // end the export is held back, in the words every unreadable log is refused in.
  const [liveLogFailed, setLiveLogFailed] = useState(false);
  // ---- who appears ----
  // The markers the reader left out of this movie. Held here and nowhere else — not with the
  // remembered settings, not in the address, not in any storage: a list of the people somebody
  // chose to leave out, kept after the movie was made, would be a record about those people.
  const [excluded, setExcluded] = useState<ReadonlySet<string>>(NOBODY);
  const showCaver = useCallback((tripLogId: string, caverId: string, shown: boolean) => {
    const id = movieMarkerId(tripLogId, caverId);
    setExcluded((before) => {
      if (before.has(id) !== shown) {
        return before;
      }
      const next = new Set(before);
      if (shown) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);
  const chooseTrips = useCallback((ids: readonly string[]) => {
    setLiveLogFailed(false);
    setTripIds(ids);
    // A trip taken out of the movie takes its choice of people with it: ticked again, it starts
    // with everybody, as every trip does.
    setExcluded((before) => {
      const kept = [...before].filter((marker) => ids.some((id) => movieMarkerIsOfTrip(marker, id)));
      return kept.length === before.size ? before : new Set(kept);
    });
  }, []);
  const timeline = useMemo(
    () => timelineOf(spansShowing(movie.spans, movie.trips, surveyModelId, excluded), settings.timeline),
    [movie.spans, movie.trips, surveyModelId, excluded, settings.timeline],
  );
  // Each ready trip's roster as the picker lists it, by the names the trip's own page gives.
  const rosters = useMemo(
    () =>
      new Map(
        movie.trips.map((trip) => [
          trip.tripLogId,
          trip.tracking.participants.map((person) => ({ caverId: person.caverId, name: trip.nameOf(person.caverId) })),
        ]),
      ),
    [movie.trips],
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
  const [failure, setFailure] = useState<Failure | null>(null);
  /**
   * What is said under the heading of a failure. The failures that were foreseen are said in
   * the reader's own language — a browser that composed the picture and gave no file of it by
   * the heading alone, one that gave no canvas to compose on by a sentence of its own — and
   * only what nobody foresaw is quoted as it came, because its words are all there is to go on.
   */
  const failureDetail = (error: unknown): string | null =>
    error instanceof MovieStillUnwrittenError
      ? null
      : error instanceof MovieFrameUncomposedError
        ? t('caveview.movie.frameUncomposed')
        : error instanceof Error
          ? error.message
          : String(error);

  const partyRef = useRef<MovieParty | null>(null);
  const drawnRef = useRef(new Map<string, DrawnMarker>());
  /** The viewer the markers and trails in the two maps below are standing on. */
  const drawnOnRef = useRef<CaveViewer | null>(null);
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
    // A viewer this party has not been drawn on starts with nothing on it, and its grouped markers
    // are named by the preview's party before anybody stands on it. Decided by the viewer itself
    // rather than by the count of viewers: the viewer is in a ref that is set before the count is,
    // so the party was drawn on a new viewer in the same commit that announced it, and a reset keyed
    // on the count then wiped the bookkeeping and had every marker and trail added a second time.
    if (drawnOnRef.current !== viewer) {
      drawnOnRef.current = viewer;
      drawnRef.current = new Map();
      trailsRef.current = new Map();
      viewer.setLiveMarkerClusterLabel(clusterLabel);
    }
    const party =
      timeline === null || frames === null
        ? null
        : movieParty(movie.trips, timeline.instants(frames.frame(index).position), surveyModelId, {
            settings,
            ...words,
            excluded,
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
          movieCaptionsAt(settings, title, party, timeline, frame, words),
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
    excluded,
    title,
    playing,
    clusterLabel,
  ]);

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
      excluded,
    });
    drawMovieCaptions(
      context,
      canvas.width,
      canvas.height,
      movieCaptionsAt(settings, title, party, timeline, frame, words),
    );
  }, [recording, recordedFrame, timeline, frames, movie.trips, surveyModelId, settings, words, excluded, title]);

  // ---- what the file will be called ----
  // Its name is shown before the export, not only said afterwards: with the title caption on it
  // carries the trip's or the cave's name to whoever the file is sent on to, and the reader should
  // see that while it can still be changed.
  const fileName = movieExportName(
    settings.captions,
    movie.trips.map((trip) => trip.title),
    place,
    localIsoDate(),
    MOVIE_EXTENSION[settings.format],
  );

  // ---- exporting ----
  const abortRef = useRef<AbortController | null>(null);
  useEffect(() => () => abortRef.current?.abort(), []);
  // Whatever closes the dialog from outside is told there is an export, and how to stop it, for
  // exactly as long as one runs — and that there is none again when this body goes.
  useEffect(() => {
    if (!recording) {
      return;
    }
    onExportRunning(() => abortRef.current?.abort());
    return () => onExportRunning(null);
  }, [recording, onExportRunning]);
  // The elapsed time moves on between frames too: one frame can take seconds in software.
  useEffect(() => {
    if (!recording) {
      return;
    }
    const timer = window.setInterval(() => setRun((before) => before && { ...before, now: performance.now() }), 1000);
    return () => window.clearInterval(timer);
  }, [recording]);

  // Set while the reader is being asked where a long video goes, so a second press asks nothing.
  const choosingFileRef = useRef(false);
  const exportMovie = async () => {
    const handle = handleRef.current;
    if (handle === null || timeline === null || movie.trips.length === 0 || choosingFileRef.current) {
      return;
    }
    // A long video is written to a file as it is made, and where that file goes has to be asked
    // now: a browser only asks in answer to the press, and the press is spent once anything else
    // has been waited for. Nothing has been started yet, so a reader who closes the question has
    // cancelled nothing — there is simply no export.
    let disk: { name: string; sink: MovieSink } | null = null;
    if (movieGoesToDisk(settings.format, movieFileSizeEstimate(settings, frameCount, gifCalibration))) {
      choosingFileRef.current = true;
      let choice: MovieFileChoice;
      try {
        choice = await chooseMovieFile(fileName, settings.format);
      } finally {
        choosingFileRef.current = false;
      }
      if (choice.kind === 'dismissed') {
        return;
      }
      if (choice.kind === 'file') {
        if (handleRef.current !== handle) {
          // The preview went away while the question was open: there is nothing left to record.
          await choice.sink.abort();
          return;
        }
        disk = choice;
      }
    }
    // From here until the recorder takes it, a file that was chosen is this function's to remove.
    let sinkHandedOver = false;
    stopPlaying();
    const controller = new AbortController();
    abortRef.current = controller;
    const startedAt = performance.now();
    setFailure(null);
    setLiveLogFailed(false);
    // The name the dialog was showing when the export was started, so the file is called what the
    // reader was told it would be — even when the export runs past midnight. A file the reader
    // chose is called what they called it.
    const name = disk?.name ?? fileName;
    setRun({ name, startedAt, progress: null, now: startedAt });
    let trips = movie.trips;
    let recorded = timeline;
    try {
      if (trips.some((trip) => movieTripIsLive(trip.tracking))) {
        // A trip still under way ends, in the file, at this moment rather than where the dialog
        // fixed it on opening — so its log is read again first. The stretch added at the end must
        // be drawn from the reports recorded during it, by whoever recorded them and wherever:
        // drawn from the log held since opening, it would show everybody standing where they were
        // an hour ago under a clock that says now.
        try {
          trips = await movie.rereadLive();
        } catch {
          if (!controller.signal.aborted) {
            setLiveLogFailed(true);
          }
          return;
        }
        if (controller.signal.aborted) {
          return;
        }
        // The preview is moved to the same end, so the frame it shows while the export runs, and
        // whatever is previewed afterwards, is the movie that was made.
        const now = Date.now();
        const spans = movieSpansAt(trips, now, surveyModelId);
        recorded =
          (spans === null
            ? null
            : timelineOf(spansShowing(spans, trips, surveyModelId, excluded), settings.timeline)) ?? timeline;
        setWindowEnd((before) => Math.max(before, now));
      }
      const asked: MovieRecording = {
        viewer: handle.viewer,
        constants: handle.cv2,
        settings,
        trips,
        excluded,
        timeline: recorded,
        surveyModelId,
        title,
        words,
        clusterLabelAfter: clusterLabel,
        signal: controller.signal,
        onProgress: (progress) => setRun((before) => before && { ...before, progress, now: performance.now() }),
      };
      let file: Blob | null;
      if (disk === null) {
        file = await recordMovie(asked);
      } else {
        sinkHandedOver = true;
        file = await recordMovieToSink(asked, disk.sink);
      }
      // No file comes back from a movie that is already in the place the reader chose for it.
      if (file !== null) {
        saveBlob(file, name);
      }
      message.success(t('caveview.movie.saved', { name }));
      if (settings.format === 'gif' && file !== null) {
        // What this GIF came to corrects the size estimated for the next one. Read from the store
        // as it is now, not as it was when the export began: another window may have made one since.
        const held = normaliseMovieGifCalibration(useUiPrefsStore.getState().movieGifCalibration);
        const learnt = movieGifCalibrationFrom(file.size, settings, frameCount, held);
        // A file that said nothing hands back what was held, and nothing is written for it.
        if (learnt !== held) {
          rememberGifCalibration(learnt);
        }
      }
    } catch (error) {
      if (!isMovieAbort(error)) {
        setFailure({ what: 'movie', detail: failureDetail(error) });
      }
    } finally {
      if (disk !== null && !sinkHandedOver) {
        // The export ended before a frame was made: the file the question created is taken away.
        void disk.sink.abort();
      }
      if (abortRef.current === controller) {
        abortRef.current = null;
      }
      setRun(null);
    }
  };
  const cancelExport = () => abortRef.current?.abort();

  // ---- a still picture of the moment the preview shows ----
  const [stillBusy, setStillBusy] = useState(false);
  const saveStill = async () => {
    const handle = handleRef.current;
    if (handle === null || timeline === null || movie.trips.length === 0 || recording || stillBusy) {
      return;
    }
    // Playing turns the camera; the picture is taken from where the movie starts, turned as far as
    // the movie has turned by this moment, exactly as the export's frame of it is.
    stopPlaying();
    setFailure(null);
    // Named as the movie is, by the one rule there is for it, so a picture and the movie it was
    // taken from sort together and neither says more than the title caption does.
    const name = movieExportName(
      settings.captions,
      movie.trips.map((trip) => trip.title),
      place,
      localIsoDate(),
      'png',
    );
    setStillBusy(true);
    try {
      // The viewer is taken and handed back before this first waits for anything, so nothing the
      // dialog draws can come between the two; what is waited for is the picture being compressed.
      const picture = await recordMovieStill(
        {
          viewer: handle.viewer,
          constants: handle.cv2,
          settings,
          trips: movie.trips,
          excluded,
          timeline,
          surveyModelId,
          title,
          words,
          clusterLabelAfter: clusterLabel,
        },
        index,
      );
      saveBlob(picture, name);
      message.success(t('caveview.movie.saved', { name }));
    } catch (error) {
      setFailure({ what: 'still', detail: failureDetail(error) });
    } finally {
      setStillBusy(false);
    }
  };

  // ---- what the file will be ----
  const estimate = movieFileSizeEstimate(settings, frameCount, gifCalibration);
  const calibrated = movieEstimateIsCalibrated(settings, gifCalibration);
  const tooLarge = settings.format === 'gif' && estimate > MOVIE_GIF_SIZE_BUDGET;
  // Every frame is rendered, in software where there is no graphics card; a video's length, rate
  // and size can add up to thousands of large frames, and an export of an hour or more.
  const tooLong =
    settings.format !== 'gif' && frameCount * frameSize.width * frameSize.height > MOVIE_LONG_EXPORT_PIXELS;
  const summary = useMemo(() => (
    <Flex vertical gap="small">
      <Typography.Text strong data-testid="movie-summary">
        {t(calibrated ? 'caveview.movie.summaryCalibrated' : 'caveview.movie.summary', {
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
  ), [t, i18n.language, frameCount, estimate, calibrated, tooLarge, tooLong]);

  const tripPicker = useMemo(
    () => (
      <MovieTripPicker
        tracked={tracked.data}
        loading={tracked.isPending}
        listFailed={tracked.error !== null}
        chosen={chosenIds}
        empty={movie.empty}
        tripsFailed={movie.failed}
        logFailed={movie.logFailed}
        newReports={newReports}
        rosters={rosters}
        excluded={excluded}
        onShowCaver={showCaver}
        disabled={recording}
        onChange={chooseTrips}
      />
    ),
    [
      tracked.data,
      tracked.isPending,
      tracked.error,
      chosenIds,
      movie.empty,
      movie.failed,
      movie.logFailed,
      newReports,
      rosters,
      excluded,
      showCaver,
      recording,
      chooseTrips,
    ],
  );

  const nothingChosen = chosenIds.length === 0;
  // A chosen trip that could not be read holds the export back rather than dropping out of it: a
  // movie quietly missing one of the trips somebody ticked would be believed.
  const readFailed = movie.failed.length > 0;
  // The list is what says a trip is this model's. So nothing is exported before it has arrived,
  // and nothing on the strength of a trip's own reads when it failed to arrive at all: a trip ticked
  // before the list was read is only in the movie once the list has confirmed it.
  const listReady = tracked.data !== undefined;
  const blocker =
    tracked.error !== null
      ? t('caveview.movie.trackedLoadError')
      : nothingChosen
        ? t('caveview.movie.chooseTrip')
        : movie.loading || !listReady
          ? t('caveview.movie.tripsLoading')
          : readFailed
            ? t('caveview.movie.tripsFailedHint')
            : movie.trips.length === 0
              ? t('trips.tracking.replay.nothingToReplay')
              : null;
  // What a frame of the movie can be drawn from — the same for one frame as for all of them, so a
  // still is held back by exactly what holds an export back, short of the format it is written in.
  const canDraw =
    previewReady
    && listReady
    && timeline !== null
    && movie.trips.length > 0
    && !readFailed
    && !recording
    && !movie.loading;
  const canExport = canDraw && formatWritable;
  const canPlay = frames !== null && previewReady && !recording;

  // ---- the preview's own keys ----
  const onPreviewKey = (event: KeyboardEvent<HTMLElement>) => {
    // Nothing while an export runs — the preview is the recorder's then — and nothing with a
    // modifier held, which is the browser's or the system's shortcut.
    if (!canPlay || frames === null || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const target = event.target as HTMLElement;
    if (target.closest(TAKES_ITS_OWN_KEYS) !== null) {
      return;
    }
    if (event.key === ' ') {
      // Or the page behind would scroll, as it does for Space pressed on anything that is not a control.
      event.preventDefault();
      // A key held down repeats; play would start and stop many times a second.
      if (!event.repeat) {
        if (playRef.current === null) {
          startPlaying();
        } else {
          stopPlaying();
        }
      }
      return;
    }
    // The slider's handle goes to its own ends on these two by itself.
    if ((event.key === 'Home' || event.key === 'End') && target.getAttribute('role') !== 'slider') {
      event.preventDefault();
      stopPlaying();
      setPosition(event.key === 'Home' ? 0 : frames.count - 1);
    }
  };

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
        <Flex vertical gap="small" className="movie-dialog-preview" onKeyDown={onPreviewKey}>
          {/* The preview can be given the focus, by Tab or by a click on it, so that its keys have
              somewhere to be pressed: a canvas takes no focus of its own. The click is answered
              here, in so many words, and on the press's way down, before the viewer has it: the
              viewer cancels every press on its drawing so that a drag turns the model, and a
              cancelled press moves no focus — left to the browser, the keys went on going to
              whichever button was pressed last. */}
          <div
            className="movie-dialog-preview-keys"
            onPointerDownCapture={(event) => event.currentTarget.focus({ preventScroll: true })}
            role="group"
            tabIndex={0}
            aria-label={t('caveview.movie.preview')}
            aria-description={t('caveview.movie.keysHint')}
            data-testid="movie-preview-keys"
          >
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
          </div>
          <Flex gap="small" align="center">
            <Button
              icon={playing ? <PauseOutlined /> : <CaretRightOutlined />}
              onClick={playing ? stopPlaying : startPlaying}
              disabled={!canPlay}
              aria-label={playing ? t('caveview.movie.stopPreview') : t('caveview.movie.playPreview')}
              data-testid="movie-play"
            />
            <Button
              icon={<CameraOutlined />}
              onClick={() => void saveStill()}
              disabled={!canDraw || stillBusy}
              loading={stillBusy}
              aria-label={t('caveview.movie.saveStill')}
              title={t('caveview.movie.saveStill')}
              data-testid="movie-still"
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
          {blocker === null && (
            <Typography.Text type="secondary" className="movie-setting-help" data-testid="movie-keys-hint">
              {t('caveview.movie.keysHint')}
            </Typography.Text>
          )}
        </Flex>
        <div className="movie-dialog-settings">
          <SettingsForm
            settings={settings}
            onChange={changeSettings}
            disabled={recording}
            formats={formats}
            videoEncoding={typeof VideoEncoder !== 'undefined'}
            layers={preview?.layers ?? null}
            terrain={preview?.terrain === true}
            autoTitle={autoTitle}
            summary={summary}
            onViewAgain={previewReady ? viewAgain : null}
            trips={tripPicker}
            onPreset={applyPreset}
            onReset={resetSettings}
          />
        </div>
      </div>

      <Flex vertical gap="small" className="movie-dialog-footer">
        {movie.logFailed.length > 0 || liveLogFailed ? (
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
            // The icon is named although it is the library's own default: its alert draws a
            // close button for an options object only when that object carries one.
            closable={{
              closeIcon: <CloseOutlined />,
              onClose: () => setFailure(null),
              'aria-label': t('common.close'),
            }}
            title={t(failure.what === 'still' ? 'caveview.movie.stillFailed' : 'caveview.movie.exportFailed')}
            description={failure.detail ?? undefined}
            data-testid={failure.what === 'still' ? 'movie-still-failed' : 'movie-export-failed'}
          />
        )}
        {run !== null && <ExportProgress run={run} />}
        <Alert type="info" showIcon title={t('caveview.movie.privacy')} data-testid="movie-privacy" />
        <Flex justify="flex-end" align="center" gap="small" wrap>
          {movie.trips.length > 0 && (
            <Typography.Text type="secondary" className="movie-dialog-file-name" data-testid="movie-file-name">
              {run === null && movieGoesToDisk(settings.format, estimate)
                ? t('caveview.movie.fileAsked', { name: fileName })
                : t('caveview.movie.fileNamed', { name: run?.name ?? fileName })}
            </Typography.Text>
          )}
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
 * that could not be read says so under its name, and can be unticked. A ticked trip still under way
 * says so, with how many reports have come in since the dialog first read it.
 *
 * Under each ticked trip that has been read is its roster, folded away, to untick the people this
 * movie should not show. Everybody starts ticked. The line it is folded under says how many of the
 * roster appear, so a movie with somebody left out says so without the list being opened.
 */
function MovieTripPicker({
  tracked,
  loading,
  listFailed,
  chosen,
  empty,
  tripsFailed,
  logFailed,
  newReports,
  rosters,
  excluded,
  onShowCaver,
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
  /** Each trip of the movie that is still under way, with the reports arrived since it was first read. */
  newReports: ReadonlyMap<string, number>;
  /** The roster of each trip that has been read, by trip. */
  rosters: ReadonlyMap<string, readonly { caverId: string; name: string }[]>;
  /** The markers left out of the movie, by the id a caver's marker has in a movie. */
  excluded: ReadonlySet<string>;
  onShowCaver: (tripLogId: string, caverId: string, shown: boolean) => void;
  disabled: boolean;
  onChange: (ids: readonly string[]) => void;
}) {
  const { t, i18n } = useTranslation();
  // Which trips' rosters are unfolded: how the list is looked at, not something about the movie.
  const [unfolded, setUnfolded] = useState<ReadonlySet<string>>(NOBODY);
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
        const roster = rosters.get(trip.tripLogId) ?? [];
        const open = unfolded.has(trip.tripLogId);
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
                {checked && newReports.has(trip.tripLogId) && (
                  <Typography.Text
                    type="secondary"
                    className="movie-setting-help"
                    data-testid={`movie-trip-live-${trip.tripLogId}`}
                  >
                    {t('caveview.movie.tripLive', { count: newReports.get(trip.tripLogId) })}
                  </Typography.Text>
                )}
              </Flex>
            </Checkbox>
            {checked && roster.length > 0 && (
              <div className="movie-trip-cavers" data-testid={`movie-trip-cavers-${trip.tripLogId}`}>
                <Button
                  type="link"
                  size="small"
                  icon={open ? <DownOutlined /> : <RightOutlined />}
                  aria-expanded={open}
                  onClick={() =>
                    setUnfolded((before) => {
                      const next = new Set(before);
                      if (!next.delete(trip.tripLogId)) {
                        next.add(trip.tripLogId);
                      }
                      return next;
                    })
                  }
                  data-testid={`movie-trip-cavers-toggle-${trip.tripLogId}`}
                >
                  {t('caveview.movie.whoAppears', {
                    shown: roster.filter((person) => !excluded.has(movieMarkerId(trip.tripLogId, person.caverId))).length,
                    total: roster.length,
                  })}
                </Button>
                {open && (
                  <Flex vertical gap={2} className="movie-trip-cavers-list">
                    <Typography.Text type="secondary" className="movie-setting-help">
                      {t('caveview.movie.whoAppearsHelp')}
                    </Typography.Text>
                    {roster.map((person) => (
                      <Checkbox
                        key={person.caverId}
                        checked={!excluded.has(movieMarkerId(trip.tripLogId, person.caverId))}
                        onChange={(event) => onShowCaver(trip.tripLogId, person.caverId, event.target.checked)}
                        data-testid={`movie-caver-${trip.tripLogId}-${person.caverId}`}
                      >
                        {person.name}
                      </Checkbox>
                    ))}
                  </Flex>
                )}
              </div>
            )}
          </div>
        );
      })}
    </Flex>
  );
}
