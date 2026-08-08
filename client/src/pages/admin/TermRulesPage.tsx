// SPDX-License-Identifier: AGPL-3.0-or-later
import { useRef, useState } from 'react';
import {
  CopyOutlined,
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  PlusOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import { App, Button, Flex, Popconfirm, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateTermRuleSet,
  useDeleteTermRuleSet,
  useImportTermRuleSet,
  useSetTermRuleSetScope,
  useTermRuleSets,
  type TermRuleDocument,
  type TermRuleSetInfo,
} from '../../api/hooks.ts';
import { downloadFile } from '../../api/download.ts';
import TermRuleSetEditor from '../../components/import/TermRuleSetEditor.tsx';

/**
 * The club's naming habits, configured once.
 *
 * Everyone reads every set — a rule names no cave and holds no coordinate, and a club that
 * could not show a neighbouring club its rules could not hand them over either. What is
 * governed is writing: your own set is yours, and anything a group or the installation
 * inherits is an administrator's, because promoting a set changes what everybody else's next
 * import proposes. Editing a set you do not own is offered as "edit a copy", which is what
 * gives you one of your own.
 */
export default function TermRulesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const sets = useTermRuleSets();
  const create = useCreateTermRuleSet();
  const remove = useDeleteTermRuleSet();
  const setScope = useSetTermRuleSetScope();
  const importSet = useImportTermRuleSet();
  const fileInput = useRef<HTMLInputElement | null>(null);

  const [editing, setEditing] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const onCopy = async (row: TermRuleSetInfo) => {
    try {
      const copy = await create.mutateAsync({
        name: t('termRules.copyOf', { name: row.name }),
        description: row.description ?? null,
        rules: null,
        copyFromId: row.id,
      });
      setEditing(copy.set.id);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async (id: string) => {
    try {
      await remove.mutateAsync(id);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onPromote = async (row: TermRuleSetInfo) => {
    try {
      await setScope.mutateAsync({
        id: row.id,
        body: { scope: 'installation', cavingGroupId: null, isDefault: true },
      });
      message.success(t('termRules.promoted'));
    } catch {
      message.error(t('termRules.promoteFailed'));
    }
  };

  const onFilePicked = async (file: File) => {
    try {
      const document = JSON.parse(await file.text()) as TermRuleDocument;
      const created = await importSet.mutateAsync({ document, name: document.name ?? file.name });
      setEditing(created.set.id);
      message.success(t('termRules.imported'));
    } catch {
      message.error(t('termRules.importFailed'));
    }
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('termRules.title')}
        </Typography.Title>
        <Space>
          <Button icon={<UploadOutlined />} onClick={() => fileInput.current?.click()}>
            {t('termRules.importFile')}
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('termRules.newSet')}
          </Button>
        </Space>
      </Flex>

      <Typography.Paragraph type="secondary">{t('termRules.intro')}</Typography.Paragraph>

      <input
        ref={fileInput}
        type="file"
        accept="application/json,.json"
        style={{ display: 'none' }}
        aria-label={t('termRules.importFile')}
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = '';
          if (file) {
            void onFilePicked(file);
          }
        }}
      />

      <Table<TermRuleSetInfo>
        rowKey="id"
        size="middle"
        scroll={{ x: 'max-content' }}
        loading={sets.isLoading}
        dataSource={sets.data}
        pagination={false}
        data-testid="term-rule-sets"
        columns={[
          {
            title: t('termRules.name'),
            dataIndex: 'name',
            render: (name: string, row) => (
              <Space>
                <Typography.Text strong>{name}</Typography.Text>
                {row.isDefault && <Tag color="green">{t('termRules.default')}</Tag>}
                {row.isSeeded && <Tag>{t('termRules.shipped')}</Tag>}
              </Space>
            ),
          },
          {
            title: t('termRules.scope'),
            dataIndex: 'scope',
            width: 150,
            render: (scope: string) => <Tag>{t(`termRules.scopes.${scope}`)}</Tag>,
          },
          {
            title: t('termRules.ruleCount'),
            dataIndex: 'ruleCount',
            width: 100,
            align: 'right',
          },
          {
            title: '',
            key: 'actions',
            width: 230,
            render: (_: unknown, row) => (
              <Flex gap={4}>
                <Tooltip title={row.canEdit ? t('termRules.edit') : t('termRules.editCopy')}>
                  <Button
                    size="small"
                    icon={row.canEdit ? <EditOutlined /> : <CopyOutlined />}
                    onClick={() => (row.canEdit ? setEditing(row.id) : void onCopy(row))}
                  />
                </Tooltip>
                <Tooltip title={t('termRules.export')}>
                  <Button
                    size="small"
                    icon={<DownloadOutlined />}
                    onClick={() =>
                      downloadFile(`/api/v1/term-rule-sets/${row.id}/export`).catch(() =>
                        message.error(t('common.saveFailed')),
                      )
                    }
                  />
                </Tooltip>
                {!row.isDefault && row.canEdit && (
                  <Tooltip title={t('termRules.makeInstallationDefault')}>
                    <Button size="small" onClick={() => void onPromote(row)}>
                      {t('termRules.promote')}
                    </Button>
                  </Tooltip>
                )}
                {row.canDelete && (
                  <Popconfirm
                    title={t('termRules.deleteConfirm')}
                    onConfirm={() => void onDelete(row.id)}
                    okButtonProps={{ danger: true }}
                  >
                    <Button size="small" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                )}
              </Flex>
            ),
          },
        ]}
      />

      <TermRuleSetEditor
        setId={editing}
        creating={creating}
        onClose={() => {
          setEditing(null);
          setCreating(false);
        }}
      />
    </div>
  );
}
