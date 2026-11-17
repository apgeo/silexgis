// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Descriptions, Empty, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useAreaSystemFigures } from '../../api/hooks/systemFigures.ts';

/**
 * The depth and height of a cave system — or of any karst area — taken across the caves filed
 * under it: highest and lowest entrance, system depth from the highest entrance to the lowest
 * surveyed point of any member, and the potential depth down to the level the area's springs flow at.
 *
 * <p>
 * Offered for the kinds that stand for a system or a tract of karst, and absent rather than empty
 * when the server has no answer: every figure is an altitude, the route is withheld whole from a
 * reader who may not place the area, and a refusal looks the same as an area that is not there. A
 * figure that cannot be computed shows a dash, never a nought. Membership is the declared one and
 * only caves this reader may place count, so two accounts can legitimately read different figures.
 * </p>
 */
export default function AreaSystemFiguresCard({
  featureId,
  featureTypeCode,
}: {
  featureId: string;
  featureTypeCode: string | null;
}) {
  const { t } = useTranslation();

  const enabled =
    featureTypeCode === 'cave_system' ||
    featureTypeCode === 'karst_area' ||
    featureTypeCode === 'massif' ||
    featureTypeCode === 'hill';
  const { data, isLoading, isError } = useAreaSystemFigures(featureId, enabled);

  if (!enabled || isError) return null;

  const dash = t('areaSystemFigures.notMeasured');
  const metres = (value: number | null | undefined) =>
    value === null || value === undefined
      ? dash
      : t('areaSystemFigures.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) });

  const body = () => {
    if (isLoading || !data) return <Empty description={t('areaSystemFigures.loading')} />;
    if (data.caveCount === 0) return <Empty description={t('areaSystemFigures.noCaves')} />;
    const f = data.figures;
    return (
      <>
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('areaSystemFigures.basisDeclared')}
        />
        {data.truncated && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            title={t('areaSystemFigures.truncated', { count: data.caveCount })}
          />
        )}
        <Descriptions size="small" column={{ xs: 1, md: 2 }} bordered>
          <Descriptions.Item label={t('areaSystemFigures.caves')}>{data.caveCount}</Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.surveyedCaves')}>
            {data.surveyedCaveCount}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.entrances')}>
            {f.entranceCount}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.highestEntrance')}>
            {metres(f.highestEntranceM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.lowestEntrance')}>
            {metres(f.lowestEntranceM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.entranceSpread')}>
            {metres(f.entranceSpreadM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.totalDepth')}>
            {metres(f.totalDepthM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.height')}>
            {metres(f.heightAboveLowestEntranceM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.resurgenceLevel')}>
            {metres(f.resurgenceLevelM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('areaSystemFigures.potentialDepth')}>
            {metres(f.potentialDepthM)}
          </Descriptions.Item>
        </Descriptions>
        {!data.hasAltitudes && (
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            {t('areaSystemFigures.noSurveyAltitudes')}
          </Typography.Paragraph>
        )}
        {data.isApproximation && (
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            {t('areaSystemFigures.approximate')}
          </Typography.Paragraph>
        )}
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('areaSystemFigures.explained')}
        </Typography.Paragraph>
      </>
    );
  };

  return (
    <Card
      title={t('areaSystemFigures.title')}
      style={{ marginBottom: 16 }}
      data-testid="area-system-figures"
    >
      {body()}
    </Card>
  );
}
