// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DeleteOutlined, EditOutlined, StarFilled } from '@ant-design/icons';
import { App, Card, Flex, Modal, Radio, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useFeatureChildren,
  useFeatureParents,
  useSetFeatureParents,
  type FeatureChild,
  type FeatureKind,
} from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import ObjectSelector from '../../components/selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../filters/selection.ts';

interface HierarchyCardProps {
  featureId: string;
  /** The feature's kind: an entrance and a centreline have a parent nobody may change. */
  kind: FeatureKind;
  canEdit: boolean;
}

/**
 * Kinds that sit under their own cave and nowhere else. The cave releases their position on its
 * own exact-view decision — its point is its main entrance's, its survey figures its centreline's
 * — so filing one under a second container would protect it while the cave went on publishing the
 * same coordinate. The server refuses any other parent set; this page does not offer one.
 */
const FIXED_PARENT_KINDS: ReadonlySet<FeatureKind> = new Set<FeatureKind>(['caveEntrance', 'centerline']);

/** Any kind of feature may contain another, so the picker offers the whole feature world. */
const PARENT_SCOPES: readonly SelectorScope[] = [
  { id: 'features', labelKey: 'filters.scopes.features', world: 'feature', where: null },
];

interface ParentRow {
  parentId: string;
  name: string | null;
  isPrimary: boolean;
}

/**
 * The feature's place in the containment DAG: its parent edges (with the one
 * primary edge highlighted) and a paged table of the features it contains.
 */
export default function HierarchyCard({ featureId, kind, canEdit }: HierarchyCardProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: parents } = useFeatureParents(featureId);
  const [childrenPage, setChildrenPage] = useState(1);
  const { data: children } = useFeatureChildren(featureId, { page: childrenPage, pageSize: 10 });
  const setParents = useSetFeatureParents();
  const parentFixed = FIXED_PARENT_KINDS.has(kind);

  const [editing, setEditing] = useState(false);
  const [rows, setRows] = useState<ParentRow[]>([]);

  const openEditor = () => {
    setRows((parents ?? []).map((p) => ({ parentId: p.id, name: p.name, isPrimary: p.isPrimary })));
    setEditing(true);
  };

  // The feature cannot contain itself, each parent appears once, and a child this card is showing
  // cannot also be its parent. That is as far as the picker can see: the children are paged, and
  // whether a candidate sits further below (a grandchild, or a child on another page) is not
  // something a search row knows, so such a pick is still offered and the server refuses it on
  // save as a loop — that refusal is the authority, this list is only a courtesy.
  const notOffered = useMemo(
    () => [
      featureId,
      ...rows.map((r) => r.parentId),
      ...(children?.items ?? []).map((c) => c.id),
    ],
    [featureId, rows, children],
  );

  const addParent = (id: string, name: string) => {
    // The first edge becomes primary automatically; the DAG requires exactly one.
    setRows((r) => [...r, { parentId: id, name, isPrimary: r.length === 0 }]);
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
    } catch (error) {
      // The one refusal that is not worth retrying says why, in words; anything else is the
      // general failure it always was.
      message.error(
        error instanceof ApiError && error.code === 'feature.parent_fixed'
          ? t('features.parentFixed')
          : t('common.saveFailed'),
      );
    }
  };

  return (
    <Card
      title={t('features.hierarchy')}
      style={{ marginTop: 16 }}
      extra={
        canEdit &&
        !parentFixed && (
          <Button size="small" icon={<EditOutlined />} onClick={openEditor}>
            {t('features.editParents')}
          </Button>
        )
      }
    >
      {parentFixed && (
        <Typography.Paragraph type="secondary" data-testid="hierarchy-parent-fixed">
          {t('features.parentFixedNote')}
        </Typography.Paragraph>
      )}
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
        <div style={{ marginTop: 12 }}>
          {/* A pick appends a row and the control itself stays empty for the next one. The
              search is kept, so several parents from one answer can be added in turn — each
              one leaves the list as it is added. */}
          <ObjectSelector
            scopes={PARENT_SCOPES}
            value={null}
            exclude={notOffered}
            pageSize={6}
            sorts={[]}
            placeholder={t('features.addParent')}
            aria-label={t('features.addParent')}
            data-testid="hierarchy-parent-picker"
            onPick={(hit) => {
              if (hit) {
                addParent(hit.id, hit.title);
              }
            }}
          />
        </div>
      </Modal>
    </Card>
  );
}
