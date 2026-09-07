// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Col, Empty, Flex, Row, Segmented, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';

import { useTripLogStats, useTripTypes, type TripStatsBreakdown } from '../../api/hooks.ts';
import {
  TripBreakdownChart,
  TripYearChart,
  type TripBreakdownPoint,
} from '../../components/statistics/TripInsightCharts.tsx';
import { tripTypeLabelOf } from '../../components/trips/tripTypes.ts';
import {
  EmptyTripListFilter,
  isTripListNarrowed,
  readTripListFilter,
  tripFacetQuery,
  writeTripListFilter,
} from './tripListFilter.ts';

/**
 * What a club's trips add up to: how many, by when, what for, where and with whom — and whether
 * the ground being covered is still new.
 *
 * <p>
 * Every figure is counted over the trips the reader may open, so two accounts see different
 * numbers for the same filter and both are right. The page says so once, plainly, and every card
 * title names the population it is drawn from: a chart headed only "Trips per year" is a claim
 * about the archive made out of one reader's share of it.
 * </p>
 * <p>
 * The scope control is the reason this page is worth more than a row of totals. A narrowed
 * archive with nothing to compare it against says how much of something there is but not whether
 * that is a lot, so the same charts can be drawn over the filter the reader arrived with or over
 * everything they may read — and the titles move with the toggle, which is the half the older
 * screens got wrong. The filter itself stays in the address either way, so switching back loses
 * nothing and the whole view is still a link.
 * </p>
 */
export default function TripStatsPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const filter = useMemo(() => readTripListFilter(searchParams), [searchParams]);
  const narrowed = isTripListNarrowed(filter);

  // Which population the charts are drawn over. In the address like the filter is, so a link
  // hands over the comparison somebody was looking at and not only the narrowing behind it. The
  // ordinary case is the absent key, so an unnarrowed page has a bare address.
  const scope = searchParams.get('scope') === 'all' ? 'all' : 'filter';
  const setScope = (next: string) => {
    const params = new URLSearchParams(searchParams);
    if (next === 'all') {
      params.set('scope', 'all');
    } else {
      params.delete('scope');
    }
    setSearchParams(params);
  };

  // Asked over the listing's own narrowings and nothing else, so "the current filter" means the
  // same thing here as it does above the table. The other scope asks over the unnarrowed
  // listing, which is the reader's whole readable archive and not the installation's.
  const query = useMemo(
    () => tripFacetQuery(scope === 'all' ? EmptyTripListFilter : filter),
    [scope, filter],
  );
  const { data, isError } = useTripLogStats(query);
  const { data: tripTypes } = useTripTypes();

  const unassigned = t('trips.grouping.unassigned');
  const labelled = (
    breakdown: TripStatsBreakdown | undefined,
    name: (value: string, label: string | null) => string,
  ): TripBreakdownPoint[] =>
    (breakdown?.values ?? []).map((option) => ({
      label: option.value === '' ? unassigned : name(option.value, option.label),
      count: option.count,
    }));

  const types = labelled(data?.types, (value) =>
    tripTypeLabelOf(Number(value), tripTypes, t) ?? value,
  );
  const areas = labelled(data?.areas, (value, label) => label ?? value);
  const participants = labelled(data?.participants, (value, label) => label ?? value);

  // The phrase every card title ends in. Two whole sentences rather than a word slotted into one,
  // so a translation can put the population where its own grammar wants it.
  const population =
    scope === 'all'
      ? t('tripStats.populationAll', { overall: data?.overall ?? 0 })
      : t('tripStats.populationFiltered', {
          matching: data?.matching ?? 0,
          overall: data?.overall ?? 0,
        });

  /** The two sentences a breakdown owes its reader, where they apply and not otherwise. */
  const notes = (breakdown: TripStatsBreakdown | undefined, counted: number) => (
    <>
      {breakdown?.overlapping && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('tripStats.overlapping', { count: counted })}
        </Typography.Paragraph>
      )}
      {breakdown !== undefined && breakdown.distinct > breakdown.values.length && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 4, marginBottom: 0 }}>
          {t('tripStats.showingValues', {
            shown: breakdown.values.length,
            distinct: breakdown.distinct,
          })}
        </Typography.Paragraph>
      )}
    </>
  );

  const counted = data?.matching ?? 0;

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('tripStats.title')}
        </Typography.Title>
        <Flex gap={8} align="center" wrap>
          {/* The filter travels back with the reader, so a narrowing survives the detour. */}
          <Button
            icon={<ArrowLeftOutlined />}
            data-testid="trip-stats-back"
            onClick={() =>
              navigate({ pathname: '/trip-logs', search: writeTripListFilter(filter).toString() })
            }
          >
            {t('tripStats.backToList')}
          </Button>
          {/* Offered whether or not anything is narrowing the listing: on an unnarrowed one the
              two scopes agree, and a control that vanishes when it would agree is a control
              nobody learns is there. */}
          <Segmented
            data-testid="trip-stats-scope"
            value={scope}
            onChange={(value) => setScope(String(value))}
            options={[
              { value: 'filter', label: t('tripStats.scopeFiltered') },
              { value: 'all', label: t('tripStats.scopeAll') },
            ]}
          />
        </Flex>
      </Flex>

      {/* Said once, at the top, and in the same words the subject totals use — because it is the
          same claim: these are the reader's figures and not the club's. */}
      <Typography.Paragraph type="secondary" data-testid="trip-stats-access">
        {t('statistics.asVisibleToYou')}
      </Typography.Paragraph>
      {narrowed && scope === 'filter' && (
        <Typography.Paragraph type="secondary" data-testid="trip-stats-scope-note">
          {t('tripStats.compareHint')}
        </Typography.Paragraph>
      )}

      {isError ? (
        <Alert type="error" showIcon message={t('tripStats.failed')} data-testid="trip-stats-error" />
      ) : data === undefined ? (
        <Skeleton active paragraph={{ rows: 8 }} />
      ) : counted === 0 ? (
        <Empty data-testid="trip-stats-empty" description={t('tripStats.empty')} />
      ) : (
        <Row gutter={[16, 16]}>
          <Col xs={24}>
            <Card size="small" title={t('tripStats.perYear', { population })}>
              <TripYearChart
                years={data.years.map((year) => ({
                  year: year.year,
                  trips: year.trips,
                  areasSoFar: year.areasSoFar,
                }))}
              />
              <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
                {t('tripStats.newGroundNote')}
              </Typography.Paragraph>
            </Card>
          </Col>
          <Col xs={24} xl={12}>
            <Card size="small" title={t('tripStats.byType', { population })}>
              {types.length === 0 ? (
                <Empty description={t('tripStats.empty')} />
              ) : (
                <TripBreakdownChart values={types} testId="chart-trip-types" />
              )}
              {notes(data.types, counted)}
            </Card>
          </Col>
          <Col xs={24} xl={12}>
            <Card size="small" title={t('tripStats.byArea', { population })}>
              {areas.length === 0 ? (
                <Empty description={t('tripStats.empty')} />
              ) : (
                <TripBreakdownChart values={areas} testId="chart-trip-areas" />
              )}
              {notes(data.areas, counted)}
            </Card>
          </Col>
          <Col xs={24}>
            <Card size="small" title={t('tripStats.byPerson', { population })}>
              {participants.length === 0 ? (
                <Empty description={t('tripStats.empty')} />
              ) : (
                <TripBreakdownChart values={participants} testId="chart-trip-people" />
              )}
              {notes(data.participants, counted)}
            </Card>
          </Col>
        </Row>
      )}
    </div>
  );
}
