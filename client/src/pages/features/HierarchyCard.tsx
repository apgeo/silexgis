// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DeleteOutlined, EditOutlined, StarFilled } from '@ant-design/icons';
import { App, Button, Card, Flex, Modal, Radio, Select, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  useFeatureChildren,
  useFeatureParents,
  useFeatures,
  useSetFeatureParents,
  type FeatureChild,
  type FeatureKind,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface HierarchyCardProps {
  featureId: string;
  canEdit: boolean;
}

interface ParentRow {
  parentId: string;
  name: string | null;
  isPrimary: boolean;
}

/**
 * The feature's place in the containment DAG: its parent edges (with the one
 * primary edge highlighted) and a paged table of the features it contains.
 */
export default function HierarchyCard({ featureId, canEdit }: HierarchyCardProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: parents } = useFeatureParents(featureId);
  const [childrenPage, setChildrenPage] = useState(1);
  const { data: children } = useFeatureChildren(featureId, { page: childrenPage, pageSize: 10 });
  const setParents = useSetFeatureParents();

  const [editing, setEditing] = useState(false);
  const [rows, setRows] = useState<ParentRow[]>([]);
  const [parentQuery, setParentQuery] = useState('');
  const debouncedParentQuery = useDebouncedValue(parentQuery);
  const { data: searchResults, isFetching: searching } = useFeatures(
    { search: debouncedParentQuery || undefined, pageSize: 20 },
    editing,
  );

  const openEditor = () => {
    setRows((parents ?? []).map((p) => ({ parentId: p.id, name: p.name, isPrimary: p.isPrimary })));
    setParentQuery('');
    setEditing(true);
  };

  const addOptions = useMemo(
    () =>
      (searchResults?.items ?? [])
        // The feature cannot contain itself, and each parent appears once.
        .filter((f) => f.id !== featureId && !rows.some((r) => r.parentId === f.id))
        .map((f) => ({ value: f.id, label: f.name ?? t('features.unnamed') })),
    [searchResults, rows, featureId, t],
  );

  const addParent = (id: string) => {
    const option = searchResults?.items.find((f) => f.id === id);
    // The first edge becomes primary automatically; the DAG requires exactly one.
    setRows((r) => [...r, { parentId: id, name: option?.name ?? null, isPrimary: r.length === 0 }]);
    setParentQuery('');
  };

  const removeParent = (id: string) => {
    setRows((r) => {
      const next = r.filter((x) => x.parentId !== id);
      if (next.length > 0 && !next.some((x) => x.isPrimary)) {
        next[0] = { ...next[0], isPrimary: true };
      }
      return next;
    });
  };

  const makePrimary = (id: string) => {
    setRows((r) => r.map((x) => ({ ...x, isPrimary: x.parentId === id })));
  };

  const onSave = async () => {
    try {
      await setParents.mutateAsync({
        id: featureId,
        parents: rows.map((r) => ({ parentId: r.parentId, isPrimary: r.isPrimary })),
      });
      setEditing(false);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Card
      title={t('features.hierarchy')}
      style={{ marginTop: 16 }}
      extra={
        canEdit && (
          <Button size="small" icon={<EditOutlined />} onClick={openEditor}>
            {t('features.editParents')}
          </Button>
        )
      }
    >
      <Typography.Text type="secondary">{t('features.parents')}</Typography.Text>
      <Flex gap={8} wrap style={{ marginTop: 4, marginBottom: 16 }}>
        {(parents ?? []).length === 0 && (
          <Typography.Text type="secondary" italic>
            {t('features.noParents')}
          </Typography.Text>
        )}
        {(parents ?? []).map((p) => (
          <Tag key={p.id} icon={p.isPrimary ? <StarFilled /> : undefined}>
            <Link to={`/features/${p.id}`}>{p.name ?? t('features.unnamed')}</Link>
          </Tag>
        ))}
      </Flex>

      <Typography.Text type="secondary">{t('features.children')}</Typography.Text>
      <Table<FeatureChild>
        style={{ marginTop: 4 }}
        rowKey="id"
        size="small"
        dataSource={children?.items}
        locale={{ emptyText: t('features.noChildren') }}
        pagination={
          (children?.totalItems ?? 0) > 10
            ? {
                current: children?.page,
                pageSize: children?.pageSize,
                total: children?.totalItems,
                onChange: setChildrenPage,
              }
            : false
        }
        columns={[
          {
            title: t('features.name'),
            dataIndex: 'name',
            render: (name: string | null, record) => (
              <Link to={`/features/${record.id}`}>{name ?? t('features.unnamed')}</Link>
            ),
          },
          {
            title: t('features.kind'),
            dataIndex: 'kind',
            width: 140,
            render: (kind: FeatureKind) => <Tag>{t(`features.kinds.${kind}`)}</Tag>,
          },
          {
            title: t('features.primary'),
            dataIndex: 'isPrimary',
            width: 100,
            render: (isPrimary: boolean) => (isPrimary ? <Tag color="green">✓</Tag> : null),
          },
        ]}
      />

      <Modal
        title={t('features.editParents')}
        open={editing}
        onCancel={() => setEditing(false)}
        onOk={() => void onSave()}
        confirmLoading={setParents.isPending}
        destroyOnHidden
      >
        <Table<ParentRow>
          rowKey="parentId"
          size="small"
          dataSource={rows}
          pagination={false}
          locale={{ emptyText: t('features.noParents') }}
          columns={[
            {
              title: t('features.name'),
              dataIndex: 'name',
              render: (name: string | null) => name ?? t('features.unnamed'),
            },
            {
              title: t('features.primary'),
              dataIndex: 'isPrimary',
              width: 90,
              render: (isPrimary: boolean, record) => (
                <Radio checked={isPrimary} onChange={() => makePrimary(record.parentId)} />
              ),
            },
            {
              key: 'remove',
              width: 50,
              render: (_, record) => (
                <Button
                  size="small"
                  type="text"
                  danger
                  icon={<DeleteOutlined />}
                  onClick={() => removeParent(record.parentId)}
                />
              ),
            },
          ]}
        />
        <Select
          style={{ width: '100%', marginTop: 12 }}
          showSearch
          filterOption={false}
          loading={searching}
          onSearch={setParentQuery}
          searchValue={parentQuery}
          // Selection appends a row; the select itself stays empty for the next add.
          value={null}
          onSelect={(value: string) => addParent(value)}
          placeholder={t('features.addParent')}
          options={addOptions}
          notFoundContent={null}
        />
      </Modal>
    </Card>
  );
}
