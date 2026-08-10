// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, DatePicker, Flex, Form, Input, Modal, Select, TimePicker } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useSearch,
  useCreateTripLog,
  useUpdateTripLog,
  type TripLogInfo,
  type TripLogWrite,
  type TripType,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import TripGeometryField from './TripGeometryField.tsx';
import { tripDateEndForWrite } from './tripDates.ts';
import type { TripGeometry } from './tripGeometry.ts';

const { RangePicker } = DatePicker;

interface TripFormModalProps {
  open: boolean;
  trip: TripLogInfo | null; // null → create
  onClose: (savedId?: string) => void;
}

interface FormValues {
  title: string;
  type?: TripType | null;
  // Always a range: a single-day trip picks the same day twice, and the equal end is dropped on write.
  dates: [Dayjs, Dayjs | null];
  entryTime?: Dayjs | null;
  exitTime?: Dayjs | null;
  locationText?: string;
  organizingCavingGroupId?: string;
  description?: string;
  results?: string;
  weather?: string;
  geom?: TripGeometry | null;
  caveIds: string[];
  participants: { caverId?: string; name: string }[];
  proposers: { caverId?: string; name: string }[];
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

// An untouched row still points at its person; a typed one carries a name for the server to add
// to the roster. Blank rows are dropped rather than creating someone with no name.
const toParticipants = (rows: { caverId?: string; name: string }[]) =>
  rows
    .filter((row) => row.caverId != null || row.name.trim().length > 0)
    .map((row) =>
      row.caverId != null
        ? { caverId: row.caverId, newCaverName: null }
        : { caverId: null, newCaverName: row.name.trim() },
    );

// Server times are wall-clock "HH:mm:ss"; parse via an ISO instant so no dayjs parse plugin is needed.
const parseTime = (value: string | null | undefined): Dayjs | null =>
  value ? dayjs(`1970-01-01T${value}`) : null;

/**
 * A Form.List of people, shared by the participants and proposers fields. A row that came from
 * the trip keeps the person it refers to; a row typed in names someone new, who is added to the
 * roster on save so later trips can pick them rather than retype them.
 */
function NameListField({ name, addLabel, placeholder }: { name: string; addLabel: string; placeholder: string }) {
  const { t } = useTranslation();
  return (
    <Form.List name={name}>
      {(fields, { add, remove }) => (
        <Flex vertical gap={8}>
          {fields.map((field) => (
            <Flex key={field.key} gap={8}>
              <Form.Item
                name={[field.name, 'name']}
                noStyle
                rules={[{ required: true, message: t('trips.participantRequired') }]}
              >
                <Input placeholder={placeholder} maxLength={200} />
              </Form.Item>
              <Button icon={<DeleteOutlined />} onClick={() => remove(field.name)} />
            </Flex>
          ))}
          <Button icon={<PlusOutlined />} onClick={() => add({ name: '' })} block>
            {addLabel}
          </Button>
        </Flex>
      )}
    </Form.List>
  );
}

/**
 * Trip editor. Participants and proposers are free-text names in the form; registered-user
 * links (for both) come with the caving-groups/members UX later — existing user links on a trip are
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
  // The map inside the form can only be built once the dialog's open transition has put the
  // content in the document — a map built against a container with no size renders nothing.
  const [shown, setShown] = useState(false);
  const { data: cavingGroups } = useCavingGroups();
  const debouncedCaveQuery = useDebouncedValue(caveQuery);
  // A trip is logged against caves, and the server's hit budget is shared across kinds —
  // ask for caves so commoner kinds cannot crowd them out of the answer.
  const { data: caveResults } = useSearch(debouncedCaveQuery, 'cave');
  // Options accumulate across searches so selected entries keep their labels.
  const [knownCaves, setKnownCaves] = useState<Map<string, string>>(new Map());
  useEffect(() => {
    if (caveResults) {
      setKnownCaves((previous) => {
        const next = new Map(previous);
        for (const hit of caveResults.features) {
          if (hit.name) {
            next.set(hit.id, hit.name);
          }
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
          dates: [dayjs(trip.tripDate), dayjs(trip.tripDateEnd ?? trip.tripDate)],
          entryTime: parseTime(trip.entryTime),
          exitTime: parseTime(trip.exitTime),
          locationText: trip.locationText ?? undefined,
          organizingCavingGroupId: trip.organizingCavingGroupId ?? undefined,
          description: trip.description ?? undefined,
          results: trip.results ?? undefined,
          weather: trip.weatherConditions ?? undefined,
          geom: trip.geom ?? null,
          caveIds: [...trip.caveIds],
          participants: trip.participants.map((p) => ({ caverId: p.caverId, name: p.name })),
          proposers: trip.proposers.map((p) => ({ caverId: p.caverId, name: p.name })),
          visibility: trip.visibility,
        });
      } else {
        form.setFieldsValue({
          // Today, both ends: a trip logged without touching the date control is a day trip today,
          // and the field is a range, so a bare day would leave it failing its own required rule.
          dates: [dayjs(), dayjs()],
          geom: null,
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
    const [start, end] = values.dates;
    const tripDate = start.format('YYYY-MM-DD');
    const body: TripLogWrite = {
      title: values.title.trim(),
      type: values.type ?? null,
      tripDate,
      tripDateEnd: tripDateEndForWrite(tripDate, end ? end.format('YYYY-MM-DD') : null),
      entryTime: values.entryTime ? values.entryTime.format('HH:mm:ss') : null,
      exitTime: values.exitTime ? values.exitTime.format('HH:mm:ss') : null,
      description: values.description?.trim() || null,
      results: values.results?.trim() || null,
      weatherConditions: values.weather?.trim() || null,
      locationText: values.locationText?.trim() || null,
      organizingCavingGroupId: values.organizingCavingGroupId ?? null,
      geom: values.geom ?? null,
      caveIds: values.caveIds,
      participants: toParticipants(values.participants),
      proposers: toParticipants(values.proposers),
      cavingGroupId: trip?.cavingGroupId ?? null,
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
      afterOpenChange={setShown}
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
          {/* Wider than its neighbours: two dates and a separator do not fit an equal third. */}
          <Form.Item name="dates" label={t('trips.dates')} rules={[{ required: true }]} style={{ flex: 2 }}>
            <RangePicker style={{ width: '100%' }} allowClear={false} />
          </Form.Item>
          <Form.Item name="visibility" label={t('features.visibility')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <Select
              options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
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
          <Form.Item
            name="organizingCavingGroupId"
            label={t('trips.organizingCavingGroup')}
            style={{ flex: 1 }}
          >
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              options={(cavingGroups ?? []).map((group) => ({ value: group.id, label: group.name }))}
            />
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
        <Form.Item name="geom" label={t('trips.geometry')}>
          <TripGeometryField active={shown} height={260} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
