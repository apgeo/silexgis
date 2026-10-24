// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined, EnvironmentOutlined } from '@ant-design/icons';
import { Alert, Card, Checkbox, Flex, Input, Radio, Select, Table, Typography } from 'antd';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import Point from 'ol/geom/Point';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router-dom';
import { saveBlob } from '../../api/download.ts';
import Button from '../../components/Button.tsx';
import { assumptionLabel, formatLabel, parseErrorLabel } from '../../components/coordinates/formatLabels.ts';
import {
  batchCsv,
  csvColumns,
  readBatch,
  type BatchError,
  type BatchRow,
  type BatchSource,
  type Separator,
} from '../../geo/formats/batch.ts';
import { FORMAT_IDS, isFormatId, readPosition, writePosition, type FormatId } from '../../geo/formats/registry.ts';
import type { ParsedPosition } from '../../geo/formats/types.ts';
import { formatMapHash } from '../../map/urlHash.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

type FormatChoice = 'any' | FormatId;

/**
 * The coordinate converter: a position typed or pasted in any format this application reads,
 * written back in every one of them at once, each with copy — and many positions at once, pasted
 * as lines or as a table, downloaded as CSV.
 *
 * Everything happens in this browser: nothing typed or pasted here is sent to the server, and
 * nothing is saved. Positions are written with the reader's own settings for each format.
 *
 * `?q=` opens it on a position, for the buttons elsewhere that hand one over; those buttons are
 * only offered for a position the reader may see exactly, so nothing coarser than it is shown
 * here is ever written finer.
 */
export default function CoordinateConverterPage() {
  const { t } = useTranslation();
  return (
    <Flex vertical gap={16} style={{ maxWidth: 1100 }}>
      <Typography.Title level={3} style={{ margin: 0 }}>
        {t('coordinates.converter.title')}
      </Typography.Title>
      <Typography.Text type="secondary">{t('coordinates.converter.intro')}</Typography.Text>
      <SingleConverter />
      <BatchConverter />
    </Flex>
  );
}

function formatChoices(t: ReturnType<typeof useTranslation>['t']) {
  return [
    { value: 'any' as FormatChoice, label: t('coordinates.input.anyFormat') },
    ...FORMAT_IDS.map((id) => ({ value: id as FormatChoice, label: formatLabel(t, id) })),
  ];
}

function SingleConverter() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const [text, setText] = useState(() => searchParams.get('q') ?? '');
  const [choice, setChoice] = useState<FormatChoice>('any');
  const [picked, setPicked] = useState(0);
  const [onMap, setOnMap] = useState(false);
  const settings = useUiPrefsStore((s) => s.coordinateSettings);
  const rules = useUiPrefsStore((s) => s.coordinateReading);
  const feedbackId = useId();

  const reading = useMemo(
    () => (text.trim() === '' ? null : readPosition(text, choice === 'any' ? undefined : choice, rules)),
    [text, choice, rules],
  );
  const candidates = reading?.kind === 'parsed' ? reading.candidates : [];
  const position: ParsedPosition | undefined = candidates[Math.min(picked, candidates.length - 1)];
  const nameOf = (format: string) => (isFormatId(format) ? formatLabel(t, format) : format);
  const readBack = (candidate: ParsedPosition) =>
    writePosition('dd', candidate.lon, candidate.lat, { dd: { decimals: 6, style: 'letters' } }) ?? '';

  return (
    <Card title={t('coordinates.converter.single')}>
      <Flex vertical gap={12}>
        <Flex gap={8} wrap>
          <Select<FormatChoice>
            value={choice}
            onChange={(value) => {
              setChoice(value);
              setPicked(0);
            }}
            popupMatchSelectWidth={false}
            style={{ width: 200 }}
            aria-label={t('coordinates.converter.readAsFormat')}
            data-testid="converter-format"
            options={formatChoices(t)}
          />
          <Input
            value={text}
            onChange={(event) => {
              setText(event.target.value);
              setPicked(0);
            }}
            allowClear
            style={{ flex: '1 1 320px' }}
            placeholder={t('coordinates.input.placeholder')}
            aria-label={t('coordinates.input.label')}
            aria-describedby={feedbackId}
            aria-invalid={reading?.kind === 'invalid'}
            data-testid="converter-input"
          />
        </Flex>
        <div id={feedbackId} aria-live="polite" data-testid="converter-feedback">
          {reading?.kind === 'invalid' && <Typography.Text type="danger">{parseErrorLabel(t, reading.error)}</Typography.Text>}
          {candidates.length > 1 && (
            <Flex vertical gap={4}>
              <Typography.Text>{t('coordinates.input.pick')}</Typography.Text>
              <Radio.Group value={Math.min(picked, candidates.length - 1)} onChange={(event) => setPicked(event.target.value as number)}>
                <Flex vertical gap={2}>
                  {candidates.map((candidate, index) => (
                    <Radio key={index} value={index}>
                      {t('coordinates.input.readAs', { format: nameOf(candidate.format), position: readBack(candidate) })}
                    </Radio>
                  ))}
                </Flex>
              </Radio.Group>
            </Flex>
          )}
          {position && (
            <Flex vertical>
              {candidates.length === 1 && (
                <Typography.Text type="secondary">
                  {t('coordinates.input.readAs', { format: nameOf(position.format), position: readBack(position) })}
                </Typography.Text>
              )}
              {position.assumptions.map((assumption) => (
                <Typography.Text key={assumption} type="secondary">
                  {assumptionLabel(t, assumption)}
                </Typography.Text>
              ))}
            </Flex>
          )}
        </div>

        {position && (
          <>
            <Flex vertical gap={6} data-testid="converter-results">
              {FORMAT_IDS.map((id) => {
                const written = writePosition(id, position.lon, position.lat, settings);
                return (
                  <Flex key={id} gap={12} wrap align="baseline">
                    <Typography.Text type="secondary" style={{ minWidth: 220 }}>
                      {formatLabel(t, id)}
                    </Typography.Text>
                    {written === null ? (
                      <Typography.Text type="secondary" italic data-testid={`converter-result-${id}`}>
                        {t('coordinates.settings.notAvailable')}
                      </Typography.Text>
                    ) : (
                      <Typography.Text
                        copyable={{ text: written, tooltips: [t('coordinates.converter.copy'), t('coordinates.converter.copied')] }}
                        data-testid={`converter-result-${id}`}
                      >
                        {written}
                      </Typography.Text>
                    )}
                  </Flex>
                );
              })}
            </Flex>
            <Flex gap={8} wrap>
              <Button icon={<EnvironmentOutlined />} onClick={() => setOnMap((value) => !value)} aria-pressed={onMap}>
                {onMap ? t('coordinates.converter.hideMap') : t('coordinates.converter.showOnMap')}
              </Button>
              <Link to={`/map${formatMapHash({ lon: position.lon, lat: position.lat, zoom: 15 })}`}>
                {t('coordinates.converter.openMainMap')}
              </Link>
            </Flex>
            {onMap && (
              <>
                <Typography.Text type="secondary">{t('coordinates.converter.mapHint')}</Typography.Text>
                <PositionMap lon={position.lon} lat={position.lat} />
              </>
            )}
          </>
        )}
      </Flex>
    </Card>
  );
}

