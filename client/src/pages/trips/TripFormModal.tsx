// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { DeleteOutlined, EllipsisOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  DatePicker,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  TimePicker,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useCreateTripLog,
  useTripParticipantRoles,
  useTripTypes,
  useUpdateTripLog,
  type TripLogInfo,
  type TripLogWrite,
  type TripParticipantRole,
} from '../../api/hooks.ts';
import { participantRoleLabel } from '../../components/trips/participantRoles.ts';
import { caverReference } from '../../components/trips/roster.ts';
import { tripDateEndForWrite } from '../../components/trips/tripDates.ts';
import { tripTypeLabel } from '../../components/trips/tripTypes.ts';
import TripGeometryField from './TripGeometryField.tsx';
import type { TripGeometry } from './tripGeometry.ts';

const { RangePicker } = DatePicker;

interface TripFormModalProps {
  open: boolean;
  trip: TripLogInfo | null; // null → create
  onClose: (savedId?: string) => void;
}

interface FormValues {
  title: string;
  tripTypeId?: number | null;
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
  meetingGeom?: TripGeometry | null;
  participants: RosterRow[];
  proposers: RosterRow[];
  visibility: TripLogInfo['visibility'];
  maxParticipants?: number | null;
}

/**
 * A row of the roster as this form holds it. Only the name is drawn until somebody asks for
 * more; the job the person did, their own hours and the note against them ride along whether
 * they are on screen or not, because a write replaces the whole roster — a form that dropped
 * what it never displayed would retype the trip's leader as an ordinary attendee and throw away
 * the hours somebody recorded, for the sake of a corrected title.
 */
interface RosterRow {
  caverId?: string;
  /**
   * The name this row arrived under. A row still reading it still means the person it was
   * loaded for; typed over, it means whoever the new text names — see `toParticipants`.
   */
  loadedName?: string;
  name: string;
  roleId?: number | null;
  entryTime?: string | null;
  exitTime?: string | null;
  note?: string | null;
}

// Who each row names is decided by the one rule that decides it everywhere a person can be
// edited beside the text naming them, rather than restated here; the server matches a bare name
// against the roster or adds them to it. A row typed in names no job, and the server reads that
// as simply having been there.
const toParticipants = (rows: RosterRow[]) =>
  rows
    .filter((row) => row.name.trim().length > 0)
    .map((row) => ({
      ...caverReference(row),
      roleId: row.roleId ?? null,
      entryTime: row.entryTime ?? null,
      exitTime: row.exitTime ?? null,
      note: row.note ?? null,
    }));

const toRosterRow = (person: TripLogInfo['participants'][number]): RosterRow => ({
  caverId: person.caverId,
  loadedName: person.name,
  name: person.name,
  roleId: person.roleId,
  entryTime: person.entryTime,
  exitTime: person.exitTime,
  note: person.note,
});

// Server times are wall-clock "HH:mm:ss"; parse via an ISO instant so no dayjs parse plugin is needed.
const parseTime = (value: string | null | undefined): Dayjs | null =>
  value ? dayjs(`1970-01-01T${value}`) : null;

/**
 * A Form.List of people, shared by the participants and proposers fields. A row that came from
 * the trip keeps the person it refers to; a row typed in names someone new, who is added to the
 * roster on save so later trips can pick them rather than retype them.
 *
 * A row is one field, because most rows are a name and nothing else and recording an ordinary
 * trip must not get slower for the sake of the ones that are not. What else a row can say — the
 * job the person did, the hours they were down if they differ from the party's, a note — opens
 * on request, one row at a time, and stays out of the way of the rest.
 *
 * `roles` is absent for the proposers column: proposing is what that column means, so a role
 * picker there could only contradict the heading above it.
 */
