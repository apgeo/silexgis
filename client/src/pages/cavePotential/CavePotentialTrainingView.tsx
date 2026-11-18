// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { Flex, Table, Tag, theme, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type {
  CavePotentialClassWeights,
  CavePotentialResultInfo,
  CavePotentialTrainingInfo,
  EvidenceLayerInfo,
} from '../../api/hooks/cavePotential.ts';
import { useECharts } from '../../components/statistics/useECharts.ts';
import { formatAuc, rateCurvesOption } from './cavePotentialCurves.ts';
import { classLabel, evidenceName } from './cavePotentialNames.ts';

/** A figure of a weight table: a dash where the class has none. */
function figure(value: number | null, digits: number): string {
  return value === null ? '–' : value.toFixed(digits);
}

interface Props {
  result: Pick<CavePotentialResultInfo, 'id' | 'method' | 'definition'>;
  training: CavePotentialTrainingInfo;
  /** The evidence the server offers, to name the classes of a layer that is classes already. */
  evidenceLayers: readonly EvidenceLayerInfo[] | undefined;
}

/**
 * What a model that learns from known entrances learnt on one run: how many entrances it counted
 * and kept back, the weight of every class of every layer, and how well the map it came to finds
 * the entrances it was made from and those it never saw.
 *
 * Everything here is a count over the whole area, a figure per class or a point of a curve. The
 * server sends no entrance, position or cell, and sends these at all only to a reader who may
 * place every entrance the run used — so nothing is hidden here, and nothing here could be.
 */
export default function CavePotentialTrainingView({ result, training, evidenceLayers }: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const option = useMemo(() => rateCurvesOption(t, token, training), [t, token, training]);
  const chart = useECharts(option);
  const weightsOfEvidence = result.method === 'weightsOfEvidence';

  return (
    <Flex vertical gap={12} data-testid="cave-potential-training-view">
      <Flex vertical gap={2}>
        <Typography.Text data-testid="cave-potential-training-counts">
          {t('cavePotential.training.counts', {
            training: training.trainingEntrances,
            trainingCells: training.trainingCells,
            heldOut: training.heldOutEntrances,
            heldOutCells: training.heldOutCells,
          })}
        </Typography.Text>
        <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="cave-potential-training-split">
          {t('cavePotential.training.split', {
            percent: Math.round(training.heldOutShare * 100),
            seed: training.seed,
            cells: training.cells,
          })}
        </Typography.Text>
        {training.protectedEntrances > 0 && (
          <Typography.Text type="warning" style={{ fontSize: 12 }} data-testid="cave-potential-training-protected">
            {t('cavePotential.training.protectedEntrances', { n: training.protectedEntrances })}
          </Typography.Text>
        )}
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t(
            weightsOfEvidence
              ? 'cavePotential.training.scaleWeightsOfEvidence'
              : 'cavePotential.training.scaleFrequencyRatio',
            { minimum: training.valueMinimum.toPrecision(3), maximum: training.valueMaximum.toPrecision(3) },
          )}
        </Typography.Text>
        {training.expectedOccurrences !== null && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="cave-potential-training-expected">
            {t('cavePotential.training.expected', {
              expected: training.expectedOccurrences.toFixed(1),
              trainingCells: training.trainingCells,
            })}
          </Typography.Text>
        )}
      </Flex>

      <Flex gap={16} wrap>
        <Typography.Text data-testid="cave-potential-auc-success">
          {t('cavePotential.training.aucSuccess', {
            auc: formatAuc(training.successRate.areaUnderCurve),
            n: training.successRate.occurrences,
          })}
        </Typography.Text>
        <Typography.Text data-testid="cave-potential-auc-prediction">
          {t('cavePotential.training.aucPrediction', {
            auc: formatAuc(training.predictionRate.areaUnderCurve),
            n: training.predictionRate.occurrences,
          })}
        </Typography.Text>
      </Flex>
      <div
        ref={chart}
        role="img"
        aria-label={t('cavePotential.training.curvesLabel')}
        data-testid="cave-potential-rate-curves"
        style={{ width: '100%', maxWidth: 560, height: 300 }}
      />
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('cavePotential.training.curvesHint')}
      </Typography.Text>

      {training.tables.map((table) => {
        const info = evidenceLayers?.find((candidate) => candidate.code === table.layer);
        const name = info ? evidenceName(t, info) : t(`cavePotential.evidence.${table.layer}`, { defaultValue: table.layer });
        return (
          <div key={table.layer}>
            <Typography.Text strong>{name}</Typography.Text>
            <Table<CavePotentialClassWeights>
              size="small"
              rowKey="class"
              pagination={false}
              scroll={{ x: 'max-content' }}
              dataSource={table.classes}
              data-testid={`cave-potential-weights-${table.layer}`}
              columns={[
                {
                  title: t('cavePotential.training.columns.class'),
                  key: 'class',
                  render: (_, row) => (
                    <Flex gap={4} align="center">
                      <span>{classLabel(t, table.layer, row.class, result.definition, info)}</span>
                      {row.corrected && (
                        <Tooltip title={t('cavePotential.training.correctedHint')}>
                          <Tag>{t('cavePotential.training.corrected')}</Tag>
                        </Tooltip>
                      )}
                    </Flex>
                  ),
                },
                { title: t('cavePotential.training.columns.cells'), key: 'cells', align: 'right', render: (_, row) => row.cells },
                {
                  title: t('cavePotential.training.columns.occurrences'),
                  key: 'occurrences',
                  align: 'right',
                  render: (_, row) => row.occurrences,
                },
                {
                  title: t('cavePotential.training.columns.frequencyRatio'),
                  key: 'frequencyRatio',
                  align: 'right',
                  render: (_, row) => figure(row.frequencyRatio, 2),
                },
                {
                  title: t('cavePotential.training.columns.positiveWeight'),
                  key: 'positiveWeight',
                  align: 'right',
                  render: (_, row) => figure(row.positiveWeight, 3),
                },
                {
                  title: t('cavePotential.training.columns.negativeWeight'),
                  key: 'negativeWeight',
                  align: 'right',
                  render: (_, row) => figure(row.negativeWeight, 3),
                },
                {
                  title: t('cavePotential.training.columns.contrast'),
                  key: 'contrast',
                  align: 'right',
                  render: (_, row) => figure(row.contrast, 3),
                },
                {
                  title: t('cavePotential.training.columns.contrastDeviation'),
                  key: 'contrastDeviation',
                  align: 'right',
                  render: (_, row) => figure(row.contrastDeviation, 3),
                },
                {
                  title: (
                    <Tooltip title={t('cavePotential.training.studentisedHint')}>
                      <span>{t('cavePotential.training.columns.studentisedContrast')}</span>
                    </Tooltip>
                  ),
                  key: 'studentisedContrast',
                  align: 'right',
                  render: (_, row) => figure(row.studentisedContrast, 2),
                },
              ]}
            />
          </div>
        );
      })}
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('cavePotential.training.tablesHint')}
      </Typography.Text>
    </Flex>
  );
}
