// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { DownloadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Descriptions,
  Flex,
  Input,
  Row,
  Segmented,
  Select,
  Skeleton,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';

import { ApiError } from '../../api/client.ts';
import { downloadFile, registryCorrelationExportUrl } from '../../api/download.ts';
import { useCaveTypes, useRegistryCorrelation, useRegistryDistribution, useRockTypes } from '../../api/hooks.ts';
import { RegistryCorrelationChart } from '../../components/statistics/RegistryCorrelationChart.tsx';
import {
  correlationLine,
  type CorrelationDomain,
} from '../../components/statistics/registryCorrelation.ts';
import {
  DefaultCorrelationLogarithmic,
  RegistryMeasureNames,
  readRegistryCorrelationFilter,
  registryCorrelationQuery,
  registryScopeQuery,
  writeRegistryCorrelationFilter,
  type RegistryCorrelationFilter,
} from './registryStatisticsFilter.ts';

/**
 * How two measurements of a cave move together, over the caves this reader may read.
 *
 * <p>
 * The registry answers with the relationship and not with the caves behind it, so this page draws
 * one line and no observations, and puts everything that says how much the line is worth right
 * beside it: the goodness of the fit, the correlation with its sign, and the number of pairs the
 * whole thing was taken over. None of those is decoration. A slope quoted without its goodness is
 * a claim with its evidence removed, and a slope over eleven caves and one over eleven hundred
 * look identical drawn and mean entirely different things.
 * </p>
 * <p>
 * Where no fit was possible there is no line and a sentence saying which of the several different
 * reasons applies — nothing recorded both measurements, or too few did for a line through them to
 * mean anything, or the horizontal measurement covers no range here. A flat line, or an empty
 * pair of axes with nothing said, would each turn one of those into a claim about the caves.
 * </p>
 */
