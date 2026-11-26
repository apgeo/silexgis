// SPDX-License-Identifier: AGPL-3.0-or-later
import { CloseOutlined, VideoCameraOutlined } from '@ant-design/icons';
import { Alert, App, Checkbox, InputNumber, Progress, Radio, Select, Space, Typography } from 'antd';
import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { DownloadError, saveBlob } from '../../../api/download.ts';
import { localIsoDate, MOVIE_GIF_SIZE_BUDGET, movieFileName, movieFileSizeEstimate, movieSlug } from '../../../caveview/movie/movieOutput.ts';
import { MOVIE_SOCIAL_OUTPUT, moviePresetOutput, type MoviePresetId } from '../../../caveview/movie/moviePresets.ts';
import { MOVIE_GIF_MAX_WIDTH, MOVIE_SIZES, MOVIE_VIDEO_FRAME_RATES } from '../../../caveview/movie/movieSettings.ts';
import { FLYBY_BUNDLE_FPS, FLYBY_BUNDLE_MIN_BLENDER, type FlybyBundleRefusal, type FlybyBundleRevealBy } from '../../../flyby/flybyBlenderBundle.ts';
import { flybyLength } from '../../../flyby/flybyDocument.ts';
import {
  FLYBY_DRAFT_FPS,
  FLYBY_MOVIE_LEVELS,
  DEFAULT_FLYBY_MOVIE_SETTINGS,
  flybyFrameSchedule,
  flybyMovieFrameSize,
  flybyMovieOutput,
  flybyMovieRefusal,
  type FlybyMovieOutput,
  type FlybyMovieSettings,
} from '../../../flyby/flybyMovie.ts';
import {
  isFlybyRecordingAbort,
  recordFlyby,
  recordFlybyToSink,
  type FlybyRecorderStage,
  type FlybyRecording,
  type FlybyRecordingProgress,
  type FlybyRecordingReport,
} from '../../../flyby/flybyRecorder.ts';
import {
  flybyLiveTypes,
  recordFlybyLive,
  type FlybyLiveProgress,
  type FlybyLiveReport,
  type FlybyLiveStage,
} from '../../../flyby/flybyLiveRecorder.ts';
import { drawFlybyTexts, flybyTextsAt } from '../../../flyby/flybyTexts.ts';
import {
  GIF_FRAME_RATES,
  MOVIE_EXTENSION,
  probeMovieFormats,
  type MovieFormat,
  type MovieFormatSupport,
  type MovieQuality,
} from '../../../movie/encode/movieEncoder.ts';
import type { MovieSink } from '../../../movie/encode/movieSink.ts';
import { videoBitrate } from '../../../movie/encode/video/videoEncoder.ts';
import { chooseMovieFile, movieGoesToDisk, type MovieFileChoice } from '../../../movie/movieFileSink.ts';
import { formatExaggeration } from '../../../scene3d/exaggeration3d.ts';
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import Button from '../../Button.tsx';
import { MOVIE_ASPECTS } from '../../caveview/movie/movieChoices.ts';
import {
  downloadFlybyBundle,
  FlybyBundleRefused,
  flybyBundlePreview,
  flybyBundleRouteCaves,
  type FlybyBundleMade,
} from './flybyBundleExport.ts';
import { flybyClock } from './flybyClock.ts';
import type { FlybyController, FlybyScene } from './useFlyby.ts';

export interface FlybyMovieDialogProps {
  flyby: FlybyController;
  scene: FlybyScene;
  /** Told when an export starts and when it ends, however it ends. */
  onExportingChange(exporting: boolean): void;
  onClose(): void;
  /** The panel's own element: the scene is boxed beside it. */
  ref?: Ref<HTMLDivElement>;
}

const FORMATS: readonly MovieFormat[] = ['mp4', 'webm', 'gif'];
const QUALITIES: readonly MovieQuality[] = ['low', 'medium', 'high'];
/** The ready-made files offered, in the order they are shown. */
const PRESETS: readonly MoviePresetId[] = ['hd', 'vertical', 'square', 'chat'];

/**
 * A flyby's camera is always moving, so a GIF of one costs what a turning model's does: the size of
 * a GIF is estimated as the tracking movie estimates one whose camera turns.
 */
const MOVING_CAMERA = { enabled: true, mode: 'speed', degreesPerSecond: 6, clockwise: true } as const;

/** Roughly how many bytes the file comes to: a video by its bitrate, a GIF by what one usually costs. */
function estimatedBytes(settings: FlybyMovieSettings, output: FlybyMovieOutput, frames: number): number {
  if (output.format !== 'gif') {
    return Math.round((videoBitrate(output.width, output.height, output.fps, output.quality) * frames) / output.fps / 8);
  }
  const full = flybyMovieFrameSize(settings);
  const atFullSize = movieFileSizeEstimate(
    { format: 'gif', size: settings.size, fps: output.fps, quality: output.quality, rotation: MOVING_CAMERA },
    frames,
  );
  return Math.round((atFullSize * output.width * output.height) / (full.width * full.height));
}

function fileSizeText(bytes: number, language: string): string {
  if (bytes < 1024 * 1024) {
    return `${Math.max(1, Math.round(bytes / 1024)).toLocaleString(language)} kB`;
  }
  return `${(bytes / (1024 * 1024)).toLocaleString(language, { maximumFractionDigits: 1 })} MB`;
}

function aspectOf(width: number, height: number): string | undefined {
  return MOVIE_ASPECTS.find((aspect) => Math.abs(aspect.ratio - width / height) < 0.01)?.id;
}

