// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Empty, Row, Statistic, Typography } from 'antd';
import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import { useCaveDepthProfile } from '../../api/hooks/karstProfiles.ts';
import { EnvelopeChart } from './DistributionCharts.tsx';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * How deep a cave's passages lie, along their length: the standard descent figure of a survey
 * publication, drawn from the survey's own altitudes.
 *
 * <p>
 * Absent rather than empty when the server has no answer. The route is withheld whole from a reader
 * who may read the cave and not place it, spelled as "no such cave", so a refusal renders nothing:
 * a card of blanks would announce that a guarded cave is here. A cave whose line work carries no
 * altitudes gets its own sentence and no curve, never a curve of zeros.
 * </p>
 */
export default function CaveDepthProfilePanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveDepthProfile(caveId);

  const series = useMemo(() => {
    const samples = data?.samples ?? [];
    const altitude = samples.map((s) => s.altitudeM);
    return { x: samples.map((s) => s.distanceAlongM), altitude, depth: samples.map((s) => s.depthM) };
  }, [data]);

  const metres = useCallback(
    (value: number | null | undefined) =>
      value === null || value === undefined
        ? '—'
        : t('depthProfile.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) }),
    [t],
  );

  const tooltipFormatter = useCallback(
    (index: number) =>
      t('depthProfile.readout', {
        distance: metres(series.x[index]),
        altitude: metres(series.altitude[index]),
        depth: metres(series.depth[index]),
      }),
    [metres, series, t],
  );

  if (isError) return null;

  const body = () => {
    if (isLoading || !data) return <Empty description={t('depthProfile.loading')} />;
    if (!data.hasAltitudes || data.samples.length === 0) {
      return <Empty description={t('depthProfile.noAltitudes')} />;
    }
    return (
      <>
        <Row gutter={[16, 16]} style={{ marginBottom: 12 }}>
          <Col xs={12} md={6}>
            <Statistic title={t('depthProfile.highest')} value={metres(data.highestM)} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title={t('depthProfile.lowest')} value={metres(data.lowestM)} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title={t('depthProfile.deepest')} value={metres(data.deepestM)} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title={t('depthProfile.passageLength')} value={metres(data.passageLengthM)} />
          </Col>
        </Row>
        <EnvelopeChart
          testId="chart-cave-depth-profile"
          x={series.x}
          curve={series.altitude}
          lower={series.altitude}
          upper={series.altitude}
          xLabel={t('depthProfile.axisDistance')}
          yLabel={t('depthProfile.axisDepth')}
          height={260}
          tooltipFormatter={tooltipFormatter}
        />
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('depthProfile.explained')}
        </Typography.Paragraph>
        <SurveyBasisNote basis={data.basis} />
      </>
    );
  };

  return (
    <Card title={t('depthProfile.title')} style={{ marginBottom: 16 }} data-testid="cave-depth-profile">
      {body()}
    </Card>
  );
}
