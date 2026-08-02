// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { Checkbox, Divider, Form, Input, InputNumber, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useFeatureTypes,
  useFeatures,
  type FeatureDetail,
  type FeatureType,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import DialogHost from '../DialogHost.tsx';
import { parsePropertiesSchema } from '../typedProperties/propertiesSchema.ts';

type Visibility = FeatureDetail['visibility'];
type GeometryClass = FeatureType['acceptedGeometryClasses'][number];
type DrawShape = 'Point' | 'LineString' | 'Polygon';

export interface FeatureAttributeValues {
  name: string | null;
  featureTypeId: number;
  description: string | null;
  visibility: Visibility;
  locationProtected: boolean;
  properties: Record<string, unknown>;
  /** Chosen primary parent — only populated when the parent picker is shown. */
  primaryParentId: string | null;
}

interface FeatureEditModalProps {
  open: boolean;
  title: string;
  /**
   * Constrains the feature-type options to types accepting the drawn geometry
   * class; null (geometry withheld/absent) leaves all types selectable.
   */
  geometryType: DrawShape | null;
  initial: Partial<FeatureAttributeValues>;
  /**
   * Shows the primary-parent picker (create flow). Editing hides it — parent
   * edges of an existing feature are managed on its detail page instead.
   */
  withParent?: boolean;
  busy?: boolean;
  onCancel: () => void;
  onSubmit: (values: FeatureAttributeValues) => void;
}

interface FormValues {
  name?: string;
  featureTypeId: number;
  description?: string;
  visibility: Visibility;
  locationProtected?: boolean;
  primaryParentId?: string;
  properties?: Record<string, unknown>;
}

// A single drawn shape also fits types that accept the corresponding multi-class:
// the server wraps as needed, so both count as compatible here.
const compatibleClasses: Record<DrawShape, GeometryClass[]> = {
  Point: ['point', 'multiPoint'],
  LineString: ['lineString', 'multiLineString'],
  Polygon: ['polygon', 'multiPolygon'],
};

/**
 * Attribute form for a generic feature. Deliberately API-agnostic: the caller
 * decides whether the values become a POST (freshly drawn feature) or a PUT
 * (editing an existing one). Extra fields render from the feature type's
 * optional properties schema; unknown existing property keys are preserved.
 */
export default function FeatureEditModal({
  open,
  title,
  geometryType,
  initial,
  withParent,
  busy,
  onCancel,
  onSubmit,
}: FeatureEditModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<FormValues>();
  const { data: featureTypes } = useFeatureTypes();

  // Parent picker: remote-search select over the generic feature list.
  const [parentQuery, setParentQuery] = useState('');
  const debouncedParentQuery = useDebouncedValue(parentQuery);
  const { data: parentResults, isFetching: searchingParents } = useFeatures(
    { search: debouncedParentQuery || undefined, pageSize: 20 },
    open && !!withParent,
  );

  const typeOptions = useMemo(
    () =>
      (featureTypes ?? [])
        .filter(
          (ft) =>
            geometryType === null ||
            ft.acceptedGeometryClasses.some((c) => compatibleClasses[geometryType].includes(c)),
        )
        .map((ft) => ({ value: Number(ft.id), label: ft.name })),
    [featureTypes, geometryType],
  );

  const selectedTypeId = Form.useWatch('featureTypeId', form);
  const selectedType = featureTypes?.find((ft) => Number(ft.id) === selectedTypeId);
  const schemaFields = useMemo(
    () => parsePropertiesSchema(selectedType?.propertiesSchema),
    [selectedType],
  );

  useEffect(() => {
    if (open) {
      form.resetFields();
      form.setFieldsValue({
        name: initial.name ?? undefined,
        featureTypeId: initial.featureTypeId,
        description: initial.description ?? undefined,
        visibility: initial.visibility ?? 'private',
        locationProtected: initial.locationProtected ?? false,
        primaryParentId: initial.primaryParentId ?? undefined,
        properties: initial.properties,
      });
      setParentQuery('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialize only when the modal opens
  }, [open]);

  const parentOptions = useMemo(
    () =>
      (parentResults?.items ?? []).map((f) => ({
        value: f.id,
        label: f.name ?? t('features.unnamed'),
      })),
    [parentResults, t],
  );

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
      locationProtected: values.locationProtected ?? false,
      primaryParentId: values.primaryParentId ?? null,
      properties,
    });
  };

  return (
    <DialogHost
      kind="feature-edit"
      title={title}
      open={open}
      onCancel={onCancel}
      onOk={() => void onOk()}
      okLoading={busy}
      width={560}
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
            options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
              value: v,
              label: t(`caves.visibilityValues.${v}`),
            }))}
          />
        </Form.Item>
        <Form.Item
          name="locationProtected"
          valuePropName="checked"
          // The label rides the checkbox itself; an empty Form.Item label would
          // reserve a blank row above it.
          style={{ marginBottom: 12 }}
        >
          <Checkbox>{t('features.locationProtected')}</Checkbox>
        </Form.Item>
        {withParent && (
          <Form.Item
            name="primaryParentId"
            label={t('features.parent')}
            rules={selectedType?.requiresParent ? [{ required: true }] : undefined}
          >
            <Select
              allowClear
              showSearch
              filterOption={false}
              loading={searchingParents}
              onSearch={setParentQuery}
              placeholder={t('features.parentSearchPlaceholder')}
              options={parentOptions}
              notFoundContent={null}
            />
          </Form.Item>
        )}
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
    </DialogHost>
  );
}
