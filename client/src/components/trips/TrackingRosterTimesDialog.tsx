// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Alert, App, Checkbox, Flex, Form, Modal, Select, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useTakeTrackingRosterTimes,
  useTrackingRosterTimes,
  type TrackingRosterTimesPerson,
  type TrackingRosterTimesProblem,
} from '../../api/hooks.ts';
import { momentInSheetZone, ownSheetZone, sheetZoneNames } from './trackingCsvZones.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

/**
 * The sentence for each reason a person's times cannot be taken. Written out key by key, so that
 * a reason the server learns to give is a compile error here until it has words.
 */
const PROBLEM_KEYS: Record<TrackingRosterTimesProblem, string> = {
  notOnRoster: 'trips.tracking.rosterTimes.problem.notOnRoster',
  noEntry: 'trips.tracking.rosterTimes.problem.noEntry',
  noExit: 'trips.tracking.rosterTimes.problem.noExit',
  exitBeforeEntry: 'trips.tracking.rosterTimes.problem.exitBeforeEntry',
  entryOffTripDate: 'trips.tracking.rosterTimes.problem.entryOffTripDate',
  exitOffTripDate: 'trips.tracking.rosterTimes.problem.exitOffTripDate',
  clockChanged: 'trips.tracking.rosterTimes.problem.clockChanged',
};

/** A roster time as it is read: the server's "09:05:00" to the minute. */
const clock = (time: string | null | undefined) => (time ? time.slice(0, 5) : null);

/** Whether a person's times can be ticked at all: the roster can hold them and they would change it. */
const takeable = (person: TrackingRosterTimesPerson) => person.problem === null && person.changes;

/**
 * Whether a person starts ticked. Everybody whose times can be taken, except where taking them
 * would replace a time somebody typed: that one is the reviewer's to tick, on purpose.
 */
const tickedByDefault = (person: TrackingRosterTimesPerson) => takeable(person) && !person.overwrites;

/**
 * Entry and exit times for the trip's roster, taken from its tracking log — reviewed person by
 * person and written in one act.
 *
 * <b>Nothing here is applied for anybody.</b> The log knows the moment somebody was reported going
 * in; the roster holds whatever a person typed on the trip's form, and a typed time is a statement
 * somebody made. So the two are shown side by side, a time that would be replaced is marked and
 * left unticked, and what is written is what was on screen — the server is sent the pair each
 * person was reviewed with and what the roster held beside it, and refuses the lot if either the
 * log or the roster has since come to say something else.
 *
 * <b>The zone is stated, and chosen here.</b> A report is an instant and a roster time is a reading
 * of a clock with no zone of its own, so the same report is 09:00 or 06:00 depending on whose
 * clock is meant. The reviewer's own zone is offered first because it is nearly always the cave's;
 * the server answers with the zone it applied and that answer is what the line under the chooser
 * prints.
 */
