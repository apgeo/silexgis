// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  useCan,
  useTripLogs,
  type TripLogInfo,
  type TripLogListParams,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import TripFormModal from './TripFormModal.tsx';
import { formatTripDates } from './tripDates.ts';

export default function TripLogListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<TripLogListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useTripLogs({ ...params, search: search || undefined });
  // The dashboard's "new trip" action routes here asking for the form to be open on arrival.
  const location = useLocation();
  const [creating, setCreating] = useState(
    Boolean((location.state as { create?: boolean } | null)?.create),
  );
  // Router state is stored in the history entry, so it outlives the modal being closed: without
  // clearing it, going Back to this entry (or reloading it) would re-open the form unasked.
  useEffect(() => {
    if ((location.state as { create?: boolean } | null)?.create) {
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location.pathname, location.state, navigate]);

  const canCreate = useCan('tripLogs', 'create');

  const onTableChange = (pagination: TablePaginationConfig) => {
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }));
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('trips.title')}
        </Typography.Title>
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('trips.new')}
          </Button>
        )}
      </Flex>
      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('trips.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </Flex>
      <Table<TripLogInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(record) => ({ onClick: () => navigate(`/trip-logs/${record.id}`), style: { cursor: 'pointer' } })}
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
            title: t('trips.type'),
            dataIndex: 'type',
            width: 150,
            render: (value: TripLogInfo['type']) =>
              value ? <Tag>{t(`trips.typeValues.${value}`)}</Tag> : null,
          },
          { title: t('trips.location'), dataIndex: 'locationText', width: 200 },
          {
            title: t('trips.participants'),
            key: 'participants',
            width: 120,
            align: 'right',
            render: (_, trip) => trip.participants.length,
          },
          {
            title: t('features.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
        ]}
      />
      {/* Gated here rather than only on the button: the form also opens from router state, and
          that path must not hand a create form to someone the server would refuse. */}
      {canCreate && (
        <TripFormModal
          open={creating}
          trip={null}
          onClose={(savedId) => {
            setCreating(false);
            if (savedId) {
              navigate(`/trip-logs/${savedId}`);
            }
          }}
        />
      )}
    </div>
  );
}
