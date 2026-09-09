// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { DownloadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Descriptions,
  Empty,
  Flex,
  Input,
  InputNumber,
  Row,
  Segmented,
  Select,
  Skeleton,
  Table,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';

import { ApiError } from '../../api/client.ts';
import { downloadFile, registryDistributionExportUrl } from '../../api/download.ts';
import {
  useCaveTypes,
  useRegistryDistribution,
  useRockTypes,
  type RegistryPercentileRow,
} from '../../api/hooks.ts';
import { RegistryDistributionChart } from '../../components/statistics/RegistryDistributionChart.tsx';
import {
  distributionBars,
  hasDistribution,
  mergedBinCount,
  paretoTailSpan,
} from '../../components/statistics/registryDistribution.ts';
import {
  DefaultRegistryBinCount,
  RegistryBinCaveCountBounds,
  RegistryBinCountBounds,
  RegistryMeasureNames,
  readRegistryDistributionFilter,
  registryDistributionQuery,
  writeRegistryDistributionFilter,
  type RegistryDistributionFilter,
} from './registryStatisticsFilter.ts';

/**
 * How one measurement is spread across the registry, over the caves this reader may read.
 *
 * <p>
 * Everything on this page was worked out once, on the server, with the reader's own access
 * composed into the question — so two accounts see different distributions of the same column and
 * both are right. The page says so once, and repeats the counts each figure came from beside it,
 * because a median over eleven caves and a median over eleven hundred look identical on a chart.
 * </p>
 * <p>
 * Three states this page refuses to smooth over. An interval the registry joined with its
 * neighbour is drawn and named as joined, because it was joined rather than dropped and a wide
 * bar with no explanation reads as a feature of the caves. A fit that could not be made is a
 * stated absence and not a flat line. And no intervals at all is a real answer — nothing carries
 * the measurement, or everything carrying it records the same value, so there is no range to
 * divide — which is not the same as an answer that has not arrived.
 * </p>
 */
