// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { PlusOutlined, SolutionOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Form, Input, InputNumber, Modal, Popconfirm, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCapabilities,
  useCreateExpeditionRosterRole,
  useDeleteExpeditionRosterRole,
  useExpeditionRosterRoles,
  useUpdateExpeditionRosterRole,
  type ExpeditionRosterRole,
  type ExpeditionRosterRoleWrite,
} from '../../api/hooks.ts';
import { expeditionRosterRoleLabel } from '../../components/expeditions/rosterRoles.ts';

const emptyDraft: ExpeditionRosterRoleWrite = {
  code: '',
  name: '',
  description: null,
  sortOrder: 0,
};

/**
 * What somebody may be recorded as having been at a camp as: the rows that ship with the
 * product, and whatever a club adds beside them.
 *
 * A list of its own, beside the trip roles and deliberately not the same list: cooking for
 * thirty people or keeping the base camp is what a camp's roster exists to record, and neither
 * is a job underground — a row added here is never offered on a trip, and a trip role is never
 * offered at a camp.
 *
 * Shipped rows keep their codes and cannot be deleted: clients translate their wording by code
 * and installations exchange camps under them, so a renamed or removed code would silently break
 * both. One of them carries more than that — a roster with no "member" row could not record that
 * somebody was simply there. Their wording and ordering stay editable, because that is
 * presentation and an installation is entitled to its own.
 *
 * Shipped rows read in the reader's language and club rows exactly as they were written, which
 * is the same one-list, two-sources rule the camp's roster follows.
 */
export default function ExpeditionRosterRolesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.taxonomies, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.taxonomies, 'write');
  const { data: roles, isLoading } = useExpeditionRosterRoles();
  const createRole = useCreateExpeditionRosterRole();
  const updateRole = useUpdateExpeditionRosterRole();
  const deleteRole = useDeleteExpeditionRosterRole();
  const [editing, setEditing] = useState<ExpeditionRosterRole | null>(null);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<ExpeditionRosterRoleWrite>();

  const open = editing !== null || creating;
  useEffect(() => {
    if (open) {
      form.setFieldsValue(
        editing
          ? {
              code: editing.code,
              name: editing.name,
              description: editing.description,
              sortOrder: editing.sortOrder,
            }
          : emptyDraft,
      );
    }
  }, [open, editing, form]);

  const close = () => {
    setEditing(null);
    setCreating(false);
  };

  const save = async () => {
    const values = await form.validateFields();
    const body: ExpeditionRosterRoleWrite = {
      code: values.code.trim(),
      name: values.name.trim(),
      description: values.description?.trim() || null,
      // An emptied number box reads back as null and the position is not nullable on the wire:
      // it would be refused while the body was still being parsed, before any rule could name
      // the field. An unstated position is the first one, as it is for a new row.
      sortOrder: values.sortOrder ?? 0,
    };

    try {
      if (editing) {
        await updateRole.mutateAsync({ ...body, id: editing.id });
      } else {
        await createRole.mutateAsync(body);
      }
      message.success(t('common.saved'));
      close();
    } catch (error) {
      message.error(problemMessage(error, t));
    }
  };

  const remove = async (role: ExpeditionRosterRole) => {
    try {
      await deleteRole.mutateAsync(role.id);
      message.success(t('common.deleted'));
    } catch (error) {
      message.error(problemMessage(error, t));
    }
  };

  if (!capabilities) {
    return null;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <SolutionOutlined /> {t('admin.campRosterRoles.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.campRosterRoles.intro')}
      </Typography.Paragraph>

      {canWrite && (
        <Flex>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.campRosterRoles.add')}
          </Button>
        </Flex>
      )}

      <Table
        rowKey="id"
        loading={isLoading}
        dataSource={roles ?? []}
        pagination={false}
        size="small"
        scroll={{ x: true }}
        columns={[
          {
            title: t('admin.campRosterRoles.name'),
            key: 'name',
            render: (_: unknown, role: ExpeditionRosterRole) => expeditionRosterRoleLabel(role, t),
          },
          {
            title: t('admin.campRosterRoles.code'),
            dataIndex: 'code',
            render: (code: string) => <Tag>{code}</Tag>,
          },
          {
            title: t('admin.campRosterRoles.source'),
            key: 'source',
            render: (_: unknown, role: ExpeditionRosterRole) =>
              role.isSeeded ? (
                <Tag color="blue">{t('admin.campRosterRoles.seeded')}</Tag>
              ) : (
                <Tag>{t('admin.campRosterRoles.custom')}</Tag>
              ),
          },
          {
            title: t('admin.campRosterRoles.description'),
            dataIndex: 'description',
            render: (description: string | null) => description ?? '',
          },
          { title: t('admin.campRosterRoles.sortOrder'), dataIndex: 'sortOrder' },
          ...(canWrite
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, role: ExpeditionRosterRole) => (
                    <Flex gap={8}>
                      <Button size="small" onClick={() => setEditing(role)}>
                        {t('admin.campRosterRoles.editAction')}
                      </Button>
                      {/* A shipped role has no delete: camps elsewhere are read by its code,
                          and one of them is what simply having been there is recorded as. */}
                      {!role.isSeeded && (
                        <Popconfirm
                          title={t('admin.campRosterRoles.deleteConfirm')}
                          onConfirm={() => void remove(role)}
                        >
                          <Button size="small" danger>
                            {t('admin.campRosterRoles.delete')}
                          </Button>
                        </Popconfirm>
                      )}
                    </Flex>
                  ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        open={open}
        title={editing ? t('admin.campRosterRoles.edit') : t('admin.campRosterRoles.add')}
        onCancel={close}
        onOk={() => void save()}
        confirmLoading={createRole.isPending || updateRole.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        width={560}
      >
        <Form form={form} layout="vertical" initialValues={emptyDraft}>
          <Form.Item
            name="name"
            label={t('admin.campRosterRoles.name')}
            extra={t('admin.campRosterRoles.nameHint')}
            rules={[{ required: true, max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="code"
            label={t('admin.campRosterRoles.code')}
            extra={t('admin.campRosterRoles.codeHint')}
            rules={[
              { required: true, max: 50 },
              { pattern: /^[a-z0-9_]+$/, message: t('admin.campRosterRoles.codeHint') },
            ]}
          >
            {/* A shipped role keeps its code — the server refuses a change — so the box is
                disabled rather than left to be refused after the fact. */}
            <Input disabled={editing?.isSeeded === true} />
          </Form.Item>
          <Form.Item
            name="description"
            label={t('admin.campRosterRoles.description')}
            rules={[{ max: 1000 }]}
          >
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="sortOrder" label={t('admin.campRosterRoles.sortOrder')}>
            <InputNumber precision={0} style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </Flex>
  );
}

/** The refusals worth naming: anything else leaves somebody guessing which box was at fault. */
function problemMessage(error: unknown, t: ReturnType<typeof useTranslation>['t']): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'expedition_roster_role.code_taken':
      return t('admin.campRosterRoles.codeTaken');
    case 'expedition_roster_role.seeded_immutable':
      return t('admin.campRosterRoles.seededImmutable');
    case 'expedition_roster_role.in_use':
      return t('admin.campRosterRoles.inUse');
    default:
      return t('common.saveFailed');
  }
}
