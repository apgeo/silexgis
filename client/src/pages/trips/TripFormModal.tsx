// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, DatePicker, Flex, Form, Input, Modal, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCaveSearch,
  useCreateTripLog,
  useUpdateTripLog,
  type TripLogInfo,
  type TripLogWrite,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface TripFormModalProps {
  open: boolean;
  trip: TripLogInfo | null; // null → create
  onClose: (savedId?: string) => void;
}

interface FormValues {
  title: string;
  dates: [Dayjs, Dayjs | null] | Dayjs; // range when multi-day
  locationText?: string;
  description?: string;
  caveIds: string[];
  participants: { nameText: string }[];
  visibility: TripLogInfo['visibility'];
}

/**
 * Trip editor. Participants are free-text names in the form; registered-user
 * participants (with account links) come with the teams/members UX later —
 * existing user links on a trip are preserved on update.
 */
export default function TripFormModal({ open, trip, onClose }: TripFormModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const createTrip = useCreateTripLog();
  const updateTrip = useUpdateTripLog();

  const [caveQuery, setCaveQuery] = useState('');
  const debouncedCaveQuery = useDebouncedValue(caveQuery);
  const { data: caveResults } = useCaveSearch(debouncedCaveQuery);
  // Options accumulate across searches so selected entries keep their labels.
  const [knownCaves, setKnownCaves] = useState<Map<string, string>>(new Map());
  useEffect(() => {
    if (caveResults) {
      setKnownCaves((previous) => {
        const next = new Map(previous);
        for (const cave of caveResults.caves) {
          next.set(cave.id, cave.name);
        }
        return next;
      });
    }
  }, [caveResults]);

  const caveOptions = useMemo(
    () => [...knownCaves.entries()].map(([value, label]) => ({ value, label })),
    [knownCaves],
  );

  useEffect(() => {
    if (open) {
      form.resetFields();
      if (trip) {
        form.setFieldsValue({
          title: trip.title,
          dates: trip.tripDateEnd
            ? [dayjs(trip.tripDate), dayjs(trip.tripDateEnd)]
            : dayjs(trip.tripDate),
          locationText: trip.locationText ?? undefined,
          description: trip.description ?? undefined,
          caveIds: [...trip.caveIds],
          participants: trip.participants
            .filter((p) => p.userId == null)
            .map((p) => ({ nameText: p.nameText ?? '' })),
          visibility: trip.visibility,
        });
      } else {
        form.setFieldsValue({ dates: dayjs(), caveIds: [], participants: [], visibility: 'private' });
      }
      setCaveQuery('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialize only when the modal opens
  }, [open]);

  const onOk = async () => {
    const values = await form.validateFields();
    const range = Array.isArray(values.dates) ? values.dates : [values.dates, null];
    const body: TripLogWrite = {
      title: values.title.trim(),
      tripDate: range[0]!.format('YYYY-MM-DD'),
      tripDateEnd: range[1] ? range[1].format('YYYY-MM-DD') : null,
      description: values.description?.trim() || null,
      locationText: values.locationText?.trim() || null,
      geom: trip?.geom ?? null,
      caveIds: values.caveIds,
      participants: [
        // Keep registered-user participants untouched; free-text ones come from the form.
        ...(trip?.participants.filter((p) => p.userId != null)
          .map((p) => ({ userId: p.userId, nameText: null })) ?? []),
        ...values.participants
          .filter((p) => p.nameText.trim().length > 0)
          .map((p) => ({ userId: null, nameText: p.nameText.trim() })),
      ],
      teamId: trip?.teamId ?? null,
      visibility: values.visibility,
    };

    try {
      const saved = trip
        ? await updateTrip.mutateAsync({ id: trip.id, body })
        : await createTrip.mutateAsync(body);
      message.success(t('common.saved'));
      onClose(saved.id);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      title={trip ? t('trips.edit') : t('trips.new')}
      open={open}
      onCancel={() => onClose()}
      onOk={() => void onOk()}
      confirmLoading={createTrip.isPending || updateTrip.isPending}
      width={640}
      destroyOnHidden
    >
      <Form<FormValues> form={form} layout="vertical">
        <Form.Item name="title" label={t('trips.titleField')} rules={[{ required: true }]}>
          <Input maxLength={255} />
        </Form.Item>
        <Flex gap={12}>
          <Form.Item name="dates" label={t('trips.date')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="visibility" label={t('features.visibility')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <Select
              options={(['private', 'team', 'authenticated', 'public'] as const).map((v) => ({
                value: v,
                label: t(`caves.visibilityValues.${v}`),
              }))}
            />
          </Form.Item>
        </Flex>
        <Form.Item name="locationText" label={t('trips.location')}>
          <Input maxLength={300} />
        </Form.Item>
        <Form.Item name="caveIds" label={t('trips.caves')}>
          <Select
            mode="multiple"
            showSearch
            filterOption={false}
            onSearch={setCaveQuery}
            placeholder={t('features.linkedCavePlaceholder')}
            options={caveOptions}
            notFoundContent={null}
          />
        </Form.Item>
        <Form.Item label={t('trips.participants')}>
          <Form.List name="participants">
            {(fields, { add, remove }) => (
              <Flex vertical gap={8}>
                {fields.map((field) => (
                  <Flex key={field.key} gap={8}>
                    <Form.Item
                      name={[field.name, 'nameText']}
                      noStyle
                      rules={[{ required: true, message: t('trips.participantRequired') }]}
                    >
                      <Input placeholder={t('trips.participantName')} maxLength={200} />
                    </Form.Item>
                    <Button icon={<DeleteOutlined />} onClick={() => remove(field.name)} />
                  </Flex>
                ))}
                <Button icon={<PlusOutlined />} onClick={() => add({ nameText: '' })} block>
                  {t('trips.addParticipant')}
                </Button>
              </Flex>
            )}
          </Form.List>
        </Form.Item>
        <Form.Item name="description" label={t('features.description')}>
          <Input.TextArea rows={4} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
