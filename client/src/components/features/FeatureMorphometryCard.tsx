// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Col, Row, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useFeatureMorphometry } from '../../api/hooks.ts';

interface FeatureMorphometryCardProps {
  featureId: string;
  /** The feature's GeoJSON geometry type, or null when it carries no geometry. */
  geometryType: string | null;
}

/** Outline classes there is a shape to measure for. Anything else has no area. */
const measurable = new Set(['Polygon', 'MultiPolygon']);

/**
 * The measured shape of a drawn outline — the parameters a closed depression is described by.
 *
 * Every length is metres and every area square metres, worked out in the installation's own
 * working coordinate system. The outline itself is stored in degrees, which are not a unit of
 * length: a degree of longitude is about 111 km at the equator and about 74 km in the Carpathians,
 * so an area computed from stored coordinates would be a number in degrees squared that changes
 * with how far north the doline is. None of that arithmetic happens in this browser — the figures
 * arrive measured.
 *
 * The card is absent rather than empty whenever there is no answer. A reader who may see this
 * feature but may not be told where it is gets nothing from the server, because a shape, a size
 * and a bearing place a doline as surely as a coordinate does; showing blanks in that case would
 * both announce that a guarded doline is here and read as "this one has no size".
 */
export default function FeatureMorphometryCard({ featureId, geometryType }: FeatureMorphometryCardProps) {
  const { t } = useTranslation();
  const enabled = geometryType !== null && measurable.has(geometryType);
  const { data, isLoading, isError } = useFeatureMorphometry(featureId, enabled);

  if (!enabled || isError) {
    return null;
  }

  if (data && !data.geometryValid) {
    // An outline that crosses itself still has an area function, and it answers zero. Saying so is
    // the only honest answer: reporting the zero would describe the drawing as a doline of no size
    // rather than as a drawing that needs fixing.
    return (
      <Card title={t('morphometry.title')} style={{ marginBottom: 16 }}>
        <Alert type="warning" showIcon message={t('morphometry.invalidGeometry')} />
      </Card>
    );
  }

  return (
    <Card title={t('morphometry.title')} style={{ marginBottom: 16 }}>
      <Row gutter={[16, 16]} data-testid="feature-morphometry">
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.area')}
            value={format(data?.areaM2, 0)}
            suffix={t('morphometry.unitSquareMetres')}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.perimeter')}
            value={format(data?.perimeterM, 1)}
            suffix={t('morphometry.unitMetres')}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.circularity')}
            value={format(data?.circularity, 3)}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.longAxis')}
            value={format(data?.longAxisM, 1)}
            suffix={t('morphometry.unitMetres')}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.shortAxis')}
            value={format(data?.shortAxisM, 1)}
            suffix={t('morphometry.unitMetres')}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.elongation')}
            value={format(data?.elongation, 2)}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.longAxisAzimuth')}
            value={format(data?.longAxisAzimuthDegrees, 1)}
            suffix={t('morphometry.unitDegrees')}
            loading={isLoading}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('morphometry.centroid')}
            value={
              data?.centroidLatitude != null && data.centroidLongitude != null
                ? `${data.centroidLatitude.toFixed(5)}, ${data.centroidLongitude.toFixed(5)}`
                : '—'
            }
            loading={isLoading}
          />
        </Col>
      </Row>
      <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0 }}>
        {t('morphometry.measuredInWorkingSystem')}
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {t('morphometry.axisIsNotABearing')}
      </Typography.Paragraph>
    </Card>
  );
}

/** A figure to the stated number of places, or an em dash while there is not one yet. */
function format(value: number | null | undefined, places: number): string {
  return value == null ? '—' : value.toFixed(places);
}
