// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import {
  Alert,
  App,
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Radio,
  Select,
  Switch,
  TimePicker,
} from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useCreateEvent,
  useEditEventSeriesFollowing,
  useEventDefaults,
  useUpdateEvent,
  type EventInfo,
  type EventKind,
  type EventRecurrenceFrequency,
  type EventWrite,
  type Visibility,
} from '../../api/hooks.ts';
import { tripDateEndForWrite } from '../../components/trips/tripDates.ts';
import { EVENT_KINDS } from './eventKinds.ts';
import { eventRefusalKey } from './eventRefusals.ts';

interface Props {
  open: boolean;
  /** The event being edited, or nothing when one is being written for the first time. */
  event?: EventInfo;
  onClose: () => void;
  onSaved?: (event: EventInfo) => void;
}

/**
 * What an edit of one occurrence of a repeating event reaches. Only ever offered on an event that
 * belongs to a run — an event standing on its own has one occurrence and nothing to choose.
 */
type EditScope = 'occurrence' | 'following';

interface FormValues {
  title: string;
  kind: EventKind;
  dates: [Dayjs, Dayjs | null] | null;
  startTime: Dayjs | null;
  endTime: Dayjs | null;
  place?: string;
  maxParticipants?: number | null;
  description?: string;
  visibility: Visibility;
  cavingGroupId?: string | null;
  scope?: EditScope;
  repeats?: boolean;
  recurrenceFrequency?: EventRecurrenceFrequency;
  recurrenceRule?: string;
  recurrenceCount?: number | null;
  recurrenceUntil?: Dayjs | null;
}

const VISIBILITIES: readonly Visibility[] = ['private', 'cavingGroup', 'authenticated', 'public'];

const FREQUENCIES: readonly EventRecurrenceFrequency[] = [
  'daily',
  'weekly',
  'fortnightly',
  'monthly',
];

/**
 * The most occurrences one request writes. Shown so that somebody typing a number is told the
 * ceiling before they are refused by it; the server holds the same figure and is the one that
 * enforces it, refusing a request past it outright rather than trimming it down to fit.
 */