function RosterField({
  name,
  addLabel,
  placeholder,
  roles,
}: {
  name: string;
  addLabel: string;
  placeholder: string;
  roles?: TripParticipantRole[];
}) {
  const { t } = useTranslation();
  // Which rows have been opened, by the list's own stable key rather than by index: removing a
  // row above renumbers the ones below it, and an index would leave the wrong row open.
  const [opened, setOpened] = useState<number[]>([]);
  const toggle = (key: number) =>
    setOpened((keys) => (keys.includes(key) ? keys.filter((k) => k !== key) : [...keys, key]));

  return (
    <Form.List name={name}>
      {(fields, { add, remove }) => (
        <Flex vertical gap={8}>
          {fields.map((field) => (
            <Flex key={field.key} vertical gap={4}>
              <Flex gap={8}>
                <Form.Item
                  name={[field.name, 'name']}
                  noStyle
                  rules={[{ required: true, message: t('trips.participantRequired') }]}
                >
                  <Input placeholder={placeholder} maxLength={200} />
                </Form.Item>
                <Button
                  icon={<EllipsisOutlined />}
                  title={t('trips.participantDetails')}
                  aria-label={t('trips.participantDetails')}
                  aria-expanded={opened.includes(field.key)}
                  onClick={() => toggle(field.key)}
                />
                <Button
                  icon={<DeleteOutlined />}
                  title={t('trips.removeParticipant')}
                  aria-label={t('trips.removeParticipant')}
                  onClick={() => remove(field.name)}
                />
              </Flex>
              {opened.includes(field.key) && (
                <Flex vertical gap={8} style={{ paddingInlineStart: 8 }} data-testid="roster-row-details">
                  {roles && (
                    <Form.Item name={[field.name, 'roleId']} noStyle>
                      <Select
                        allowClear
                        size="small"
                        placeholder={t('trips.participantRole')}
                        options={roles.map((role) => ({
                          value: role.id,
                          label: participantRoleLabel(role, t),
                        }))}
                      />
                    </Form.Item>
                  )}
                  <Flex gap={8}>
                    {/* The store keeps a wall-clock string, the picker wants an instant: the
                        conversion lives here so the roster's rows read the same whether they
                        were loaded from the trip or typed on this screen. */}
                    <Form.Item
                      name={[field.name, 'entryTime']}
                      noStyle
                      getValueProps={(value: string | null) => ({ value: parseTime(value) })}
                      normalize={(value: Dayjs | null) => (value ? value.format('HH:mm:ss') : null)}
                    >
                      <TimePicker
                        style={{ flex: 1 }}
                        size="small"
                        format="HH:mm"
                        minuteStep={5}
                        placeholder={t('trips.entryTime')}
                      />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, 'exitTime']}
                      noStyle
                      getValueProps={(value: string | null) => ({ value: parseTime(value) })}
                      normalize={(value: Dayjs | null) => (value ? value.format('HH:mm:ss') : null)}
                    >
                      <TimePicker
                        style={{ flex: 1 }}
                        size="small"
                        format="HH:mm"
                        minuteStep={5}
                        placeholder={t('trips.exitTime')}
                      />
                    </Form.Item>
                  </Flex>
                  <Form.Item name={[field.name, 'note']} noStyle>
                    <Input size="small" placeholder={t('trips.participantNote')} maxLength={500} />
                  </Form.Item>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {t('trips.participantTimesHint')}
                  </Typography.Text>
                </Flex>
              )}
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

  // The map inside the form can only be built once the dialog's open transition has put the
  // content in the document — a map built against a container with no size renders nothing.
  const [shown, setShown] = useState(false);
  const { data: cavingGroups } = useCavingGroups();
  const { data: tripTypes } = useTripTypes();
  const { data: participantRoles } = useTripParticipantRoles();
  // Proposing is a column of its own here, and the server reads a proposer row out of that
  // column. Offering the role again beside a name in the attendees column would let a row
  // contradict the heading above it and then move to the other column on the next read.
  const attendeeRoles = (participantRoles ?? []).filter((role) => role.code !== 'proposer');

  useEffect(() => {
    if (open) {
      form.resetFields();
      if (trip) {
        form.setFieldsValue({
          title: trip.title,
          tripTypeId: trip.tripTypeId ?? undefined,
          dates: [dayjs(trip.tripDate), dayjs(trip.tripDateEnd ?? trip.tripDate)],
          entryTime: parseTime(trip.entryTime),
          exitTime: parseTime(trip.exitTime),
          locationText: trip.locationText ?? undefined,
          organizingCavingGroupId: trip.organizingCavingGroupId ?? undefined,
          description: trip.description ?? undefined,
          results: trip.results ?? undefined,
          weather: trip.weatherConditions ?? undefined,
          geom: trip.geom ?? null,
          meetingGeom: trip.meetingGeom ?? null,
          participants: trip.participants.map(toRosterRow),
          proposers: trip.proposers.map(toRosterRow),
          visibility: trip.visibility,
          maxParticipants: trip.maxParticipants ?? undefined,
        });
      } else {
        form.setFieldsValue({
          // Today, both ends: a trip logged without touching the date control is a day trip today,
          // and the field is a range, so a bare day would leave it failing its own required rule.
          dates: [dayjs(), dayjs()],
          geom: null,
          meetingGeom: null,
          participants: [],
          proposers: [],
          visibility: 'private',
        });
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialize only when the modal opens
  }, [open]);

  const onOk = async () => {
    const values = await form.validateFields();
    // Validation answers with the fields it validated, and a roster row carries more than the
    // one field the form draws for it — the job, the times, the note. Read those from the form's
    // own store, or every save would send back a row stripped of everything that was not on
    // screen, and the write replaces the whole roster with what it is sent.
    const stored = form.getFieldsValue(true) as FormValues;
    const [start, end] = values.dates;
    const tripDate = start.format('YYYY-MM-DD');
    const body: TripLogWrite = {
      title: values.title.trim(),
      tripTypeId: values.tripTypeId ?? null,
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
      meetingGeom: values.meetingGeom ?? null,
      // Not a cleared list — no list at all. Which caves the trip is about is recorded on its
      // page, role by role, and this form must not be able to undo that by saving a title.
      caveIds: null,
      participants: toParticipants(stored.participants),
      proposers: toParticipants(stored.proposers),
      cavingGroupId: trip?.cavingGroupId ?? null,
      visibility: values.visibility,
      // Carried through untouched. This form does not offer the measured facts, and a write
      // sets every one of them, so sending blanks here would unmeasure a trip whose title
      // somebody corrected — and sending false would quietly say nothing went wrong on a trip
      // where something did.
      depthReachedM: trip?.depthReachedM ?? null,
      lengthSurveyedM: trip?.lengthSurveyedM ?? null,
      surveyStations: trip?.surveyStations ?? null,
      ropeMetres: trip?.ropeMetres ?? null,
      hadIncident: trip?.hadIncident ?? false,
      // Not three cleared sections — no sections at all, the same reading `caveIds` above gets.
      // What a trip found, needed and learned is recorded in its own sections on the trip's
      // page, and a form that never showed them must not be able to empty them by saving a
      // title. Echoing the stored objects back instead would look equivalent and is not: it
      // would re-measure each of them against the purpose's schema as it now stands, so
      // correcting a title on an old report could fail on a section nobody opened.
      fieldData: null,
      logistics: null,
      safety: null,
      // How many the trip has room for, as this form now reads it. Empty is a trip with no
      // limit, which is a real answer and not a missing one — so it is sent as an explicit
      // absence, and clearing the box really does take the limit off.
      maxParticipants: values.maxParticipants ?? null,
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
          <Form.Item name="tripTypeId" label={t('trips.type')} style={{ flex: 1 }}>
            <Select
              allowClear
              placeholder={t('trips.type')}
              options={(tripTypes ?? []).map((type) => ({
                value: type.id,
                label: tripTypeLabel(type, t),
              }))}
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
          {/* How many places the trip has. Left empty for a trip that turns nobody away; a
              number is what makes the people past it a waiting list rather than a refusal —
              nobody is ever refused here, they stand in the order they answered in. */}
          <Form.Item
            name="maxParticipants"
            label={t('trips.maxParticipants')}
            tooltip={t('trips.maxParticipantsHelp')}
            style={{ flex: 1 }}
          >
            <InputNumber min={1} precision={0} style={{ width: '100%' }} data-testid="trip-max-participants" />
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
        {/* Where the trip went is recorded on the trip's page, role by role, so that what it
            did there is recorded with it. A plain list of caves here as well would be a second
            way to say the same thing, saying less, and the two would have to be kept in step by
            whoever happened to remember. This form leaves the caves alone entirely — it sends no
            list at all, which is what tells the server not to touch them. */}
        <Flex gap={12} align="start">
          <Form.Item label={t('trips.participants')} style={{ flex: 1 }}>
            <RosterField
              name="participants"
              addLabel={t('trips.addParticipant')}
              placeholder={t('trips.participantName')}
              roles={attendeeRoles}
            />
          </Form.Item>
          <Form.Item label={t('trips.proposers')} style={{ flex: 1 }}>
            <RosterField
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
        {/* Where the party gathers, drawn by the same control as the sketch above and carrying the
            same warning, because it is disclosed on the same terms: everybody who may read the
            trip is told it exactly. A club that draws the approach as well draws it here too —
            one shape, so there is no rule about which of two to believe. */}
        <Form.Item
          name="meetingGeom"
          label={t('trips.meetingGeometry')}
          tooltip={t('trips.meetingGeometryHint')}
        >
          <TripGeometryField
            active={shown}
            height={260}
            testId="trip-meeting-geometry"
            warningTitle={t('trips.meetingGeometryWarning')}
            warningDetail={t('trips.meetingGeometryWarningDetail')}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}
