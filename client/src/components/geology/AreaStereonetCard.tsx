// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty, Space, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useAreaStereonet } from '../../api/hooks/structural.ts';
import MeasurementTypeSelect from './MeasurementTypeSelect.tsx';
import StereonetDiagram from './StereonetDiagram.tsx';

/**
 * The structural readings filed under an area, on a stereonet. Shown for an outline only, and absent
 * when the server refuses (an area this reader may not read) or when nothing was recorded under it
 * for every kind — an empty net on every karst area would be noise.
 */
export default function AreaStereonetCard({
  featureId,
  geometryType,
}: {
  featureId: string;
  geometryType: string | null;
}) {
  const { t } = useTranslation();
  const [measurementType, setMeasurementType] = useState('');
  const enabled = geometryType === 'Polygon' || geometryType === 'MultiPolygon';
  const { data, isLoading, isError } = useAreaStereonet(featureId, measurementType || undefined, enabled);

  if (!enabled || isError) return null;
  if (!isLoading && data && data.readingCount === 0 && measurementType === '') return null;

  return (
    <Card title={t('structural.stereonet.areaTitle')} style={{ marginBottom: 16 }} data-testid="area-stereonet">
      <Space orientation="vertical" size={8} style={{ width: '100%' }}>
        <MeasurementTypeSelect value={measurementType} onChange={setMeasurementType} testId="area-stereonet-type" />
        {isLoading || !data ? (
          <Empty description={t('structural.stereonet.loading')} />
        ) : data.readingCount === 0 ? (
          <Typography.Paragraph type="secondary">{t('structural.stereonet.noReadings')}</Typography.Paragraph>
        ) : (
          <>
            <Typography.Text>{t('structural.stereonet.readingCount', { count: data.readingCount })}</Typography.Text>
            <StereonetDiagram data={data} />
          </>
        )}
      </Space>
    </Card>
  );
}
