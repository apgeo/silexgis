// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { PlusOutlined, ProfileOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Form, Input, InputNumber, Modal, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCapabilities,
  useCreateDocumentType,
  useDocumentTypes,
  useUpdateDocumentType,
  type DocumentType,
  type DocumentTypeWrite,
} from '../../api/hooks.ts';
import { parsePropertiesSchema } from '../../components/typedProperties/propertiesSchema.ts';

/** The blank kind a "new" click starts from. */
const emptyDraft: DocumentTypeWrite = {
  code: '',
  name: '',
  description: null,
  sortOrder: 0,
  metadataSchema: null,
};

/**
 * Authoring the kinds of document this installation recognises, and the JSON Schema each
 * kind describes its documents with.
 *
 * The schema is edited as raw text rather than through a field builder: JSON Schema says
 * far more than a builder could offer, and the form documents render only understands a
 * flat subset of it. The preview below the editor names exactly which fields will appear,
 * so the gap between "what the schema says" and "what people will be asked for" is visible
 * while it is being written instead of being discovered afterwards.
 *
 * Rewriting a schema does not rewrite the documents already stored under it. The server
 * publishes a new schema version, every existing document keeps the version it was checked
 * against, and it is re-checked the next time someone edits it — so tightening a kind never
 * retroactively invalidates history.
 */
export default function DocumentTypesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.taxonomies, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.taxonomies, 'write');
  const { data: types, isLoading } = useDocumentTypes();
  const createType = useCreateDocumentType();
  const updateType = useUpdateDocumentType();

  const [editing, setEditing] = useState<DocumentType | null>(null);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<DocumentTypeWrite>();

  const open = editing !== null || creating;

  useEffect(() => {
    if (open) {
      form.setFieldsValue(editing ? { ...editing } : emptyDraft);
    }
  }, [open, editing, form]);

  const close = () => {
    setEditing(null);
    setCreating(false);
  };

  const save = async () => {
    const values = await form.validateFields();
    // A blank box means "this kind has no schema", which is a different thing from an empty
    // JSON object — the latter would accept anything and stamp a version for no reason.
    const body: DocumentTypeWrite = {
      ...values,
      description: values.description?.trim() || null,
      metadataSchema: values.metadataSchema?.trim() || null,
    };

    try {
      if (editing) {
        await updateType.mutateAsync({ ...body, id: editing.id });
      } else {
        await createType.mutateAsync(body);
      }
      message.success(t('common.saved'));
      close();
    } catch (error) {
      // The two refusals worth naming: the schema text is not usable JSON Schema, and the
      // code already belongs to another kind. Anything else is a generic failure.
      if (error instanceof ApiError && error.code === 'document_type.schema_invalid') {
        message.error(t('admin.documentTypes.schemaInvalid'));
      } else if (error instanceof ApiError && error.code === 'document_type.code_taken') {
        message.error(t('admin.documentTypes.codeTaken'));
      } else {
        message.error(t('common.saveFailed'));
      }
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
        <ProfileOutlined /> {t('admin.documentTypes.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.documentTypes.intro')}
      </Typography.Paragraph>

      {canWrite && (
        <Flex>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.documentTypes.add')}
          </Button>
        </Flex>
      )}

      <Table
        rowKey="id"
        loading={isLoading}
        dataSource={types ?? []}
        pagination={false}
        size="small"
        scroll={{ x: true }}
        columns={[
          { title: t('admin.documentTypes.name'), dataIndex: 'name' },
          {
            title: t('admin.documentTypes.code'),
            dataIndex: 'code',
            render: (code: string) => <Tag>{code}</Tag>,
          },
          {
            title: t('admin.documentTypes.fields'),
            key: 'fields',
            render: (_: unknown, type: DocumentType) => {
              const fields = parsePropertiesSchema(type.metadataSchema);
              return fields.length === 0 ? (
                <Typography.Text type="secondary">{t('admin.documentTypes.noSchema')}</Typography.Text>
              ) : (
                fields.map((field) => <Tag key={field.key}>{field.label}</Tag>)
              );
            },
          },
          {
            title: t('admin.documentTypes.schemaVersion'),
            dataIndex: 'metadataSchemaVersion',
            render: (version: number) => `v${version}`,
          },
          ...(canWrite
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, type: DocumentType) => (
                    <Button size="small" onClick={() => setEditing(type)}>
                      {t('admin.documentTypes.editAction')}
                    </Button>
                  ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        open={open}
        title={editing ? t('admin.documentTypes.edit') : t('admin.documentTypes.add')}
        onCancel={close}
        onOk={() => void save()}
        confirmLoading={createType.isPending || updateType.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        width={640}
      >
        <Form form={form} layout="vertical" initialValues={emptyDraft}>
          <Form.Item
            name="name"
            label={t('admin.documentTypes.name')}
            rules={[{ required: true, max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="code"
            label={t('admin.documentTypes.code')}
            extra={t('admin.documentTypes.codeHint')}
            rules={[
              { required: true, max: 50 },
              { pattern: /^[a-z0-9_]+$/, message: t('admin.documentTypes.codeHint') },
            ]}
          >
            {/* A code is the natural key installations exchange rows by; changing one on a
                kind that is already in use re-points nothing, so it is left editable but
                the hint says what it is for. */}
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('admin.documentTypes.description')} rules={[{ max: 1000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="sortOrder" label={t('admin.documentTypes.sortOrder')}>
            <InputNumber precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="metadataSchema"
            label={t('admin.documentTypes.schema')}
            extra={t('admin.documentTypes.schemaHint')}
            rules={[{ max: 64000 }]}
          >
            <Input.TextArea rows={10} style={{ fontFamily: 'monospace' }} />
          </Form.Item>
          <SchemaPreview form={form} />
        </Form>
      </Modal>
    </Flex>
  );
}

/**
 * Names the fields the document form will actually offer for the schema as currently typed.
 * A property the flat subset cannot render simply does not appear here, which is the point:
 * it is cheaper to see that while writing the schema than after documents are filed under it.
 */
function SchemaPreview({ form }: { form: ReturnType<typeof Form.useForm<DocumentTypeWrite>>[0] }) {
  const { t } = useTranslation();
  const schema = Form.useWatch('metadataSchema', form);
  const fields = parsePropertiesSchema(schema);

  return (
    <Flex vertical gap={4}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('admin.documentTypes.preview')}
      </Typography.Text>
      {fields.length === 0 ? (
        <Typography.Text type="secondary">{t('admin.documentTypes.noFields')}</Typography.Text>
      ) : (
        <Flex gap={4} wrap>
          {fields.map((field) => (
            <Tag key={field.key} color={field.required ? 'blue' : undefined}>
              {field.label} · {t(`admin.documentTypes.kinds.${field.kind}`)}
            </Tag>
          ))}
        </Flex>
      )}
    </Flex>
  );
}