interface ExportRun {
  progress: FlybyRecordingProgress | null;
  /** How far a live recording has got. */
  live: FlybyLiveProgress | null;
  /** How far a bundle has got: the flyby being got ready, then the server putting the bundle together. */
  bundle?: 'preparing' | 'asking';
}

/** The words for a bundle the server refused, by the code it refused with. */
const BUNDLE_FAILURES: Record<string, string> = {
  'flyby_bundle.nothing_to_draw': 'flyby.bundle.failed.nothingToDraw',
  'flyby_bundle.too_long': 'flyby.bundle.failed.tooLong',
  'flyby_bundle.frame_rate_unsupported': 'flyby.bundle.failed.frameRate',
};

/** What the panel says once a movie is made. */
type MadeReport =
  | { kind: 'frames'; report: FlybyRecordingReport; level: FlybyMovieOutput['level'] }
  | { kind: 'live'; report: FlybyLiveReport; fps: number };

/**
 * Below this share of the frame rate asked for, a live recording is said to have stuttered: the
 * machine drew too few frames for the movie to look smooth.
 */
const LIVE_SMOOTH_SHARE = 0.8;

/**
 * The panel a flyby is written to a movie file from.
 *
 * <b>The scene is the preview.</b> While this panel is open the scene is boxed to the shape of the
 * movie's frame, beside the panel, and the flyby can be played and scrubbed from its bar: what is
 * seen is what the file will hold. An export then takes the flyby frame by frame, each frame drawn
 * for its own moment, so the file is never jerky however slowly the machine draws.
 *
 * <b>Closing is not how an export is called off, so it asks first.</b> Cancel export says what it
 * does; the close button and Escape are pressed by habit, and while an export runs they ask whether
 * to stop it, opening on "keep going".
 *
 * <b>A copy of what the reader could see.</b> The file outlives the check that let this reader see
 * where the caves are, so the panel says so beside Export, always.
 *
 * <b>A fourth way, live.</b> Where the browser has a recorder of its own, the flyby can instead be
 * played in real time and filmed as it plays: no slower than the flyby, but what the file holds is
 * what the screen showed at the speed this machine drew it. The panel says so beside the choice, and
 * a live recording is written only as the containers that recorder writes.
 *
 * <b>And one that is not a movie at all.</b> Beside the formats stands the Blender bundle: a zip the
 * server writes from the camera's path, which its reader renders with Blender on a machine of their
 * own. Nothing is filmed for it — the camera is read at every frame, at one fixed rate — so the
 * choices about how a frame is taken go away, and in their place the panel lists what of the flyby a
 * bundle does not carry, before it is made.
 */
