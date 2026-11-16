// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Divider, Empty, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useCaveStructureComparison } from '../../api/hooks.ts';
import { useCaveStereonet } from '../../api/hooks/structural.ts';
import MeasurementTypeSelect from '../geology/MeasurementTypeSelect.tsx';
import StereonetDiagram from '../geology/StereonetDiagram.tsx';
import StrikeComparisonSection from '../geology/StrikeComparisonSection.tsx';
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
 * Two kinds of structure are on the other side. Mapped fracture and fault <em>lines</em> are set
 * against the passage rose against rose, by length. Readings taken at a point — bedding, joints,
 * fault planes — are set against it by their strikes with Watson's U², and plotted as poles on a
 * stereonet; the reader picks which kind of plane both of those read.
 * </p>
 */
export default function CaveStructurePanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const [measurementType, setMeasurementType] = useState('');
  const { data, isLoading, isError } = useCaveStructureComparison(caveId, undefined, measurementType || undefined);
  const stereonet = useCaveStereonet(caveId, measurementType || undefined);

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
      <Divider />
      <MeasurementTypeSelect value={measurementType} onChange={setMeasurementType} testId="cave-structure-type" />
      {data?.measuredStrikes && (
        <div style={{ marginTop: 12 }}>
          <StrikeComparisonSection data={data.measuredStrikes} />
        </div>
      )}
      {stereonet.data && (
        <div style={{ marginTop: 12 }} data-testid="cave-stereonet">
          <Typography.Title level={5}>{t('structural.stereonet.title')}</Typography.Title>
          {stereonet.data.readingCount === 0 ? (
            <Typography.Paragraph type="secondary">{t('structural.stereonet.noReadings')}</Typography.Paragraph>
          ) : (
            <>
              <Typography.Text>
                {t('structural.stereonet.caveReach', {
                  count: stereonet.data.readingCount,
                  radius: Math.round(stereonet.data.radiusMetres ?? 0),
                })}
              </Typography.Text>
              <StereonetDiagram data={stereonet.data} />
            </>
          )}
        </div>
      )}
    </Card>
  );
}