export default function RegistryCorrelationPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [searchParams, setSearchParams] = useSearchParams();

  const filter = useMemo(() => readRegistryCorrelationFilter(searchParams), [searchParams]);

  const apply = (change: Partial<RegistryCorrelationFilter>) => {
    setSearchParams(writeRegistryCorrelationFilter({ ...filter, ...change }));
  };

  const query = useMemo(() => registryCorrelationQuery(filter), [filter]);
  const { data, isError, error, isPlaceholderData } = useRegistryCorrelation(query);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();

  // Where the line starts and ends. The registry publishes no pair of coordinates, so the only
  // honest ends for a drawn line are the ends of the horizontal measurement itself over the same
  // narrowed set — asked of the published distribution of that measurement, not worked out here.
  // Its intervals are not read; two intervals are asked for because none can be.
  const boundsQuery = useMemo(
    () => ({ ...registryScopeQuery(filter), measure: filter.x, bins: 2 }),
    [filter],
  );
  const { data: bounds, isError: boundsFailed } = useRegistryDistribution(boundsQuery);

  const logarithmic = data?.logarithmic ?? filter.logarithmic ?? DefaultCorrelationLogarithmic;

  /**
   * The ends of the horizontal measurement, and whether they are known at all.
   *
   * Two separate answers meet here and they do not arrive together: the range is asked of the
   * distribution route, which does strictly more work than the fit, so on an ordinary load the fit
   * is in hand while the range is still on its way. A pair of nulls read as "no range in this set"
   * would put a sentence about the registry on screen every time — and permanently, if that
   * request failed. The ends are also refused unless they belong to the same measurement the fit
   * was taken over: both queries keep the previous answer while a new one loads, so a range left
   * over from the pair that was on screen a moment ago would otherwise be drawn across as this
   * pair's own.
   */
  const domain = useMemo<CorrelationDomain>(() => {
    if (data !== undefined && bounds !== undefined && bounds.measure === data.x) {
      return { state: 'answered', minimum: bounds.minimum, maximum: bounds.maximum };
    }
    return boundsFailed ? { state: 'unavailable' } : { state: 'pending' };
  }, [data, bounds, boundsFailed]);

  const drawn = useMemo(() => correlationLine(data, domain), [data, domain]);

  const label = (measure: string) =>
    t(`registryStats.measures.${measure}`, { defaultValue: measure });
  // Named after the answer rather than after the question: while a new pair is being worked out
  // the figures on screen are the previous pair's, and labelling them with the pair now being
  // asked for would put one measurement's name over another measurement's numbers.
  const xLabel = label(data?.x ?? filter.x);
  const yLabel = label(data?.y ?? filter.y);

  const figure = (value: number | null, digits: number) =>
    value === null ? t('registryStats.noFigure') : value.toFixed(digits);

  const onExport = () => {
    downloadFile(registryCorrelationExportUrl(query)).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  const measureOptions = (exclude: string) =>
    RegistryMeasureNames.filter((name) => name !== exclude).map((name) => ({
      value: name,
      label: label(name),
    }));

  return (
    <div style={{ padding: 24 }} data-testid="registry-correlation">
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('registryStats.correlationTitle')}
        </Typography.Title>
        {/* Never offered for something the screen is not showing: the file is the same answer. */}
        <Button
          icon={<DownloadOutlined />}
          data-testid="registry-correlation-export"
          disabled={data === undefined || isPlaceholderData}
          onClick={onExport}
        >
          {t('common.export')}
        </Button>
      </Flex>

      <Typography.Paragraph type="secondary" data-testid="registry-correlation-access">
        {t('registryStats.asVisibleToYou')}
      </Typography.Paragraph>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap align="flex-end">
          <label>
            <Typography.Text type="secondary">{t('registryStats.horizontal')}</Typography.Text>
            <Select
              data-testid="registry-correlation-x"
              style={{ width: 240, display: 'block' }}
              value={filter.x}
              onChange={(value: string) => apply({ x: value })}
              /* The two cannot be the same measurement — the registry refuses to fit one against
                 itself — so the other one is not offered here rather than offered and refused. */
              options={measureOptions(filter.y)}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.vertical')}</Typography.Text>
            <Select
              data-testid="registry-correlation-y"
              style={{ width: 240, display: 'block' }}
              value={filter.y}
              onChange={(value: string) => apply({ y: value })}
              options={measureOptions(filter.x)}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.caveType')}</Typography.Text>
            <Select
              data-testid="registry-correlation-cave-type"
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
              data-testid="registry-correlation-rock-type"
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
              data-testid="registry-correlation-region"
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
          {/* Logarithmic unless somebody says otherwise, which is what the registry does when
              asked nothing — so the control starts where the answer already is. */}
          <Segmented
            data-testid="registry-correlation-form"
            value={logarithmic ? 'log' : 'linear'}
            onChange={(value) => apply({ logarithmic: value === 'log' })}
            options={[
              { value: 'log', label: t('registryStats.logFit') },
              { value: 'linear', label: t('registryStats.linearFit') },
            ]}
          />
        </Flex>
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('registryStats.filterNote')}
        </Typography.Paragraph>
      </Card>

      {isError ? (
        /* The server's own sentence goes under the heading, because it is the half that names
           which control is wrong. This client passes an unchecked pair on deliberately, so the
           refusal is the only place a reader is told the registry will not fit a measurement
           against itself. The exception's own message is an internal, untranslated string. */
        <Alert
          type="error"
          showIcon
          data-testid="registry-correlation-error"
          message={t('registryStats.correlationFailed')}
          description={error instanceof ApiError ? error.detail : undefined}
        />
      ) : data === undefined ? (
        <Skeleton active paragraph={{ rows: 8 }} />
      ) : (
        <Row gutter={[16, 16]}>
          <Col xs={24} xl={14}>
            <Card
              size="small"
              title={t('registryStats.pair', { x: xLabel, y: yLabel })}
              data-testid="registry-correlation-chart-card"
            >
              {isPlaceholderData && (
                <Typography.Paragraph type="secondary" data-testid="registry-correlation-stale">
                  {t('registryStats.stale')}
                </Typography.Paragraph>
              )}
              {drawn.line === null ? (
                <Typography.Paragraph data-testid="registry-correlation-absent">
                  {t(`registryStats.absence.${drawn.absence}`)}
                </Typography.Paragraph>
              ) : (
                <>
                  <RegistryCorrelationChart
                    line={drawn.line}
                    logarithmic={logarithmic}
                    xLabel={xLabel}
                    yLabel={yLabel}
                  />
                  {/* Said plainly, because a single confident line over empty axes invites the
                      reading that the caves were looked at and lay along it. */}
                  <Typography.Paragraph
                    type="secondary"
                    data-testid="registry-correlation-drawn-note"
                    style={{ marginTop: 8, marginBottom: 0 }}
                  >
                    {t('registryStats.drawnNote')}
                  </Typography.Paragraph>
                </>
              )}
              {/* The server's own sentence about what it counted over, in its own words. */}
              <Typography.Paragraph
                type="secondary"
                data-testid="registry-correlation-basis"
                style={{ marginTop: 4, marginBottom: 0 }}
              >
                {data.basis}
              </Typography.Paragraph>
            </Card>
          </Col>

          <Col xs={24} xl={10}>
            <Card size="small" title={t('registryStats.fit')} data-testid="registry-correlation-fit">
              <Descriptions size="small" column={1}>
                {/* The slope never stands alone. Every row below it is what says whether it is
                    worth reading, so they are one block and not a block and an appendix. */}
                <Descriptions.Item label={t('registryStats.slope')}>
                  <span data-testid="registry-correlation-slope">{figure(data.slope, 3)}</span>
                </Descriptions.Item>
                <Descriptions.Item label={t('registryStats.intercept')}>
                  {figure(data.intercept, 3)}
                </Descriptions.Item>
                <Descriptions.Item label={t('registryStats.rSquared')}>
                  <span data-testid="registry-correlation-r2">{figure(data.rSquared, 3)}</span>
                </Descriptions.Item>
                {/* Kept beside the goodness because it is the one that keeps its sign, and the
                    direction of a relationship is not recoverable from the goodness alone. */}
                <Descriptions.Item label={t('registryStats.correlationCoefficient')}>
                  {figure(data.correlation, 3)}
                </Descriptions.Item>
                <Descriptions.Item label={t('registryStats.pairs')}>
                  <span data-testid="registry-correlation-count">
                    {t('registryStats.pairsValue', { caves: data.count })}
                  </span>
                </Descriptions.Item>
                <Descriptions.Item label={t('registryStats.form')}>
                  {logarithmic ? t('registryStats.logFit') : t('registryStats.linearFit')}
                </Descriptions.Item>
              </Descriptions>
              {logarithmic && (
                <Typography.Paragraph
                  type="secondary"
                  data-testid="registry-correlation-positive-note"
                  style={{ marginTop: 8, marginBottom: 0 }}
                >
                  {t('registryStats.positiveOnly')}
                </Typography.Paragraph>
              )}
            </Card>
          </Col>
        </Row>
      )}
    </div>
  );
}
