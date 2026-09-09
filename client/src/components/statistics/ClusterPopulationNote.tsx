// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { RegistryClustering } from '../../api/hooks.ts';

interface ClusterPopulationNoteProps {
  /** The grouping the colours came from, or `undefined` while none has been answered. */
  clustering: RegistryClustering | undefined;
  /**
   * Whether a grouping could be asked for at all. True when the list is narrowed by something the
   * registry-wide grouping has no parameter for, in which case no grouping was requested and the
   * absence of colour is a decision rather than a wait.
   */
  unaskable: boolean;
}

/**
 * Who the grouping was computed over, said before the grouping is looked at.
 *
 * <p>
 * Most caves in a register have no survey, and so have no measurements to group on. A scatter of
 * four hundred coloured points drawn over a register of three thousand, with nothing on screen
 * saying so, is a claim about the register that is false — and it is false in the direction that
 * flatters the data, because the caves it silently drops are exactly the ones nobody measured.
 * So the account of who was left out is published above the picture rather than beneath it, and
 * it is published whether or not anything could be grouped at all.
 * </p>
 * <p>
 * Every number here is the server's. Nothing is computed on this side: the eligibility, the
 * per-measure shortfall, the standardisation and the width-against-separation reading all come
 * from the one answer that produced the colours, so the sentence and the picture cannot drift
 * apart. The only judgements made here are which sentences apply.
 * </p>
 * <p>
 * Three of those sentences exist because the grouping cannot refuse to answer. It returns exactly
 * the number of groups it was asked for on any data whatever, noise included, so the existence of
 * groups is evidence of nothing: the width-against-separation reading is the only figure that can
 * contradict them, a group too small to summarise is a count and not a finding, and a run that hit
 * its iteration limit is arbitrary in its details. Left unsaid, all three look exactly like a
 * confident answer.
 * </p>
 */
export default function ClusterPopulationNote({ clustering, unaskable }: ClusterPopulationNoteProps) {
  const { t } = useTranslation();

  if (unaskable) {
    return (
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 12 }}
        data-testid="cave-cluster-unaskable"
        title={t('karstStats.clusterNotAsked')}
      />
    );
  }

  if (clustering === undefined) return null;

  const { population, scaling, clusters, separation } = clustering;
  const measureLabel = (measure: string) =>
    t(`registryStats.measures.${measure}`, { defaultValue: measure });

  // A measure nobody is missing costs the grouping nothing, so saying so for every measure would
  // bury the one that does cost it. Only the shortfalls are named.
  const shortfalls = population.measures.filter((coverage) => coverage.missing > 0);

  // A measure whose grouped caves all recorded the same value contributed nothing to any distance.
  // The reader chose that measure, so they are told it did no work rather than left to assume it did.
  const inert = scaling.filter((s) => s.standardDeviation === 0);

  const withheld = clusters.some(
    (cluster) => cluster.count > 0 && cluster.count < clustering.minimumPublishableClusterSize,
  );

  // Two different readings, and collapsing them would lose the stronger one: a ratio at or above
  // one says the groups are as wide as the gaps between them, while no ratio at all says the
  // middles did not separate even enough for the question to be asked.
  const weak = separation !== null && separation.ratio !== null && separation.ratio >= 1;
  const unseparated = separation !== null && separation.ratio === null;

  return (
    <Alert
      type="info"
      showIcon
      style={{ marginBottom: 12 }}
      data-testid="cave-cluster-population"
      title={
        <span data-testid="cave-cluster-excluded">
          {t('karstStats.clusterPopulation', {
            eligible: population.eligible,
            considered: population.considered,
            excluded: population.excluded,
          })}
        </span>
      }
      description={
        <>
          <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="cave-cluster-measures">
            {t('karstStats.clusterMeasures', {
              measures: clustering.measures.map(measureLabel).join(', '),
            })}
          </Typography.Paragraph>

          {shortfalls.map((coverage) => (
            <Typography.Paragraph
              key={coverage.measure}
              style={{ marginBottom: 4 }}
              data-testid={`cave-cluster-coverage-${coverage.measure}`}
            >
              {t('karstStats.clusterCoverage', {
                measure: measureLabel(coverage.measure),
                missing: coverage.missing,
                soleReason: coverage.soleReason,
              })}
            </Typography.Paragraph>
          ))}

          {inert.map((s) => (
            <Typography.Paragraph
              key={s.measure}
              style={{ marginBottom: 4 }}
              data-testid={`cave-cluster-inert-${s.measure}`}
            >
              {t('karstStats.clusterNoWork', { measure: measureLabel(s.measure) })}
            </Typography.Paragraph>
          ))}

          {clusters.length === 0 && (
            <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="cave-cluster-empty">
              {t('karstStats.clusterEmpty', { minimum: clustering.minimumEligibleCount })}
            </Typography.Paragraph>
          )}

          {weak && (
            <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="cave-cluster-weak">
              {t('karstStats.clusterWeak')}
            </Typography.Paragraph>
          )}

          {unseparated && (
            <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="cave-cluster-weak">
              {t('karstStats.clusterNoSeparation')}
            </Typography.Paragraph>
          )}

          {withheld && (
            <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="cave-cluster-withheld">
              {t('karstStats.clusterWithheld', { floor: clustering.minimumPublishableClusterSize })}
            </Typography.Paragraph>
          )}

          {!clustering.converged && (
            <Typography.Paragraph style={{ marginBottom: 4 }} data-testid="cave-cluster-arbitrary">
              {t('karstStats.clusterArbitrary')}
            </Typography.Paragraph>
          )}

          {/* The server's own sentence about what it counted over, in its own words, because two
              readers with different access get different groupings of the same registry and both
              are right. */}
          <Typography.Paragraph
            type="secondary"
            style={{ marginBottom: 0 }}
            data-testid="cave-cluster-basis"
          >
            {clustering.basis}
          </Typography.Paragraph>
        </>
      }
    />
  );
}
