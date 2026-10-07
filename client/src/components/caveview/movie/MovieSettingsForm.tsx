// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  Alert,
  Button,
  Checkbox,
  Collapse,
  ConfigProvider,
  Flex,
  Input,
  InputNumber,
  Radio,
  Select,
  Slider,
  Switch,
  Tooltip,
  Typography,
} from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { MovieFormatSupport } from '../../../caveview/movie/encode/movieEncoder.ts';
import {
  MOVIE_GIF_MAX_FRAMES,
  MOVIE_GIF_MAX_WIDTH,
  MOVIE_LABEL_SIZE_RANGE,
  MOVIE_SIZES,
  MOVIE_VIDEO_FRAME_RATES,
  MOVIE_VIEW_DIRECTIONS,
  MOVIE_VIEW_LAYERS,
  normaliseMovieSettings,
  type MovieCaverLabels,
  type MovieColourBy,
  type MovieFormat,
  type MovieQuality,
  type MovieSettings,
  type MovieViewDirection,
  type MovieViewLayer,
} from '../../../caveview/movie/movieSettings.ts';
import { GIF_FRAME_RATES } from '../../../caveview/movie/encode/movieEncoder.ts';
import { MOVIE_PRESETS, type MoviePresetId } from '../../../caveview/movie/moviePresets.ts';
import {
  MOVIE_ASPECTS,
  MOVIE_CAVER_LABELS,
  MOVIE_COLOUR_BY,
  MOVIE_FORMATS,
  MOVIE_QUALITIES,
  MOVIE_SHADINGS,
  type MovieSettingsGroup,
  type MovieShadingId,
} from './movieChoices.ts';

/**
 * Every setting of a tracking movie, in the groups a reader looks for them in: the trips, the
 * file, how it moves, how the cavers are drawn, what of the model shows, and what is written over
 * it.
 *
 * <b>A choice the movie cannot take is shown and refused, not hidden.</b> A format this browser
 * cannot write, a size a GIF is not made at, a layer the model has none of, a shading that needs
 * terrain: each stays in its list, disabled, saying why — so a reader who knows the option exists
 * learns why it is not on offer here rather than wondering where it went.
 *
 * <b>Every change goes through the settings' own repair</b>, which holds a GIF to its frame rates,
 * its widest size and its frame count. So switching to GIF from a large video shrinks the frame to
 * the largest GIF of the same shape, rather than leaving a combination nothing can make.
 */

export interface MovieSettingsFormProps {
  settings: MovieSettings;
  onChange: (settings: MovieSettings) => void;
  /** While an export runs nothing may change under it. */
  disabled: boolean;
  /** What this browser can write at the chosen size and rate; null while that is being asked. */
  formats: readonly MovieFormatSupport[] | null;
  /** Whether this browser has a video encoder at all, which decides how a refusal is worded. */
  videoEncoding: boolean;
  /** The layers the loaded model has; null until it has loaded. */
  layers: ReadonlySet<MovieViewLayer> | null;
  /** Whether the loaded model stands on real terrain. */
  terrain: boolean;
  /** The title the movie takes when the reader writes none. */
  autoTitle: string;
  /** The trip picker, shown as the first group. */
  trips: ReactNode;
  /**
   * Turns the preview to the starting view again, after the reader turned or zoomed it away; null
   * until there is a preview to turn.
   */
  onViewAgain: (() => void) | null;
  /** The file's frame count and estimated size, shown under the output settings. */
  summary: ReactNode;
  /**
   * Sets the file up for a purpose. Asked of the dialog rather than done here, since one preset has
   * to ask the browser which video it can write before it changes anything.
   */
  onPreset: (preset: MoviePresetId) => void;
  /** Puts every setting back to what a first opening shows. */
  onReset: () => void;
}

