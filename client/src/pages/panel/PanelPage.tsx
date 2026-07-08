// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Flex, Input, Table, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import {
  useCave,
  useCaves,
  useSurveyModels,
  type CaveListItem,
  type CaveListParams,
} from '../../api/hooks.ts';
import CaveViewPanel from '../../components/caveview/CaveViewPanel.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { publish, subscribe } from '../../workspace/workspaceBus.ts';

/**
 * A single panel rendered chrome-less for pop-out windows (multi-monitor work).
 * Interactions publish references over the workspace bus; the main window's map
 * reacts. Panels: the cave registry and the 3D survey viewer.
 */
export default function PanelPage() {
  const { t } = useTranslation();
  const { panelId } = useParams<{ panelId: string }>();

  switch (panelId) {
    case 'registry':
      return <RegistryPanel />;
    case 'viewer3d':
      return <Viewer3dPanel />;
    default:
      return (
        <Flex align="center" justify="center" style={{ height: '100vh' }}>
          <Typography.Text type="secondary">{t('panel.unknown')}</Typography.Text>
        </Flex>
      );
  }
}

/**
 * Follows cave selections published on the workspace bus (registry pop-out, map click)
 * and renders the selected cave's first 3D survey model — the multi-monitor scenario:
 * map in one window, synced 3D in another.
 */
function Viewer3dPanel() {
  const { t } = useTranslation();
  const [caveId, setCaveId] = useState<string | null>(null);

  useEffect(
    () =>
      subscribe((event) => {
        if (
          event.kind === 'selection' &&
          (event.selection?.kind === 'cave' || event.selection?.kind === 'entrance')
        ) {
          setCaveId(event.selection.caveId);
        }
      }),
    [],
  );

  const { data: cave } = useCave(caveId ?? undefined);
  const { data: models } = useSurveyModels(caveId ?? undefined);
  const model = models?.[0];

  return (
    <Flex vertical style={{ height: '100vh', padding: 12 }} gap={8}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        {cave ? `${t('panel.viewer3dTitle')} — ${cave.name}` : t('panel.viewer3dTitle')}
      </Typography.Title>
      {model ? (
        <div style={{ flex: 1, minHeight: 0 }}>
          <CaveViewPanel
            fileUrl={model.modelUrl}
            fileName={`${model.name}.${model.format === 'lox' ? 'lox' : '3d'}`}
            height="100%"
          />
        </div>
      ) : (
        <Flex align="center" justify="center" style={{ flex: 1 }}>
          <Typography.Text type="secondary">{t('panel.viewer3dEmpty')}</Typography.Text>
        </Flex>
      )}
    </Flex>
  );
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
