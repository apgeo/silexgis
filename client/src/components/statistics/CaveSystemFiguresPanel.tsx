// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Descriptions, Empty, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useCaveSystemFigures } from '../../api/hooks/entranceAltitudes.ts';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * A cave's depth and height taken across all its entrances and its survey: highest and lowest
 * entrance, total depth from the highest entrance, height above the lowest, and the potential depth
 * down to the level the area's springs flow at.
 *
 * <p>
 * Absent rather than empty when the server has no answer, since every figure is an altitude and the
 * route is withheld whole from a reader who may not place the cave. A figure that cannot be
 * computed shows a dash, never a nought: a cave surveyed as a plan drawing is not a cave of no depth.
 * </p>
 */
export default function CaveSystemFiguresPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveSystemFigures(caveId);

  if (isError) return null;

  const dash = t('systemFigures.notMeasured');
  const metres = (value: number | null | undefined) =>
    value === null || value === undefined
      ? dash
      : t('systemFigures.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) });

  const body = () => {
    if (isLoading || !data) return <Empty description={t('systemFigures.loading')} />;
    const f = data.figures;
    if (f.entranceCount === 0) return <Empty description={t('systemFigures.noEntrances')} />;
    return (
      <>
        <Descriptions size="small" column={{ xs: 1, md: 2 }} bordered>
          <Descriptions.Item label={t('systemFigures.entrances')}>{f.entranceCount}</Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.highestEntrance')}>
            {metres(f.highestEntranceM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.lowestEntrance')}>
            {metres(f.lowestEntranceM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.entranceSpread')}>
            {metres(f.entranceSpreadM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.totalDepth')}>
            {metres(f.totalDepthM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.height')}>
            {metres(f.heightAboveLowestEntranceM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.resurgenceLevel')}>
            {metres(f.resurgenceLevelM)}
          </Descriptions.Item>
          <Descriptions.Item label={t('systemFigures.potentialDepth')}>
            {metres(f.potentialDepthM)}
          </Descriptions.Item>
          {data.potentialDepth.declaredM !== null && (
            <Descriptions.Item label={t('systemFigures.declaredPotentialDepth')}>
              {metres(data.potentialDepth.declaredM)}
              {data.potentialDepth.agreement === 'disagrees' && (
                <Typography.Text type="warning" style={{ marginLeft: 8 }}>
                  {t('systemFigures.disagreesWithRecord')}
                </Typography.Text>
              )}
            </Descriptions.Item>
          )}
        </Descriptions>
        {!data.hasAltitudes && (
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            {t('systemFigures.noSurveyAltitudes')}
          </Typography.Paragraph>
        )}
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('systemFigures.explained')}
        </Typography.Paragraph>
        <SurveyBasisNote basis={data.basis} />
      </>
    );
  };

  return (
    <Card title={t('systemFigures.title')} style={{ marginBottom: 16 }} data-testid="cave-system-figures">
      {body()}
    </Card>
  );
}