const MAX_OCCURRENCES = 104;

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
  const editFollowing = useEditEventSeriesFollowing();
  // Only an occurrence of a run can be edited as a run. An event standing on its own has nothing
  // to choose between, so it is never asked.
  const inSeries = !!event?.seriesId;
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
        maxParticipants: event.maxParticipants ?? undefined,
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
    let values: FormValues;
    try {
      values = await form.validateFields();
    } catch {
      // The refusal is taken rather than left to travel. Every failure is already drawn against
      // the field it belongs to, so there is nothing further to say — but an unanswered rejection
      // is reported as a fault by the observers watching for them, and a form submitted a moment
      // too early is somebody typing, not a defect.
      return;
    }
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
      maxParticipants: values.maxParticipants ?? null,
      description: values.description || null,
      visibility: values.visibility,
      cavingGroupId: values.cavingGroupId || null,
    };
    // A repetition is read only when an event is written for the first time. From the moment the
    // run exists it is ordinary events, and each one is edited as itself — so the route that
    // changes one occurrence is never sent a repetition, and refuses one if it is.
    if (!event && values.repeats) {
      body.recurrence = {
        frequency: values.recurrenceFrequency,
        rule: values.recurrenceRule,
        count: values.recurrenceCount ?? null,
        until: values.recurrenceUntil ? values.recurrenceUntil.format('YYYY-MM-DD') : null,
      };
    }

    try {
      if (event && values.scope === 'following') {
        // The whole of the rest of the run in one act. It answers with how many occurrences it
        // really reached and with the addressed one as it now stands, so the page behind this
        // dialog can redraw without a second read.
        const result = await editFollowing.mutateAsync({ id: event.id, body });
        message.success(t('events.seriesEdited', { count: result.changed }));
        onSaved?.(result.anchor);
        onClose();
        return;
      }
      const saved = event
        ? await update.mutateAsync({ id: event.id, body })
        : await create.mutateAsync(body);
      message.success(t('common.saved'));
      onSaved?.(saved);
      onClose();
    } catch (error) {
      // Every refusal somebody can reach from this form has words of its own, because "save
      // failed" would leave the author with no idea what to change — an event people have already
      // answered cannot be turned into a kind nobody is asked to, and a run asked for with no end
      // is refused entire rather than trimmed to whatever length somebody else would have guessed.
      message.error(t(eventRefusalKey(error, 'common.saveFailed')));
    }
  };

  return (
    <Modal
      open={open}
      title={event ? t('events.edit') : t('events.create')}
      onCancel={onClose}
      onOk={() => void submit()}
      confirmLoading={create.isPending || update.isPending || editFollowing.isPending}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" data-testid="event-form">
        {/* What an edit of one occurrence reaches. Asked before anything else on the form, because
            it changes the meaning of every field below it — and defaulted to the narrow answer:
            somebody correcting one evening's place must not silently rewrite the next two years,
            so the wider act is always the one deliberately chosen. */}
        {inSeries && (
          <Form.Item
            name="scope"
            label={t('events.scope')}
            initialValue={'occurrence' satisfies EditScope}
          >
            <Radio.Group data-testid="event-scope">
              <Radio.Button
                value={'occurrence' satisfies EditScope}
                data-testid="event-scope-occurrence"
              >
                {t('events.scopeOccurrence')}
              </Radio.Button>
              <Radio.Button
                value={'following' satisfies EditScope}
                data-testid="event-scope-following"
              >
                {t('events.scopeFollowing')}
              </Radio.Button>
            </Radio.Group>
          </Form.Item>
        )}
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
        {/* How many places the event has. Left empty for one that turns nobody away; a number is
            what makes the people past it a waiting list rather than a refusal — nobody is ever
            refused an answer here, they stand in the order they answered in. A kind nobody comes
            to takes no answers at all, so a number on one counts nothing. */}
        <Form.Item
          name="maxParticipants"
          label={t('events.maxParticipants')}
          tooltip={t('events.maxParticipantsHelp')}
        >
          <InputNumber min={1} precision={0} style={{ width: '100%' }} data-testid="event-max-participants" />
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
        {/* A repetition is offered only while an event is being written for the first time. It is
            not a property the event keeps: the run is worked out once, here, and written as the
            ordinary events it is — after which there is nothing left to switch off, only rows to
            edit and to call off. Editing one occurrence into a run would mean writing rows that
            are not the one being edited, so the route that changes one refuses a repetition. */}
        {!event && (
          <>
            <Form.Item
              name="repeats"
              label={t('events.repeats')}
              tooltip={t('events.repeatsHelp')}
              valuePropName="checked"
              initialValue={false}
            >
              <Switch data-testid="event-repeats" />
            </Form.Item>
            <Form.Item noStyle shouldUpdate={(prev, next) => prev.repeats !== next.repeats}>
              {({ getFieldValue }) =>
                getFieldValue('repeats') ? (
                  <>
                    <Form.Item
                      name="recurrenceFrequency"
                      label={t('events.repeatFrequency')}
                      rules={[{ required: true }]}
                      initialValue={'weekly' satisfies EventRecurrenceFrequency}
                    >
                      <Select
                        data-testid="event-repeat-frequency"
                        options={FREQUENCIES.map((value) => ({
                          value,
                          label: t(`events.repeatFrequencyValues.${value}`),
                        }))}
                      />
                    </Form.Item>
                    {/* The words and the repetition are two different things and are deliberately
                        two different fields. This sentence is for a person and nothing parses it;
                        the frequency above is stepped by once, at creation, and is not stored at
                        all. Keeping them apart is what stops the stored sentence becoming a rule
                        the application would later be expected to honour. */}
                    <Form.Item
                      name="recurrenceRule"
                      label={t('events.repeatRule')}
                      tooltip={t('events.repeatRuleHelp')}
                      rules={[{ required: true }]}
                    >
                      <Input maxLength={200} data-testid="event-repeat-rule" />
                    </Form.Item>
                    {/* Where it stops. One of the two is enough and both may be given, but a run
                        told neither is refused rather than written to some length nobody asked
                        for — which is why the pair is validated together rather than each on its
                        own. The server holds the same rule and is the one that enforces it. */}
                    <Form.Item
                      name="recurrenceCount"
                      label={t('events.repeatCount')}
                      tooltip={t('events.repeatCountHelp', { max: MAX_OCCURRENCES })}
                      dependencies={['recurrenceUntil']}
                      rules={[
                        ({ getFieldValue }) => ({
                          validator: (_rule, value) =>
                            value || getFieldValue('recurrenceUntil')
                              ? Promise.resolve()
                              : Promise.reject(new Error(t('events.repeatBoundRequired'))),
                        }),
                      ]}
                    >
                      <InputNumber
                        min={2}
                        max={MAX_OCCURRENCES}
                        precision={0}
                        style={{ width: '100%' }}
                        data-testid="event-repeat-count"
                      />
                    </Form.Item>
                    <Form.Item
                      name="recurrenceUntil"
                      label={t('events.repeatUntil')}
                      tooltip={t('events.repeatUntilHelp')}
                      dependencies={['recurrenceCount']}
                    >
                      <DatePicker style={{ width: '100%' }} data-testid="event-repeat-until" />
                    </Form.Item>
                    <Alert type="info" showIcon title={t('events.repeatsHelp')} />
                  </>
                ) : null
              }
            </Form.Item>
          </>
        )}
      </Form>
    </Modal>
  );
}
