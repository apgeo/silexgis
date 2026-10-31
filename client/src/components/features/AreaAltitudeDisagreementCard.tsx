// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Empty, Row, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useAreaAltitudeDisagreement } from '../../api/hooks/entranceAltitudes.ts';
import EntranceAltitudeTable from '../statistics/EntranceAltitudeTable.tsx';

/**
 * The entrances under one area ranked by how far their recorded altitude is from the elevation
 * model: the worst offenders first, because a gap of tens of metres nearly always means a wrong
 * coordinate.
 *
 * <p>
 * Read-only on purpose. The fill action lives on the cave's own page, where the reader is looking at
 * one cave's record; an area list that offered it would invite filling a column of entrances without
 * looking at any of them.
 * </p>
 * <p>
 * Counts and order are over the entrances this reader may place, and the card says so. It renders
 * nothing on a refusal, for the same reason the hypsometry card does.
 * </p>
 */
export default function AreaAltitudeDisagreementCard({
  featureId,
  geometryType,
}: {
  featureId: string;
  /** The feature's GeoJSON geometry type, or null when it carries none. */
  geometryType: string | null;
}) {
  const { t } = useTranslation();
  const enabled = geometryType === 'Polygon' || geometryType === 'MultiPolygon';
  const { data, isLoading, isError } = useAreaAltitudeDisagreement(featureId, enabled);

  if (!enabled || isError) return null;

  return (
    <Card title={t('altitudeCheck.areaTitle')} style={{ marginBottom: 16 }} data-testid="area-altitude-card">
      {isLoading || !data ? (
        <Empty description={t('altitudeCheck.loading')} />
      ) : !data.hasTerrain ? (
        <Empty description={t('altitudeCheck.noTerrainBuilt')} />
      ) : data.entranceCount === 0 ? (
        <Empty description={t('altitudeCheck.noEntrances')} />
      ) : (
        <>
          <Row gutter={[16, 16]} style={{ marginBottom: 12 }}>
            <Col xs={12} md={6}>
              <Statistic title={t('altitudeCheck.entrancesPlaced')} value={data.entranceCount} />
            </Col>
            <Col xs={12} md={6}>
              <Statistic title={t('altitudeCheck.compared')} value={data.comparedCount} />
            </Col>
            <Col xs={12} md={6}>
              <Statistic title={t('altitudeCheck.disagreeingTitle')} value={data.disagreeingCount} />
            </Col>
            <Col xs={12} md={6}>
              <Statistic title={t('altitudeCheck.notRecordedTitle')} value={data.notRecordedCount} />
            </Col>
          </Row>
          <EntranceAltitudeTable
            rows={data.entrances}
            canFill={false}
            filling={null}
            onFill={() => undefined}
            linkCaves
          />
          {data.truncated && (
            <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
              {t('altitudeCheck.truncated', { count: data.entrances.length })}
            </Typography.Paragraph>
          )}
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            {t('altitudeCheck.countedOverWhatYouMayPlace')}
          </Typography.Paragraph>
        </>
      )}
    </Card>
  );
}
