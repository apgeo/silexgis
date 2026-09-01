// SPDX-License-Identifier: AGPL-3.0-or-later
import { Col, Empty, Row, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { OrientationBin, RoseDivergence } from '../../api/hooks.ts';
import RoseDiagram from './RoseDiagram.tsx';

/**
 * Two roses side by side and how far apart they are.
 *
 * <p>
 * The two halves are drawn with the same diagram on purpose. What makes the comparison mean
 * anything is that both sets of bearings were folded the same way, cut at the same sectors and
 * measured the same way — on the spheroid, from the shape as drawn — and showing them in two
 * differently-built figures would invite a reader to compare the pictures rather than the numbers.
 * </p>
 * <p>
 * The divergence is stated as a share of a quarter turn rather than as a percentage of agreement,
 * because it is a distance and not a score: nought means the two lie along each other and one means
 * they lie across each other, and there is no direction in which "more" is better.
 * </p>
 */
export interface StructureComparisonViewProps {
  leftTitle: string;
  rightTitle: string;
  left: { bins: OrientationBin[]; meanAxisDegrees: number | null } | null;
  right: { bins: OrientationBin[]; meanAxisDegrees: number | null } | null;
  divergence: RoseDivergence | null;
  /** What is said when one side has nothing in it. */
  leftEmpty: string;
  rightEmpty: string;
  testId: string;
}

export default function StructureComparisonView({
  leftTitle,
  rightTitle,
  left,
  right,
  divergence,
  leftEmpty,
  rightEmpty,
  testId,
}: StructureComparisonViewProps) {
  const { t } = useTranslation();

  return (
    <div data-testid={testId}>
      <Row gutter={[16, 16]}>
        <Col xs={24} md={12}>
          <Typography.Text strong>{leftTitle}</Typography.Text>
          {left ? (
            <RoseDiagram bins={left.bins} meanAxis={{ length: left.meanAxisDegrees }} />
          ) : (
            <Empty description={leftEmpty} />
          )}
        </Col>
        <Col xs={24} md={12}>
          <Typography.Text strong>{rightTitle}</Typography.Text>
          {right ? (
            <RoseDiagram bins={right.bins} meanAxis={{ length: right.meanAxisDegrees }} />
          ) : (
            <Empty description={rightEmpty} />
          )}
        </Col>
      </Row>

      {/* Absent rather than nought when either side is empty. Nothing was measured on one of them,
          so there is no disagreement to report, and a nought here would say the two agree
          perfectly about a structure nobody has mapped. */}
      {divergence ? (
        <Row gutter={[16, 16]} style={{ marginTop: 16 }} data-testid={`${testId}-divergence`}>
          <Col xs={12} md={8}>
            <Statistic
              title={t('structureComparison.divergence')}
              value={divergence.normalized.toFixed(2)}
            />
          </Col>
          <Col xs={12} md={8}>
            <Statistic
              title={t('structureComparison.meanSeparation')}
              value={
                divergence.meanAxisSeparationDegrees == null
                  ? '—'
                  : divergence.meanAxisSeparationDegrees.toFixed(1)
              }
              suffix={t('structureComparison.unitDegrees')}
            />
          </Col>
        </Row>
      ) : (
        <Typography.Paragraph
          type="secondary"
          data-testid={`${testId}-no-divergence`}
          style={{ marginTop: 16, marginBottom: 0 }}
        >
          {t('structureComparison.noDivergence')}
        </Typography.Paragraph>
      )}

      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        {t('structureComparison.divergenceScale')}
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {t('structureComparison.linesOnly')}
      </Typography.Paragraph>
    </div>
  );
}
