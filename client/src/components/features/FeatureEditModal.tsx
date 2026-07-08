// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { Checkbox, Divider, Form, Input, InputNumber, Modal, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCave,
  useCaveSearch,
  useFeatureTypes,
  type SurfaceFeatureDetail,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { parsePropertiesSchema } from './propertiesSchema.ts';

type Visibility = SurfaceFeatureDetail['visibility'];
type DrawShape = 'Point' | 'LineString' | 'Polygon';

export interface FeatureAttributeValues {
  name: string | null;
  featureTypeId: number;
  description: string | null;
  visibility: Visibility;
  caveId: string | null;
  properties: Record<string, unknown>;
}

interface FeatureEditModalProps {
  open: boolean;
  title: string;
  /** Constrains the feature-type options to kinds compatible with the geometry. */
  geometryType: DrawShape;
  initial: Partial<FeatureAttributeValues>;
  busy?: boolean;
  onCancel: () => void;
  onSubmit: (values: FeatureAttributeValues) => void;
}

interface FormValues {
  name?: string;
  featureTypeId: number;
  description?: string;
  visibility: Visibility;
  caveId?: string;
  properties?: Record<string, unknown>;
}

const compatibleKinds: Record<DrawShape, string[]> = {
  Point: ['point', 'any'],
  LineString: ['line', 'any'],
  Polygon: ['polygon', 'any'],
};

/**
 * Attribute form for a surface feature. Deliberately API-agnostic: the caller
 * decides whether the values become a POST (freshly drawn feature) or a PUT
 * (editing an existing one). Extra fields render from the feature type's
 * optional properties schema; unknown existing property keys are preserved.
 */
export default function FeatureEditModal({
  open,
  title,
  geometryType,
  initial,
  busy,
  onCancel,
  onSubmit,
}: FeatureEditModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<FormValues>();
  const { data: featureTypes } = useFeatureTypes();

  // Cave link: remote-search select. The currently linked cave may not be in
  // the search results, so its label is fetched separately.
  const [caveQuery, setCaveQuery] = useState('');
  const debouncedCaveQuery = useDebouncedValue(caveQuery);
  const { data: caveResults, isFetching: searchingCaves } = useCaveSearch(debouncedCaveQuery);
  const { data: linkedCave } = useCave(initial.caveId ?? undefined);

  const typeOptions = useMemo(
    () =>
      (featureTypes ?? [])
        .filter((ft) => compatibleKinds[geometryType].includes(ft.geometryKind))
        .map((ft) => ({ value: Number(ft.id), label: ft.name })),
    [featureTypes, geometryType],
  );

  const selectedTypeId = Form.useWatch('featureTypeId', form);
  const schemaFields = useMemo(() => {
    const type = featureTypes?.find((ft) => Number(ft.id) === selectedTypeId);
    return parsePropertiesSchema(type?.propertiesSchema);
  }, [featureTypes, selectedTypeId]);

  useEffect(() => {
    if (open) {
      form.resetFields();
      form.setFieldsValue({
        name: initial.name ?? undefined,
        featureTypeId: initial.featureTypeId,
        description: initial.description ?? undefined,
        visibility: initial.visibility ?? 'private',
        caveId: initial.caveId ?? undefined,
        properties: initial.properties,
      });
      setCaveQuery('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialize only when the modal opens
  }, [open]);

  const caveOptions = useMemo(() => {
    const options = (caveResults?.caves ?? []).map((cave) => ({
      value: cave.id,
      label: `${cave.name}${cave.region ? ` — ${cave.region}` : ''}`,
    }));
    if (linkedCave && !options.some((o) => o.value === linkedCave.id)) {
      options.unshift({ value: linkedCave.id, label: linkedCave.name });
    }
    return options;
  }, [caveResults, linkedCave]);

  const onOk = async () => {
    const values = await form.validateFields();

    // Merge schema-driven values over any pre-existing properties so keys the
    // current schema does not know about survive the round-trip.
    const properties: Record<string, unknown> = { ...(initial.properties ?? {}) };
    for (const field of schemaFields) {
      const value = values.properties?.[field.key];
      if (value === undefined || value === null || value === '') {
        delete properties[field.key];
      } else {
        properties[field.key] = value;
      }
    }

    onSubmit({
      name: values.name?.trim() ? values.name.trim() : null,
      featureTypeId: values.featureTypeId,
      description: values.description?.trim() ? values.description.trim() : null,
      visibility: values.visibility,
      caveId: values.caveId ?? null,
      properties,
    });
  };

  return (
    <Modal
      title={title}
      open={open}
      onCancel={onCancel}
      onOk={() => void onOk()}
      confirmLoading={busy}
      width={560}
      destroyOnHidden
    >
      <Form<FormValues> form={form} layout="vertical">
        <Form.Item name="name" label={t('features.name')}>
          <Input maxLength={200} />
        </Form.Item>
        <Form.Item name="featureTypeId" label={t('features.type')} rules={[{ required: true }]}>
          <Select options={typeOptions} />
        </Form.Item>
        <Form.Item name="visibility" label={t('features.visibility')} rules={[{ required: true }]}>
          <Select
            options={(['private', 'team', 'authenticated', 'public'] as const).map((v) => ({
              value: v,
              label: t(`caves.visibilityValues.${v}`),
            }))}
          />
        </Form.Item>
        <Form.Item name="caveId" label={t('features.linkedCave')}>
          <Select
            allowClear
            showSearch
            filterOption={false}
            loading={searchingCaves}
            onSearch={setCaveQuery}
            placeholder={t('features.linkedCavePlaceholder')}
            options={caveOptions}
            notFoundContent={null}
          />
        </Form.Item>
        <Form.Item name="description" label={t('features.description')}>
          <Input.TextArea rows={3} />
        </Form.Item>
        {schemaFields.length > 0 && (
          <>
            <Divider plain style={{ margin: '8px 0' }}>
              {t('features.typedProperties')}
            </Divider>
            {schemaFields.map((field) => (
              <Form.Item
                key={field.key}
                name={['properties', field.key]}
                label={field.label}
                rules={field.required ? [{ required: true }] : undefined}
                valuePropName={field.kind === 'boolean' ? 'checked' : 'value'}
              >
                {field.kind === 'boolean' ? (
                  <Checkbox />
                ) : field.kind === 'enum' ? (
                  <Select
                    allowClear
                    options={field.enumValues?.map((v) => ({ value: v, label: v }))}
                  />
                ) : field.kind === 'string' ? (
                  <Input />
                ) : (
                  <InputNumber
                    style={{ width: '100%' }}
                    min={field.min}
                    max={field.max}
                    precision={field.kind === 'integer' ? 0 : undefined}
                  />
                )}
              </Form.Item>
            ))}
          </>
        )}
      </Form>
    </Modal>
  );
}
