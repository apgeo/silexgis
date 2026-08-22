// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DatePicker, Empty, Flex, Select, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useMyTripLogs,
  useTripTypes,
  type ActivityState,
  type MyTripLogListParams,
  type TripLogInfo,
} from '../../api/hooks.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { countPeople } from '../../components/trips/roster.ts';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import { ACTIVITY_STATES } from '../../components/trips/tripStates.ts';
import { tripTypeLabelOf } from '../../components/trips/tripTypes.ts';

const asDate = (value: Dayjs | null | undefined): string | undefined =>
  value ? value.format('YYYY-MM-DD') : undefined;

/**
 * The trips the reader is on, soonest first.
 *
 * Whose trips these are is never asked for. The page sends no identifier of any kind and there is
 * no control here that could name a person: the server works "mine" out from whoever is making the
 * request, because a parameter for it would let somebody assemble where a named person has been
 * out of trips they may never open. That is why this is a page of its own rather than a filter on
 * the trip list.
 *
 * Two filters and no more, each a question somebody actually asks of their own coming fortnight:
 * which days, and how far along the plan is. They are read straight off the query string on the
 * server, the way the camps are — the saved-filter machinery earns its cost where filters are
 * saved and shared, and a list this size would pay all of it for none of it.
 *
 * The order is the server's and is not recomputed here. It is ascending, which is the opposite of
 * every other trip listing and is the whole point of this one: the next thing somebody is going on
 * is the row they came for, so it is the first row. Re-sorting the page of rows the server handed
 * back would only reorder the page, which is a different and wrong answer as soon as there is more
 * than one.
 */
export default function MyTripsPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  // No window is sent until somebody picks one. The server starts at today, and it has to be the
  // one deciding: the browser's idea of today is its own timezone's, every date this application
  // stores is UTC, and two answers to "what is today" is one more than a list can have.
  const [params, setParams] = useState<MyTripLogListParams>({ page: 1, pageSize: 20 });
  const { data, isFetching, isError } = useMyTripLogs(params);
  const { data: tripTypes } = useTripTypes();

  const onTableChange = (pagination: TablePaginationConfig) => {
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }));
  };

  // Any narrowing returns to the first page. Staying on page four of a list that now has one is
  // an empty table nobody asked for, and it reads as a filter that found nothing.
  const narrow = (change: Partial<MyTripLogListParams>) =>
    setParams((p) => ({ ...p, ...change, page: 1 }));

  const isNarrowed =
    params.state !== undefined || params.from !== undefined || params.to !== undefined;

  // Three different situations, three different sentences, because only one of them is a fact
  // about the reader. "You are not on any trip yet" is a claim about somebody's diary: a request
  // that never answered has not earned it, and neither has a filter that excluded everything —
  // saying it to a caver on twelve trips who asked for the cancelled ones would be flatly untrue.
  // An account on nothing yet is the ordinary case for somebody who has just joined, not a fault
  // and not a search that found nothing, so that wording says what would put a trip here rather
  // than reporting an absence.
  const emptyText = isError
    ? t('trips.mine.failed')
    : isNarrowed
      ? t('trips.mine.emptyFiltered')
      : t('trips.mine.empty');

  const nothingHere = (
    <Empty
      data-testid="my-trips-empty"
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={emptyText}
    />
  );

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('trips.mine.title')}
        </Typography.Title>
      </Flex>
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Select<ActivityState | undefined>
          allowClear
          placeholder={t('trips.mine.stateFilter')}
          style={{ minWidth: 180 }}
          data-testid="my-trips-state-filter"
          value={params.state}
          onChange={(value) => narrow({ state: value })}
          options={ACTIVITY_STATES.map((state) => ({
            value: state,
            label: t(`trips.stateValues.${state}`),
          }))}
        />
        <DatePicker.RangePicker
          allowEmpty={[true, true]}
          data-testid="my-trips-date-filter"
          onChange={(range) => narrow({ from: asDate(range?.[0]), to: asDate(range?.[1]) })}
        />
      </Flex>
      <Table<TripLogInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(record) => ({
          onClick: () => navigate(`/trip-logs/${record.id}`),
          style: { cursor: 'pointer' },
        })}
        locale={{ emptyText: nothingHere }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('trips.date'),
            key: 'date',
            width: 200,
            render: (_, trip) =>
              formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.resolvedLanguage),
          },
          { title: t('trips.titleField'), dataIndex: 'title' },
          {
            title: t('trips.state'),
            dataIndex: 'state',
            width: 140,
            render: (value: TripLogInfo['state']) => <TripStateTag state={value} />,
          },
          {
            title: t('trips.type'),
            dataIndex: 'tripTypeId',
            width: 150,
            render: (value: TripLogInfo['tripTypeId']) => {
              const label = tripTypeLabelOf(value, tripTypes, t);
              return label ? <Tag>{label}</Tag> : null;
            },
          },
          {
            title: t('trips.participants'),
            key: 'participants',
            width: 120,
            align: 'right',
            render: (_, trip) => countPeople(trip.participants),
          },
        ]}
      />
    </div>
  );
}
