// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Flex, Input, Table, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { useCaves, type CaveListItem, type CaveListParams } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { publish } from '../../workspace/workspaceBus.ts';

/**
 * A single panel rendered chrome-less for pop-out windows (multi-monitor work).
 * Interactions publish references over the workspace bus; the main window's map
 * reacts. Currently one panel exists: the cave registry.
 */
export default function PanelPage() {
  const { t } = useTranslation();
  const { panelId } = useParams<{ panelId: string }>();

  if (panelId !== 'registry') {
    return (
      <Flex align="center" justify="center" style={{ height: '100vh' }}>
        <Typography.Text type="secondary">{t('panel.unknown')}</Typography.Text>
      </Flex>
    );
  }

  return <RegistryPanel />;
}

function RegistryPanel() {
  const { t } = useTranslation();
  const [params, setParams] = useState<CaveListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useCaves({ ...params, search: search || undefined });

  const pick = (cave: CaveListItem) => {
    publish({ kind: 'selection', selection: { kind: 'cave', caveId: cave.id } });
    if (cave.mainGeom) {
      publish({
        kind: 'fly-to',
        lon: cave.mainGeom.coordinates[0],
        lat: cave.mainGeom.coordinates[1],
        zoom: 14,
      });
    }
  };

  return (
    <div style={{ padding: 16, height: '100vh', overflow: 'auto' }}>
      <Flex gap={8} align="center" style={{ marginBottom: 12 }}>
        <Typography.Title level={5} style={{ margin: 0, flex: 1 }}>
          {t('panel.registryTitle')}
        </Typography.Title>
        <Input.Search
          placeholder={t('caves.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 280 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </Flex>
      <Table<CaveListItem>
        rowKey="id"
        size="small"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination: TablePaginationConfig) =>
          setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }))}
        onRow={(record) => ({ onClick: () => pick(record), style: { cursor: 'pointer' } })}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: false,
        }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name' },
          { title: t('caves.region'), dataIndex: 'region', width: 140 },
          { title: t('caves.depth'), dataIndex: 'depth', width: 100, align: 'right' },
        ]}
      />
    </div>
  );
}
