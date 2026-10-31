// SPDX-License-Identifier: AGPL-3.0-or-later
import { LineChartOutlined } from '@ant-design/icons';
import { Alert, Card, Col, Empty, Row, Spin, Statistic, Tooltip, Typography } from 'antd';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import Button from '../Button.tsx';
import { useTerrainProfile } from '../../api/hooks/karstProfiles.ts';
import { armTerrainProfileDraw, showTerrainProfileLine } from '../../map/terrainProfileTool.ts';
import { EnvelopeChart } from '../statistics/DistributionCharts.tsx';

/**
 * "Draw a line, get the ground under it." A button arms a one-line draw on the map; the finished
 * line is sent for its elevation profile, which is shown beside the button as distance against
 * elevation with the ascent and descent along it.
 *
 * <p>
 * Readings the elevation data does not reach are drawn as gaps, never joined across: a profile
 * that bridged them would assert a ground height where nobody knows one. The drawing itself lives
 * in the map module; this component holds only whether the tool is armed and the last answer. The
 * route needs the right to read elevation, so the tool is mounted only for those who hold it.
 * </p>
 */
export default function TerrainProfileTool() {
  const { t } = useTranslation();
  const [armed, setArmed] = useState(false);
  const disarm = useRef<(() => void) | null>(null);
  const profile = useTerrainProfile();
  const { mutate, reset } = profile;

  const stop = useCallback(() => {
    disarm.current?.();
    disarm.current = null;
    setArmed(false);
  }, []);

  const start = useCallback(() => {
    reset();
    showTerrainProfileLine(null);
    setArmed(true);
    disarm.current = armTerrainProfileDraw((points) => {
      disarm.current = null;
      setArmed(false);
      showTerrainProfileLine(points);
      mutate(points);
    });
  }, [mutate, reset]);

  // Leaving the map takes the tool and its line away with it.
  useEffect(
    () => () => {
      disarm.current?.();
      showTerrainProfileLine(null);
    },
    [],
  );

  const close = () => {
    stop();
    reset();
    showTerrainProfileLine(null);
  };

  const data = profile.data;
  const series = useMemo(() => {
    const samples = data?.samples ?? [];
    return { x: samples.map((s) => s.distanceAlongM), elevation: samples.map((s) => s.elevationM) };
  }, [data]);

  const metres = useCallback(
    (value: number | null | undefined) =>
      value === null || value === undefined
        ? '—'
        : t('terrainProfile.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) }),
    [t],
  );

  const tooltipFormatter = useCallback(
    (index: number) =>
      t('terrainProfile.readout', {
        distance: metres(series.x[index]),
        elevation: metres(series.elevation[index]),
      }),
    [metres, series, t],
  );

  const showPanel = armed || profile.isPending || profile.isError || !!data;

  return (
    <div className="terrain-profile-tool map-chrome" data-testid="terrain-profile-tool">
      {showPanel && (
        <Card
          size="small"
          className="terrain-profile-panel"
          title={t('terrainProfile.title')}
          extra={
            <Button size="small" type="text" onClick={close} data-testid="terrain-profile-close">
              {t('common.close')}
            </Button>
          }
        >
          {armed && <Typography.Text type="secondary">{t('terrainProfile.drawHint')}</Typography.Text>}
          {profile.isPending && <Spin />}
          {profile.isError && <Alert type="warning" showIcon message={t('terrainProfile.failed')} />}
          {data && !data.hasTerrain && <Empty description={t('terrainProfile.noTerrainBuilt')} />}
          {data && data.hasTerrain && data.readCount === 0 && (
            <Empty description={t('terrainProfile.noCoverage')} />
          )}
          {data && data.hasTerrain && data.readCount > 0 && (
            <>
              <Row gutter={[12, 8]} style={{ marginBottom: 8 }}>
                <Col span={8}>
                  <Statistic title={t('terrainProfile.length')} value={metres(data.lengthM)} />
                </Col>
                <Col span={8}>
                  <Statistic title={t('terrainProfile.ascent')} value={metres(data.ascentM)} />
                </Col>
                <Col span={8}>
                  <Statistic title={t('terrainProfile.descent')} value={metres(data.descentM)} />
                </Col>
              </Row>
              <EnvelopeChart
                testId="chart-terrain-profile"
                x={series.x}
                curve={series.elevation}
                lower={series.elevation}
                upper={series.elevation}
                xLabel={t('terrainProfile.axisDistance')}
                yLabel={t('terrainProfile.axisElevation')}
                height={200}
                tooltipFormatter={tooltipFormatter}
              />
              <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
                {t('terrainProfile.explained')}
              </Typography.Paragraph>
            </>
          )}
        </Card>
      )}
      <Tooltip title={t('terrainProfile.tool')}>
        <Button
          size="small"
          type={armed ? 'primary' : 'default'}
          icon={<LineChartOutlined />}
          aria-label={t('terrainProfile.tool')}
          aria-pressed={armed}
          onClick={armed ? stop : start}
          data-testid="terrain-profile-button"
        />
      </Tooltip>
    </div>
  );
}
