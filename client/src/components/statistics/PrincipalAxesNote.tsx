// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { sharePercent, type Projection } from './principalAxesLabels.ts';

/**
 * What each principal axis is made of, and which measures it could not use.
 *
 * <p>
 * An axis with no account of its loadings is a pair of unlabelled numbers: the reader cannot tell
 * whether the long direction is "big caves" or "deep caves", and the picture invites a story the
 * data has not told. The loadings are the server's own and are listed in the order of the measures
 * the axes were taken over. A measure the grouped caves all recorded identically had no spread to
 * contribute and was left out by the server; it is named here so its absence is not mistaken for
 * the measure being unimportant.
 * </p>
 */
export default function PrincipalAxesNote({
  projection,
  positioned,
  onPage,
}: {
  projection: Projection;
  /** How many of the page's caves are drawn on the axes. */
  positioned: number;
  /** How many caves the page holds. */
  onPage: number;
}) {
  const { t } = useTranslation();
  const label = (measure: string) => t(`registryStats.measures.${measure}`, { defaultValue: measure });
  const describe = (loadings: readonly number[]) =>
    projection.measures
      .map((measure, i) => `${label(measure)} ${loadings[i] >= 0 ? '+' : '−'}${Math.abs(loadings[i]).toFixed(2)}`)
      .join(', ');

  return (
    <div data-testid="principal-axes-note" style={{ marginTop: 8 }}>
      <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="principal-axes-first">
        {t('karstStats.axisLoadings', {
          axis: 1,
          share: sharePercent(projection.first.explainedShare),
          loadings: describe(projection.first.loadings),
        })}
      </Typography.Paragraph>
      <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="principal-axes-second">
        {t('karstStats.axisLoadings', {
          axis: 2,
          share: sharePercent(projection.second.explainedShare),
          loadings: describe(projection.second.loadings),
        })}
      </Typography.Paragraph>
      {projection.droppedMeasures.length > 0 && (
        <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="principal-axes-dropped">
          {t('karstStats.axisDropped', { measures: projection.droppedMeasures.map(label).join(', ') })}
        </Typography.Paragraph>
      )}
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }} data-testid="principal-axes-scope">
        {t('karstStats.axisScope', { drawn: positioned, total: onPage })}
      </Typography.Paragraph>
    </div>
  );
}
