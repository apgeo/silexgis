// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { SurveySegmentBasis } from '../../api/hooks.ts';

/**
 * Which body of line work a figure was measured from, said in words under the figure.
 *
 * <p>
 * One home for this sentence, because the two panels that show survey figures must not word it
 * differently: a reader who saw "measured from the survey" beside one number and nothing beside
 * another would reasonably take the second for the same kind of measurement, and it is not. A
 * cave with a survey file is measured by the surveyor's own per-leg flags; a cave with only a
 * stored centerline is measured by the shape of that centerline, which keeps most of the length
 * but drops loose shots and the last metres of every dead end. Setting one against the other as
 * though they were one figure is the mistake this sentence exists to prevent.
 * </p>
 * <p>
 * It sits in the same register as the sentence saying figures are counted over what the reader may
 * see: a secondary paragraph directly beneath the numbers, not a tooltip somebody has to find.
 * </p>
 */
export default function SurveyBasisNote({ basis }: { basis: SurveySegmentBasis }) {
  const { t } = useTranslation();

  // Nothing was measured at all, and the panel around this says so in its own words. A basis
  // sentence here would be describing a measurement that was never taken.
  if (basis === 'unavailable') return null;

  return (
    <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0 }}>
      {basis === 'surveyFlags'
        ? t('statistics.cave.basisSurveyFlags')
        : t('statistics.cave.basisSkeletonHeuristic')}
    </Typography.Paragraph>
  );
}
