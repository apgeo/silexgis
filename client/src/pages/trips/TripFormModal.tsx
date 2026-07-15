// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, DatePicker, Flex, Form, Input, Modal, Select, TimePicker } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCaveSearch,
  useCreateTripLog,
  useUpdateTripLog,
  type TripLogInfo,
  type TripLogWrite,
  type TripType,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface TripFormModalProps {
  open: boolean;
  trip: TripLogInfo | null; // null → create
  onClose: (savedId?: string) => void;
}

interface FormValues {
  title: string;
  type?: TripType | null;
  dates: [Dayjs, Dayjs | null] | Dayjs; // range when multi-day
  entryTime?: Dayjs | null;
  exitTime?: Dayjs | null;
  locationText?: string;
  organizingClub?: string;
  description?: string;
  results?: string;
  weather?: string;
  caveIds: string[];
  participants: { nameText: string }[];
  proposers: { nameText: string }[];
  visibility: TripLogInfo['visibility'];
}

const TRIP_TYPES: TripType[] = [
  'exploration',
  'survey',
  'maintenance',
  'training',
  'tourism',
  'rescue',
  'science',
  'other',
];

// Server times are wall-clock "HH:mm:ss"; parse via an ISO instant so no dayjs parse plugin is needed.
const parseTime = (value: string | null | undefined): Dayjs | null =>
  value ? dayjs(`1970-01-01T${value}`) : null;

/** A Form.List of free-text names, shared by the participants and proposers fields. */
function NameListField({ name, addLabel, placeholder }: { name: string; addLabel: string; placeholder: string }) {
  const { t } = useTranslation();
  return (
    <Form.List name={name}>
      {(fields, { add, remove }) => (
        <Flex vertical gap={8}>
          {fields.map((field) => (
            <Flex key={field.key} gap={8}>
              <Form.Item
                name={[field.name, 'nameText']}
                noStyle
                rules={[{ required: true, message: t('trips.participantRequired') }]}
              >
                <Input placeholder={placeholder} maxLength={200} />
              </Form.Item>
              <Button icon={<DeleteOutlined />} onClick={() => remove(field.name)} />
            </Flex>
          ))}
          <Button icon={<PlusOutlined />} onClick={() => add({ nameText: '' })} block>
            {addLabel}
          </Button>
        </Flex>
      )}
    </Form.List>
  );
}

/**
 * Trip editor. Participants and proposers are free-text names in the form; registered-user
 * links (for both) come with the teams/members UX later — existing user links on a trip are
 * preserved on update. Attachments, incl. the completed report document, are managed on the
 * trip detail page (the entity must exist before files can be attached).
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
          type: trip.type ?? undefined,
          dates: trip.tripDateEnd
            ? [dayjs(trip.tripDate), dayjs(trip.tripDateEnd)]
            : dayjs(trip.tripDate),
          entryTime: parseTime(trip.entryTime),
          exitTime: parseTime(trip.exitTime),
          locationText: trip.locationText ?? undefined,
          organizingClub: trip.organizingClub ?? undefined,
          description: trip.description ?? undefined,
          results: trip.results ?? undefined,
          weather: trip.weatherConditions ?? undefined,
          caveIds: [...trip.caveIds],
          participants: trip.participants
            .filter((p) => p.userId == null)
            .map((p) => ({ nameText: p.nameText ?? '' })),
          proposers: trip.proposers
            .filter((p) => p.userId == null)
            .map((p) => ({ nameText: p.nameText ?? '' })),
          visibility: trip.visibility,
        });
      } else {
        form.setFieldsValue({
          dates: dayjs(),
          caveIds: [],
          participants: [],
          proposers: [],
          visibility: 'private',
        });
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
      type: values.type ?? null,
      tripDate: range[0]!.format('YYYY-MM-DD'),
      tripDateEnd: range[1] ? range[1].format('YYYY-MM-DD') : null,
      entryTime: values.entryTime ? values.entryTime.format('HH:mm:ss') : null,
      exitTime: values.exitTime ? values.exitTime.format('HH:mm:ss') : null,
      description: values.description?.trim() || null,
      results: values.results?.trim() || null,
      weatherConditions: values.weather?.trim() || null,
      locationText: values.locationText?.trim() || null,
      organizingClub: values.organizingClub?.trim() || null,
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
      proposers: [
        ...(trip?.proposers.filter((p) => p.userId != null)
          .map((p) => ({ userId: p.userId, nameText: null })) ?? []),
        ...values.proposers
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
      width={720}
      destroyOnHidden
    >
      <Form<FormValues> form={form} layout="vertical">
        <Form.Item name="title" label={t('trips.titleField')} rules={[{ required: true }]}>
          <Input maxLength={255} />
        </Form.Item>
        <Flex gap={12}>
          <Form.Item name="type" label={t('trips.type')} style={{ flex: 1 }}>
            <Select
              allowClear
              placeholder={t('trips.type')}
              options={TRIP_TYPES.map((v) => ({ value: v, label: t(`trips.typeValues.${v}`) }))}
            />
          </Form.Item>
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
        <Flex gap={12}>
          <Form.Item name="entryTime" label={t('trips.entryTime')} style={{ flex: 1 }}>
            <TimePicker style={{ width: '100%' }} format="HH:mm" minuteStep={5} />
          </Form.Item>
          <Form.Item name="exitTime" label={t('trips.exitTime')} style={{ flex: 1 }}>
            <TimePicker style={{ width: '100%' }} format="HH:mm" minuteStep={5} />
          </Form.Item>
        </Flex>
        <Flex gap={12}>
          <Form.Item name="locationText" label={t('trips.location')} style={{ flex: 1 }}>
            <Input maxLength={300} />
          </Form.Item>
          <Form.Item name="organizingClub" label={t('trips.organizingClub')} style={{ flex: 1 }}>
            <Input maxLength={200} />
          </Form.Item>
        </Flex>
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
        <Flex gap={12} align="start">
          <Form.Item label={t('trips.participants')} style={{ flex: 1 }}>
            <NameListField
              name="participants"
              addLabel={t('trips.addParticipant')}
              placeholder={t('trips.participantName')}
            />
          </Form.Item>
          <Form.Item label={t('trips.proposers')} style={{ flex: 1 }}>
            <NameListField
              name="proposers"
              addLabel={t('trips.addProposer')}
              placeholder={t('trips.proposerName')}
            />
          </Form.Item>
        </Flex>
        <Form.Item name="results" label={t('trips.results')}>
          <Input.TextArea rows={3} maxLength={10000} />
        </Form.Item>
        <Form.Item name="weather" label={t('trips.weather')}>
          <Input maxLength={300} />
        </Form.Item>
        <Form.Item name="description" label={t('features.description')}>
          <Input.TextArea rows={4} maxLength={10000} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
