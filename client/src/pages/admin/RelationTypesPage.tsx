// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { ApartmentOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateResLinkRelationType,
  useDeleteResLinkRelationType,
  useResLinkRelationTypes,
  useUpdateResLinkRelationType,
  type ResLinkRelationType,
  type ResLinkRelationTypeWrite,
} from '../../api/hooks.ts';
import { useIsFullAdmin } from '../../components/reslinks/permissions.ts';
import { resLinkProblemMessage } from '../../components/reslinks/problems.ts';
import { relationPhrase } from '../../components/reslinks/relations.ts';

/** The blank relation an "add" click starts from. */
const emptyDraft: ResLinkRelationTypeWrite = {
  code: '',
  name: '',
  description: null,
  sortOrder: 0,
  directed: false,
  inverseName: null,
};

/**
 * The wording resource links are recorded with: the relations this installation ships and
 * whatever an administrator adds beside them.
 *
 * Shipped rows are listed but not edited here. Their codes are the vocabulary installations
 * exchange links by, every client translates their labels from that code, and how they read
 * decides how existing links validate the end they read from — so this page shows them in
 * the reader's own language and leaves them alone. Custom rows are installation-local: they
 * show whatever wording their author stored, in whatever language it was written.
 *
 * A relation that reads one way carries two phrases, one for each end. Recording both is
 * what makes a link readable from either side, which is why the second box appears the
 * moment the direction switch goes on.
 */
export default function RelationTypesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const canManage = useIsFullAdmin();
  const { data: relationTypes, isLoading } = useResLinkRelationTypes();
  const createType = useCreateResLinkRelationType();
  const updateType = useUpdateResLinkRelationType();
  const deleteType = useDeleteResLinkRelationType();

  const [editing, setEditing] = useState<ResLinkRelationType | null>(null);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<ResLinkRelationTypeWrite>();
  const directed = Form.useWatch('directed', form) ?? false;

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
              directed: editing.directed,
              inverseName: editing.inverseName,
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
    const body: ResLinkRelationTypeWrite = {
      ...values,
      code: values.code.trim(),
      name: values.name.trim(),
      description: values.description?.trim() || null,
      // An emptied number box reads back as null, and the position is not nullable on the
      // wire: it would be refused while the body was still being parsed, before any rule
      // could name the field. An unstated position is the first one, as it is for a new row.
      sortOrder: values.sortOrder ?? 0,
      // A relation that reads the same both ways has no second phrase, and the server
      // refuses one — so switching the direction off discards whatever was typed rather
      // than sending it along and earning a refusal.
      inverseName: values.directed ? values.inverseName?.trim() || null : null,
    };

    try {
      if (editing) {
        await updateType.mutateAsync({ id: editing.id, body });
      } else {
        await createType.mutateAsync(body);
      }
      message.success(t('common.saved'));
      close();
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const remove = async (row: ResLinkRelationType) => {
    try {
      await deleteType.mutateAsync(row.id);
      message.success(t('common.deleted'));
    } catch (error) {
      // The refusal worth naming above all others: links still record this relation, and
      // deleting it would leave them saying nothing. The message names that, not a status.
      message.error(resLinkProblemMessage(error, t));
    }
  };

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <ApartmentOutlined /> {t('admin.relationTypes.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.relationTypes.intro')}
      </Typography.Paragraph>

      {canManage && (
        <Flex>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.relationTypes.add')}
          </Button>
        </Flex>
      )}

      <Table
        rowKey="id"
        loading={isLoading}
        dataSource={relationTypes ?? []}
        pagination={false}
        size="small"
        scroll={{ x: true }}
        columns={[
          {
            title: t('admin.relationTypes.name'),
            key: 'name',
            // Shipped rows read in the reader's language, custom rows exactly as stored —
            // the same one-list, two-sources rule the pickers and link rows follow.
            render: (_: unknown, row: ResLinkRelationType) => relationPhrase(row, 'forward', t),
          },
          {
            title: t('admin.relationTypes.inverseName'),
            key: 'inverseName',
            render: (_: unknown, row: ResLinkRelationType) =>
              row.directed ? (
                relationPhrase(row, 'inverse', t)
              ) : (
                <Typography.Text type="secondary">{t('admin.relationTypes.bothWays')}</Typography.Text>
              ),
          },
          {
            title: t('admin.relationTypes.code'),
            dataIndex: 'code',
            render: (code: string) => <Tag>{code}</Tag>,
          },
          {
            title: t('admin.relationTypes.description'),
            dataIndex: 'description',
            render: (description: string | null) => description ?? '',
          },
          { title: t('admin.relationTypes.sortOrder'), dataIndex: 'sortOrder' },
          {
            title: t('admin.relationTypes.source'),
            key: 'source',
            render: (_: unknown, row: ResLinkRelationType) =>
              row.seeded ? (
                <Tag color="blue">{t('admin.relationTypes.seeded')}</Tag>
              ) : (
                <Tag>{t('admin.relationTypes.custom')}</Tag>
              ),
          },
          ...(canManage
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, row: ResLinkRelationType) =>
                    row.seeded ? (
                      <Typography.Text type="secondary">
                        {t('admin.relationTypes.seededReadOnly')}
                      </Typography.Text>
                    ) : (
                      <Flex gap={8}>
                        <Button size="small" onClick={() => setEditing(row)}>
                          {t('common.edit')}
                        </Button>
                        <Popconfirm
                          title={t('admin.relationTypes.deleteConfirm')}
                          okText={t('admin.relationTypes.delete')}
                          okButtonProps={{ danger: true }}
                          cancelText={t('common.cancel')}
                          onConfirm={() => void remove(row)}
                        >
                          <Button size="small" danger>
                            {t('admin.relationTypes.delete')}
                          </Button>
                        </Popconfirm>
                      </Flex>
                    ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        open={open}
        title={editing ? t('admin.relationTypes.edit') : t('admin.relationTypes.add')}
        onCancel={close}
        onOk={() => void save()}
        confirmLoading={createType.isPending || updateType.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        width={560}
      >
        <Form form={form} layout="vertical" initialValues={emptyDraft}>
          <Form.Item
            name="name"
            label={t('admin.relationTypes.name')}
            extra={t('admin.relationTypes.nameHint')}
            rules={[{ required: true, max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="code"
            label={t('admin.relationTypes.code')}
            extra={t('admin.relationTypes.codeHint')}
            rules={[{ required: true, max: 50 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="directed"
            label={t('admin.relationTypes.directed')}
            extra={t('admin.relationTypes.directedHint')}
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
          {directed && (
            <Form.Item
              name="inverseName"
              label={t('admin.relationTypes.inverseName')}
              extra={t('admin.relationTypes.inverseNameHint')}
              rules={[{ max: 100 }]}
            >
              <Input />
            </Form.Item>
          )}
          <Form.Item
            name="description"
            label={t('admin.relationTypes.description')}
            rules={[{ max: 1000 }]}
          >
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="sortOrder" label={t('admin.relationTypes.sortOrder')}>
            <InputNumber precision={0} style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </Flex>
  );
}
