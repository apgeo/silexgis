// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Empty, Row, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useAreaHypsometry } from '../../api/hooks.ts';
import ElevationHistogram from '../statistics/ElevationHistogram.tsx';

/**
 * How the entrance altitudes under one area are distributed, with the springs among them drawn as
 * base-level reference lines.
 *
 * <p>
 * Every entrance weighs one here, where the per-cave view weighs metres of passage. That is a
 * different measurement of a different thing — where the holes are, not where the passage is — and
 * the axis label says so rather than leaving the two charts looking interchangeable.
 * </p>
 * <p>
 * The count is of entrances this reader may place, and is deliberately not a count of the entrances
 * in the area. The difference between the two is a statement about which caves are guarded here,
 * and the card says which number it is showing.
 * </p>
 */
export default function AreaHypsometryCard({
  featureId,
  geometryType,
}: {
  featureId: string;
  /** The feature's GeoJSON geometry type, or null when it carries none. */
  geometryType: string | null;
}) {
  const { t } = useTranslation();
  // Only an outline is an area anything can sit inside. A point feature has no subtree worth
  // asking about, and asking anyway would put an always-empty card on every marker in the map.
  const enabled = geometryType === 'Polygon' || geometryType === 'MultiPolygon';
  const { data, isLoading, isError } = useAreaHypsometry(featureId, enabled);

  if (!enabled || isError) return null;

  return (
    <Card title={t('hypsometry.areaTitle')} style={{ marginBottom: 16 }}>
      {isLoading || !data ? (
        <Empty description={t('hypsometry.loading')} />
      ) : data.proposal.sampleCount === 0 ? (
        <Empty description={t('hypsometry.noEntrances')} />
      ) : (
        <>
          <Row gutter={[16, 16]} style={{ marginBottom: 12 }}>
            <Col xs={12} md={8}>
              <Statistic
                title={t('hypsometry.entrancesPlaced')}
                value={data.entranceCount}
              />
            </Col>
            <Col xs={12} md={8}>
              <Statistic
                title={t('hypsometry.springsSeen')}
                value={data.springAltitudesM.length}
              />
            </Col>
          </Row>
          <ElevationHistogram
            testId="chart-area-hypsometry"
            bins={data.proposal.bins}
            bands={data.proposal.bands}
            referenceHeightsM={data.springAltitudesM}
            measure="count"
          />
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            {t('hypsometry.countedOverWhatYouMayPlace')}
          </Typography.Paragraph>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('hypsometry.proposalIsNotAnAssertion')}
          </Typography.Paragraph>
        </>
      )}
    </Card>
  );
}
