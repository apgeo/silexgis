// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Flex, Input, Table, Tag, Typography } from 'antd';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import type { AuditEntry } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

const actionColor: Record<string, string> = {
  created: 'green',
  updated: 'blue',
  deleted: 'red',
};

/** Admin audit trail: who changed what, with per-property diffs. */
export default function AuditPage() {
  const { t, i18n } = useTranslation();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [entityTypeInput, setEntityTypeInput] = useState('');
  const entityType = useDebouncedValue(entityTypeInput);

  const { data, isFetching } = useQuery({
    queryKey: ['audit', page, pageSize, entityType],
    queryFn: async () => {
      const { data: result, error, response } = await api.GET('/api/v1/audit', {
        params: { query: { page, pageSize, entityType: entityType || undefined } },
      });
      if (error !== undefined || result === undefined) {
        throw new Error(`API error ${response.status}`);
      }
      return result;
    },
    placeholderData: keepPreviousData,
  });

  return (
    <div style={{ padding: 24 }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('audit.title')}
      </Typography.Title>
      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('audit.entityTypePlaceholder')}
          allowClear
          style={{ maxWidth: 260 }}
          onChange={(e) => setEntityTypeInput(e.target.value)}
        />
      </Flex>
      <Table<AuditEntry>
        rowKey="id"
        size="small"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 50);
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        expandable={{
          rowExpandable: (record) => record.changes != null,
          expandedRowRender: (record) => (
            <pre style={{ margin: 0, fontSize: 12, whiteSpace: 'pre-wrap' }}>
              {JSON.stringify(record.changes, null, 2)}
            </pre>
          ),
        }}
        columns={[
          {
            title: t('audit.at'),
            dataIndex: 'at',
            width: 180,
            render: (value: string) => new Date(value).toLocaleString(i18n.resolvedLanguage),
          },
          { title: t('audit.user'), dataIndex: 'userName', width: 180 },
          {
            title: t('audit.action'),
            dataIndex: 'action',
            width: 120,
            render: (value: string) => <Tag color={actionColor[value]}>{value}</Tag>,
          },
          { title: t('audit.entityType'), dataIndex: 'entityType', width: 160 },
          { title: t('audit.entityId'), dataIndex: 'entityId', ellipsis: true },
        ]}
      />
    </div>
  );
}