function Row({ label, help, children }: { label: ReactNode; help?: ReactNode; children: ReactNode }) {
  return (
    <div className="movie-setting">
      <Flex justify="space-between" align="center" gap="small" wrap>
        <Typography.Text>{label}</Typography.Text>
        {children}
      </Flex>
      {help !== undefined && help !== null && (
        <Typography.Text type="secondary" className="movie-setting-help">
          {help}
        </Typography.Text>
      )}
    </div>
  );
}

/**
 * A slider with its value written beside it, in the words `format` gives. Written rather than put in
 * a tooltip: a tooltip kept open on a focused handle while the value steps quickly re-renders its
 * popup container in a loop until React gives up — and the value beside the slider says it anyway.
 */
function SliderWithValue({
  min,
  max,
  step,
  value,
  marks,
  format,
  onChange,
  testId,
  label,
}: {
  min: number;
  max: number;
  step: number;
  value: number;
  marks?: Record<number, string>;
  format: (value: number) => string;
  onChange: (value: number) => void;
  testId: string;
  /** The handle's accessible name: the slider passes no label of its own to it. */
  label: string;
}) {
  return (
    <Flex gap="small" align="center" style={{ minWidth: 200 }}>
      <Slider
        style={{ flex: 1 }}
        min={min}
        max={max}
        step={step}
        value={value}
        marks={marks}
        ariaLabelForHandle={label}
        tooltip={{ formatter: null }}
        ariaValueTextFormatterForHandle={(shown) => (shown === undefined ? '' : format(shown))}
        onChange={(next: number) => onChange(next)}
      />
      <Typography.Text className="movie-setting-value" data-testid={testId}>
        {format(value)}
      </Typography.Text>
    </Flex>
  );
}

/** The width in pixels the viewer draws its lines at, for its 0..1 line-width slider. */
function lineWidthPixels(slider: number): number {
  return slider * 10 + 1;
}

/**
 * How many times their true height the viewer draws a model's heights, for its 0..1 vertical-scale
 * slider: true height at the middle, a quarter of it at 0 and four times it at 1.
 */
function verticalScaleFactor(slider: number): number {
  return 2 ** ((slider - 0.5) * 4);
}

function aspectOf(width: number, height: number): (typeof MOVIE_ASPECTS)[number]['id'] {
  const ratio = width / height;
  return MOVIE_ASPECTS.reduce((best, aspect) =>
    Math.abs(aspect.ratio - ratio) < Math.abs(best.ratio - ratio) ? aspect : best,
  ).id;
}

