// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { BlockOutlined, PlusOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Checkbox,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCapabilities,
  useCreateFeatureType,
  useDeleteFeatureType,
  useFeatureTypes,
  useUpdateFeatureType,
  type FeatureType,
  type FeatureTypeWrite,
} from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import { parsePropertiesSchema } from '../../components/typedProperties/propertiesSchema.ts';
import { bundledFeatureSymbols } from '../../map/featureSymbols.ts';
import { featureSymbolUrl } from '../../map/markerPalette.ts';

const categories: FeatureTypeWrite['category'][] = ['surface', 'underground', 'area', 'structure'];
const shapes: FeatureTypeWrite['acceptedGeometryClasses'] = [
  'point',
  'lineString',
  'polygon',
  'multiPoint',
  'multiLineString',
  'multiPolygon',
];
const displays: FeatureTypeWrite['protectedDisplay'][] = ['snapPoint', 'withhold'];

/** The blank kind a "new" click starts from: a point marker, snapped when protected. */
const emptyDraft: FeatureTypeWrite = {
  code: '',
  name: '',
  description: null,
  sortOrder: 0,
  category: 'surface',
  acceptedGeometryClasses: ['point', 'multiPoint'],
  requiresParent: false,
  protectedDisplay: 'snapPoint',
  symbolFile: null,
  style: null,
  propertiesSchema: null,
};

/** The refusals worth naming; anything else is a generic failure. */
const refusals: Record<string, string> = {
  'feature_type.schema_invalid': 'admin.featureTypes.schemaInvalid',
  'feature_type.style_invalid': 'admin.featureTypes.styleInvalid',
  'feature_type.code_taken': 'admin.featureTypes.codeTaken',
  'feature_type.seeded_immutable': 'admin.featureTypes.seededImmutable',
  'feature_type.in_use': 'admin.featureTypes.inUse',
};

/**
 * The kinds of feature this installation records — the ones that ship and the ones a club adds —
 * with what each may hold and how a protected one is shown.
 *
 * <p>
 * What the form offers follows what the server allows, so a refusal is the exception rather than
 * the way an administrator learns the rules. A shipped kind keeps its code, its category, its
 * protected display and its need for a parent, and those boxes are not offered for it. Once
 * features use a kind, the server also refuses taking a shape away, changing the category or
 * starting to need a parent; the form cannot know whether features exist, so it says so in a
 * hint and names the refusal when it comes.
 * </p>
 */
