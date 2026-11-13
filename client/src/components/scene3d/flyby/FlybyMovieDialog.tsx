// SPDX-License-Identifier: AGPL-3.0-or-later
import { CloseOutlined, VideoCameraOutlined } from '@ant-design/icons';
import { Alert, App, Checkbox, InputNumber, Progress, Radio, Select, Space, Typography } from 'antd';
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { saveBlob } from '../../../api/download.ts';
import { localIsoDate, MOVIE_GIF_SIZE_BUDGET, movieFileName, movieFileSizeEstimate, movieSlug } from '../../../caveview/movie/movieOutput.ts';
import { MOVIE_SOCIAL_OUTPUT, moviePresetOutput, type MoviePresetId } from '../../../caveview/movie/moviePresets.ts';
import { MOVIE_GIF_MAX_WIDTH, MOVIE_SIZES, MOVIE_VIDEO_FRAME_RATES } from '../../../caveview/movie/movieSettings.ts';
import { flybyLength } from '../../../flyby/flybyDocument.ts';
import {
  FLYBY_DRAFT_FPS,
  FLYBY_MOVIE_LEVELS,
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
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import Button from '../../Button.tsx';
import { MOVIE_ASPECTS } from '../../caveview/movie/movieChoices.ts';
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
}

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
 * The way each frame is taken is a list of choices rather than a switch so that a fourth — a live
 * recording by the browser's own recorder, which films what the screen shows at the speed it is
 * drawn — has a place to go.
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
  const schedule = flybyFrameSchedule(length, output.fps, part === 'whole' ? undefined : stretch, settings.holdEndS);
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
  const supported = (format: MovieFormat) =>
    format === 'gif' || formats?.some((entry) => entry.format === format && entry.supported) === true;
  const writesVideo = formats === null || supported('webm') || supported('mp4');
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
    if (!writes(settingsRef.current.format)) {
      setSettings({ ...settingsRef.current, format: FORMATS.find(writes) ?? 'gif' });
    }
  }, [formats, setSettings]);
  const formatRefused = formats !== null && !supported(settings.format);

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
  const [report, setReport] = useState<{ report: FlybyRecordingReport; level: FlybyMovieOutput['level'] } | null>(null);
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
    // Asked from the press itself: a browser only asks where to save in direct answer to one.
    let disk: { name: string; sink: MovieSink } | null = null;
    if (movieGoesToDisk(output.format, estimate)) {
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
    setRun({ progress: null });
    flyby.pause();
    // The camera as the person left it, before getting the flyby ready moves it about. Handed back
    // by the recorder on its way out, or here when the recorder never took the scene.
    const engine = scene.engine();
    const found = engine?.getCamera();
    let handedBack = false;
    const handBack = () => {
      handedBack = true;
      if (found) {
        flyby.stage.setCamera(found);
      }
      flyby.stage.refreshData();
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
        getCamera: flyby.stage.getCamera,
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
      };
      const recording: FlybyRecording = {
        timeline,
        stage,
        ...asked,
        signal: controller.signal,
        onProgress: (progress) => setRun({ progress }),
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
      setReport({ report: result.report, level: output.level });
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
  const progress = run?.progress ?? null;

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
          value={settings.format}
          disabled={exporting}
          onChange={(event) => change({ format: event.target.value as MovieFormat })}
          aria-label={t('caveview.movie.format')}
          data-testid="flyby-movie-format"
        >
          {FORMATS.map((format) => (
            <Radio.Button
              key={format}
              value={format}
              disabled={formats !== null && !supported(format) && settings.format !== format}
              data-testid={`flyby-movie-format-${format}`}
            >
              {t(`caveview.movie.formats.${format}`)}
            </Radio.Button>
          ))}
        </Radio.Group>
        <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
          {t(`caveview.movie.formatHelp.${settings.format}`)}
        </Typography.Text>
        {formats === null && !isGif && (
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
            {t('caveview.movie.formatChecking')}
          </Typography.Text>
        )}
        {!writesVideo && (
          <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-movie-no-video">
            {t('caveview.movie.formatNoVideoEncoder')}
          </Typography.Text>
        )}
        {formatRefused && writesVideo && (
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
            style={{ minWidth: 150 }}
            onChange={(size: string) => change({ size })}
            aria-label={t('caveview.movie.size')}
            data-testid="flyby-movie-size"
            options={MOVIE_ASPECTS.map((aspect) => ({
              label: t(`caveview.movie.aspects.${aspect.id}`),
              options: MOVIE_SIZES.filter((entry) => aspectOf(entry.width, entry.height) === aspect.id).map((entry) => ({
                value: entry.id,
                label: `${entry.width} × ${entry.height}`,
                disabled: isGif && !entry.gif,
                title: isGif && !entry.gif ? t('caveview.movie.sizeGifLimit', { width: MOVIE_GIF_MAX_WIDTH }) : undefined,
              })),
            }))}
          />
        </div>
        <div className="scene3d-flyby-field">
          <Typography.Text>{t('caveview.movie.fps')}</Typography.Text>
          <Select
            size="small"
            value={settings.fps}
            disabled={exporting}
            style={{ minWidth: 80 }}
            onChange={(fps: number) => change({ fps })}
            aria-label={t('caveview.movie.fps')}
            data-testid="flyby-movie-fps"
            options={rates.map((rate) => ({ value: rate, label: formatNumber(rate) }))}
          />
        </div>
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

        <Typography.Text strong>{t('flyby.movie.level')}</Typography.Text>
        <Radio.Group
          value={settings.level}
          disabled={settings.draft || exporting}
          onChange={(event) => change({ level: event.target.value as FlybyMovieSettings['level'] })}
          aria-label={t('flyby.movie.level')}
          data-testid="flyby-movie-level"
          className="scene3d-flyby-movie-choices"
        >
          {FLYBY_MOVIE_LEVELS.map((level) => (
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
        {t('flyby.movie.summary', {
          count: schedule.count,
          size: sizeLabel,
          length: flybyClock(schedule.count / output.fps),
          bytes: fileSizeText(estimate, i18n.language),
        })}
      </Typography.Text>
      {refusal?.kind === 'gifTooLong' && (
        <Alert type="warning" showIcon title={t('flyby.movie.gifTooLong', refusal)} data-testid="flyby-movie-gif-too-long" />
      )}
      {refusal === null && isGif && estimate > MOVIE_GIF_SIZE_BUDGET && (
        <Alert
          type="info"
          showIcon
          title={t('flyby.movie.gifLarge', { size: fileSizeText(MOVIE_GIF_SIZE_BUDGET, i18n.language) })}
          data-testid="flyby-movie-gif-large"
        />
      )}
      <Typography.Text type="secondary" className="scene3d-flyby-movie-help" data-testid="flyby-movie-file">
        {run === null && movieGoesToDisk(output.format, estimate)
          ? t('caveview.movie.fileAsked', { name: fileName })
          : t('caveview.movie.fileNamed', { name: fileName })}
      </Typography.Text>
      <Alert type="info" showIcon title={t('flyby.movie.privacy')} data-testid="flyby-movie-privacy" />

      {run !== null && (
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
      {report !== null && report.level !== 'quick' && (
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

      <Space>
        {run === null ? (
          <Button
            type="primary"
            icon={<VideoCameraOutlined />}
            disabled={!flyby.document || refusal !== null || formatRefused || formats === null}
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
