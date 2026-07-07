// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Select, Table, Tag, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useCaveTypes, useCaves, useMe, type CaveListItem, type CaveListParams } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

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
  const [params, setParams] = useState<CaveListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useCaves({ ...params, search: search || undefined });
  const { data: caveTypes } = useCaveTypes();
  const { data: me } = useMe();

  const canCreate = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;
  const typeName = (id: number) => caveTypes?.find((x) => x.id === id)?.name ?? '';

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
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/caves/new')}>
            {t('caves.newCave')}
          </Button>
        )}
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
      </Flex>
      <Table<CaveListItem>
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