export default function FeatureTypesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.taxonomies, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.taxonomies, 'write');
  const canDelete = hasAccessAction(capabilities?.domains.taxonomies, 'delete');
  const { data: types, isLoading } = useFeatureTypes();
  const createType = useCreateFeatureType();
  const updateType = useUpdateFeatureType();
  const deleteType = useDeleteFeatureType();

  const [editing, setEditing] = useState<FeatureType | null>(null);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<FeatureTypeWrite>();

  const open = editing !== null || creating;
  const shipped = editing?.isSeeded === true;

  useEffect(() => {
    if (open) {
      form.setFieldsValue(
        editing
          ? {
              code: editing.code,
              name: editing.name,
              description: editing.description,
              sortOrder: editing.sortOrder,
              category: editing.category,
              acceptedGeometryClasses: editing.acceptedGeometryClasses,
              requiresParent: editing.requiresParent,
              protectedDisplay: editing.protectedDisplay,
              symbolFile: editing.symbolFile,
              style: editing.style,
              propertiesSchema: editing.propertiesSchema,
            }
          : emptyDraft,
      );
    }
  }, [open, editing, form]);

  const close = () => {
    setEditing(null);
    setCreating(false);
  };

  const refused = (error: unknown) => {
    const key = error instanceof ApiError && error.code ? refusals[error.code] : undefined;
    message.error(key ? t(key) : t('common.saveFailed'));
  };

  const save = async () => {
    const values = await form.validateFields();
    // A blank box means "none", which is a different thing from an empty JSON object.
    const body: FeatureTypeWrite = {
      ...values,
      description: values.description?.trim() || null,
      symbolFile: values.symbolFile || null,
      style: values.style?.trim() || null,
      propertiesSchema: values.propertiesSchema?.trim() || null,
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
      refused(error);
    }
  };

  const remove = async (type: FeatureType) => {
    try {
      await deleteType.mutateAsync(type.id);
      message.success(t('common.deleted'));
    } catch (error) {
      refused(error);
    }
  };

  if (!capabilities) {
    return null;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }} data-testid="feature-types-page">
      <Typography.Title level={4} style={{ margin: 0 }}>
        <BlockOutlined /> {t('admin.featureTypes.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.featureTypes.intro')}
      </Typography.Paragraph>

      {canWrite && (
        <Flex>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.featureTypes.add')}
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
          {
            title: '',
            key: 'symbol',
            width: 40,
            render: (_: unknown, type: FeatureType) =>
              type.symbolFile ? (
                <img src={featureSymbolUrl(type.symbolFile)} alt="" width={20} height={20} />
              ) : null,
          },
          { title: t('admin.featureTypes.name'), dataIndex: 'name' },
          {
            title: t('admin.featureTypes.code'),
            dataIndex: 'code',
            render: (code: string, type: FeatureType) => (
              <>
                <Tag>{code}</Tag>
                {type.isSeeded && <Tag color="blue">{t('admin.featureTypes.seeded')}</Tag>}
              </>
            ),
          },
          {
            title: t('admin.featureTypes.category'),
            dataIndex: 'category',
            render: (category: FeatureType['category']) => t(`admin.featureTypes.categories.${category}`),
          },
          {
            title: t('admin.featureTypes.shapes'),
            key: 'shapes',
            render: (_: unknown, type: FeatureType) =>
              type.acceptedGeometryClasses.map((shape) => (
                <Tag key={shape}>{t(`admin.featureTypes.shapeNames.${shape}`)}</Tag>
              )),
          },
          {
            title: t('admin.featureTypes.protectedDisplay'),
            dataIndex: 'protectedDisplay',
            render: (display: FeatureType['protectedDisplay']) =>
              t(`admin.featureTypes.displays.${display}`),
          },
          {
            title: t('admin.featureTypes.schemaVersion'),
            dataIndex: 'propertiesSchemaVersion',
            render: (version: number) => `v${version}`,
          },
          ...(canWrite || canDelete
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, type: FeatureType) => (
                    <Flex gap={8}>
                      {canWrite && (
                        <Button size="small" onClick={() => setEditing(type)}>
                          {t('admin.featureTypes.editAction')}
                        </Button>
                      )}
                      {canDelete && !type.isSeeded && (
                        <Popconfirm
                          title={t('admin.featureTypes.deleteConfirm', { name: type.name })}
                          okText={t('admin.featureTypes.delete')}
                          cancelText={t('common.cancel')}
                          onConfirm={() => void remove(type)}
                        >
                          <Button size="small" danger>
                            {t('admin.featureTypes.delete')}
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
        title={editing ? t('admin.featureTypes.edit') : t('admin.featureTypes.add')}
        onCancel={close}
        onOk={() => void save()}
        confirmLoading={createType.isPending || updateType.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        width={720}
      >
        <Form form={form} layout="vertical" initialValues={emptyDraft}>
          {shipped && (
            <Alert type="info" showIcon style={{ marginBottom: 16 }} title={t('admin.featureTypes.shippedNote')} />
          )}
          <Form.Item name="name" label={t('admin.featureTypes.name')} rules={[{ required: true, max: 100 }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="code"
            label={t('admin.featureTypes.code')}
            extra={t('admin.featureTypes.codeHint')}
            rules={[
              { required: true, max: 50 },
              { pattern: /^[a-z0-9_]+$/, message: t('admin.featureTypes.codeHint') },
            ]}
          >
            <Input disabled={shipped} />
          </Form.Item>
          <Form.Item name="description" label={t('admin.featureTypes.description')} rules={[{ max: 1000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Flex gap={16} wrap>
            <Form.Item name="sortOrder" label={t('admin.featureTypes.sortOrder')}>
              <InputNumber precision={0} style={{ width: 120 }} />
            </Form.Item>
            <Form.Item name="category" label={t('admin.featureTypes.category')}>
              <Select
                disabled={shipped}
                style={{ width: 200 }}
                options={categories.map((value) => ({
                  value,
                  label: t(`admin.featureTypes.categories.${value}`),
                }))}
              />
            </Form.Item>
            <Form.Item name="protectedDisplay" label={t('admin.featureTypes.protectedDisplay')}>
              <Select
                disabled={shipped}
                style={{ width: 240 }}
                options={displays.map((value) => ({
                  value,
                  label: t(`admin.featureTypes.displays.${value}`),
                }))}
              />
            </Form.Item>
          </Flex>
          <Form.Item
            name="acceptedGeometryClasses"
            label={t('admin.featureTypes.shapes')}
            extra={t('admin.featureTypes.shapesHint')}
            rules={[{ required: true, type: 'array', min: 1, message: t('admin.featureTypes.shapesRequired') }]}
          >
            <Checkbox.Group
              options={shapes.map((value) => ({
                value,
                label: t(`admin.featureTypes.shapeNames.${value}`),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="requiresParent"
            label={t('admin.featureTypes.requiresParent')}
            extra={t('admin.featureTypes.requiresParentHint')}
            valuePropName="checked"
          >
            <Switch disabled={shipped} />
          </Form.Item>
          <Form.Item name="symbolFile" label={t('admin.featureTypes.symbol')}>
            <Select
              allowClear
              style={{ width: 280 }}
              placeholder={t('admin.featureTypes.noSymbol')}
              options={bundledFeatureSymbols.map((file) => ({
                value: file,
                label: (
                  <Flex gap={8} align="center">
                    <img src={featureSymbolUrl(file)} alt="" width={16} height={16} />
                    {file}
                  </Flex>
                ),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="style"
            label={t('admin.featureTypes.style')}
            extra={t('admin.featureTypes.styleHint')}
            rules={[{ max: 8000 }]}
          >
            <Input.TextArea rows={3} style={{ fontFamily: 'monospace' }} />
          </Form.Item>
          <Form.Item
            name="propertiesSchema"
            label={t('admin.featureTypes.schema')}
            extra={t('admin.featureTypes.schemaHint')}
            rules={[{ max: 64000 }]}
          >
            <Input.TextArea rows={8} style={{ fontFamily: 'monospace' }} />
          </Form.Item>
          <SchemaPreview form={form} />
        </Form>
      </Modal>
    </Flex>
  );
}

/** Names the fields the feature form will offer for the schema as currently typed. */
function SchemaPreview({ form }: { form: ReturnType<typeof Form.useForm<FeatureTypeWrite>>[0] }) {
  const { t } = useTranslation();
  const schema = Form.useWatch('propertiesSchema', form);
  const fields = parsePropertiesSchema(schema);

  return (
    <Flex vertical gap={4}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('admin.featureTypes.preview')}
      </Typography.Text>
      {fields.length === 0 ? (
        <Typography.Text type="secondary">{t('admin.featureTypes.noFields')}</Typography.Text>
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
