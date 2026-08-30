// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { CompassOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Form, Input, InputNumber, Modal, Popconfirm, Select, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCapabilities,
  useChecklists,
  useCreateTripType,
  useDeleteTripType,
  useTripTypes,
  useUpdateTripType,
  type TripType,
  type TripTypeWrite,
} from '../../api/hooks.ts';
import { parsePropertiesSchema } from '../../components/typedProperties/propertiesSchema.ts';

const emptyDraft: TripTypeWrite = {
  code: '',
  name: '',
  description: null,
  sortOrder: 0,
  fieldDataSchema: null,
  logisticsSchema: null,
  safetySchema: null,
  defaultChecklistId: null,
};

/** The three schema boxes, in the order a report is written in. */
const SCHEMA_FIELDS = ['fieldDataSchema', 'logisticsSchema', 'safetySchema'] as const;
type SchemaField = (typeof SCHEMA_FIELDS)[number];

/**
 * What trips at this installation are recorded as being for, and what each purpose asks a
 * report to record.
 *
 * Each purpose carries three schemas — field data, logistics, safety — edited as raw text
 * rather than through a field builder: JSON Schema says far more than a builder could offer,
 * and the form a trip renders understands only a flat subset of it. The preview below each box
 * names exactly which fields will appear, which is cheaper to see while writing the schema
 * than after reports are written under it.
 *
 * Rewriting a schema does not rewrite the reports already stored under it. The server
 * publishes a new version of that schema, every trip keeps the version its section was checked
 * against, and it is re-checked the next time somebody edits that section — so tightening what
 * a purpose asks for never retroactively invalidates history.
 *
 * The purposes that ship with the product keep their codes and cannot be deleted: clients
 * translate their wording by code and installations exchange trips under them, so a renamed or
 * removed code would silently break both. Everything else about them — wording, ordering, and
 * all three schemas — is an installation's to change.
 */
