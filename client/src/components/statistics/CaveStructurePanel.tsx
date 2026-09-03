// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useCaveStructureComparison } from '../../api/hooks.ts';
import StructureComparisonView from './StructureComparisonView.tsx';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * Whether a cave's passages follow the structure mapped around it.
 *
 * <p>
 * Absent rather than empty when the server has no answer, which covers a cave this reader may see
 * but may not place: the refusal is spelled as "no such cave", and a card of blanks would announce
 * that a guarded cave is here.
 * </p>
 * <p>
 * Only mapped fracture and fault <em>lines</em> are on the other side. Bedding and joint readings
 * taken at a station with a compass are a different kind of record and this installation stores
 * none, so that comparison is said to be unavailable rather than offered as an affordance that
 * always comes back empty.
 * </p>
 */
export default function CaveStructurePanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveStructureComparison(caveId);

  if (isError) return null;

  const body = () => {
    if (isLoading || !data) return <Empty description={t('structureComparison.loading')} />;

    return (
      <StructureComparisonView
        testId="cave-structure-comparison"
        leftTitle={t('structureComparison.passageRose')}
        rightTitle={t('structureComparison.structureRose', {
          count: data.structureFeatureCount,
          radius: Math.round(data.radiusMetres),
        })}
        left={
          data.passage
            ? { bins: data.passage.bins, meanAxisDegrees: data.passage.byLength.meanAxisDegrees }
            : null
        }
        right={
          data.structure
            ? { bins: data.structure.bins, meanAxisDegrees: data.structure.byLength.meanAxisDegrees }
            : null
        }
        divergence={data.divergence ?? null}
        leftEmpty={t('structureComparison.noPassageTrend')}
        rightEmpty={t('structureComparison.noStructureNearby')}
      />
    );
  };

  return (
    <Card title={t('structureComparison.caveTitle')} style={{ marginBottom: 16 }}>
      {body()}
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        {t('structureComparison.dolineModeElsewhere')}
      </Typography.Paragraph>
      {data && <SurveyBasisNote basis={data.basis} />}
    </Card>
  );
}