/**
 * A small map of its own with a temporary marker on the converted position: nothing is added to
 * the workspace map, and nothing is saved — it disappears with the page.
 */
function PositionMap({ lon, lat }: { lon: number; lat: number }) {
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const marker = useRef(new Feature<Point>());

  useEffect(() => {
    if (!target.current) {
      return undefined;
    }
    const built = new Map({
      target: target.current,
      layers: [
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        new VectorLayer({
          source: new VectorSource({ features: [marker.current] }),
          style: new Style({
            image: new CircleStyle({
              radius: 8,
              fill: new Fill({ color: '#146262' }),
              stroke: new Stroke({ color: '#fff', width: 2 }),
            }),
          }),
        }),
      ],
      view: new View({ zoom: 14 }),
    });
    map.current = built;
    return () => {
      built.setTarget(undefined);
      map.current = null;
    };
  }, []);

  useEffect(() => {
    const at = fromLonLat([lon, lat]);
    marker.current.setGeometry(new Point(at));
    map.current?.getView().setCenter(at);
  }, [lon, lat]);

  return <div ref={target} style={{ height: 320, width: '100%' }} data-testid="converter-map" />;
}

const SEPARATOR_OPTIONS: { value: Separator | 'auto'; key: string }[] = [
  { value: 'auto', key: 'auto' },
  { value: ',', key: 'comma' },
  { value: ';', key: 'semicolon' },
  { value: '\t', key: 'tab' },
];

