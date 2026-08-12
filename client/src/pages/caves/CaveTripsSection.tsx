// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Card, Table, Tag } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useTripLogs, type TripLogInfo } from '../../api/hooks.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';

/** A cave page shows a page of trips at a time; the whole list lives on the trips page. */
const PAGE_SIZE = 10;

/**
 * The trips that named this cave, newest first — which is the trip list's own default order,
 * so no ordering is asked for here.
 *
 * A caller who may read this cave but may not place it is answered with an empty page: the
 * trips carry their own sketched geometries, so listing the trips of a cave would place it.
 * That is the server's decision and it needs no branch here — an empty table is the correct
 * rendering of it, and the count in the header above is filtered through the same rule, so
 * the two never disagree.
 */
export default function CaveTripsSection({ caveId }: { caveId: string }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const { data, isFetching } = useTripLogs({ caveId, page, pageSize: PAGE_SIZE });

  const onTableChange = (pagination: TablePaginationConfig) => {
    setPage(pagination.current ?? 1);
  };

  return (
    <Card title={t('trips.title')} style={{ marginTop: 16 }}>
      <Table<TripLogInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(trip) => ({
          onClick: () => navigate(`/trip-logs/${trip.id}`),
          style: { cursor: 'pointer' },
        })}
        locale={{ emptyText: t('caves.tripLogsEmpty') }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize ?? PAGE_SIZE,
          total: data?.totalItems,
          hideOnSinglePage: true,
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
            dataIndex: 'type',
            width: 150,
            render: (value: TripLogInfo['type']) =>
              value ? <Tag>{t(`trips.typeValues.${value}`)}</Tag> : null,
          },
          {
            title: t('trips.participants'),
            key: 'participants',
            width: 120,
            align: 'right',
            render: (_, trip) => trip.participants.length,
          },
        ]}
      />
    </Card>
  );
}
