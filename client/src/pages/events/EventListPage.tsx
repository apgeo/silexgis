// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { Button, DatePicker, Flex, Input, Select, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useCan,
  useEvents,
  type ActivityState,
  type EventInfo,
  type EventKind,
  type EventListParams,
} from '../../api/hooks.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
// One lifecycle, one list of its words: a trip, a camp and an event all offer the whole of it.
import { ACTIVITY_STATES } from '../../components/trips/tripStates.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import EventFormModal from './EventFormModal.tsx';
import { EVENT_KINDS } from './eventKinds.ts';

const asDate = (value: Dayjs | null | undefined): string | undefined =>
  value ? value.format('YYYY-MM-DD') : undefined;

/**
 * The dated things a club runs that are not trips and not camps.
 *
 * The date window asks what an event *overlapped*, not what it started inside: a training weekend
 * running across the end of a month belongs to both halves, and an event that lasted one day ran
 * on the day it started. The dates themselves are calendar days and the times wall-clock, printed
 * from their own parts — never read as instants, which would put an evening in the wrong cell for
 * every reader west of the server.
 */
export default function EventListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<EventListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const [formOpen, setFormOpen] = useState(false);
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useEvents({ ...params, search: search || undefined });
  // Whether to offer the button at all. What a caller may actually do is settled per row on the
  // server, so this decides the offer and never the answer.
  const canCreate = useCan('events', 'create');

  const onTableChange = (pagination: TablePaginationConfig) => {
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }));
  };

  // Any narrowing returns to the first page. Staying on page four of a list that now has one is
  // an empty table nobody asked for, and it reads as a filter that found nothing.
  const narrow = (change: Partial<EventListParams>) =>
    setParams((p) => ({ ...p, ...change, page: 1 }));

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('events.title')}
        </Typography.Title>
        {canCreate && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            data-testid="event-create"
            onClick={() => setFormOpen(true)}
          >
            {t('events.create')}
          </Button>
        )}
      </Flex>
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('events.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          data-testid="event-search"
          onChange={(e) => {
            setSearchInput(e.target.value);
            setParams((p) => ({ ...p, page: 1 }));
          }}
        />
        <Select<EventKind | undefined>
          allowClear
          placeholder={t('events.kindFilter')}
          style={{ minWidth: 180 }}
          data-testid="event-kind-filter"
          value={params.kind as EventKind | undefined}
          onChange={(value) => narrow({ kind: value })}
          options={EVENT_KINDS.map((kind) => ({
            value: kind,
            label: t(`events.kindValues.${kind}`),
          }))}
        />
        <Select<ActivityState | undefined>
          allowClear
          placeholder={t('events.stateFilter')}
          style={{ minWidth: 180 }}
          data-testid="event-state-filter"
          value={params.state as ActivityState | undefined}
          onChange={(value) => narrow({ state: value })}
          options={ACTIVITY_STATES.map((state) => ({
            value: state,
            label: t(`trips.stateValues.${state}`),
          }))}
        />
        <DatePicker.RangePicker
          allowEmpty={[true, true]}
          data-testid="event-date-filter"
          onChange={(range) => narrow({ from: asDate(range?.[0]), to: asDate(range?.[1]) })}
        />
      </Flex>
      <Table<EventInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        data-testid="event-table"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(record) => ({
          onClick: () => navigate(`/events/${record.id}`),
          style: { cursor: 'pointer' },
        })}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('events.dates'),
            key: 'dates',
            width: 220,
            // An absent end means "it did not run on past its first day", the same as it does on
            // a trip, so a one-evening event reads as that day and never as a range of itself.
            render: (_, row) => formatTripDates(row.startDate, row.endDate, i18n.resolvedLanguage),
          },
          { title: t('events.titleField'), dataIndex: 'title' },
          {
            title: t('events.kind'),
            dataIndex: 'kind',
            width: 160,
            render: (value: EventKind) => <Tag>{t(`events.kindValues.${value}`)}</Tag>,
          },
          {
            title: t('events.state'),
            dataIndex: 'state',
            width: 150,
            render: (value: EventInfo['state']) => <TripStateTag state={value} />,
          },
          {
            title: t('features.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
        ]}
      />
      <EventFormModal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onSaved={(saved) => navigate(`/events/${saved.id}`)}
      />
    </div>
  );
}