export default function FlybyMovieDialog({ flyby, scene, onExportingChange, onClose, ref }: FlybyMovieDialogProps) {
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const titleId = useId();
  const settings = useFlybyStore((state) => state.movie);
  const setSettings = useFlybyStore((state) => state.setMovie);
  const change = (next: Partial<FlybyMovieSettings>) => setSettings({ ...settings, ...next });

  const output = flybyMovieOutput(settings);
  const length = flyby.document ? flybyLength(flyby.document) : 0;
  const [part, setPart] = useState<'whole' | 'stretch'>('whole');
  const [stretch, setStretch] = useState<{ from: number; to: number }>({ from: 0, to: length });
  // Whether what is made is a Blender bundle rather than a movie: a choice about this file, kept
  // beside the movie's settings and not among them, so the next movie opens as the last one was.
  const [asBundle, setAsBundle] = useState(false);
  // Whether walls the view is still waiting for a yes about go into a bundle all the same.
  const [includeWalls, setIncludeWalls] = useState(false);
  // Whether the caves of a bundle appear part by part while the clip runs, and by what.
  const [reveal, setReveal] = useState(false);
  const [revealBy, setRevealBy] = useState<FlybyBundleRevealBy>('distance');
  const schedule = flybyFrameSchedule(
    length,
    asBundle ? FLYBY_BUNDLE_FPS : output.fps,
    part === 'whole' ? undefined : stretch,
    settings.holdEndS,
  );
  const refusal = flybyMovieRefusal(output, schedule);
  const estimate = estimatedBytes(settings, output, schedule.count);
  const fileName = movieFileName(
    movieSlug(flyby.name).length > 0 ? flyby.name : 'flyby',
    localIsoDate(),
    MOVIE_EXTENSION[settings.format],
  );

  // ---- what this browser writes, at this size and rate ----
  const [formats, setFormats] = useState<MovieFormatSupport[] | null>(null);
  const firstAnswer = useRef(true);
  const settingsRef = useRef(settings);
  settingsRef.current = settings;

  // What this browser records live: asked once, as the browser's answer does not change.
  const [liveTypes] = useState(() => flybyLiveTypes());
  const levels = FLYBY_MOVIE_LEVELS.filter((level) => level !== 'live' || liveTypes.length > 0);
  const isLive = output.level === 'live' && liveTypes.length > 0;
  const liveType = liveTypes.find((type) => type.format === settings.format) ?? null;
  // A live recording remembered from a browser that had the recorder, opened in one that has not.
  useEffect(() => {
    if (settingsRef.current.level === 'live' && liveTypes.length === 0) {
      setSettings({ ...settingsRef.current, level: DEFAULT_FLYBY_MOVIE_SETTINGS.level });
    }
  }, [liveTypes, setSettings]);
  const chooseLevel = (level: FlybyMovieSettings['level']) => {
    // Recorded live, the file is what the browser's recorder writes; a format it does not write is
    // turned to the one it writes best.
    if (level === 'live' && !liveTypes.some((type) => type.format === settings.format)) {
      change({ level, format: liveTypes[0].format });
      return;
    }
    change({ level });
  };

  useEffect(() => {
    let current = true;
    setFormats(null);
    probeMovieFormats({ width: output.width, height: output.height }, output.fps)
      .catch(() => [{ format: 'gif' as const, supported: true, codec: null }])
      .then((answer) => {
        if (current) {
          setFormats(answer);
        }
      });
    return () => {
      current = false;
    };
  }, [output.width, output.height, output.fps]);
  const encodes = (format: MovieFormat) =>
    format === 'gif' || formats?.some((entry) => entry.format === format && entry.supported) === true;
  const supported = (format: MovieFormat) => (isLive ? liveTypes.some((type) => type.format === format) : encodes(format));
  const writesVideo = formats === null || encodes('webm') || encodes('mp4');
  // Opened for the first time in a browser that cannot write what was remembered, the panel turns to
  // what it can write, rather than open on a refusal.
  // Only on the first answer: a format the person then picks is theirs to keep.
  useEffect(() => {
    if (formats === null || !firstAnswer.current) {
      return;
    }
    firstAnswer.current = false;
    const writes = (format: MovieFormat) =>
      format === 'gif' || formats.some((entry) => entry.format === format && entry.supported);
    const { level, format } = settingsRef.current;
    // What is recorded live is the recorder's to write, not the encoders' asked here.
    const recordedLive = level === 'live' && liveTypes.some((type) => type.format === format);
    if (!recordedLive && !writes(format)) {
      setSettings({ ...settingsRef.current, format: FORMATS.find(writes) ?? 'gif' });
    }
  }, [formats, liveTypes, setSettings]);
  const formatRefused = isLive ? liveType === null : formats !== null && !supported(settings.format);

  const applyPreset = async (id: MoviePresetId) => {
    const preset = await moviePresetOutput(id);
    if (preset === null) {
      void message.warning(
        id === 'vertical' || id === 'square'
          ? t('caveview.movie.presetMp4Unavailable', { size: MOVIE_SOCIAL_OUTPUT[id].size.replace('x', ' × ') })
          : t('caveview.movie.presetHdUnavailable'),
      );
      return;
    }
    const { format, size, fps, quality, holdEndS } = preset;
    setSettings({
      ...useFlybyStore.getState().movie,
      ...(format === undefined ? {} : { format }),
      ...(size === undefined ? {} : { size }),
      ...(fps === undefined ? {} : { fps }),
      ...(quality === undefined ? {} : { quality }),
      ...(holdEndS === undefined ? {} : { holdEndS }),
    });
  };

  // ---- exporting ----
  const [run, setRun] = useState<ExportRun | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [report, setReport] = useState<MadeReport | null>(null);
  const [bundleMade, setBundleMade] = useState<FlybyBundleMade | null>(null);

  // ---- a bundle: what it would leave out, said while the reader is still choosing ----
  const flown = flyby.document;
  const routeCaves = flybyBundleRouteCaves(flown, flyby.preparedCaves());
  const wallsPending = asBundle ? (scene.walls?.pending(routeCaves) ?? []) : [];
  const wallsPendingBytes = wallsPending.reduce((total, walls) => total + walls.bytes, 0);
  const wallsDeclined = wallsPending.length > 0 && !includeWalls;
  const { from: scheduleFrom, to: scheduleTo, count: scheduleCount } = schedule;
  const bundlePreview = useMemo(
    () =>
      asBundle && flown
        ? flybyBundlePreview(flown, scene, { from: scheduleFrom, to: scheduleTo, count: scheduleCount }, wallsDeclined)
        : null,
    [asBundle, flown, scene, scheduleFrom, scheduleTo, scheduleCount, wallsDeclined],
  );
  const bundleRefusalText = (refused: NonNullable<FlybyBundleRefusal>) =>
    refused.kind === 'tooLong' ? t('flyby.bundle.refusal.tooLong', refused) : t(`flyby.bundle.refusal.${refused.kind}`);
  const abortRef = useRef<AbortController | null>(null);
  const choosingFileRef = useRef(false);
  const exporting = run !== null;

  useEffect(() => {
    onExportingChange(exporting);
  }, [exporting, onExportingChange]);
  // A panel taken away mid-export stops it; the recorder hands the scene back on its way out.
  useEffect(
    () => () => {
      abortRef.current?.abort();
      onExportingChange(false);
    },
    [onExportingChange],
  );

  const exportMovie = async () => {
    if (exporting || choosingFileRef.current || !flyby.document || refusal !== null || formatRefused) {
      return;
    }
    // Asked from the press itself: a browser only asks where to save in direct answer to one. A live
    // recording is gathered by the browser's recorder and saved when it ends.
    let disk: { name: string; sink: MovieSink } | null = null;
    if (!isLive && movieGoesToDisk(output.format, estimate)) {
      choosingFileRef.current = true;
      let choice: MovieFileChoice;
      try {
        choice = await chooseMovieFile(fileName, output.format);
      } finally {
        choosingFileRef.current = false;
      }
      if (choice.kind === 'dismissed') {
        return;
      }
      if (choice.kind === 'file') {
        disk = choice;
      }
    }
    let sinkHandedOver = false;
    const controller = new AbortController();
    abortRef.current = controller;
    const name = disk?.name ?? fileName;
    const asked = { output, holdEndS: settings.holdEndS, stretch: part === 'whole' ? undefined : { ...stretch } };
    setFailure(null);
    setReport(null);
    setRun({ progress: null, live: null });
    flyby.pause();
    // The camera as the person left it, before getting the flyby ready moves it about. Handed back
    // by the recorder on its way out, or here when the recorder never took the scene.
    const engine = scene.engine();
    const found = engine?.getCamera();
    // And the moment the flyby was being held at, if it was: the scene goes back to that moment,
    // look and all, rather than to a camera with the viewer's own look under a bar that still says
    // "paused at 0:12".
    const held = flyby.playerState.status === 'idle' ? null : flyby.playerState.t;
    let handedBack = false;
    const handBack = () => {
      handedBack = true;
      // Whatever the frames changed of the scene's look is given back before the camera is.
      flyby.stage.look?.restore();
      if (found) {
        flyby.stage.setCamera(found);
      }
      flyby.stage.refreshData();
      if (held !== null) {
        flyby.seek(held);
      }
    };
    try {
      const timeline = await flyby.ensureTimeline();
      if (!timeline || !engine || controller.signal.aborted) {
        if (!controller.signal.aborted) {
          setFailure(t('flyby.movie.notReady'));
        }
        return;
      }
      const live = () => {
        const now = scene.engine();
        if (!now) {
          throw new Error('The 3D scene has gone.');
        }
        return now;
      };
      const stage: FlybyRecorderStage = {
        setCamera: flyby.stage.setCamera,
        groundHeight: flyby.stage.groundHeight,
        refreshData: flyby.stage.refreshData,
        settle: flyby.stage.settle,
        surfaceSize: () => {
          const surface = scene.surface();
          return { width: surface?.clientWidth ?? 0, height: surface?.clientHeight ?? 0 };
        },
        beginCapture: (size) => live().beginCapture(size),
        captureFrame: () => live().captureFrame(),
        endCapture: () => scene.engine()?.endCapture(),
        credits: () => live().getVisibleCredits(),
        remember: () => handBack,
        // The same look the player puts on the scene at each moment, so a frame shows the
        // background, the layers and the colours the screen showed then.
        ...(flyby.stage.look ? { look: flyby.stage.look } : {}),
      };
      // And the same texts, drawn by the same function the view draws them with.
      const compose: FlybyRecording['compose'] = (context, t, width, height) =>
        drawFlybyTexts(context, flybyTextsAt(timeline.texts, t), width, height);
      if (isLive && liveType !== null) {
        const liveStage: FlybyLiveStage = {
          ...stage,
          now: flyby.stage.now,
          requestFrame: flyby.stage.requestFrame,
          cancelFrame: flyby.stage.cancelFrame,
        };
        const made = await recordFlybyLive({
          timeline,
          stage: liveStage,
          ...asked,
          type: liveType,
          compose,
          signal: controller.signal,
          onProgress: (live) => setRun({ progress: null, live }),
        });
        saveBlob(made.file, name);
        void message.success(t('caveview.movie.saved', { name }));
        setReport({ kind: 'live', report: made.report, fps: output.fps });
        return;
      }
      const recording: FlybyRecording = {
        timeline,
        stage,
        ...asked,
        compose,
        signal: controller.signal,
        onProgress: (progress) => setRun({ progress, live: null }),
      };
      let result: { file: Blob | null; report: FlybyRecordingReport };
      if (disk === null) {
        result = await recordFlyby(recording);
      } else {
        sinkHandedOver = true;
        result = await recordFlybyToSink(recording, disk.sink);
      }
      if (result.file !== null) {
        saveBlob(result.file, name);
      }
      void message.success(t('caveview.movie.saved', { name }));
      setReport({ kind: 'frames', report: result.report, level: output.level });
    } catch (error) {
      if (!isFlybyRecordingAbort(error)) {
        setFailure(error instanceof Error ? error.message : String(error));
      }
    } finally {
      if (disk !== null && !sinkHandedOver) {
        void disk.sink.abort();
      }
      // Getting the flyby ready moves the camera even when nothing comes to be recorded.
      if (!handedBack) {
        handBack();
      }
      if (abortRef.current === controller) {
        abortRef.current = null;
      }
      setRun(null);
    }
  };
  /**
   * Asks the server for the Blender bundle. Nothing is filmed: the flyby is got ready exactly as for
   * a careful movie — its route loaded, so the ground under the camera is the ground a movie finds —
   * and the camera is read at every frame.
   */
  const exportBundle = async () => {
    if (exporting || !flyby.document || bundlePreview?.refusal) {
      return;
    }
    const controller = new AbortController();
    abortRef.current = controller;
    setFailure(null);
    setReport(null);
    setBundleMade(null);
    setRun({ progress: null, live: null, bundle: 'preparing' });
    flyby.pause();
    const engine = scene.engine();
    const found = engine?.getCamera();
    const held = flyby.playerState.status === 'idle' ? null : flyby.playerState.t;
    try {
      // The walls the view waits for a yes about are asked about in this panel, for the bundle;
      // getting the flyby ready does not ask again whether to download them into the view.
      const timeline = await flyby.ensureTimeline(true);
      if (!timeline || !engine || controller.signal.aborted) {
        if (!controller.signal.aborted) {
          setFailure(t('flyby.movie.notReady'));
        }
        return;
      }
      setRun({ progress: null, live: null, bundle: 'asking' });
      const made = await downloadFlybyBundle({
        name: flyby.name,
        timeline,
        schedule: flybyFrameSchedule(
          timeline.length,
          FLYBY_BUNDLE_FPS,
          part === 'whole' ? undefined : { ...stretch },
          settings.holdEndS,
        ),
        size: flybyMovieFrameSize(settings),
        quality: settings.quality,
        scene,
        groundHeight: flyby.stage.groundHeight,
        routeCaves: flyby.preparedCaves(),
        includeWalls,
        reveal: reveal ? revealBy : null,
        reliefWords: (factor) => t('flyby.relief.mark', { factor: formatExaggeration(factor, i18n.language) }),
        language: i18n.language,
        signal: controller.signal,
      });
      setBundleMade(made);
      void message.success(t('flyby.bundle.saved'));
    } catch (error) {
      if (controller.signal.aborted || isFlybyRecordingAbort(error)) {
        return;
      }
      if (error instanceof FlybyBundleRefused) {
        setFailure(bundleRefusalText(error.refusal));
      } else if (error instanceof DownloadError) {
        setFailure(t(BUNDLE_FAILURES[error.code ?? ''] ?? 'flyby.bundle.failed.other'));
      } else {
        setFailure(error instanceof Error ? error.message : String(error));
      }
    } finally {
      // Getting the flyby ready moved the camera about; the view goes back as it was found.
      flyby.stage.look?.restore();
      if (found) {
        flyby.stage.setCamera(found);
      }
      flyby.stage.refreshData();
      if (held !== null) {
        flyby.seek(held);
      }
      if (abortRef.current === controller) {
        abortRef.current = null;
      }
      setRun(null);
    }
  };
  const cancelExport = () => abortRef.current?.abort();

  // ---- closing ----
  const questionRef = useRef<{ destroy(): void } | null>(null);
  const requestClose = useCallback(() => {
    if (abortRef.current === null) {
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
      // Opens on "keep going": Enter pressed by habit after Escape must not throw the export away.
      focusable: { autoFocusButton: 'cancel' },
      onOk: () => {
        questionRef.current = null;
        abortRef.current?.abort();
        onClose();
      },
      onCancel: () => {
        questionRef.current = null;
      },
    });
  }, [modal, onClose, t]);
  // An export that ends while the question is up leaves nothing to ask about.
  useEffect(() => {
    if (!exporting) {
      questionRef.current?.destroy();
      questionRef.current = null;
    }
  }, [exporting]);
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      requestClose();
    }
  };

  // ---- what is shown ----
  const formatNumber = (value: number) => value.toLocaleString(i18n.language, { maximumFractionDigits: 2 });
  const isGif = settings.format === 'gif';
  const rates = isGif ? GIF_FRAME_RATES : MOVIE_VIDEO_FRAME_RATES;
  const sizeLabel = `${output.width} × ${output.height}`;
  // A bundle is rendered at the size chosen; a draft's halving is the movie's own.
  const bundleSize = flybyMovieFrameSize(settings);
  const bundleSizeLabel = `${bundleSize.width} × ${bundleSize.height}`;
  const progress = run?.progress ?? null;
  const liveProgress = run?.live ?? null;

  return (
    <div
      ref={ref}
      className="scene3d-flyby-panel scene3d-flyby-movie"
      role="dialog"
      aria-labelledby={titleId}
      data-testid="flyby-movie-dialog"
      onKeyDown={onKeyDown}
    >
      <div className="scene3d-flyby-panel-header">
        <Typography.Title level={5} id={titleId}>
          <VideoCameraOutlined /> {t('flyby.movie.title')}
        </Typography.Title>
        <Button
          size="small"
          type="text"
          icon={<CloseOutlined />}
          onClick={requestClose}
          aria-label={t('flyby.movie.close')}
          data-testid="flyby-movie-close"
        />
      </div>
      <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
        {t('flyby.movie.framedHint')}
      </Typography.Text>

      <div className="scene3d-flyby-group">
        <Typography.Text strong>{t('flyby.movie.presetsTitle')}</Typography.Text>
        <Space wrap size={4}>
          {PRESETS.map((id) => (
            <Button
              key={id}
              size="small"
              disabled={exporting}
              onClick={() => void applyPreset(id)}
              data-testid={`flyby-movie-preset-${id}`}
            >
              {t(`caveview.movie.presets.${id}`)}
            </Button>
          ))}
        </Space>

        <Typography.Text strong>{t('caveview.movie.format')}</Typography.Text>
        <Radio.Group
          optionType="button"
          size="small"
          value={asBundle ? 'blender' : settings.format}
          disabled={exporting}
          onChange={(event) => {
            const chosen = event.target.value as MovieFormat | 'blender';
            setAsBundle(chosen === 'blender');
            if (chosen !== 'blender') {
              change({ format: chosen });
            }
          }}
          aria-label={t('caveview.movie.format')}
          data-testid="flyby-movie-format"
        >
          {FORMATS.map((format) => (
            <Radio.Button
              key={format}
              value={format}
              disabled={(isLive || formats !== null) && !supported(format) && settings.format !== format}
              data-testid={`flyby-movie-format-${format}`}
            >
              {t(`caveview.movie.formats.${format}`)}
            </Radio.Button>
          ))}
          <Radio.Button value="blender" data-testid="flyby-movie-format-blender">
            {t('flyby.bundle.format')}
          </Radio.Button>
        </Radio.Group>
        <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-movie-format-help">
          {asBundle
            ? t('flyby.bundle.help', { version: FLYBY_BUNDLE_MIN_BLENDER })
            : t(`caveview.movie.formatHelp.${settings.format}`)}
        </Typography.Text>
        {!asBundle && formats === null && !isGif && (
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t('caveview.movie.formatChecking')}
          </Typography.Text>
        )}
        {!asBundle && !writesVideo && (
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-movie-no-video">
            {t('caveview.movie.formatNoVideoEncoder')}
          </Typography.Text>
        )}
        {!asBundle && isLive && liveType === null && (
          <Alert
            type="warning"
            showIcon
            title={t('flyby.movie.liveFormats', {
              formats: liveTypes.map((type) => t(`caveview.movie.formats.${type.format}`)).join(', '),
            })}
            data-testid="flyby-movie-live-format"
          />
        )}
        {!asBundle && !isLive && formatRefused && writesVideo && (
          <Alert
            type="warning"
            showIcon
            title={t('caveview.movie.formatUnsupported', {
              format: t(`caveview.movie.formats.${settings.format}`),
              size: sizeLabel,
              fps: formatNumber(output.fps),
            })}
            data-testid="flyby-movie-format-refused"
          />
        )}

        <div className="scene3d-flyby-field">
          <Typography.Text>{t('caveview.movie.size')}</Typography.Text>
          <Select
            size="small"
            value={settings.size}
            disabled={exporting}
            // Two dozen sizes: all of them in the list, so each can be reached by scrolling it.
            virtual={false}
            style={{ minWidth: 150 }}
            onChange={(size: string) => change({ size })}
            aria-label={t('caveview.movie.size')}
            data-testid="flyby-movie-size"
            options={MOVIE_ASPECTS.map((aspect) => ({
              label: t(`caveview.movie.aspects.${aspect.id}`),
              options: MOVIE_SIZES.filter((entry) => aspectOf(entry.width, entry.height) === aspect.id).map((entry) => ({
                value: entry.id,
                label: `${entry.width} × ${entry.height}`,
                disabled: !asBundle && isGif && !entry.gif,
                title: !asBundle && isGif && !entry.gif ? t('caveview.movie.sizeGifLimit', { width: MOVIE_GIF_MAX_WIDTH }) : undefined,
              })),
            }))}
          />
        </div>
        <div className="scene3d-flyby-field">
          <Typography.Text>{t('caveview.movie.fps')}</Typography.Text>
          <Select
            size="small"
            value={asBundle ? FLYBY_BUNDLE_FPS : settings.fps}
            disabled={exporting || asBundle}
            style={{ minWidth: 80 }}
            onChange={(fps: number) => change({ fps })}
            aria-label={t('caveview.movie.fps')}
            data-testid="flyby-movie-fps"
            options={(asBundle ? [FLYBY_BUNDLE_FPS] : rates).map((rate) => ({ value: rate, label: formatNumber(rate) }))}
          />
        </div>
        {asBundle && (
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-bundle-fps">
            {t('flyby.bundle.fps', { fps: FLYBY_BUNDLE_FPS })}
          </Typography.Text>
        )}
        <div className="scene3d-flyby-field">
          <Typography.Text>{t('caveview.movie.quality')}</Typography.Text>
          <Radio.Group
            optionType="button"
            size="small"
            value={settings.quality}
            disabled={exporting}
            onChange={(event) => change({ quality: event.target.value as MovieQuality })}
            aria-label={t('caveview.movie.quality')}
            data-testid="flyby-movie-quality"
            options={QUALITIES.map((quality) => ({ value: quality, label: t(`caveview.movie.qualities.${quality}`) }))}
          />
        </div>

        {!asBundle && (
          <>
            <Typography.Text strong>{t('flyby.movie.level')}</Typography.Text>
            <Radio.Group
              value={settings.level}
              disabled={settings.draft || exporting}
              onChange={(event) => chooseLevel(event.target.value as FlybyMovieSettings['level'])}
              aria-label={t('flyby.movie.level')}
              data-testid="flyby-movie-level"
              className="scene3d-flyby-movie-choices"
            >
              {levels.map((level) => (
                <Radio key={level} value={level} data-testid={`flyby-movie-level-${level}`}>
                  {t(`flyby.movie.levels.${level}`)}
                </Radio>
              ))}
            </Radio.Group>
            <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
              {settings.draft ? t('flyby.movie.levelHelp.quick') : t(`flyby.movie.levelHelp.${settings.level}`)}
            </Typography.Text>
            <Checkbox checked={settings.draft} disabled={exporting} onChange={(event) => change({ draft: event.target.checked })} data-testid="flyby-movie-draft">
              {t('flyby.movie.draft')}
            </Checkbox>
            <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
              {t('flyby.movie.draftHelp', { fps: isGif ? GIF_FRAME_RATES[0] : FLYBY_DRAFT_FPS })}
            </Typography.Text>
          </>
        )}

        <Typography.Text strong>{t('flyby.movie.part')}</Typography.Text>
        <Radio.Group
          value={part}
          disabled={exporting}
          onChange={(event) => setPart(event.target.value as 'whole' | 'stretch')}
          aria-label={t('flyby.movie.part')}
          data-testid="flyby-movie-part"
          className="scene3d-flyby-movie-choices"
          options={[
            { value: 'whole', label: t('flyby.movie.partWhole', { length: flybyClock(length) }) },
            { value: 'stretch', label: t('flyby.movie.partStretch') },
          ]}
        />
        {part === 'stretch' && (
          <Space size={6} wrap>
            <Typography.Text>{t('flyby.movie.from')}</Typography.Text>
            <InputNumber
              size="small"
              min={0}
              max={length}
              step={0.5}
              value={stretch.from}
              disabled={exporting}
              onChange={(value) => value !== null && setStretch((before) => ({ ...before, from: value }))}
              aria-label={t('flyby.movie.from')}
              data-testid="flyby-movie-from"
            />
            <Typography.Text>{t('flyby.movie.to')}</Typography.Text>
            <InputNumber
              size="small"
              min={0}
              max={length}
              step={0.5}
              value={stretch.to}
              disabled={exporting}
              onChange={(value) => value !== null && setStretch((before) => ({ ...before, to: value }))}
              aria-label={t('flyby.movie.to')}
              data-testid="flyby-movie-to"
            />
          </Space>
        )}
        <div className="scene3d-flyby-field">
          <Typography.Text>{t('caveview.movie.holdEnd')}</Typography.Text>
          <InputNumber
            size="small"
            min={0}
            max={10}
            step={0.5}
            value={settings.holdEndS}
            disabled={exporting}
            onChange={(value) => value !== null && change({ holdEndS: value })}
            aria-label={t('caveview.movie.holdEnd')}
            data-testid="flyby-movie-hold"
          />
        </div>
      </div>

      <Typography.Text data-testid="flyby-movie-summary">
        {asBundle
          ? t('flyby.bundle.summary', {
              count: schedule.count,
              size: bundleSizeLabel,
              length: flybyClock(schedule.count / FLYBY_BUNDLE_FPS),
            })
          : t(isLive ? 'flyby.movie.summaryLive' : 'flyby.movie.summary', {
              count: schedule.count,
              size: sizeLabel,
              length: flybyClock(schedule.count / output.fps),
              bytes: fileSizeText(estimate, i18n.language),
            })}
      </Typography.Text>
      {asBundle && wallsPending.length > 0 && (
        <div className="scene3d-flyby-group">
          <Checkbox
            checked={includeWalls}
            disabled={exporting}
            onChange={(event) => setIncludeWalls(event.target.checked)}
            data-testid="flyby-bundle-walls"
          >
            {t('flyby.bundle.walls', { count: wallsPending.length, size: fileSizeText(wallsPendingBytes, i18n.language) })}
          </Checkbox>
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t('flyby.bundle.wallsHelp')}
          </Typography.Text>
        </div>
      )}
      {asBundle && (
        <div className="scene3d-flyby-group">
          <Checkbox
            checked={reveal}
            disabled={exporting}
            onChange={(event) => setReveal(event.target.checked)}
            data-testid="flyby-bundle-reveal"
          >
            {t('flyby.bundle.reveal')}
          </Checkbox>
          {reveal && (
            <Radio.Group
              size="small"
              value={revealBy}
              disabled={exporting}
              onChange={(event) => setRevealBy(event.target.value as FlybyBundleRevealBy)}
              data-testid="flyby-bundle-reveal-by"
            >
              <Radio.Button value="distance" data-testid="flyby-bundle-reveal-by-distance">
                {t('flyby.bundle.revealByDistance')}
              </Radio.Button>
              <Radio.Button value="date" data-testid="flyby-bundle-reveal-by-date">
                {t('flyby.bundle.revealByDate')}
              </Radio.Button>
            </Radio.Group>
          )}
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t(reveal && revealBy === 'date' ? 'flyby.bundle.revealDateHelp' : 'flyby.bundle.revealHelp')}
          </Typography.Text>
        </div>
      )}
      {asBundle && bundlePreview?.refusal && (
        <Alert type="warning" showIcon title={bundleRefusalText(bundlePreview.refusal)} data-testid="flyby-bundle-refused" />
      )}
      {asBundle && bundlePreview && (
        <div className="scene3d-flyby-group" data-testid="flyby-bundle-left-out">
          <Typography.Text strong>{t('flyby.bundle.leftOutTitle')}</Typography.Text>
          <ul className="scene3d-flyby-bundle-list">
            {bundlePreview.omissions.map((omission) => (
              <li key={omission} data-testid={`flyby-bundle-left-out-${omission}`}>
                {t(`flyby.bundle.leftOut.${omission}`)}
              </li>
            ))}
          </ul>
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t('flyby.bundle.leftOutHelp')}
          </Typography.Text>
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-bundle-pointed">
            {t('flyby.bundle.pointed')}
          </Typography.Text>
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-bundle-daylight">
            {t('flyby.bundle.daylight')}
          </Typography.Text>
        </div>
      )}
      {!asBundle && refusal?.kind === 'gifTooLong' && (
        <Alert type="warning" showIcon title={t('flyby.movie.gifTooLong', refusal)} data-testid="flyby-movie-gif-too-long" />
      )}
      {!asBundle && refusal === null && isGif && estimate > MOVIE_GIF_SIZE_BUDGET && (
        <Alert
          type="info"
          showIcon
          title={t('flyby.movie.gifLarge', { size: fileSizeText(MOVIE_GIF_SIZE_BUDGET, i18n.language) })}
          data-testid="flyby-movie-gif-large"
        />
      )}
      <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-movie-file">
        {asBundle
          ? t('flyby.bundle.fileNamed')
          : run === null && !isLive && movieGoesToDisk(output.format, estimate)
            ? t('caveview.movie.fileAsked', { name: fileName })
            : t('caveview.movie.fileNamed', { name: fileName })}
      </Typography.Text>
      <Alert
        type="info"
        showIcon
        title={asBundle ? t('flyby.bundle.privacy') : t('flyby.movie.privacy')}
        data-testid="flyby-movie-privacy"
      />

      {run !== null && run.bundle !== undefined && (
        <div data-testid="flyby-movie-progress" role="status">
          <Progress percent={run.bundle === 'asking' ? 60 : 20} size="small" status="active" showInfo={false} />
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t(`flyby.bundle.progress.${run.bundle}`)}
          </Typography.Text>
        </div>
      )}
      {run !== null && run.bundle === undefined && liveProgress !== null && (
        <div data-testid="flyby-movie-progress" role="status">
          <Progress percent={liveProgress.total > 0 ? Math.floor((liveProgress.seconds / liveProgress.total) * 100) : 0} size="small" />
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t('flyby.movie.liveProgress', { done: flybyClock(liveProgress.seconds), total: flybyClock(liveProgress.total) })}
          </Typography.Text>
        </div>
      )}
      {run !== null && run.bundle === undefined && liveProgress === null && (
        <div data-testid="flyby-movie-progress" role="status">
          <Progress
            percent={progress === null || progress.total === 0 ? 0 : Math.floor((progress.done / progress.total) * 100)}
            size="small"
          />
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {progress === null
              ? t('flyby.movie.preparing')
              : t(`caveview.movie.progress.${progress.stage}`, { done: progress.step, total: progress.steps })}
            {progress?.secondsLeft != null && progress.stage !== 'finishing'
              ? ` · ${t('flyby.movie.timeLeft', { time: flybyClock(progress.secondsLeft) })}`
              : ''}
          </Typography.Text>
        </div>
      )}
      {failure !== null && (
        <Alert
          type="error"
          showIcon
          title={t('caveview.movie.exportFailed')}
          description={failure}
          data-testid="flyby-movie-failed"
        />
      )}
      {!asBundle && report?.kind === 'live' && (
        <Alert
          type={report.report.fps < report.fps * LIVE_SMOOTH_SHARE ? 'warning' : 'success'}
          showIcon
          data-testid="flyby-movie-report"
          title={t('flyby.movie.liveReport', {
            count: report.report.frames,
            time: flybyClock(report.report.seconds),
            fps: report.report.fps.toLocaleString(i18n.language, { maximumFractionDigits: 1 }),
          })}
          description={report.report.fps < report.fps * LIVE_SMOOTH_SHARE ? t('flyby.movie.liveReportSlow', { fps: report.fps }) : undefined}
        />
      )}
      {!asBundle && report?.kind === 'frames' && report.level !== 'quick' && (
        <Alert
          type={report.report.unsettledFrames > 0 ? 'warning' : 'success'}
          showIcon
          data-testid="flyby-movie-report"
          title={t('flyby.movie.reportFrames', { count: report.report.frames, time: flybyClock(report.report.seconds) })}
          description={
            report.report.unsettledFrames > 0
              ? t('flyby.movie.reportUnsettled', { count: report.report.unsettledFrames })
              : t('flyby.movie.reportSettled')
          }
        />
      )}

      {asBundle && bundleMade !== null && (
        <Alert
          type={bundleMade.notDrawn > 0 || bundleMade.wallsLeftOut > 0 || bundleMade.markersNotDrawn > 0 ? 'warning' : 'success'}
          showIcon
          data-testid="flyby-bundle-report"
          title={t('flyby.bundle.saved')}
          description={
            <ul className="scene3d-flyby-bundle-list">
              {bundleMade.walls > 0 && <li>{t('flyby.bundle.reportWalls', { count: bundleMade.walls })}</li>}
              {bundleMade.lines > 0 && <li>{t('flyby.bundle.reportLines', { count: bundleMade.lines })}</li>}
              {bundleMade.wallsLeftOut > 0 && (
                <li data-testid="flyby-bundle-report-walls-left-out">
                  {t('flyby.bundle.reportWallsLeftOut', { count: bundleMade.wallsLeftOut })}
                </li>
              )}
              {bundleMade.markers > 0 && (
                <li data-testid="flyby-bundle-report-markers">{t('flyby.bundle.reportMarkers', { count: bundleMade.markers })}</li>
              )}
              {bundleMade.photos > 0 && (
                <li data-testid="flyby-bundle-report-photos">{t('flyby.bundle.reportPhotos', { count: bundleMade.photos })}</li>
              )}
              {bundleMade.markersNotDrawn > 0 && (
                <li data-testid="flyby-bundle-report-markers-not-drawn">
                  {t('flyby.bundle.reportMarkersNotDrawn', { count: bundleMade.markersNotDrawn })}
                </li>
              )}
              {bundleMade.notDrawn > 0 && (
                <li data-testid="flyby-bundle-report-not-drawn">{t('flyby.bundle.reportNotDrawn', { count: bundleMade.notDrawn })}</li>
              )}
              {bundleMade.undatedLegs > 0 && (
                <li data-testid="flyby-bundle-report-undated-legs">
                  {t('flyby.bundle.reportUndatedLegs', { count: bundleMade.undatedLegs })}
                </li>
              )}
              {bundleMade.cavesOnSurveyDate > 0 && (
                <li data-testid="flyby-bundle-report-caves-on-survey-date">
                  {t('flyby.bundle.reportCavesOnSurveyDate', { count: bundleMade.cavesOnSurveyDate })}
                </li>
              )}
              {bundleMade.undatedCaves > 0 && (
                <li data-testid="flyby-bundle-report-undated-caves">
                  {t('flyby.bundle.reportUndatedCaves', { count: bundleMade.undatedCaves })}
                </li>
              )}
              <li>{t('flyby.bundle.reportReadme')}</li>
            </ul>
          }
        />
      )}

      <Space>
        {run === null && asBundle ? (
          <Button
            type="primary"
            icon={<VideoCameraOutlined />}
            disabled={!flyby.document || bundlePreview === null || bundlePreview.refusal !== null}
            onClick={() => void exportBundle()}
            data-testid="flyby-movie-export"
          >
            {t('flyby.bundle.export')}
          </Button>
        ) : run === null ? (
          <Button
            type="primary"
            icon={<VideoCameraOutlined />}
            disabled={!flyby.document || refusal !== null || formatRefused || (!isLive && formats === null)}
            onClick={() => void exportMovie()}
            data-testid="flyby-movie-export"
          >
            {t('caveview.movie.export', { format: t(`caveview.movie.formats.${settings.format}`) })}
          </Button>
        ) : (
          <Button danger onClick={cancelExport} data-testid="flyby-movie-cancel">
            {t('caveview.movie.cancelExport')}
          </Button>
        )}
      </Space>
    </div>
  );
}