export default function MovieSettingsForm({
  settings,
  onChange,
  disabled,
  formats,
  videoEncoding,
  layers,
  terrain,
  autoTitle,
  trips,
  summary,
  onViewAgain,
  onPreset,
  onReset,
}: MovieSettingsFormProps) {
  const { t, i18n } = useTranslation();
  const formatNumber = (value: number, digits: number) =>
    value.toLocaleString(i18n.language, { maximumFractionDigits: digits });
  const change = (next: MovieSettings) => onChange(normaliseMovieSettings(next));
  const patch = <G extends 'rotation' | 'timeline' | 'cavers' | 'view' | 'captions'>(
    group: G,
    values: Partial<MovieSettings[G]>,
  ) => change({ ...settings, [group]: { ...settings[group], ...values } });

  const size = MOVIE_SIZES.find((entry) => entry.id === settings.size) ?? MOVIE_SIZES[0];
  const isGif = settings.format === 'gif';
  const rates = isGif ? GIF_FRAME_RATES : MOVIE_VIDEO_FRAME_RATES;
  const gifMaxSeconds = (MOVIE_GIF_MAX_FRAMES - Math.round(settings.holdEndS * settings.fps)) / settings.fps;

  const formatRefusal = (format: MovieFormat): string | null => {
    if (format === 'gif') return null;
    const support = formats?.find((entry) => entry.format === format);
    if (support === undefined || support.supported) return null;
    return videoEncoding
      ? t('caveview.movie.formatUnsupported', {
          format: t(`caveview.movie.formats.${format}`),
          size: `${size.width} × ${size.height}`,
          fps: settings.fps,
        })
      : t('caveview.movie.formatNoVideoEncoder');
  };

  // What the model has is known once it has loaded, and so is whether it stands on terrain.
  const modelLoaded = layers !== null;

  const output = (
    <Flex vertical gap="middle">
      <Row label={t('caveview.movie.format')} help={t(`caveview.movie.formatHelp.${settings.format}`)}>
        <Radio.Group
          optionType="button"
          value={settings.format}
          onChange={(event) => change({ ...settings, format: event.target.value as MovieFormat })}
          data-testid="movie-format"
          aria-label={t('caveview.movie.format')}
        >
          {MOVIE_FORMATS.map((format) => {
            const refusal = formatRefusal(format);
            // The checked format stays pressable, so a refusal of the one in use can be read and
            // another picked, but is not offered as a choice to switch to.
            const button = (
              <Radio.Button
                key={format}
                value={format}
                disabled={disabled || (refusal !== null && settings.format !== format)}
                data-testid={`movie-format-${format}`}
              >
                {t(`caveview.movie.formats.${format}`)}
              </Radio.Button>
            );
            return refusal === null ? (
              button
            ) : (
              <Tooltip key={format} title={refusal}>
                {button}
              </Tooltip>
            );
          })}
        </Radio.Group>
      </Row>
      {formats === null && !isGif && (
        <Typography.Text type="secondary">{t('caveview.movie.formatChecking')}</Typography.Text>
      )}
      {formatRefusal(settings.format) !== null && (
        <Alert type="warning" showIcon title={formatRefusal(settings.format)} data-testid="movie-format-refused" />
      )}
      {/* The other formats' refusals, written out: a disabled button shows no tooltip in every
          browser, and a reader looking for a video should not have to guess why there is none. */}
      {[
        ...new Set(
          MOVIE_FORMATS.filter((format) => format !== settings.format)
            .map(formatRefusal)
            .filter((refusal): refusal is string => refusal !== null),
        ),
      ].map((refusal) => (
        <Typography.Text
          key={refusal}
          type="secondary"
          className="movie-setting-help"
          data-testid="movie-format-refusal"
        >
          {refusal}
        </Typography.Text>
      ))}
      <Row
        label={t('caveview.movie.size')}
        help={isGif ? t('caveview.movie.sizeGifLimit', { width: MOVIE_GIF_MAX_WIDTH }) : undefined}
      >
        <Select
          value={settings.size}
          style={{ minWidth: 180 }}
          onChange={(value: string) => change({ ...settings, size: value })}
          data-testid="movie-size"
          aria-label={t('caveview.movie.size')}
          options={MOVIE_ASPECTS.map((aspect) => ({
            label: t(`caveview.movie.aspects.${aspect.id}`),
            options: MOVIE_SIZES.filter((entry) => aspectOf(entry.width, entry.height) === aspect.id).map((entry) => ({
              value: entry.id,
              label: `${entry.width} × ${entry.height}`,
              disabled: isGif && !entry.gif,
              title:
                isGif && !entry.gif
                  ? t('caveview.movie.sizeGifLimit', {
                      width: MOVIE_GIF_MAX_WIDTH,
                    })
                  : undefined,
            })),
          }))}
        />
      </Row>
      <Row label={t('caveview.movie.fps')} help={isGif ? t('caveview.movie.fpsGifHelp') : undefined}>
        <Select
          value={settings.fps}
          style={{ minWidth: 100 }}
          onChange={(value: number) => change({ ...settings, fps: value })}
          data-testid="movie-fps"
          aria-label={t('caveview.movie.fps')}
          options={rates.map((rate) => ({
            value: rate,
            label: rate.toLocaleString(i18n.language),
          }))}
        />
      </Row>
      <Row
        label={t('caveview.movie.duration')}
        help={
          isGif
            ? t('caveview.movie.durationGifLimit', {
                frames: MOVIE_GIF_MAX_FRAMES,
                seconds: gifMaxSeconds.toLocaleString(i18n.language, { maximumFractionDigits: 2 }),
              })
            : undefined
        }
      >
        {/* The same bound the settings' repair cuts the length to, unrounded: a field whose limit
            were rounded down would show the length the movie will have as out of range. */}
        <InputNumber
          min={1}
          max={isGif ? gifMaxSeconds : 300}
          step={1}
          value={settings.durationS}
          onChange={(value) => value !== null && change({ ...settings, durationS: value })}
          data-testid="movie-duration"
          aria-label={t('caveview.movie.duration')}
        />
      </Row>
      <Row label={t('caveview.movie.holdEnd')}>
        <InputNumber
          min={0}
          max={10}
          step={0.5}
          value={settings.holdEndS}
          onChange={(value) => value !== null && change({ ...settings, holdEndS: value })}
          data-testid="movie-hold"
          aria-label={t('caveview.movie.holdEnd')}
        />
      </Row>
      <Row
        label={t('caveview.movie.quality')}
        help={isGif ? t('caveview.movie.qualityHelpGif') : t('caveview.movie.qualityHelpVideo')}
      >
        <Radio.Group
          optionType="button"
          value={settings.quality}
          onChange={(event) => change({ ...settings, quality: event.target.value as MovieQuality })}
          data-testid="movie-quality"
          aria-label={t('caveview.movie.quality')}
          options={MOVIE_QUALITIES.map((quality) => ({
            value: quality,
            label: t(`caveview.movie.qualities.${quality}`),
          }))}
        />
      </Row>
      {summary}
    </Flex>
  );

  const { rotation, timeline, cavers, view, captions } = settings;
  const motion = (
    <Flex vertical gap="middle">
      <Row label={t('caveview.movie.rotation')}>
        <Switch
          checked={rotation.enabled}
          onChange={(enabled) => patch('rotation', { enabled })}
          data-testid="movie-rotation"
          aria-label={t('caveview.movie.rotation')}
        />
      </Row>
      <Row label={t('caveview.movie.rotationMode')}>
        <Radio.Group
          disabled={disabled || !rotation.enabled}
          aria-label={t('caveview.movie.rotationMode')}
          value={rotation.mode}
          onChange={(event) =>
            patch('rotation', {
              mode: event.target.value as 'speed' | 'fullTurn',
            })
          }
          options={(['speed', 'fullTurn'] as const).map((mode) => ({
            value: mode,
            label: t(`caveview.movie.rotationModes.${mode}`),
          }))}
        />
      </Row>
      <Row label={t('caveview.movie.degreesPerSecond')}>
        <InputNumber
          min={0.5}
          max={90}
          step={0.5}
          disabled={disabled || !rotation.enabled || rotation.mode !== 'speed'}
          value={rotation.degreesPerSecond}
          onChange={(value) => value !== null && patch('rotation', { degreesPerSecond: value })}
          data-testid="movie-degrees"
          aria-label={t('caveview.movie.degreesPerSecond')}
        />
      </Row>
      <Row label={t('caveview.movie.direction')}>
        <Radio.Group
          disabled={disabled || !rotation.enabled}
          aria-label={t('caveview.movie.direction')}
          value={rotation.clockwise}
          onChange={(event) => patch('rotation', { clockwise: event.target.value as boolean })}
          options={[
            { value: true, label: t('caveview.movie.clockwise') },
            { value: false, label: t('caveview.movie.counterClockwise') },
          ]}
        />
      </Row>
      <Row label={t('caveview.movie.transition')} help={t('caveview.movie.transitionHelp')}>
        <InputNumber
          min={0}
          max={10}
          step={0.1}
          value={cavers.transitionS}
          onChange={(value) => value !== null && patch('cavers', { transitionS: value })}
          data-testid="movie-transition"
          aria-label={t('caveview.movie.transition')}
        />
      </Row>
      <Row label={t('caveview.movie.timelineMode')} help={t(`caveview.movie.timelineHelp.${timeline.mode}`)}>
        <Radio.Group
          value={timeline.mode}
          onChange={(event) =>
            patch('timeline', {
              mode: event.target.value as 'calendar' | 'together',
            })
          }
          data-testid="movie-timeline"
          aria-label={t('caveview.movie.timelineMode')}
          options={(['calendar', 'together'] as const).map((mode) => ({
            value: mode,
            label: t(`caveview.movie.timelineModes.${mode}`),
          }))}
        />
      </Row>
      {/* In either order of play: side by side, a stretch in which none of the trips reports
          anything is as empty as it is in calendar order, and is cut short the same way. */}
      <Row label={t('caveview.movie.shortenQuiet')} help={t('caveview.movie.shortenQuietHelp')}>
        <Flex gap="small" align="center">
          <Switch
            checked={timeline.shortenQuiet}
            onChange={(shortenQuiet) => patch('timeline', { shortenQuiet })}
            data-testid="movie-shorten-quiet"
            aria-label={t('caveview.movie.shortenQuiet')}
          />
          <InputNumber
            min={1}
            max={1440}
            disabled={disabled || !timeline.shortenQuiet}
            value={timeline.quietGapMin}
            suffix={t('caveview.movie.minutes')}
            onChange={(value) => value !== null && patch('timeline', { quietGapMin: value })}
            aria-label={t('caveview.movie.quietGap')}
            data-testid="movie-quiet-gap"
          />
        </Flex>
      </Row>
    </Flex>
  );

  const caverSettings = (
    <Flex vertical gap="middle">
      <Row label={t('caveview.movie.labels')} help={t('caveview.movie.labelsHelp')}>
        <Select
          value={cavers.labels}
          style={{ minWidth: 150 }}
          onChange={(labels: MovieCaverLabels) => patch('cavers', { labels })}
          data-testid="movie-labels"
          aria-label={t('caveview.movie.labels')}
          options={MOVIE_CAVER_LABELS.map((mode) => ({
            value: mode,
            label: t(`caveview.movie.caverLabels.${mode}`),
          }))}
        />
      </Row>
      <Row label={t('caveview.movie.labelSize')} help={t('caveview.movie.labelSizeHelp')}>
        <Flex gap="small" align="center" style={{ minWidth: 200 }}>
          <Slider
            style={{ flex: 1 }}
            min={MOVIE_LABEL_SIZE_RANGE.min}
            max={MOVIE_LABEL_SIZE_RANGE.max}
            disabled={disabled || cavers.labels === 'off'}
            value={cavers.labelSize}
            // Written beside it instead, as the other sliders here are.
            tooltip={{ formatter: null }}
            ariaLabelForHandle={t('caveview.movie.labelSize')}
            onChange={(labelSize: number) => patch('cavers', { labelSize })}
          />
          <Typography.Text>{cavers.labelSize}</Typography.Text>
        </Flex>
      </Row>
      <Row label={t('caveview.movie.labelPlate')}>
        <Switch
          disabled={disabled || cavers.labels === 'off'}
          aria-label={t('caveview.movie.labelPlate')}
          checked={cavers.labelPlate}
          onChange={(labelPlate) => patch('cavers', { labelPlate })}
        />
      </Row>
      <Row label={t('caveview.movie.showTimes')}>
        <Switch
          disabled={disabled || cavers.labels === 'off'}
          aria-label={t('caveview.movie.showTimes')}
          checked={cavers.showTimes}
          onChange={(showTimes) => patch('cavers', { showTimes })}
        />
      </Row>
      <Row label={t('caveview.movie.colourBy')} help={t(`caveview.movie.colourByHelp.${cavers.colourBy}`)}>
        <Select
          aria-label={t('caveview.movie.colourBy')}
          value={cavers.colourBy}
          style={{ minWidth: 150 }}
          onChange={(colourBy: MovieColourBy) => patch('cavers', { colourBy })}
          options={MOVIE_COLOUR_BY.map((mode) => ({
            value: mode,
            label: t(`caveview.movie.colourByValues.${mode}`),
          }))}
        />
      </Row>
      <Row label={t('caveview.movie.showOut')}>
        <Switch
          aria-label={t('caveview.movie.showOut')}
          checked={cavers.showOut}
          onChange={(showOut) => patch('cavers', { showOut })}
        />
      </Row>
      <Row label={t('caveview.movie.trails')}>
        <Switch
          aria-label={t('caveview.movie.trails')}
          checked={cavers.trails}
          onChange={(trails) => patch('cavers', { trails })}
          data-testid="movie-trails"
        />
      </Row>
    </Flex>
  );

  const viewSettings = (
    <Flex vertical gap="middle">
      <Row label={t('caveview.movie.viewDirection')} help={t('caveview.movie.viewDirectionHelp')}>
        <Flex gap="small" wrap>
          <Select
            value={view.direction}
            style={{ minWidth: 180 }}
            onChange={(direction: MovieViewDirection) => patch('view', { direction })}
            aria-label={t('caveview.movie.viewDirection')}
            data-testid="movie-view-direction"
            options={MOVIE_VIEW_DIRECTIONS.map((direction) => ({
              value: direction,
              label: t(`caveview.movie.viewDirections.${direction}`),
            }))}
          />
          <Button
            // Said outright, since a button's own `disabled` overrides the form's while an export runs.
            disabled={disabled || onViewAgain === null}
            onClick={() => onViewAgain?.()}
            data-testid="movie-view-again"
          >
            {t('caveview.movie.viewDirectionAgain')}
          </Button>
        </Flex>
      </Row>
      <div className="movie-layers">
        {MOVIE_VIEW_LAYERS.map(({ key }) => {
          const missing = layers !== null && !layers.has(key);
          const box = (
            <Checkbox
              key={key}
              checked={view[key] && !missing}
              disabled={disabled || missing}
              onChange={(event) => patch('view', { [key]: event.target.checked } as Partial<MovieSettings['view']>)}
              data-testid={`movie-layer-${key}`}
            >
              {t(`caveview.movie.layers.${key}`)}
            </Checkbox>
          );
          return missing ? (
            <Tooltip key={key} title={t('caveview.movie.layerMissing')}>
              <span>{box}</span>
            </Tooltip>
          ) : (
            box
          );
        })}
      </div>
      {/* Said once, in words, rather than only in each greyed box's tooltip, which a finger never
          opens. */}
      {layers !== null && MOVIE_VIEW_LAYERS.some(({ key }) => !layers.has(key)) && (
        <Typography.Text type="secondary" className="movie-setting-help" data-testid="movie-layers-missing">
          {t('caveview.movie.layersMissing')}
        </Typography.Text>
      )}
      {view.HUD && <Alert type="warning" showIcon title={t('caveview.movie.hudWarning')} />}
      {/* Offered only over terrain the survey's file carries, which is whole once the model has
          loaded. The flat plane the viewer lays under a survey that names its coordinate system is
          not offered: it is tiles asked for again as the camera moves, and would still be arriving
          in a movie that turns. */}
      <Row
        label={t('caveview.movie.terrain')}
        help={modelLoaded && !terrain ? t('caveview.movie.terrainNoneHelp') : t('caveview.movie.terrainHelp')}
      >
        <Switch
          disabled={disabled || !terrain}
          // A choice remembered from another model is not shown as made on one that cannot draw it.
          checked={terrain && view.terrain}
          onChange={(shown) => patch('view', { terrain: shown })}
          data-testid="movie-terrain"
          aria-label={t('caveview.movie.terrain')}
        />
      </Row>
      <Row
        label={t('caveview.movie.shading')}
        help={modelLoaded && !terrain ? t('caveview.movie.shadingTerrainHelp') : undefined}
      >
        <Select
          value={view.shading ?? 'default'}
          style={{ minWidth: 200 }}
          // The options say why a depth shading cannot be chosen; a list only as wide as the box
          // would cut that off.
          popupMatchSelectWidth={false}
          disabled={disabled || !modelLoaded}
          onChange={(id: MovieShadingId | 'default') => patch('view', { shading: id === 'default' ? null : id })}
          data-testid="movie-shading"
          aria-label={t('caveview.movie.shading')}
          options={[
            { value: 'default', label: t('caveview.movie.shadings.default') },
            ...MOVIE_SHADINGS.map((shading) => ({
              value: shading.id,
              label:
                shading.terrain && !terrain
                  ? `${t(`caveview.movie.shadings.${shading.id}`)} — ${t('caveview.movie.shadingNeedsTerrain')}`
                  : t(`caveview.movie.shadings.${shading.id}`),
              disabled: shading.terrain && !terrain,
            })),
          ]}
        />
      </Row>
      <Row label={t('caveview.movie.camera')}>
        <Radio.Group
          optionType="button"
          aria-label={t('caveview.movie.camera')}
          value={view.camera}
          onChange={(event) =>
            patch('view', {
              camera: event.target.value as 'perspective' | 'orthographic',
            })
          }
          options={(['perspective', 'orthographic'] as const).map((camera) => ({
            value: camera,
            label: t(`caveview.movie.cameras.${camera}`),
          }))}
        />
      </Row>
      {/* The viewer's own sliders run from 0 to 1; each is shown as what it means. */}
      <Row label={t('caveview.movie.linewidth')}>
        <SliderWithValue
          min={0}
          max={1}
          step={0.05}
          value={view.linewidth}
          format={(value) => t('caveview.movie.pixels', { value: formatNumber(lineWidthPixels(value), 1) })}
          onChange={(linewidth) => patch('view', { linewidth })}
          testId="movie-linewidth"
          label={t('caveview.movie.linewidth')}
        />
      </Row>
      <Row label={t('caveview.movie.zScale')} help={t('caveview.movie.zScaleHelp')}>
        <SliderWithValue
          min={0}
          max={1}
          step={0.05}
          value={view.zScale}
          marks={{ 0.5: ' ' }}
          format={(value) => t('caveview.movie.times', { value: formatNumber(verticalScaleFactor(value), 2) })}
          onChange={(zScale) => patch('view', { zScale })}
          testId="movie-zscale"
          label={t('caveview.movie.zScale')}
        />
      </Row>
    </Flex>
  );

  const captionSettings = (
    <Flex vertical gap="middle">
      <Row label={t('caveview.movie.captionTitle')}>
        <Switch
          checked={captions.title}
          onChange={(title) => patch('captions', { title })}
          data-testid="movie-caption-title"
          aria-label={t('caveview.movie.captionTitle')}
        />
      </Row>
      <Input
        disabled={disabled || !captions.title}
        maxLength={200}
        value={captions.titleText}
        placeholder={autoTitle.length > 0 ? autoTitle : t('caveview.movie.titlePlaceholder')}
        onChange={(event) => patch('captions', { titleText: event.target.value })}
        aria-label={t('caveview.movie.titleText')}
        data-testid="movie-title-text"
        allowClear
      />
      <Row label={t('caveview.movie.captionClock')}>
        <Switch
          aria-label={t('caveview.movie.captionClock')}
          checked={captions.clock}
          onChange={(clock) => patch('captions', { clock })}
        />
      </Row>
      <Row label={t('caveview.movie.captionSpeed')} help={t('caveview.movie.captionSpeedHelp')}>
        <Switch
          // The figure is part of the clock's caption, so with the clock off there is nothing to switch.
          disabled={disabled || !captions.clock}
          checked={captions.speed}
          onChange={(speed) => patch('captions', { speed })}
          data-testid="movie-caption-speed"
          aria-label={t('caveview.movie.captionSpeed')}
        />
      </Row>
      <Row label={t('caveview.movie.captionLegend')}>
        <Switch
          aria-label={t('caveview.movie.captionLegend')}
          checked={captions.legend}
          onChange={(legend) => patch('captions', { legend })}
        />
      </Row>
      <Row label={t('caveview.movie.captionProgress')}>
        <Switch
          aria-label={t('caveview.movie.captionProgress')}
          checked={captions.progress}
          onChange={(progress) => patch('captions', { progress })}
        />
      </Row>
      <Row label={t('caveview.movie.captionNote')} help={t('caveview.movie.captionNoteHelp')}>
        <Switch
          checked={captions.note}
          onChange={(note) => patch('captions', { note })}
          data-testid="movie-caption-note"
          aria-label={t('caveview.movie.captionNote')}
        />
      </Row>
      <Row label={t('caveview.movie.captionSize')}>
        <SliderWithValue
          min={0.5}
          max={2}
          step={0.1}
          value={captions.size}
          format={(value) => t('caveview.movie.percent', { value: formatNumber(value * 100, 0) })}
          onChange={(size) => patch('captions', { size })}
          testId="movie-caption-size"
          label={t('caveview.movie.captionSize')}
        />
      </Row>
    </Flex>
  );

  const group = (key: MovieSettingsGroup, children: ReactNode) => ({
    key,
    label: t(`caveview.movie.groups.${key}`),
    children,
  });

  return (
    // Every control at once, the way antd's own forms are disabled — a native fieldset would stop
    // the buttons but leave the selects and sliders looking as if they could be changed.
    <ConfigProvider componentDisabled={disabled}>
      <div className="movie-settings" data-testid="movie-settings">
        {/* Above the groups, since a preset is where a reader in a hurry starts and stops. Each
            button's own `disabled` is said outright: it overrides the form's while an export runs. */}
        <div className="movie-presets" role="group" aria-label={t('caveview.movie.presetsTitle')}>
          <Typography.Text type="secondary">{t('caveview.movie.presetsTitle')}</Typography.Text>
          <Flex gap="small" wrap>
            {MOVIE_PRESETS.map((preset) => (
              <Button
                key={preset}
                size="small"
                disabled={disabled}
                title={t(`caveview.movie.presetHelp.${preset}`)}
                onClick={() => onPreset(preset)}
                data-testid={`movie-preset-${preset}`}
              >
                {t(`caveview.movie.presets.${preset}`)}
              </Button>
            ))}
          </Flex>
          <Typography.Text type="secondary" className="movie-setting-help">
            {t('caveview.movie.presetsHelp')}
          </Typography.Text>
        </div>
        {/* Outside the presets, and under words of its own: a preset is promised to change the file
            only, and this puts the captions, the labels and the view back as well. */}
        <div className="movie-reset">
          <Button
            size="small"
            type="text"
            disabled={disabled}
            title={t('caveview.movie.resetHelp')}
            onClick={onReset}
            data-testid="movie-reset"
          >
            {t('caveview.movie.reset')}
          </Button>
          <Typography.Text type="secondary" className="movie-setting-help" data-testid="movie-reset-help">
            {t('caveview.movie.resetHelp')}
          </Typography.Text>
        </div>
        <Collapse
          size="small"
          defaultActiveKey={['trips', 'output']}
          items={[
            group('trips', trips),
            group('output', output),
            group('motion', motion),
            group('cavers', caverSettings),
            group('view', viewSettings),
            group('captions', captionSettings),
          ]}
        />
      </div>
    </ConfigProvider>
  );
}