export default function TrackingRosterTimesDialog({
  tripLogId,
  nameOf,
  onClose,
}: {
  tripLogId: string;
  /** What this reader calls a person — the tracking tab's own names, off-roster people included. */
  nameOf: (caverId: string) => string;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const ownZone = useMemo(ownSheetZone, []);
  const [zone, setZone] = useState(ownZone ?? 'UTC');
  /** Ticks the reviewer changed, by person. Anybody absent keeps the default for their row. */
  const [changed, setChanged] = useState<Record<string, boolean>>({});
  const times = useTrackingRosterTimes(tripLogId, zone);
  const take = useTakeTrackingRosterTimes();

  const zoneOptions = useMemo(
    () => [
      ...(ownZone
        ? [{ value: ownZone, label: t('trips.tracking.rosterTimes.zoneMine', { zone: ownZone }) }]
        : []),
      { value: 'UTC', label: 'UTC' },
      ...sheetZoneNames()
        .filter((name) => name !== ownZone && name !== 'UTC')
        .map((name) => ({ value: name, label: name })),
    ],
    [ownZone, t],
  );

  const people = times.data?.people ?? [];
  const appliedZone = times.data?.timeZone ?? null;
  const isTicked = (person: TrackingRosterTimesPerson) =>
    takeable(person) && (changed[person.caverId] ?? tickedByDefault(person));
  const ticked = people.filter(isTicked);

  const chooseZone = (next: string) => {
    // The ticks were made against other readings: on another zone's clocks a row may no longer be
    // takeable at all, so each starts again from what the new answer says.
    setChanged({});
    setZone(next);
  };

  const run = async () => {
    if (appliedZone === null) return;
    try {
      const written = await take.mutateAsync({
        tripLogId,
        timeZone: appliedZone,
        people: ticked.map((person) => ({
          caverId: person.caverId,
          entry: person.entry!,
          exit: person.exit!,
          // What the roster was shown to hold beside them, so that a time typed since this was
          // read is refused rather than replaced unseen.
          currentEntry: person.currentEntry,
          currentExit: person.currentExit,
          overwrites: person.overwrites,
        })),
      });
      if (written.rows > 0) {
        message.success(t('trips.tracking.rosterTimes.written', { count: written.people }));
      } else {
        message.info(t('trips.tracking.rosterTimes.nothingWritten'));
      }
      onClose();
    } catch (error) {
      message.error(trackingProblemMessage(error, t, t('trips.tracking.rosterTimes.failed')));
      // Whatever refused it — the log moved, the trip was saved by somebody else — what is on
      // screen is no longer what would be written. Read again, and start the ticks from that.
      setChanged({});
      void times.refetch();
    }
  };

  /** What the log says, as readings on the zone's clocks — or, beside a problem, as whole moments. */
  const fromLog = (person: TrackingRosterTimesPerson) => {
    if (person.enteredAt === null && person.exitedAt === null) {
      return <Typography.Text type="secondary">—</Typography.Text>;
    }
    // A day the roster cannot hold is the whole point of two of the problems, so the day is shown
    // with the time wherever something is wrong; where nothing is, the day is the trip's.
    const moment = (instant: string | null, reading: string | null | undefined) =>
      instant === null
        ? '—'
        : person.problem === null || person.problem === 'notOnRoster'
          ? (clock(reading) ?? '—')
          : momentInSheetZone(instant, i18n.language, appliedZone);
    return (
      <span data-testid={`roster-times-log-${person.caverId}`}>
        {moment(person.enteredAt, person.entry)} – {moment(person.exitedAt, person.exit)}
      </span>
    );
  };

  const onRosterNow = (person: TrackingRosterTimesPerson) => {
    if (!person.onRoster) return <Typography.Text type="secondary">—</Typography.Text>;
    if (person.currentEntry === null && person.currentExit === null) {
      return <Typography.Text type="secondary">{t('trips.tracking.rosterTimes.none')}</Typography.Text>;
    }
    return (
      <span data-testid={`roster-times-current-${person.caverId}`}>
        {clock(person.currentEntry) ?? '—'} – {clock(person.currentExit) ?? '—'}
      </span>
    );
  };

  /** Why a row is as it is: the reason it cannot be ticked, or what ticking it would do. */
  const remark = (person: TrackingRosterTimesPerson) => {
    if (person.problem !== null) {
      return (
        <Typography.Text type="secondary" data-testid={`roster-times-problem-${person.caverId}`}>
          {t(PROBLEM_KEYS[person.problem])}
        </Typography.Text>
      );
    }
    return (
      <Flex vertical gap={2}>
        {!person.changes && (
          <Typography.Text type="secondary" data-testid={`roster-times-same-${person.caverId}`}>
            {t('trips.tracking.rosterTimes.same')}
          </Typography.Text>
        )}
        {person.overwrites && (
          <Tag color="warning" data-testid={`roster-times-overwrites-${person.caverId}`}>
            {t('trips.tracking.rosterTimes.overwrites')}
          </Tag>
        )}
        {person.stays > 1 && (
          <Typography.Text type="secondary" data-testid={`roster-times-stays-${person.caverId}`}>
            {t('trips.tracking.rosterTimes.stays', { count: person.stays })}
          </Typography.Text>
        )}
      </Flex>
    );
  };

  return (
    <Modal
      open
      width={760}
      title={t('trips.tracking.rosterTimes.title')}
      okText={t('trips.tracking.rosterTimes.write', { count: ticked.length })}
      okButtonProps={{
        disabled: ticked.length === 0 || times.isFetching,
        'data-testid': 'roster-times-write',
      }}
      cancelText={t('common.cancel')}
      confirmLoading={take.isPending}
      onOk={() => void run()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Typography.Paragraph>{t('trips.tracking.rosterTimes.intro')}</Typography.Paragraph>
      <Form layout="vertical">
        <Form.Item
          label={t('trips.tracking.rosterTimes.zone')}
          htmlFor="roster-times-zone"
          extra={
            <>
              {t('trips.tracking.rosterTimes.zoneHelp')}
              {appliedZone !== null && (
                <>
                  {' '}
                  <span data-testid="roster-times-zone-applied">
                    {t('trips.tracking.rosterTimes.zoneApplied', { zone: appliedZone })}
                  </span>
                </>
              )}
            </>
          }
        >
          <Select<string>
            id="roster-times-zone"
            showSearch
            optionFilterProp="label"
            value={zone}
            onChange={chooseZone}
            data-testid="roster-times-zone"
            options={zoneOptions}
          />
        </Form.Item>
      </Form>
      {times.error ? (
        <Alert
          type="error"
          showIcon
          data-testid="roster-times-unavailable"
          title={trackingProblemMessage(times.error, t, t('trips.tracking.rosterTimes.unavailable'))}
        />
      ) : (
        <Table<TrackingRosterTimesPerson>
          size="small"
          rowKey="caverId"
          pagination={false}
          loading={times.isPending}
          dataSource={people}
          scroll={{ x: 'max-content' }}
          data-testid="roster-times-table"
          columns={[
            {
              title: t('trips.tracking.rosterTimes.take'),
              key: 'take',
              width: 56,
              render: (_value, person) => (
                <Checkbox
                  checked={isTicked(person)}
                  disabled={!takeable(person)}
                  aria-label={t('trips.tracking.rosterTimes.takeOf', { name: nameOf(person.caverId) })}
                  data-testid={`roster-times-take-${person.caverId}`}
                  onChange={(event) =>
                    setChanged((before) => ({ ...before, [person.caverId]: event.target.checked }))
                  }
                />
              ),
            },
            {
              title: t('trips.tracking.rosterTimes.person'),
              key: 'person',
              render: (_value, person) => nameOf(person.caverId),
            },
            {
              title: t('trips.tracking.rosterTimes.fromLog'),
              key: 'fromLog',
              render: (_value, person) => fromLog(person),
            },
            {
              title: t('trips.tracking.rosterTimes.onRoster'),
              key: 'onRoster',
              render: (_value, person) => onRosterNow(person),
            },
            { title: '', key: 'remark', render: (_value, person) => remark(person) },
          ]}
        />
      )}
    </Modal>
  );
}
