// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DatePicker, Flex, Input, Select, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useExpeditions,
  type ActivityState,
  type ExpeditionInfo,
  type ExpeditionListParams,
} from '../../api/hooks.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/**
 * The lifecycle states a camp may hold — all of them, which is the difference between a camp and a
 * trip. Written out as the wire vocabulary rather than derived from anything, so a state added on
 * the server does not silently become an option nobody decided to offer; the labels are the ones
 * the trip already uses, because it is one lifecycle and a second set of words for it would be a
 * second set to keep in step.
 */
const STATES: readonly ActivityState[] = [
  'draft',
  'proposed',
  'planned',
  'confirmed',
  'done',
  'published',
  'cancelled',
  'delayed',
];

const asDate = (value: Dayjs | null | undefined): string | undefined =>
  value ? value.format('YYYY-MM-DD') : undefined;

/**
 * The camps, newest first, narrowed by hand.
 *
 * Three filters and no more, each of them a question somebody actually asks of a list of camps:
 * which season, what it was called, and how far along it is. They are spelled out here and read
 * straight off the query string on the server rather than going through the saved-filter
 * machinery — that mechanism earns its cost on the surfaces where filters are saved and shared,
 * and a list this size would pay all of it for none of it.
 *
 * The date window asks what a camp *overlapped*, not what it started inside: a fortnight camp
 * running across the end of a month belongs to both halves of the season, and a camp that lasted
 * one day is a camp that ran on the day it started.
 */
export default function ExpeditionListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<ExpeditionListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useExpeditions({ ...params, search: search || undefined });

  const onTableChange = (pagination: TablePaginationConfig) => {
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }));
  };

  // Any narrowing returns to the first page. Staying on page four of a list that now has one is
  // an empty table nobody asked for, and it reads as a filter that found nothing.
  const narrow = (change: Partial<ExpeditionListParams>) =>
    setParams((p) => ({ ...p, ...change, page: 1 }));

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('expeditions.title')}
        </Typography.Title>
      </Flex>
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('expeditions.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          data-testid="expedition-search"
          onChange={(e) => {
            setSearchInput(e.target.value);
            setParams((p) => ({ ...p, page: 1 }));
          }}
        />
        <Select<ActivityState | undefined>
          allowClear
          placeholder={t('expeditions.stateFilter')}
          style={{ minWidth: 180 }}
          data-testid="expedition-state-filter"
          value={params.state as ActivityState | undefined}
          onChange={(value) => narrow({ state: value })}
          options={STATES.map((state) => ({
            value: state,
            label: t(`trips.stateValues.${state}`),
          }))}
        />
        <DatePicker.RangePicker
          allowEmpty={[true, true]}
          data-testid="expedition-date-filter"
          onChange={(range) =>
            narrow({ from: asDate(range?.[0]), to: asDate(range?.[1]) })
          }
        />
      </Flex>
      <Table<ExpeditionInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(record) => ({
          onClick: () => navigate(`/expeditions/${record.id}`),
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
            title: t('expeditions.dates'),
            key: 'dates',
            width: 220,
            // An absent end means "it did not run on past its first day", the same as it does on
            // a trip, so a one-day camp reads as that day and never as a range of itself.
            render: (_, camp) =>
              formatTripDates(camp.startDate, camp.endDate, i18n.resolvedLanguage),
          },
          { title: t('expeditions.nameField'), dataIndex: 'name' },
          {
            title: t('expeditions.state'),
            dataIndex: 'state',
            width: 150,
            render: (value: ExpeditionInfo['state']) => <TripStateTag state={value} />,
          },
          {
            title: t('features.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
        ]}
      />
    </div>
  );
}
