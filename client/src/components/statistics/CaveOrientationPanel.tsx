// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Empty, Row, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useCaveOrientation } from '../../api/hooks.ts';
import DipHistogram from './DipHistogram.tsx';
import RoseDiagram from './RoseDiagram.tsx';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * Which way a cave's passages run, and how steeply.
 *
 * <p>
 * The two figures answer different questions from the same line work and are shown together
 * because neither is much use alone: a strong trend in plan says nothing about whether the cave
 * descends along it.
 * </p>
 * <p>
 * Steepness is refused rather than reported when the line work carries no altitudes. That refusal
 * is drawn as a stated reason and never as an empty chart or a mean of zero — a plan drawing shown
 * as a histogram piled on 0° would say the cave is level everywhere, which is a claim about the
 * cave rather than about the drawing, and the reader has no way to tell the two apart.
 * </p>
 */
export default function CaveOrientationPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveOrientation(caveId);

  // Refused for this reader, or refused because the cave's exact position is closed to them —
  // the server answers both the same way on purpose, and a panel of blanks would misreport it.
  if (isError) return null;

  const body = () => {
    if (isLoading || !data) return <Empty description={t('statistics.orientation.loading')} />;
    if (data.basis === 'unavailable' || data.segmentCount === 0) {
      return <Empty description={t('statistics.cave.nothingMeasured')} />;
    }

    return (
      <Row gutter={[16, 16]}>
        <Col xs={24} md={10}>
          <RoseDiagram
            bins={data.bins}
            meanAxis={{
              length: data.byLength.meanAxisDegrees,
              count: data.byCount.meanAxisDegrees,
            }}
          />
        </Col>
        <Col xs={24} md={14}>
          {data.dip ? (
            <DipHistogram bins={data.dip.bins} />
          ) : (
            <div data-testid="dip-refused">
              <Empty description={t('statistics.orientation.dipRefusedTitle')} />
              <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
                {t('statistics.orientation.dipRefusedReason')}
              </Typography.Paragraph>
            </div>
          )}
          {data.dip?.meanAbsoluteDipDegrees != null && (
            <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
              {t('statistics.orientation.meanSteepness', {
                value: Math.round(data.dip.meanAbsoluteDipDegrees),
              })}
            </Typography.Paragraph>
          )}
        </Col>
      </Row>
    );
  };

  return (
    <Card size="small" title={t('statistics.orientation.title')} style={{ marginTop: 16 }}>
      <div data-testid="cave-orientation">
        {body()}
        {data && <SurveyBasisNote basis={data.basis} />}
      </div>
    </Card>
  );
}
