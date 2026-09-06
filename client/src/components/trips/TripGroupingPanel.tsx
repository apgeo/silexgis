// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Empty, Flex, Select, Space, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import type { TripGroup, TripListGrouping, TripType } from '../../api/hooks.ts';
import { formatTripDates } from './tripDates.ts';
import { tripTypeLabelOf } from './tripTypes.ts';

/**
 * The trip listing broken into slices, one or two levels deep.
 *
 * It answers "who and what, per slice" — the question the table cannot, because a table shows
 * trips one at a time and this shows what a set of them has in common. Every slice carries what
 * the original computed and then threw away: not only how many trips, but the days they span, the
 * purposes they were for and who was on them, each with its own count beside it. A name without
 * its count says somebody appeared; a name with one says how much.
 *
 * The dimensions are offered with "none" first, because not grouping is the ordinary state and a
 * panel whose first option reshapes the page is a panel that reshapes it by accident.
 *
 * The one thing this must say out loud is that the numbers do not add up. A trip counts into every
 * area it names and every person it carries, so slicing by either makes the counts sum to more
 * than the trips — which is the right answer to "how many trips did each caver do" and looks like
 * an error wherever nothing says so. The server marks the answer and the label below is that mark
 * spelled out; it is not decoration.
 */
export interface TripGroupingPanelProps {
  grouping: TripListGrouping | undefined;
  groupBy: string;
  thenBy: string;
  tripTypes: TripType[] | undefined;
  language: string | undefined;
  onChange: (change: { groupBy?: string; thenBy?: string }) => void;
}

/** The dimensions a listing can be cut by, "none" first. */
const dimensions = ['none', 'year', 'type', 'state', 'visibility', 'incident', 'area', 'participant'];

/**
 * What to call a slice. The server labels only what the client holds no vocabulary for — a person,
 * an area — so everything else is translated here, in the reader's own language, from the value
 * the filter itself takes.
 */
function sliceName(
  slice: { value: string; label?: string | null },
  dimension: string,
  tripTypes: TripType[] | undefined,
  t: TFunction,
): string {
  if (slice.value === '') {
    return t('trips.grouping.unassigned');
  }
  if (slice.label) {
    return slice.label;
  }
  switch (dimension) {
    case 'type':
      return tripTypeLabelOf(Number(slice.value), tripTypes, t) ?? slice.value;
    case 'state':
      return t(`trips.stateValues.${slice.value}`);
    case 'visibility':
      return t(`caves.visibilityValues.${slice.value}`);
    case 'incident':
      return slice.value === 'true' ? t('trips.filters.incidentYes') : t('trips.filters.incidentNo');
    default:
      return slice.value;
  }
}

export default function TripGroupingPanel({
  grouping,
  groupBy,
  thenBy,
  tripTypes,
  language,
  onChange,
}: TripGroupingPanelProps) {
  const { t } = useTranslation();

  const options = (exclude: string) =>
    dimensions
      .filter((word) => word === 'none' || word !== exclude)
      .map((word) => ({ value: word, label: t(`trips.grouping.dimensions.${word}`) }));

  // A name and its count together, always: a name on its own says somebody appeared, and a name
  // with a number beside it says how much — which is the figure the original threw away.
  const namesOf = (
    values: readonly { value: string; count: number; label?: string | null }[],
    dimension: string,
  ) =>
    values.map((value) => (
      <Tag key={value.value}>
        {`${sliceName(value, dimension, tripTypes, t)} (${value.count})`}
      </Tag>
    ));

  const rows = grouping?.groups ?? [];

  return (
    <Card size="small" style={{ marginBottom: 12 }} data-testid="trip-grouping-panel">
      <Flex gap={8} wrap align="center" style={{ marginBottom: rows.length > 0 ? 12 : 0 }}>
        <Typography.Text type="secondary">{t('trips.grouping.groupBy')}</Typography.Text>
        <Select
          style={{ minWidth: 160 }}
          data-testid="trip-grouping-primary"
          value={groupBy}
          options={options(thenBy)}
          onChange={(value) => onChange({ groupBy: value, thenBy: value === 'none' ? 'none' : thenBy })}
        />
        <Typography.Text type="secondary">{t('trips.grouping.thenBy')}</Typography.Text>
        <Select
          style={{ minWidth: 160 }}
          data-testid="trip-grouping-secondary"
          value={thenBy}
          disabled={groupBy === 'none'}
          options={options(groupBy)}
          onChange={(value) => onChange({ thenBy: value })}
        />
      </Flex>
      {groupBy !== 'none' && grouping?.overlapping && (
        // The axis label. A trip counts into every value it holds, so the slice counts exceed the
        // trip count on purpose — and a total that quietly exceeds its population is the one
        // figure nobody double-checks.
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="trip-grouping-overlap"
          message={t('trips.grouping.overlapping', { count: grouping.matching })}
        />
      )}
      {groupBy !== 'none' && grouping?.truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('trips.grouping.truncated', { count: grouping.matching })}
        />
      )}
      {groupBy !== 'none' && (
        <Table<TripGroup>
          size="small"
          rowKey={(group) => group.value}
          dataSource={rows}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{
            emptyText: (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('trips.grouping.empty')} />
            ),
          }}
          expandable={
            thenBy === 'none'
              ? undefined
              : {
                  rowExpandable: (group) => group.groups.length > 0,
                  expandedRowRender: (group) => (
                    <Space wrap data-testid="trip-grouping-second-level">
                      {group.groups.map((inner) => (
                        <Tag key={inner.value}>
                          {`${sliceName(inner, thenBy, tripTypes, t)} (${inner.count})`}
                        </Tag>
                      ))}
                    </Space>
                  ),
                }
          }
          columns={[
            {
              title: t(`trips.grouping.dimensions.${groupBy}`),
              key: 'value',
              render: (_, group) => sliceName(group, groupBy, tripTypes, t),
            },
            {
              title: t('trips.grouping.trips'),
              key: 'count',
              width: 100,
              align: 'right',
              render: (_, group) => group.count,
            },
            {
              title: t('trips.grouping.span'),
              key: 'span',
              width: 220,
              render: (_, group) => formatTripDates(group.firstDay, group.lastDay, language),
            },
            {
              title: t('trips.grouping.topTypes'),
              key: 'topTypes',
              render: (_, group) => <Space wrap>{namesOf(group.topTypes, 'type')}</Space>,
            },
            {
              title: t('trips.grouping.topPeople'),
              key: 'topPeople',
              render: (_, group) => <Space wrap>{namesOf(group.topPeople, 'participant')}</Space>,
            },
          ]}
        />
      )}
    </Card>
  );
}
