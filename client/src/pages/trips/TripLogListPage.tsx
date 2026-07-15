// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useMe, useTripLogs, type TripLogInfo, type TripLogListParams } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import TripFormModal from './TripFormModal.tsx';

export default function TripLogListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<TripLogListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useTripLogs({ ...params, search: search || undefined });
  const { data: me } = useMe();
  const [creating, setCreating] = useState(false);

  const canCreate = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;

  const onTableChange = (pagination: TablePaginationConfig) => {
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }));
  };

  const formatDate = (trip: TripLogInfo) => {
    const start = new Date(trip.tripDate).toLocaleDateString(i18n.resolvedLanguage);
    return trip.tripDateEnd
      ? `${start} – ${new Date(trip.tripDateEnd).toLocaleDateString(i18n.resolvedLanguage)}`
      : start;
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
          { title: t('trips.date'), key: 'date', width: 200, render: (_, trip) => formatDate(trip) },
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
    </div>
  );
}
