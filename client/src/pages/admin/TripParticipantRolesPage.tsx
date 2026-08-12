// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { IdcardOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Form, Input, InputNumber, Modal, Popconfirm, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCapabilities,
  useCreateTripParticipantRole,
  useDeleteTripParticipantRole,
  useTripParticipantRoles,
  useUpdateTripParticipantRole,
  type TripParticipantRole,
  type TripParticipantRoleWrite,
} from '../../api/hooks.ts';
import { participantRoleLabel } from '../../components/trips/participantRoles.ts';

const emptyDraft: TripParticipantRoleWrite = {
  code: '',
  name: '',
  description: null,
  sortOrder: 0,
};

/**
 * The jobs somebody may be recorded as having done on a trip: the rows that ship with the
 * product, and whatever a club adds beside them — a club that runs a kind of job nobody thought
 * of records it here rather than waiting for a release.
 *
 * Shipped rows keep their codes and cannot be deleted: clients translate their wording by code
 * and installations exchange trips under them, so a renamed or removed code would silently break
 * both. Two of them carry more than that — a roster with no "participant" row could not record
 * attendance, and "proposer" is what the right to edit a proposed trip is decided by. Their
 * wording and ordering stay editable, because that is presentation and an installation is
 * entitled to its own.
 *
 * Shipped rows read in the reader's language and club rows exactly as they were written, which
 * is the same one-list, two-sources rule the roster and its pickers follow.
 */
export default function TripParticipantRolesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.taxonomies, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.taxonomies, 'write');
  const { data: roles, isLoading } = useTripParticipantRoles();
  const createRole = useCreateTripParticipantRole();
  const updateRole = useUpdateTripParticipantRole();
  const deleteRole = useDeleteTripParticipantRole();
  const [editing, setEditing] = useState<TripParticipantRole | null>(null);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<TripParticipantRoleWrite>();

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
    const body: TripParticipantRoleWrite = {
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

  const remove = async (role: TripParticipantRole) => {
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
        <IdcardOutlined /> {t('admin.participantRoles.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.participantRoles.intro')}
      </Typography.Paragraph>

      {canWrite && (
        <Flex>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.participantRoles.add')}
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
            title: t('admin.participantRoles.name'),
            key: 'name',
            render: (_: unknown, role: TripParticipantRole) => participantRoleLabel(role, t),
          },
          {
            title: t('admin.participantRoles.code'),
            dataIndex: 'code',
            render: (code: string) => <Tag>{code}</Tag>,
          },
          {
            title: t('admin.participantRoles.source'),
            key: 'source',
            render: (_: unknown, role: TripParticipantRole) =>
              role.isSeeded ? (
                <Tag color="blue">{t('admin.participantRoles.seeded')}</Tag>
              ) : (
                <Tag>{t('admin.participantRoles.custom')}</Tag>
              ),
          },
          {
            title: t('admin.participantRoles.description'),
            dataIndex: 'description',
            render: (description: string | null) => description ?? '',
          },
          { title: t('admin.participantRoles.sortOrder'), dataIndex: 'sortOrder' },
          ...(canWrite
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, role: TripParticipantRole) => (
                    <Flex gap={8}>
                      <Button size="small" onClick={() => setEditing(role)}>
                        {t('admin.participantRoles.editAction')}
                      </Button>
                      {/* A shipped job has no delete: trips elsewhere are read by its code, and
                          two of them are what attendance and the right to edit a proposal are
                          recorded as. */}
                      {!role.isSeeded && (
                        <Popconfirm
                          title={t('admin.participantRoles.deleteConfirm')}
                          onConfirm={() => void remove(role)}
                        >
                          <Button size="small" danger>
                            {t('admin.participantRoles.delete')}
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
        title={editing ? t('admin.participantRoles.edit') : t('admin.participantRoles.add')}
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
            label={t('admin.participantRoles.name')}
            extra={t('admin.participantRoles.nameHint')}
            rules={[{ required: true, max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="code"
            label={t('admin.participantRoles.code')}
            extra={t('admin.participantRoles.codeHint')}
            rules={[
              { required: true, max: 50 },
              { pattern: /^[a-z0-9_]+$/, message: t('admin.participantRoles.codeHint') },
            ]}
          >
            {/* A shipped job keeps its code — the server refuses a change — so the box is
                disabled rather than left to be refused after the fact. */}
            <Input disabled={editing?.isSeeded === true} />
          </Form.Item>
          <Form.Item
            name="description"
            label={t('admin.participantRoles.description')}
            rules={[{ max: 1000 }]}
          >
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="sortOrder" label={t('admin.participantRoles.sortOrder')}>
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
    case 'trip_participant_role.code_taken':
      return t('admin.participantRoles.codeTaken');
    case 'trip_participant_role.seeded_immutable':
      return t('admin.participantRoles.seededImmutable');
    case 'trip_participant_role.in_use':
      return t('admin.participantRoles.inUse');
    default:
      return t('common.saveFailed');
  }
}
