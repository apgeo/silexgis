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
 * Trend is refused the same way, and for a case that is easy to mistake for an empty cave: a
 * bearing exists only where a leg moves in plan, so a cave surveyed as pure vertical pitches has
 * a surveyed length, a depth and no measurable direction at all. That is not "no line work", and
 * saying so here would contradict the figures the panel above is showing from the same survey.
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
    // No line work at all, which is a different statement from line work that cannot be asked
    // this question — and only this branch may say the cave has nothing to measure.
    if (data.basis === 'unavailable') {
      return <Empty description={t('statistics.cave.nothingMeasured')} />;
    }

    // A bearing is only measured where a leg moves in plan. A cave surveyed as pure vertical
    // pitches moves not at all, and a survey whose every leg is marked as already surveyed on
    // another trip contributes none, so both arrive here with a real basis, a real surveyed
    // length and no trend whatsoever. Saying "no line work to measure" for those would flatly
    // contradict the surveyed length the panel above is showing at the same moment.
    const hasTrend = data.segmentCount > 0 && data.bins.some((b) => b.count > 0);

    return (
      <Row gutter={[16, 16]}>
        <Col xs={24} md={10}>
          {hasTrend ? (
            <RoseDiagram
              bins={data.bins}
              meanAxis={{
                length: data.byLength.meanAxisDegrees,
                count: data.byCount.meanAxisDegrees,
              }}
            />
          ) : (
            <div data-testid="rose-refused">
              <Empty description={t('statistics.orientation.noTrendTitle')} />
              <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
                {t('statistics.orientation.noTrendReason')}
              </Typography.Paragraph>
            </div>
          )}
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
