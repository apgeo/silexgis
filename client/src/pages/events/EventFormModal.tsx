// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App, DatePicker, Form, Input, Modal, Select, TimePicker } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useCreateEvent,
  useEventDefaults,
  useUpdateEvent,
  type EventInfo,
  type EventKind,
  type EventWrite,
  type Visibility,
} from '../../api/hooks.ts';
import { tripDateEndForWrite } from '../../components/trips/tripDates.ts';
import { EVENT_KINDS } from './eventKinds.ts';

interface Props {
  open: boolean;
  /** The event being edited, or nothing when one is being written for the first time. */
  event?: EventInfo;
  onClose: () => void;
  onSaved?: (event: EventInfo) => void;
}

interface FormValues {
  title: string;
  kind: EventKind;
  dates: [Dayjs, Dayjs | null] | null;
  startTime: Dayjs | null;
  endTime: Dayjs | null;
  place?: string;
  description?: string;
  visibility: Visibility;
  cavingGroupId?: string | null;
}

const VISIBILITIES: readonly Visibility[] = ['private', 'cavingGroup', 'authenticated', 'public'];

/**
 * Server dates are calendar days and server times are wall-clock strings, both without a zone.
 * Neither is ever handed to `dayjs()` bare: a day parsed as an instant is UTC midnight, which
 * renders as the previous day for every reader west of Greenwich, and a time has no day at all
 * to be read against. So a day is rebuilt from its parts and a time is anchored to a fixed one.
 */
const parseDay = (value: string | null | undefined): Dayjs | null => {
  if (!value) {
    return null;
  }
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return dayjs(new Date(year, month - 1, day));
};

const parseTime = (value: string | null | undefined): Dayjs | null =>
  value ? dayjs(`1970-01-01T${value}`) : null;

/**
 * Writing an event down, or changing one already written.
 *
 * The audience is not guessed here. A new event's form is opened showing the audience the server
 * says a create would apply, read from the same rule the write uses — so what the form displays
 * and what the create produces cannot drift apart.
 */
export default function EventFormModal({ open, event, onClose, onSaved }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const create = useCreateEvent();
  const update = useUpdateEvent();
  const { data: groups } = useCavingGroups();
  // Only asked for while a new event is being written: an event that exists already has an
  // audience of its own, and the default has nothing to say about it.
  const { data: defaults } = useEventDefaults(open && !event);

  // Reinitialised when the dialog opens and at no other moment. The answer naming the default
  // audience arrives after the dialog is already on screen, and a reset that ran when it landed
  // would clear the title, the dates and the times somebody had spent the intervening moment
  // typing — with nothing said about where they went.
  useEffect(() => {
    if (!open) {
      return;
    }
    form.resetFields();
    if (event) {
      form.setFieldsValue({
        title: event.title,
        kind: event.kind,
        dates: [parseDay(event.startDate)!, parseDay(event.endDate)],
        startTime: parseTime(event.startTime),
        endTime: parseTime(event.endTime),
        place: event.place ?? undefined,
        description: event.description ?? undefined,
        visibility: event.visibility,
        cavingGroupId: event.cavingGroupId ?? undefined,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialize only when the modal opens
  }, [open]);

  // The default audience, seeded on its own the moment it arrives and only into the two fields it
  // is about — never by rebuilding the form around it. It is not applied over a choice the author
  // has already made: a form that overwrote a deliberately-narrowed audience with the default a
  // moment later would be changing who can read something behind the author's back.
  useEffect(() => {
    if (!open || event || !defaults) {
      return;
    }
    const untouched = (['visibility', 'cavingGroupId'] as const).filter(
      (field) => !form.isFieldTouched(field),
    );
    if (untouched.includes('visibility')) {
      form.setFieldValue('visibility', defaults.visibility);
    }
    if (untouched.includes('cavingGroupId')) {
      form.setFieldValue('cavingGroupId', defaults.cavingGroupId ?? undefined);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- seeded when the dialog opens or the answer lands
  }, [open, event, defaults]);

  const submit = async () => {
    const values = await form.validateFields();
    const startDate = values.dates![0].format('YYYY-MM-DD');
    const endDate = values.dates?.[1] ? values.dates[1].format('YYYY-MM-DD') : null;
    const body: EventWrite = {
      title: values.title,
      kind: values.kind,
      startDate,
      // An end equal to the start is one day rather than a range of itself — the same
      // normalisation every other dated row on the calendar is written with.
      endDate: tripDateEndForWrite(startDate, endDate),
      startTime: values.startTime ? values.startTime.format('HH:mm:ss') : null,
      endTime: values.endTime ? values.endTime.format('HH:mm:ss') : null,
      place: values.place || null,
      description: values.description || null,
      visibility: values.visibility,
      cavingGroupId: values.cavingGroupId || null,
    };
    try {
      const saved = event
        ? await update.mutateAsync({ id: event.id, body })
        : await create.mutateAsync(body);
      message.success(t('common.saved'));
      onSaved?.(saved);
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      open={open}
      title={event ? t('events.edit') : t('events.create')}
      onCancel={onClose}
      onOk={() => void submit()}
      confirmLoading={create.isPending || update.isPending}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" data-testid="event-form">
        <Form.Item name="title" label={t('events.titleField')} rules={[{ required: true }]}>
          <Input maxLength={200} data-testid="event-title" />
        </Form.Item>
        <Form.Item
          name="kind"
          label={t('events.kind')}
          rules={[{ required: true }]}
          initialValue={'clubMeeting' satisfies EventKind}
        >
          <Select
            data-testid="event-kind"
            options={EVENT_KINDS.map((kind) => ({
              value: kind,
              label: t(`events.kindValues.${kind}`),
            }))}
          />
        </Form.Item>
        <Form.Item name="dates" label={t('events.dates')} rules={[{ required: true }]}>
          {/* The second day is optional: most events are one evening, and a range control has no
              other way to say so than by leaving the far end empty. */}
          <DatePicker.RangePicker allowEmpty={[false, true]} data-testid="event-dates" />
        </Form.Item>
        <Form.Item name="startTime" label={t('events.startTime')}>
          <TimePicker format="HH:mm" minuteStep={5} data-testid="event-start-time" />
        </Form.Item>
        <Form.Item name="endTime" label={t('events.endTime')}>
          {/* Not required to follow the start: a wall-clock time carries no day, so an evening
              that runs from 21:00 to 00:30 is ordinary rather than mistyped. */}
          <TimePicker format="HH:mm" minuteStep={5} data-testid="event-end-time" />
        </Form.Item>
        <Form.Item name="place" label={t('events.place')}>
          {/* Words, never a position: nobody navigates to a club night by coordinate. */}
          <Input maxLength={255} data-testid="event-place" />
        </Form.Item>
        <Form.Item name="description" label={t('events.description')}>
          <Input.TextArea rows={3} maxLength={4000} />
        </Form.Item>
        <Form.Item name="visibility" label={t('features.visibility')} rules={[{ required: true }]}>
          <Select
            data-testid="event-visibility"
            options={VISIBILITIES.map((value) => ({
              value,
              label: t(`caves.visibilityValues.${value}`),
            }))}
          />
        </Form.Item>
        <Form.Item name="cavingGroupId" label={t('events.cavingGroup')}>
          {/* A group-visible event must also name the group it is for: one that names none
              admits nobody, which is why the two are decided together. */}
          <Select
            allowClear
            data-testid="event-caving-group"
            options={(groups ?? []).map((group) => ({
              value: group.id,
              label: group.name,
            }))}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}