export default function RegistryDistributionPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [searchParams, setSearchParams] = useSearchParams();

  const filter = useMemo(() => readRegistryDistributionFilter(searchParams), [searchParams]);

  // A rendering choice rather than a narrowing, but it lives in the address for the same reason
  // the narrowings do: what somebody is looking at should be what they can hand over as a link.
  const logCount = searchParams.get('scale') === 'log';

  const apply = (change: Partial<RegistryDistributionFilter>) => {
    const next = writeRegistryDistributionFilter({ ...filter, ...change });
    if (logCount) {
      next.set('scale', 'log');
    }
    setSearchParams(next);
  };

  const setScale = (value: string) => {
    const next = writeRegistryDistributionFilter(filter);
    if (value === 'log') {
      next.set('scale', 'log');
    }
    setSearchParams(next);
  };

  /**
   * The two typed controls hold what is being typed until the reader is done with the field.
   *
   * They are typed rather than picked, and a value on the way to another one is still a whole
   * question: applied on each keystroke, "25" asks for two intervals on the way there — which the
   * registry answers, so the distribution is re-binned, a request nobody asked for is sent, and a
   * history entry is pushed for a number nobody meant. (A value the registry would refuse never
   * leaves the control: the number field withholds anything below its minimum until the field is
   * left.) Nothing is corrected here on the way out: what the reader finished typing is asked as
   * typed, and the registry is what says whether it will publish it.
   */
  const [binsDraft, setBinsDraft] = useState<number | null>(filter.bins ?? null);
  const [floorDraft, setFloorDraft] = useState<number | null>(filter.minimumBinCaveCount ?? null);
  useEffect(() => setBinsDraft(filter.bins ?? null), [filter.bins]);
  useEffect(
    () => setFloorDraft(filter.minimumBinCaveCount ?? null),
    [filter.minimumBinCaveCount],
  );

  const commitBins = () => {
    if ((binsDraft ?? undefined) !== filter.bins) {
      apply({ bins: binsDraft ?? undefined });
    }
  };
  const commitFloor = () => {
    if ((floorDraft ?? undefined) !== filter.minimumBinCaveCount) {
      apply({ minimumBinCaveCount: floorDraft ?? undefined });
    }
  };

  const query = useMemo(() => registryDistributionQuery(filter), [filter]);
  const { data, isError, error, isPlaceholderData } = useRegistryDistribution(query);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();

  const number = (value: number) => value.toLocaleString();

  const bars = useMemo(
    () =>
      distributionBars(data?.bins ?? [], {
        bound: number,
        joined: (range) => t('registryStats.joinedLabel', range),
      }),
    [data?.bins, t],
  );
  const joined = mergedBinCount(data?.bins ?? []);

  // Named after the answer and never after the question. The previous answer stays on screen while
  // a new one is worked out, so a label taken from the request would title one measurement's bars,
  // axis, percentiles and fits with the name of the measurement now being asked for.
  const answered = data?.measure ?? filter.measure;
  const measureLabel = t(`registryStats.measures.${answered}`, { defaultValue: answered });

  const tailSpan = useMemo(
    () => paretoTailSpan(data?.bins ?? [], data?.paretoTail?.lowerBound ?? null),
    [data?.bins, data?.paretoTail?.lowerBound],
  );

  const onExport = () => {
    downloadFile(registryDistributionExportUrl(query)).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  const percentiles: RegistryPercentileRow[] = data?.percentiles ?? [];

  return (
    <div style={{ padding: 24 }} data-testid="registry-distribution">
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('registryStats.distributionTitle')}
        </Typography.Title>
        {/* Never offered for something the screen is not showing: the file is the same answer, so
            there is nothing to save while there is nothing to look at. */}
        <Button
          icon={<DownloadOutlined />}
          data-testid="registry-distribution-export"
          disabled={data === undefined || isPlaceholderData}
          onClick={onExport}
        >
          {t('common.export')}
        </Button>
      </Flex>

      <Typography.Paragraph type="secondary" data-testid="registry-distribution-access">
        {t('registryStats.asVisibleToYou')}
      </Typography.Paragraph>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap align="flex-end">
          <label>
            <Typography.Text type="secondary">{t('registryStats.measure')}</Typography.Text>
            <Select
              data-testid="registry-measure"
              style={{ width: 240, display: 'block' }}
              value={filter.measure}
              onChange={(value: string) => apply({ measure: value })}
              options={RegistryMeasureNames.map((name) => ({
                value: name,
                label: t(`registryStats.measures.${name}`),
              }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.bins')}</Typography.Text>
            <InputNumber
              data-testid="registry-bins"
              style={{ width: 120, display: 'block' }}
              min={RegistryBinCountBounds.minimum}
              max={RegistryBinCountBounds.maximum}
              placeholder={String(DefaultRegistryBinCount)}
              value={binsDraft}
              onChange={(value) => setBinsDraft(value)}
              onBlur={commitBins}
              onPressEnter={commitBins}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.floor')}</Typography.Text>
            <InputNumber
              data-testid="registry-floor"
              style={{ width: 140, display: 'block' }}
              min={RegistryBinCaveCountBounds.minimum}
              max={RegistryBinCaveCountBounds.maximum}
              placeholder={String(RegistryBinCaveCountBounds.minimum)}
              value={floorDraft}
              onChange={(value) => setFloorDraft(value)}
              onBlur={commitFloor}
              onPressEnter={commitFloor}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.caveType')}</Typography.Text>
            <Select
              data-testid="registry-cave-type"
              allowClear
              style={{ width: 200, display: 'block' }}
              placeholder={t('registryStats.anyValue')}
              value={filter.caveTypeId}
              onChange={(value?: number) => apply({ caveTypeId: value })}
              options={caveTypes?.map((type) => ({ value: Number(type.id), label: type.name }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.rockType')}</Typography.Text>
            <Select
              data-testid="registry-rock-type"
              allowClear
              style={{ width: 200, display: 'block' }}
              placeholder={t('registryStats.anyValue')}
              value={filter.rockTypeId}
              onChange={(value?: number) => apply({ rockTypeId: value })}
              options={rockTypes?.map((type) => ({ value: Number(type.id), label: type.name }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.region')}</Typography.Text>
            <Input
              data-testid="registry-region"
              allowClear
              style={{ width: 200, display: 'block' }}
              placeholder={t('registryStats.regionPlaceholder')}
              defaultValue={filter.region}
              onBlur={(event) => apply({ region: event.target.value.trim() || undefined })}
              onPressEnter={(event) =>
                apply({ region: event.currentTarget.value.trim() || undefined })
              }
            />
          </label>
          <Segmented
            data-testid="registry-scale"
            value={logCount ? 'log' : 'linear'}
            onChange={(value) => setScale(String(value))}
            options={[
              { value: 'linear', label: t('registryStats.linearCount') },
              { value: 'log', label: t('registryStats.logCount') },
            ]}
          />
        </Flex>
        {/* The counts on the controls are not offered, because there are none to offer: nothing
            counts how many caves a narrowing would leave without answering the question itself. */}
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('registryStats.filterNote')}
        </Typography.Paragraph>
      </Card>

      {isError ? (
        /* The server's own sentence goes under the heading, because it is the half that names
           which control is wrong. This client passes an out-of-range interval count or floor on
           deliberately rather than clamping it, so the refusal is the only place a reader is told
           what the registry will publish. The exception's own message is an internal string. */
        <Alert
          type="error"
          showIcon
          data-testid="registry-distribution-error"
          message={t('registryStats.failed')}
          description={error instanceof ApiError ? error.detail : undefined}
        />
      ) : data === undefined ? (
        <Skeleton active paragraph={{ rows: 8 }} />
      ) : (
        <Row gutter={[16, 16]}>
          <Col xs={24}>
            <Card size="small" title={measureLabel} data-testid="registry-distribution-chart-card">
              {hasDistribution(data.bins) ? (
                <RegistryDistributionChart bars={bars} xLabel={measureLabel} logCount={logCount} />
              ) : (
                <Empty
                  data-testid="registry-distribution-empty"
                  description={t('registryStats.noRange')}
                />
              )}
              {/* The two counts every figure on this page was taken over, said in one sentence
                  rather than as a pair of numbers in a corner. */}
              <Typography.Paragraph
                type="secondary"
                data-testid="registry-distribution-counts"
                style={{ marginTop: 8, marginBottom: 0 }}
              >
                {t('registryStats.counts', {
                  measured: data.measuredCount,
                  total: data.caveCount,
                })}
              </Typography.Paragraph>
              {isPlaceholderData && (
                <Typography.Paragraph
                  type="secondary"
                  data-testid="registry-distribution-stale"
                  style={{ marginTop: 8, marginBottom: 0 }}
                >
                  {t('registryStats.stale')}
                </Typography.Paragraph>
              )}
              {/* Where on the axis the tabulated tail begins. The exponent is fitted on the
                  server over the values themselves, and drawing a curve for it here would mean
                  turning it into counts per interval — a second definition of the distribution,
                  over intervals that are not even of equal width once any are joined. Naming the
                  interval it starts at ties the figures beside the chart to the bars. */}
              {tailSpan !== null && (
                <Typography.Paragraph
                  type="secondary"
                  data-testid="registry-distribution-tail-span"
                  style={{ marginTop: 4, marginBottom: 0 }}
                >
                  {t('registryStats.tailSpan', {
                    from: number(Math.round(tailSpan.from)),
                    intervals: tailSpan.intervals,
                    total: tailSpan.total,
                  })}
                </Typography.Paragraph>
              )}
              {joined > 0 && (
                <Typography.Paragraph
                  type="secondary"
                  data-testid="registry-distribution-joined"
                  style={{ marginTop: 4, marginBottom: 0 }}
                >
                  {t('registryStats.joinedSummary', { joined })}
                </Typography.Paragraph>
              )}
              {/* The server's own sentence about what it counted over, shown in its own words. */}
              <Typography.Paragraph
                type="secondary"
                data-testid="registry-distribution-basis"
                style={{ marginTop: 4, marginBottom: 0 }}
              >
                {data.basis}
              </Typography.Paragraph>
            </Card>
          </Col>

          <Col xs={24} xl={12}>
            <Card size="small" title={t('registryStats.percentiles')}>
              <Table<RegistryPercentileRow>
                data-testid="registry-percentiles"
                size="small"
                rowKey={(row) => String(row.fraction)}
                pagination={false}
                dataSource={percentiles}
                locale={{ emptyText: t('registryStats.noRange') }}
                columns={[
                  {
                    title: t('registryStats.fraction'),
                    dataIndex: 'fraction',
                    render: (fraction: number) =>
                      t('registryStats.percentileName', { percent: fraction * 100 }),
                  },
                  {
                    title: measureLabel,
                    dataIndex: 'value',
                    // A blank where there is no figure. A zero written here would be a figure.
                    render: (value: number | null) =>
                      value === null ? t('registryStats.noFigure') : number(value),
                  },
                ]}
              />
            </Card>
          </Col>

          <Col xs={24} xl={12}>
            <Card size="small" title={t('registryStats.fits')} data-testid="registry-fits">
              {data.lognormal === null ? (
                <Typography.Paragraph type="secondary" data-testid="registry-lognormal-absent">
                  {t('registryStats.lognormalAbsent')}
                </Typography.Paragraph>
              ) : (
                <Descriptions size="small" column={1} title={t('registryStats.lognormal')}>
                  <Descriptions.Item label="μ">{data.lognormal.mu.toFixed(3)}</Descriptions.Item>
                  <Descriptions.Item label="σ">{data.lognormal.sigma.toFixed(3)}</Descriptions.Item>
                  <Descriptions.Item label={t('registryStats.median')}>
                    {number(Math.round(data.lognormal.median))}
                  </Descriptions.Item>
                  <Descriptions.Item label={t('registryStats.mean')}>
                    {number(Math.round(data.lognormal.mean))}
                  </Descriptions.Item>
                  <Descriptions.Item label={t('registryStats.fittedOver')}>
                    {t('registryStats.fittedOverValue', { caves: data.lognormal.count })}
                  </Descriptions.Item>
                </Descriptions>
              )}
              {data.paretoTail === null ? (
                <Typography.Paragraph type="secondary" data-testid="registry-pareto-absent">
                  {t('registryStats.paretoAbsent')}
                </Typography.Paragraph>
              ) : (
                <Descriptions size="small" column={1} title={t('registryStats.paretoTail')}>
                  {/* The exponent never appears without its standard error: read alone it invites
                      a claim about the tail that the sample behind it does not support. */}
                  <Descriptions.Item label="α">
                    {t('registryStats.alphaValue', {
                      alpha: data.paretoTail.alpha.toFixed(3),
                      error: data.paretoTail.alphaStandardError.toFixed(3),
                    })}
                  </Descriptions.Item>
                  <Descriptions.Item label={t('registryStats.tailFrom')}>
                    {number(Math.round(data.paretoTail.lowerBound))}
                  </Descriptions.Item>
                  <Descriptions.Item label={t('registryStats.fittedOver')}>
                    {t('registryStats.fittedOverValue', { caves: data.paretoTail.tailCount })}
                  </Descriptions.Item>
                  {/* A goodness figure and not a test: there is no threshold here at which the
                      tail becomes true, so it is labelled as a distance and never as a verdict. */}
                  <Descriptions.Item label={t('registryStats.goodness')}>
                    {data.paretoTail.kolmogorovSmirnov.toFixed(4)}
                  </Descriptions.Item>
                </Descriptions>
              )}
            </Card>
          </Col>
        </Row>
      )}
    </div>
  );
}
