// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, LockOutlined, PlusOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { App, Alert, Button, Flex, Form, Input, Modal, Popconfirm, Spin, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  hasAccessAction,
  useAccessCatalog,
  useCapabilities,
  useCreatePermissionGroup,
  useDeletePermissionGroup,
  usePermissionGroups,
  type PermissionGroup,
} from '../../../api/hooks.ts';
import { ApiError } from '../../../api/client.ts';
import PermissionGroupEditor from './PermissionGroupEditor.tsx';

/**
 * Rulesets and their trustees — the concept that replaced global roles. The list is
 * plain; everything interesting lives in the per-group editor. Governed by its own
 * resource domain, which the seeded Administrators group deliberately does not hold:
 * running an installation and rewriting its security model are different jobs.
 */
export default function PermissionGroupsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.permissionGroups, 'read');
  const canCreate = hasAccessAction(capabilities?.domains.permissionGroups, 'create');
  const { data: groups, isPending } = usePermissionGroups(canRead);
  const { data: catalog } = useAccessCatalog(canRead);
  const createGroup = useCreatePermissionGroup();
  const deleteGroup = useDeletePermissionGroup();
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm<{ name: string; description?: string }>();

  if (!capabilities) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  const onCreate = async () => {
    const values = await form.validateFields();
    try {
      const created = await createGroup.mutateAsync({
        name: values.name,
        description: values.description ?? null,
      });
      setCreating(false);
      form.resetFields();
      message.success(t('common.saved'));
      // Straight into the new group's editor — a fresh ruleset is empty and useless.
      setEditingId(created.id);
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'permission_group.name_taken'
          ? t('permissionGroups.nameTaken')
          : t('common.saveFailed'),
      );
    }
  };

  const onDelete = async (group: PermissionGroup) => {
    try {
      await deleteGroup.mutateAsync(group.id);
      message.success(t('common.deleted'));
      if (editingId === group.id) {
        setEditingId(null);
      }
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'permission_group.last_full_admin'
          ? t('permissionGroups.lastFullAdmin')
          : t('common.saveFailed'),
      );
    }
  };

  const editing = groups?.find((group) => group.id === editingId) ?? null;

  return (
    <div style={{ padding: 24, maxWidth: 1100 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          <SafetyCertificateOutlined /> {t('permissionGroups.title')}
        </Typography.Title>
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('permissionGroups.new')}
          </Button>
        )}
      </Flex>
      <Typography.Paragraph type="secondary">{t('permissionGroups.intro')}</Typography.Paragraph>

      <Table<PermissionGroup>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isPending}
        dataSource={groups}
        pagination={false}
        columns={[
          {
            title: t('permissionGroups.name'),
            dataIndex: 'name',
            render: (name: string, group) => (
              <Flex gap={8} align="center">
                <Typography.Link onClick={() => setEditingId(group.id)}>{name}</Typography.Link>
                {group.isProtected && (
                  <Tag icon={<LockOutlined />}>{t('permissionGroups.protected')}</Tag>
                )}
                {group.isSeeded && <Tag color="blue">{t('permissionGroups.seeded')}</Tag>}
              </Flex>
            ),
          },
          { title: t('permissionGroups.description'), dataIndex: 'description', ellipsis: true },
          {
            title: t('permissionGroups.membersColumn'),
            dataIndex: 'memberCount',
            width: 110,
            align: 'right',
          },
          {
            title: t('permissionGroups.rulesColumn'),
            dataIndex: 'entryCount',
            width: 110,
            align: 'right',
          },
          {
            title: '',
            key: 'actions',
            width: 150,
            render: (_, group) => (
              <Flex gap={8}>
                <Button size="small" onClick={() => setEditingId(group.id)}>
                  {t('permissionGroups.manage')}
                </Button>
                <Popconfirm
                  title={t('permissionGroups.deleteConfirm')}
                  onConfirm={() => void onDelete(group)}
                  disabled={group.isProtected}
                >
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    disabled={group.isProtected}
                  />
                </Popconfirm>
              </Flex>
            ),
          },
        ]}
      />

      <Modal
        title={t('permissionGroups.new')}
        open={creating}
        onCancel={() => {
          setCreating(false);
          form.resetFields();
        }}
        onOk={() => void onCreate()}
        confirmLoading={createGroup.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" requiredMark={false}>
          <Form.Item name="name" label={t('permissionGroups.name')} rules={[{ required: true }, { max: 100 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('permissionGroups.description')} rules={[{ max: 1000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>

      {editing && catalog && (
        <PermissionGroupEditor
          group={editing}
          catalog={catalog}
          open
          onClose={() => setEditingId(null)}
        />
      )}
    </div>
  );
}
