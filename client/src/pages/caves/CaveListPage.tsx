// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DownloadOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, Dropdown, Flex, Input, Select, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { downloadFile } from '../../api/download.ts';
import {
  useCanCreateContent,
  useCaveTypes,
  useCaves,
  useTags,
  type CaveListItem,
  type CaveListParams,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

const exportFormats = ['csv', 'geojson', 'gpx', 'kml', 'shapefile'] as const;

const sortableFields: Record<string, string> = {
  name: 'name',
  region: 'region',
  surveyedLength: 'surveyedLength',
  depth: 'depth',
  updatedAt: 'updatedAt',
};

export default function CaveListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [params, setParams] = useState<CaveListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useCaves({ ...params, search: search || undefined });
  const { data: caveTypes } = useCaveTypes();
  const { data: tags } = useTags('');

  const canCreate = useCanCreateContent();
  const typeName = (id: number) => caveTypes?.find((x) => x.id === id)?.name ?? '';

  // Exports honor the current filters (not the current page — the server streams all rows).
  const onExport = (format: string) => {
    const query = new URLSearchParams({ format });
    if (search) {
      query.set('search', search);
    }
    if (params.caveTypeId !== undefined) {
      query.set('caveTypeId', String(params.caveTypeId));
    }
    downloadFile(`/api/v1/export/caves?${query}`).catch(() => message.error(t('common.saveFailed')));
  };

  const onTableChange = (
    pagination: TablePaginationConfig,
    _filters: unknown,
    sorter: SorterResult<CaveListItem> | SorterResult<CaveListItem>[],
  ) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.field ? sortableFields[String(single.field)] : undefined;
    const sort = field && single.order ? `${single.order === 'descend' ? '-' : ''}${field}` : undefined;
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize, sort }));
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('caves.title')}
        </Typography.Title>
        <Flex gap={8}>
          <Dropdown
            menu={{
              items: exportFormats.map((format) => ({
                key: format,
                label: format.toUpperCase(),
                onClick: () => onExport(format),
              })),
            }}
          >
            <Button icon={<DownloadOutlined />}>{t('common.export')}</Button>
          </Dropdown>
          {canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/caves/new')}>
              {t('caves.newCave')}
            </Button>
          )}
        </Flex>
      </Flex>
      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('caves.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <Select
          allowClear
          placeholder={t('caves.allTypes')}
          style={{ width: 200 }}
          options={caveTypes?.map((x) => ({ value: Number(x.id), label: x.name }))}
          onChange={(value?: number) => setParams((p) => ({ ...p, page: 1, caveTypeId: value }))}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('tags.filterPlaceholder')}
          style={{ width: 200 }}
          options={tags?.map((x) => ({ value: x.slug, label: x.name }))}
          onChange={(value?: string) => setParams((p) => ({ ...p, page: 1, tag: value }))}
        />
      </Flex>
      <Table<CaveListItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(record) => ({ onClick: () => navigate(`/caves/${record.id}`), style: { cursor: 'pointer' } })}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name', sorter: true },
          { title: t('caves.identificationCode'), dataIndex: 'identificationCode', width: 120 },
          { title: t('caves.type'), dataIndex: 'caveTypeId', width: 160, render: typeName },
          { title: t('caves.region'), dataIndex: 'region', sorter: true, width: 140 },
          { title: t('caves.surveyedLength'), dataIndex: 'surveyedLength', sorter: true, width: 120, align: 'right' },
          { title: t('caves.depth'), dataIndex: 'depth', sorter: true, width: 110, align: 'right' },
          { title: t('caves.entrances'), dataIndex: 'entranceCount', width: 100, align: 'right' },
          {
            title: t('caves.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
        ]}
      />
    </div>
  );
}