function BatchConverter() {
  const { t } = useTranslation();
  const settings = useUiPrefsStore((s) => s.coordinateSettings);
  const rules = useUiPrefsStore((s) => s.coordinateReading);
  const [text, setText] = useState('');
  const [mode, setMode] = useState<'lines' | 'csv'>('lines');
  const [separator, setSeparator] = useState<Separator | 'auto'>('auto');
  const [header, setHeader] = useState(true);
  const [columns, setColumns] = useState<number[]>([0]);
  const [choice, setChoice] = useState<FormatChoice>('any');
  const [result, setResult] = useState<{ rows: BatchRow[]; leftOut: number } | null>(null);

  const columnNames = useMemo(() => (mode === 'csv' ? csvColumns(text, separator) : []), [mode, text, separator]);
  const errorText = (error: BatchError) =>
    error === 'ambiguous' || error === 'missingColumn' || error === 'empty'
      ? t(`coordinates.converter.batchErrors.${error}`)
      : parseErrorLabel(t, error);

  const convert = () => {
    const source: BatchSource = mode === 'lines' ? { mode } : { mode, separator, header, columns };
    setResult(readBatch(text, source, choice === 'any' ? undefined : choice, rules));
  };

  const download = () => {
    if (!result) return;
    const csv = batchCsv(result.rows, FORMAT_IDS, settings, {
      line: t('coordinates.converter.columns.line'),
      input: t('coordinates.converter.columns.input'),
      latitude: t('entrances.latitude'),
      longitude: t('entrances.longitude'),
      error: t('coordinates.converter.columns.error'),
      format: (id) => formatLabel(t, id),
      errorText,
    });
    // A byte-order mark, so that a spreadsheet opens the file's degree and minute signs as UTF-8.
    saveBlob(new Blob(['﻿', csv], { type: 'text/csv;charset=utf-8' }), 'coordinates.csv');
  };

  const failed = result?.rows.filter((row) => row.outcome.kind === 'error').length ?? 0;

  return (
    <Card title={t('coordinates.converter.batch')}>
      <Flex vertical gap={12}>
        <Typography.Text type="secondary">{t('coordinates.converter.batchHint')}</Typography.Text>
        <Flex gap={12} wrap align="center">
          <Radio.Group
            value={mode}
            onChange={(event) => {
              setMode(event.target.value as 'lines' | 'csv');
              setResult(null);
            }}
            optionType="button"
            options={[
              { value: 'lines', label: t('coordinates.converter.modeLines') },
              { value: 'csv', label: t('coordinates.converter.modeCsv') },
            ]}
            aria-label={t('coordinates.converter.mode')}
          />
          <Select<FormatChoice>
            value={choice}
            onChange={setChoice}
            popupMatchSelectWidth={false}
            style={{ width: 200 }}
            aria-label={t('coordinates.converter.readAsFormat')}
            options={formatChoices(t)}
          />
        </Flex>
        {mode === 'csv' && (
          <Flex gap={12} wrap align="center">
            <Select<Separator | 'auto'>
              value={separator}
              onChange={setSeparator}
              style={{ width: 180 }}
              aria-label={t('coordinates.converter.separator')}
              options={SEPARATOR_OPTIONS.map(({ value, key }) => ({ value, label: t(`coordinates.converter.separators.${key}`) }))}
            />
            <Checkbox checked={header} onChange={(event) => setHeader(event.target.checked)}>
              {t('coordinates.converter.header')}
            </Checkbox>
            <Select<number[]>
              mode="multiple"
              value={columns}
              onChange={(next) => setColumns(next.slice(-2))}
              style={{ minWidth: 260 }}
              aria-label={t('coordinates.converter.positionColumns')}
              placeholder={t('coordinates.converter.positionColumns')}
              data-testid="converter-batch-columns"
              options={columnNames.map((name, index) => ({
                value: index,
                label: header && name !== '' ? `${index + 1}: ${name}` : t('coordinates.converter.columnN', { n: index + 1 }),
              }))}
            />
          </Flex>
        )}
        <Input.TextArea
          value={text}
          onChange={(event) => setText(event.target.value)}
          autoSize={{ minRows: 4, maxRows: 14 }}
          placeholder={t(mode === 'lines' ? 'coordinates.converter.batchPlaceholder' : 'coordinates.converter.csvPlaceholder')}
          aria-label={t('coordinates.converter.batchInput')}
          data-testid="converter-batch-input"
        />
        <Flex gap={8} wrap>
          <Button type="primary" onClick={convert} disabled={text.trim() === ''} data-testid="converter-batch-convert">
            {t('coordinates.converter.convert')}
          </Button>
          <Button
            icon={<DownloadOutlined />}
            onClick={download}
            disabled={!result || result.rows.length === 0}
            data-testid="converter-batch-download"
          >
            {t('coordinates.converter.download')}
          </Button>
        </Flex>
        {result && (
          <>
            <Typography.Text data-testid="converter-batch-summary">
              {t('coordinates.converter.summary', { count: result.rows.length, failed })}
            </Typography.Text>
            {result.leftOut > 0 && (
              <Alert type="warning" showIcon message={t('coordinates.converter.leftOut', { count: result.leftOut })} />
            )}
            <Table<BatchRow>
              size="small"
              rowKey="line"
              dataSource={result.rows}
              pagination={result.rows.length > 50 ? { pageSize: 50 } : false}
              scroll={{ x: true }}
              data-testid="converter-batch-table"
              columns={[
                { title: t('coordinates.converter.columns.line'), dataIndex: 'line', width: 70 },
                { title: t('coordinates.converter.columns.input'), dataIndex: 'input' },
                {
                  title: t('coordinates.converter.columns.result'),
                  key: 'result',
                  render: (_: unknown, row) =>
                    row.outcome.kind === 'ok' ? (
                      writePosition('dd', row.outcome.lon, row.outcome.lat, settings)
                    ) : (
                      <Typography.Text type="danger">{errorText(row.outcome.error)}</Typography.Text>
                    ),
                },
              ]}
            />
          </>
        )}
      </Flex>
    </Card>
  );
}
