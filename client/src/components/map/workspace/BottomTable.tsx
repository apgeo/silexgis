// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloseOutlined, FullscreenExitOutlined, FullscreenOutlined } from '@ant-design/icons';
import { Empty, Flex, Table, Tabs, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../Button.tsx';
import { RowButton } from '../../RowOpeners.tsx';
import FeatureListPanel from '../FeatureListPanel.tsx';
import { getFeatureTypeNameByCode } from '../../../map/featureLayer.ts';
import { useSelectionSummary } from '../../../api/hooks/workingSet.ts';
import type { FeatureListItem } from '../../../api/hooks.ts';
import { useWorkspaceStore } from '../../../stores/workspaceStore.ts';

interface Props {
  maximised: boolean;
  onToggleMaximised: () => void;
  onClose: () => void;
}

/**
 * The table under the map: what the map shows, and what is picked.
 *
 * Both tabs are lists the workspace already had, read the way it already read them. "In view" is
 * the live index of the map's own loaded layers — the same features the map draws, already cut to
 * what the reader may see and already carrying the positions the server chose to give — so it can
 * say nothing about a protected entrance that the map does not. "Picked" is the working set, read
 * through the visibility-filtered feature list the selection card reads it through.
 *
 * Wide and short where the side panel is tall and narrow, so the two lists sit side by side.
 */
export default function BottomTable({ maximised, onToggleMaximised, onClose }: Props) {
  const { t } = useTranslation();
  const [tab, setTab] = useState('inview');
  const setCount = useWorkspaceStore((s) => s.selectionSet.length);
  const maximiseLabel = maximised
    ? t('workspace.restore', { name: t('workspace.tableName') })
    : t('workspace.maximise', { name: t('workspace.tableName') });

  return (
    <Tabs
      className="workspace-table-tabs"
      size="small"
      activeKey={tab}
      onChange={setTab}
      data-testid="workspace-table"
      tabBarExtraContent={
        <Flex gap={4}>
          <Tooltip title={`${maximiseLabel} (M)`}>
            <Button
              size="small"
              type="text"
              icon={maximised ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
              aria-label={maximiseLabel}
              aria-pressed={maximised}
              aria-keyshortcuts="M"
              onClick={onToggleMaximised}
              data-testid="workspace-table-maximise"
            />
          </Tooltip>
          <Tooltip title={t('workspace.close', { name: t('workspace.tableName') })}>
            <Button
              size="small"
              type="text"
              icon={<CloseOutlined />}
              aria-label={t('workspace.close', { name: t('workspace.tableName') })}
              onClick={onClose}
              data-testid="workspace-table-close"
            />
          </Tooltip>
        </Flex>
      }
      items={[
        { key: 'inview', label: t('map.tabInView'), children: <FeatureListPanel layout="row" /> },
        {
          key: 'picked',
          label: t('workspace.pickedTab', { count: setCount }),
          children: <PickedTable />,
        },
      ]}
    />
  );
}

/** The working set as rows: what each picked object is, and a way to bring it forward. */
function PickedTable() {
  const { t } = useTranslation();
  const selectionSet = useWorkspaceStore((s) => s.selectionSet);
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const ids = selectionSet.map((ref) => ref.id);
  const { data, isFetching } = useSelectionSummary(ids);
  // A set somebody emptied answers with the last page until the query notices; nothing picked is
  // nothing to list, whatever the cache still holds.
  const items = ids.length === 0 ? [] : (data?.items ?? []);

  if (ids.length === 0) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%', padding: 16 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('workspace.pickedEmpty')} />
      </Flex>
    );
  }

  return (
    <div style={{ padding: 8, height: '100%', overflow: 'auto' }}>
      <Table<FeatureListItem>
        size="small"
        rowKey="id"
        loading={isFetching && items.length === 0}
        pagination={false}
        dataSource={items}
        scroll={{ x: 'max-content' }}
        columns={[
          {
            key: 'name',
            title: t('caves.name'),
            render: (_, item) => (
              <RowButton onClick={() => setSelection({ kind: 'feature', featureId: item.id })}>
                {item.name ?? t('features.unnamed')}
              </RowButton>
            ),
          },
          {
            key: 'type',
            title: t('features.type'),
            render: (_, item) => getFeatureTypeNameByCode(item.featureTypeCode) ?? item.featureTypeCode,
          },
          {
            key: 'visibility',
            title: t('features.visibility'),
            render: (_, item) => <Tag>{t(`caves.visibilityValues.${item.visibility}`)}</Tag>,
          },
        ]}
      />
      {/* What the set asked about and what came back can differ — an object the reader may not see
          is simply not listed — and the count above is of the set, so the gap is not explained in
          a way that would itself say something about the missing objects. */}
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('panel.multiHint')}
      </Typography.Text>
    </div>
  );
}
