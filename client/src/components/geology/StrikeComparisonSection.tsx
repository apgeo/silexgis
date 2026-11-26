// SPDX-License-Identifier: AGPL-3.0-or-later
import { Col, Row, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { MeasuredStrikeComparison } from '../../api/hooks/structural.ts';
import RoseDiagram from '../statistics/RoseDiagram.tsx';

/** Below this probability the panel says the two samples differ. */
const SIGNIFICANCE = 0.05;

/**
 * A cave's passage trends against the strikes of the structural readings around it, by Watson's
 * two-sample U². The server runs the test; this says what it found, and — because a survey's legs
 * are not independent observations — how much weight the answer can bear.
 */
export default function StrikeComparisonSection({ data }: { data: MeasuredStrikeComparison }) {
  const { t } = useTranslation();
  const formatP = (p: number) => (p < 0.001 ? '< 0.001' : p.toFixed(3));

  return (
    <div data-testid="strike-comparison">
      <Typography.Title level={4}>{t('structural.strikeComparison.title')}</Typography.Title>
      <Row gutter={[16, 16]}>
        <Col xs={24} md={12}>
          <Typography.Text strong>
            {t('structural.strikeComparison.strikeRose', { count: data.readingCount })}
          </Typography.Text>
          {data.strikes ? (
            <RoseDiagram
              bins={data.strikes.bins}
              meanAxis={{ count: data.strikes.byCount.meanAxisDegrees ?? null }}
              defaultWeighting="count"
            />
          ) : (
            <Typography.Paragraph type="secondary">{t('structural.strikeComparison.noStrikes')}</Typography.Paragraph>
          )}
        </Col>
        <Col xs={24} md={12}>
          {data.watson ? (
            <>
              <Row gutter={16}>
                <Col span={12}>
                  <Statistic
                    title={t('structural.strikeComparison.statistic')}
                    value={data.watson.statistic.toFixed(3)}
                    data-testid="strike-comparison-u2"
                  />
                </Col>
                <Col span={12}>
                  <Statistic
                    title={t('structural.strikeComparison.pValue')}
                    value={formatP(data.watson.pValue)}
                    data-testid="strike-comparison-p"
                  />
                </Col>
              </Row>
              <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>
                {t('structural.strikeComparison.counts', {
                  legs: data.watson.firstCount,
                  readings: data.watson.secondCount,
                })}
              </Typography.Paragraph>
              <Typography.Paragraph data-testid="strike-comparison-verdict">
                {data.watson.pValue < SIGNIFICANCE
                  ? t('structural.strikeComparison.different', { p: formatP(data.watson.pValue) })
                  : t('structural.strikeComparison.alike', { p: formatP(data.watson.pValue) })}
              </Typography.Paragraph>
            </>
          ) : (
            <Typography.Paragraph type="secondary" data-testid="strike-comparison-too-few">
              {t('structural.strikeComparison.tooFew', {
                min: data.minimumSampleSize,
                legs: data.passageLegCount,
                readings: data.readingCount,
              })}
            </Typography.Paragraph>
          )}
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('structural.strikeComparison.caveat')}
          </Typography.Paragraph>
        </Col>
      </Row>
    </div>
  );
}