export default function TripTypesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.taxonomies, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.taxonomies, 'write');
  const { data: types, isLoading } = useTripTypes();
  const { data: checklists } = useChecklists();
  const createType = useCreateTripType();
  const updateType = useUpdateTripType();
  const deleteType = useDeleteTripType();
  const [editing, setEditing] = useState<TripType | null>(null);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<TripTypeWrite>();

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
    // A blank box means "this section asks for nothing", which is a different thing from an
    // empty JSON object — the latter would accept anything and publish a version for no reason.
    const body: TripTypeWrite = {
      ...values,
      description: values.description?.trim() || null,
      fieldDataSchema: values.fieldDataSchema?.trim() || null,
      logisticsSchema: values.logisticsSchema?.trim() || null,
      safetySchema: values.safetySchema?.trim() || null,
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
      message.error(problemMessage(error, t));
    }
  };

  const remove = async (type: TripType) => {
    try {
      await deleteType.mutateAsync(type.id);
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
        <CompassOutlined /> {t('admin.tripTypes.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.tripTypes.intro')}
      </Typography.Paragraph>

      {canWrite && (
        <Flex>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.tripTypes.add')}
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
          { title: t('admin.tripTypes.name'), dataIndex: 'name' },
          {
            title: t('admin.tripTypes.code'),
            dataIndex: 'code',
            render: (code: string) => <Tag>{code}</Tag>,
          },
          {
            title: t('admin.tripTypes.source'),
            key: 'source',
            render: (_: unknown, type: TripType) =>
              type.isSeeded ? (
                <Tag color="blue">{t('admin.tripTypes.seeded')}</Tag>
              ) : (
                <Tag>{t('admin.tripTypes.custom')}</Tag>
              ),
          },
          {
            title: t('admin.tripTypes.sections'),
            key: 'sections',
            render: (_: unknown, type: TripType) => {
              const counts = SCHEMA_FIELDS.map((field) => parsePropertiesSchema(type[field]).length);
              return counts.every((count) => count === 0) ? (
                <Typography.Text type="secondary">{t('admin.tripTypes.noSchema')}</Typography.Text>
              ) : (
                <Flex gap={4} wrap>
                  {SCHEMA_FIELDS.map((field, index) => (
                    <Tag key={field}>
                      {t(`admin.tripTypes.${field}`)} · {counts[index]} · v{type[versionOf(field)]}
                    </Tag>
                  ))}
                </Flex>
              );
            },
          },
          ...(canWrite
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, type: TripType) => (
                    <Flex gap={8}>
                      <Button size="small" onClick={() => setEditing(type)}>
                        {t('admin.tripTypes.editAction')}
                      </Button>
                      {/* A shipped purpose has no delete: other installations read trips by
                          its code, so removing it here would break records elsewhere. */}
                      {!type.isSeeded && (
                        <Popconfirm
                          title={t('admin.tripTypes.deleteConfirm')}
                          onConfirm={() => void remove(type)}
                        >
                          <Button size="small" danger>
                            {t('admin.tripTypes.delete')}
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
        title={editing ? t('admin.tripTypes.edit') : t('admin.tripTypes.add')}
        onCancel={close}
        onOk={() => void save()}
        confirmLoading={createType.isPending || updateType.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        width={720}
      >
        <Form form={form} layout="vertical" initialValues={emptyDraft}>
          <Form.Item name="name" label={t('admin.tripTypes.name')} rules={[{ required: true, max: 100 }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="code"
            label={t('admin.tripTypes.code')}
            extra={t('admin.tripTypes.codeHint')}
            rules={[
              { required: true, max: 50 },
              { pattern: /^[a-z0-9_]+$/, message: t('admin.tripTypes.codeHint') },
            ]}
          >
            {/* A shipped purpose keeps its code — the server refuses a change — so the box is
                disabled rather than left to be refused after the fact. */}
            <Input disabled={editing?.isSeeded === true} />
          </Form.Item>
          <Form.Item name="description" label={t('admin.tripTypes.description')} rules={[{ max: 1000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="sortOrder" label={t('admin.tripTypes.sortOrder')}>
            <InputNumber precision={0} style={{ width: '100%' }} />
          </Form.Item>
          {/* A reference to a list, never a copy of one: correcting a line corrects it for every
              trip of this purpose at once. Only the lists this administrator may read are
              offered — naming one they cannot open would be choosing something they cannot see.
              Pointing a purpose at a list confers nothing on anybody who reads a trip of it. */}
          <Form.Item
            name="defaultChecklistId"
            label={t('admin.tripTypes.defaultChecklist')}
            extra={t('admin.tripTypes.defaultChecklistHint')}
          >
            <Select
              allowClear
              options={(checklists ?? []).map((list) => ({ value: list.id, label: list.title }))}
            />
          </Form.Item>
          {SCHEMA_FIELDS.map((field) => (
            <div key={field}>
              <Form.Item
                name={field}
                label={t(`admin.tripTypes.${field}`)}
                extra={t('admin.tripTypes.schemaHint')}
                rules={[{ max: 64000 }]}
              >
                <Input.TextArea rows={8} style={{ fontFamily: 'monospace' }} />
              </Form.Item>
              <SchemaPreview form={form} field={field} />
            </div>
          ))}
        </Form>
      </Modal>
    </Flex>
  );
}

const versionOf = (field: SchemaField) =>
  ({
    fieldDataSchema: 'fieldDataSchemaVersion',
    logisticsSchema: 'logisticsSchemaVersion',
    safetySchema: 'safetySchemaVersion',
  })[field] as 'fieldDataSchemaVersion' | 'logisticsSchemaVersion' | 'safetySchemaVersion';

/**
 * Names the fields the trip form will actually offer for the schema as currently typed. A
 * property the flat subset cannot render simply does not appear here, which is the point: it is
 * cheaper to see that while writing the schema than after reports are written under it.
 *
 * The title the schema carries is shown, not the translated wording a trip's own form uses for
 * the shipped codes — this is a preview of what was authored, and translating it here would
 * hide a title that has just been typed behind wording that came from somewhere else.
 */
function SchemaPreview({
  form,
  field,
}: {
  form: ReturnType<typeof Form.useForm<TripTypeWrite>>[0];
  field: SchemaField;
}) {
  const { t } = useTranslation();
  const schema = Form.useWatch(field, form);
  const fields = parsePropertiesSchema(schema);

  return (
    <Flex vertical gap={4} style={{ marginBottom: 16 }}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('admin.tripTypes.preview')}
      </Typography.Text>
      {fields.length === 0 ? (
        <Typography.Text type="secondary">{t('admin.tripTypes.noFields')}</Typography.Text>
      ) : (
        <Flex gap={4} wrap>
          {fields.map((schemaField) => (
            <Tag key={schemaField.key} color={schemaField.required ? 'blue' : undefined}>
              {schemaField.label} · {t(`admin.tripTypes.kinds.${schemaField.kind}`)}
            </Tag>
          ))}
        </Flex>
      )}
      {/* Said where the choice is made, not discovered afterwards: a trip is recorded before its
          report is written, so a field this schema demands has nowhere to be answered when the
          trip is created and every new trip of this purpose is refused. */}
      {fields.some((schemaField) => schemaField.required) && (
        <Typography.Text type="warning" style={{ fontSize: 12 }} data-testid={`schema-required-warning-${field}`}>
          {t('admin.tripTypes.requiredWarning')}
        </Typography.Text>
      )}
    </Flex>
  );
}

/** The refusals worth naming: anything else leaves somebody guessing which box was at fault. */
function problemMessage(error: unknown, t: ReturnType<typeof useTranslation>['t']): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  switch (error.code) {
    case 'trip_type.schema_invalid':
      return t('admin.tripTypes.schemaInvalid');
    case 'trip_type.code_taken':
      return t('admin.tripTypes.codeTaken');
    case 'trip_type.seeded_immutable':
      return t('admin.tripTypes.seededImmutable');
    case 'trip_type.in_use':
      return t('admin.tripTypes.inUse');
    default:
      return t('common.saveFailed');
  }
}
