// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty } from 'antd';
import { useTranslation } from 'react-i18next';

import { useAreaStructureComparison } from '../../api/hooks.ts';
import StructureComparisonView from '../statistics/StructureComparisonView.tsx';

/**
 * Whether the closed depressions of an area lie along the structure mapped in it.
 *
 * <p>
 * The surface half of the same question the cave view asks underground. A depression opens where
 * the rock is already broken, so a doline field whose long axes line up with the mapped fracture
 * set is the same structure seen twice; one that does not is being steered by something else.
 * </p>
 * <p>
 * Each outline is weighted by how much longer it is than it is wide, so a round hollow — whose
 * "long axis" is whatever direction its least noise happened to favour — contributes next to
 * nothing rather than an equal vote.
 * </p>
 */
export default function AreaStructureCard({
  featureId,
  geometryType,
}: {
  featureId: string;
  geometryType: string | null;
}) {
  const { t } = useTranslation();
  const enabled = geometryType === 'Polygon' || geometryType === 'MultiPolygon';
  const { data, isLoading, isError } = useAreaStructureComparison(featureId, enabled);

  if (!enabled || isError) return null;

  return (
    <Card title={t('structureComparison.areaTitle')} style={{ marginBottom: 16 }}>
      {isLoading || !data ? (
        <Empty description={t('structureComparison.loading')} />
      ) : (
        <StructureComparisonView
          testId="area-structure-comparison"
          leftTitle={t('structureComparison.dolineRose', { count: data.dolineCount })}
          rightTitle={t('structureComparison.areaStructureRose', {
            count: data.structureFeatureCount,
          })}
          left={
            data.dolines
              ? { bins: data.dolines.bins, meanAxisDegrees: data.dolines.byLength.meanAxisDegrees }
              : null
          }
          right={
            data.structure
              ? {
                  bins: data.structure.bins,
                  meanAxisDegrees: data.structure.byLength.meanAxisDegrees,
                }
              : null
          }
          divergence={data.divergence ?? null}
          leftEmpty={t('structureComparison.noDolines')}
          rightEmpty={t('structureComparison.noStructureInArea')}
        />
      )}
    </Card>
  );
}
