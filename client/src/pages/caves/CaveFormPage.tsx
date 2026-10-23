// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { App, Card, Collapse, Flex, Form, Input, InputNumber, Select, Switch, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useCave,
  useCaveTypes,
  useCreateCave,
  useFeatureTypes,
  useRockTypes,
  useUpdateCave,
  type CaveWrite,
} from '../../api/hooks.ts';
import { parsePropertiesSchema } from '../../components/typedProperties/propertiesSchema.ts';
import TypedField from '../../components/typedProperties/TypedField.tsx';

type CaveFormValues = Omit<CaveWrite, 'cavingGroupId'>;

/** Create/edit form, sectioned to mirror the cave sheet. CavingGroup binding arrives with teams UI. */
export default function CaveFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const isEdit = !!id;
  const [form] = Form.useForm<CaveFormValues>();

  const { data: cave } = useCave(id);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();
  const { data: featureTypes } = useFeatureTypes();
  const createCave = useCreateCave();
  const updateCave = useUpdateCave(id ?? '');

  // The fields a club has added to the type every cave carries. The ones the type ships with are
  // written by imports and devices and are not offered here; they ride along in the stored document.
  const clubFields = useMemo(
    () =>
      parsePropertiesSchema(featureTypes?.find((type) => type.boundKind === 'cave')?.propertiesSchema).filter(
        (field) => !field.readOnly,
      ),
    [featureTypes],
  );
  const [extra, setExtra] = useState<Record<string, unknown>>({});

  useEffect(() => {
    if (cave && isEdit) {
      form.setFieldsValue(cave as unknown as CaveFormValues);
      setExtra((cave.properties as Record<string, unknown> | null) ?? {});
    }
  }, [cave, isEdit, form]);

  const setExtraValue = (key: string, value: unknown) =>
    setExtra((current) => {
      const next = { ...current };
      if (value === undefined || value === null || value === '') {
        delete next[key];
      } else {
        next[key] = value;
      }
      return next;
    });

  const onFinish = async (values: CaveFormValues) => {
    const body: CaveWrite = {
      ...values,
      isShowCave: values.isShowCave ?? false,
      locationProtected: values.locationProtected ?? false,
      explorationStatus: values.explorationStatus ?? 'unknown',
      visibility: values.visibility ?? 'private',
      cavingGroupId: cave?.cavingGroupId ?? null,
      // The stored document with the club's own fields as edited here; every key this form does
      // not offer is sent back unchanged. The cave stays under its current primary parent (the
      // write DTO addresses containment by parent id).
      properties: { ...((cave?.properties as Record<string, unknown> | null) ?? {}), ...extra },
      parentId: cave?.parents.find((p) => p.isPrimary)?.id ?? null,
    };
    try {
      const saved = isEdit ? await updateCave.mutateAsync(body) : await createCave.mutateAsync(body);
      message.success(t('common.saved'));
      navigate(`/caves/${saved.id}`);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const numberField = (name: keyof CaveFormValues, label: string) => (
    <Form.Item name={name} label={label} style={{ width: 180 }}>
      <InputNumber min={0} style={{ width: '100%' }} />
    </Form.Item>
  );

  return (
    <div style={{ padding: 24, maxWidth: 1000 }}>
      <Typography.Title level={3}>
        {isEdit ? t('caves.editCave') : t('caves.newCave')}
      </Typography.Title>
      <Card>
        <Form<CaveFormValues> form={form} layout="vertical" onFinish={(v) => void onFinish(v)} requiredMark>
          <Collapse
            defaultActiveKey={['identification', 'localization', 'access', 'clubFields']}
            items={[
              {
                key: 'identification',
                label: t('caves.sections.identification'),
                children: (
                  <>
                    <Form.Item name="name" label={t('caves.name')} rules={[{ required: true }, { max: 255 }]}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="otherToponyms" label={t('caves.fields.otherToponyms')} rules={[{ max: 250 }]}>
                      <Input />
                    </Form.Item>
                    <Flex gap={16} wrap>
                      <Form.Item
                        name="identificationCode"
                        label={t('caves.identificationCode')}
                        rules={[{ max: 50 }]}
                        style={{ width: 220 }}
                      >
                        <Input />
                      </Form.Item>
                      <Form.Item
                        name="caveTypeId"
                        label={t('caves.type')}
                        rules={[{ required: true }]}
                        style={{ width: 240 }}
                      >
                        <Select options={caveTypes?.map((x) => ({ value: x.id, label: x.name }))} />
                      </Form.Item>
                      <Form.Item name="website" label={t('caves.fields.website')} style={{ width: 280 }}>
                        <Input />
                      </Form.Item>
                    </Flex>
                    <Form.Item name="description" label={t('caves.fields.description')}>
                      <Input.TextArea rows={4} />
                    </Form.Item>
                  </>
                ),
              },
              {
                key: 'localization',
                label: t('caves.sections.localization'),
                children: (
                  <Flex gap={16} wrap>
                    <Form.Item name="region" label={t('caves.region')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="hydrographicBasin" label={t('caves.fields.hydrographicBasin')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="valley" label={t('caves.fields.valley')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="tributaryRiver" label={t('caves.fields.tributaryRiver')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="closestAddress" label={t('caves.fields.closestAddress')} style={{ width: 340 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="landRegistryNumber" label={t('caves.fields.landRegistryNumber')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="locationNotes" label={t('caves.fields.locationNotes')} style={{ width: '100%' }}>
                      <Input.TextArea rows={2} />
                    </Form.Item>
                  </Flex>
                ),
              },
              {
                key: 'geology',
                label: t('caves.sections.geology'),
                children: (
                  <Flex gap={16} wrap>
                    <Form.Item name="rockTypeId" label={t('caves.fields.rockType')} style={{ width: 240 }}>
                      <Select allowClear options={rockTypes?.map((x) => ({ value: x.id, label: x.name }))} />
                    </Form.Item>
                    <Form.Item name="rockAge" label={t('caves.fields.rockAge')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                  </Flex>
                ),
              },
              {
                key: 'morphometry',
                label: t('caves.sections.morphometry'),
                children: (
                  <Flex gap={16} wrap>
                    {numberField('surveyedLength', t('caves.fields.surveyedLength'))}
                    {numberField('estimatedLength', t('caves.fields.estimatedLength'))}
                    {numberField('realExtension', t('caves.fields.realExtension'))}
                    {numberField('projectedExtension', t('caves.fields.projectedExtension'))}
                    {numberField('depth', t('caves.depth'))}
                    {numberField('positiveDepth', t('caves.fields.positiveDepth'))}
                    {numberField('negativeDepth', t('caves.fields.negativeDepth'))}
                    {numberField('potentialDepth', t('caves.fields.potentialDepth'))}
                    {numberField('altitude', t('caves.fields.altitude'))}
                    {numberField('volume', t('caves.fields.volume'))}
                    {numberField('area', t('caves.fields.area'))}
                    {numberField('ramificationIndex', t('caves.fields.ramificationIndex'))}
                    {numberField('caveAge', t('caves.fields.caveAge'))}
                  </Flex>
                ),
              },
              {
                key: 'status',
                label: t('caves.sections.status'),
                children: (
                  <Flex gap={16} wrap align="end">
                    <Form.Item name="explorationStatus" label={t('caves.fields.explorationStatus')} style={{ width: 220 }}>
                      <Select
                        options={['unknown', 'ongoing', 'finished', 'abandoned'].map((v) => ({
                          value: v,
                          label: t(`caves.explorationValues.${v}`),
                        }))}
                      />
                    </Form.Item>
                    <Form.Item name="protectionClass" label={t('caves.fields.protectionClass')} style={{ width: 220 }}>
                      <Input />
                    </Form.Item>
                    <Form.Item name="isShowCave" label={t('caves.fields.isShowCave')} valuePropName="checked">
                      <Switch />
                    </Form.Item>
                    {numberField('showCaveLength', t('caves.fields.showCaveLength'))}
                  </Flex>
                ),
              },
              {
                key: 'discovery',
                label: t('caves.sections.discovery'),
                children: (
                  <Flex gap={16} wrap>
                    <Form.Item name="discoveryDate" label={t('caves.fields.discoveryDate')} style={{ width: 220 }}>
                      <Input placeholder="1952 / 1952-07 / 1952-07-14" />
                    </Form.Item>
                    <Form.Item name="discoverer" label={t('caves.fields.discoverer')} style={{ width: 340 }}>
                      <Input />
                    </Form.Item>
                  </Flex>
                ),
              },
              {
                key: 'access',
                label: t('caves.sections.access'),
                children: (
                  <Flex gap={24} wrap align="end">
                    <Form.Item name="visibility" label={t('caves.visibility')} initialValue="private" style={{ width: 240 }}>
                      <Select
                        options={['private', 'authenticated', 'public'].map((v) => ({
                          value: v,
                          label: t(`caves.visibilityValues.${v}`),
                        }))}
                      />
                    </Form.Item>
                    <Form.Item
                      name="locationProtected"
                      label={t('caves.fields.locationProtected')}
                      tooltip={t('caves.fields.locationProtectedHint')}
                      valuePropName="checked"
                    >
                      <Switch />
                    </Form.Item>
                  </Flex>
                ),
              },
              ...(clubFields.length > 0
                ? [
                    {
                      key: 'clubFields',
                      label: t('caves.sections.clubFields'),
                      children: (
                        <Flex gap={16} wrap data-testid="cave-club-fields">
                          {clubFields.map((field) => (
                            <Form.Item key={field.key} label={field.label} required={field.required} style={{ width: 280 }}>
                              <TypedField
                                field={field}
                                value={extra[field.key]}
                                onChange={(value) => setExtraValue(field.key, value)}
                                data-testid={`cave-club-field-${field.key}`}
                              />
                            </Form.Item>
                          ))}
                        </Flex>
                      ),
                    },
                  ]
                : []),
            ]}
          />
          <Flex gap={8} justify="end" style={{ marginTop: 16 }}>
            <Button onClick={() => navigate(-1)}>{t('common.cancel')}</Button>
            <Button type="primary" htmlType="submit" loading={createCave.isPending || updateCave.isPending}>
              {t('common.save')}
            </Button>
          </Flex>
        </Form>
      </Card>
    </div>
  );
}
