// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { ArrowLeftOutlined, ArrowRightOutlined, DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Card, Flex, Input, Modal, Select, Table, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  useFeature,
  useFeatureLinks,
  useLinkKinds,
  useSetFeatureLinks,
  type FeatureLink,
} from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import ObjectSelector from '../../components/selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../filters/selection.ts';

/** A typed relation may point at a feature of any kind, so the target is the whole feature world. */
const TARGET_SCOPES: readonly SelectorScope[] = [
  { id: 'features', labelKey: 'filters.scopes.features', world: 'feature', where: null },
];

/**
 * Resolves a linked feature's display name through its envelope. Link rows only
 * carry ids; endpoints the caller may not read resolve to an "inaccessible"
 * placeholder instead of leaking anything.
 */
function FeatureNameLink({ id, link = true }: { id: string; link?: boolean }) {
  const { t } = useTranslation();
  const { data, isError } = useFeature(id);
  if (isError) {
    return (
      <Typography.Text type="secondary" italic>
        {t('features.inaccessible')}
      </Typography.Text>
    );
  }
  const name = data ? (data.feature.name ?? t('features.unnamed')) : '…';
  return link ? <Link to={`/features/${id}`}>{name}</Link> : <span>{name}</span>;
}

interface LinkRow {
  key: number;
  toId: string;
  /** Label captured from the picked row; existing rows resolve by envelope. */
  toName: string | null;
  linkKindCode: string;
  note: string | null;
}

interface LinksCardProps {
  featureId: string;
  canEdit: boolean;
}

let nextRowKey = 1;

/**
 * Typed relations of the feature, both directions. Editing replaces the
 * outgoing set only — incoming links belong to the feature on the other end.
 */
export default function LinksCard({ featureId, canEdit }: LinksCardProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: links } = useFeatureLinks(featureId);
  const { data: linkKinds } = useLinkKinds();
  const setLinks = useSetFeatureLinks();

  const kindName = (code: string) => linkKinds?.find((k) => k.code === code)?.name ?? code;

  const [editing, setEditing] = useState(false);
  const [rows, setRows] = useState<LinkRow[]>([]);
  const [draftToId, setDraftToId] = useState<string | null>(null);
  const [draftToName, setDraftToName] = useState<string | null>(null);
  const [draftKind, setDraftKind] = useState<string | null>(null);
  const [draftNote, setDraftNote] = useState('');
  // A feature is not linked to itself. The server refuses that too; leaving it out here only
  // spares somebody the refusal.
  const notOffered = useMemo(() => [featureId], [featureId]);

  const openEditor = () => {
    setRows(
      (links ?? [])
        .filter((l) => l.fromId === featureId)
        .map((l) => ({
          key: nextRowKey++,
          toId: l.toId,
          toName: null,
          linkKindCode: l.linkKindCode,
          note: l.note,
        })),
    );
    setDraftToId(null);
    setDraftToName(null);
    setDraftKind(null);
    setDraftNote('');
    setEditing(true);
  };

  const addRow = () => {
    if (!draftToId || !draftKind) {
      return;
    }
    setRows((r) => [
      ...r,
      {
        key: nextRowKey++,
        toId: draftToId,
        toName: draftToName,
        linkKindCode: draftKind,
        note: draftNote.trim() ? draftNote.trim() : null,
      },
    ]);
    setDraftToId(null);
    setDraftToName(null);
    setDraftKind(null);
    setDraftNote('');
  };

  const onSave = async () => {
    try {
      await setLinks.mutateAsync({
        id: featureId,
        links: rows.map((r) => ({ toId: r.toId, linkKindCode: r.linkKindCode, note: r.note })),
      });
      setEditing(false);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Card
      title={t('features.links')}
      style={{ marginTop: 16 }}
      extra={
        canEdit && (
          <Button size="small" icon={<EditOutlined />} onClick={openEditor}>
            {t('features.editLinks')}
          </Button>
        )
      }
    >
      <Table<FeatureLink>
        rowKey={(l) => `${l.fromId}:${l.toId}:${l.linkKindCode}`}
        size="small"
        dataSource={links}
        pagination={false}
        locale={{ emptyText: t('features.noLinks') }}
        columns={[
          {
            key: 'direction',
            width: 50,
            render: (_, l) =>
              l.fromId === featureId ? (
                <Tooltip title={t('features.outgoing')}>
                  <ArrowRightOutlined />
                </Tooltip>
              ) : (
                <Tooltip title={t('features.incoming')}>
                  <ArrowLeftOutlined />
                </Tooltip>
              ),
          },
          {
            title: t('features.linkKind'),
            dataIndex: 'linkKindCode',
            width: 180,
            render: kindName,
          },
          {
            title: t('features.linkTarget'),
            key: 'other',
            render: (_, l) => <FeatureNameLink id={l.fromId === featureId ? l.toId : l.fromId} />,
          },
          { title: t('features.linkNote'), dataIndex: 'note', ellipsis: true },
        ]}
      />

      <Modal
        title={t('features.editLinks')}
        open={editing}
        onCancel={() => setEditing(false)}
        onOk={() => void onSave()}
        confirmLoading={setLinks.isPending}
        width={640}
        destroyOnHidden
      >
        <Table<LinkRow>
          rowKey="key"
          size="small"
          dataSource={rows}
          pagination={false}
          locale={{ emptyText: t('features.noLinks') }}
          columns={[
            {
              title: t('features.linkKind'),
              dataIndex: 'linkKindCode',
              width: 160,
              render: kindName,
            },
            {
              title: t('features.linkTarget'),
              key: 'target',
              render: (_, r) => (r.toName ?? <FeatureNameLink id={r.toId} link={false} />),
            },
            { title: t('features.linkNote'), dataIndex: 'note', ellipsis: true },
            {
              key: 'remove',
              width: 50,
              render: (_, r) => (
                <Button
                  size="small"
                  type="text"
                  danger
                  icon={<DeleteOutlined />}
                  onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                />
              ),
            },
          ]}
        />
        <div style={{ marginTop: 12 }}>
          <ObjectSelector
            scopes={TARGET_SCOPES}
            value={draftToId}
            exclude={notOffered}
            allowClear
            pageSize={6}
            sorts={[]}
            placeholder={t('features.linkTarget')}
            aria-label={t('features.linkTarget')}
            data-testid="feature-link-target-picker"
            onChange={(next) => setDraftToId(typeof next === 'string' ? next : null)}
            onPick={(hit) => setDraftToName(hit?.title ?? null)}
          />
        </div>
        <Flex gap={8} style={{ marginTop: 8 }} wrap>
          <Select
            style={{ width: 170 }}
            value={draftKind}
            onChange={(value: string | null) => setDraftKind(value)}
            placeholder={t('features.linkKind')}
            options={linkKinds?.map((k) => ({ value: k.code, label: k.name }))}
            allowClear
          />
          <Input
            style={{ width: 150, flex: 1 }}
            value={draftNote}
            onChange={(e) => setDraftNote(e.target.value)}
            placeholder={t('features.linkNote')}
          />
          <Button icon={<PlusOutlined />} disabled={!draftToId || !draftKind} onClick={addRow}>
            {t('features.addLink')}
          </Button>
        </Flex>
      </Modal>
    </Card>
  );
}
