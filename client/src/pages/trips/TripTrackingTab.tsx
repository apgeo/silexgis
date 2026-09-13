// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EyeInvisibleOutlined } from '@ant-design/icons';
import { Alert, App, Popconfirm, Skeleton, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useDeleteTrackingEvent,
  useTripTracking,
  useTripTrackingEvents,
  type TrackingEvent,
  type TrackingParticipant,
  type TripLogInfo,
} from '../../api/hooks.ts';
import TrackingConfigCard from '../../components/trips/TrackingConfigCard.tsx';
import TrackingModelPanel from '../../components/trips/TrackingModelPanel.tsx';
import TrackingReportForm from '../../components/trips/TrackingReportForm.tsx';
import { trackingProblemMessage } from '../../components/trips/trackingProblems.ts';

/** How many reports the log shows without being asked for more. */
const RECENT_EVENTS = 20;

/**
 * Where the party is, as far as anybody above ground has been told.
 *
 * Three rules shape this surface and each of them is a defect if it is softened:
 *
 * **A position that was withheld is said to have been withheld.** Station names and depths are
 * location data and are kept from a reader without the right to place the cave — they arrive as
 * absences, exactly as they do for somebody nobody has reported yet. Drawing both as "unknown"
 * would tell a rescue co-ordinator that nobody knows where a caver is, when what is true is that
 * *they* are not being told. So where the answer says something was withheld, an absence against a
 * person who *has* been reported is drawn as a withholding.
 *
 * **A position is the latest report that claimed a place, and the last report is whatever came
 * last.** A note or an exit says something happened, not where — so the two columns disagree on
 * purpose, and a surface that folded them together would move somebody back to an entrance because
 * their last word was a radio check.
 *
 * **A wrong report is deleted, never edited.** What is on the log is what somebody said at a
 * moment; rewriting one in place would leave a record indistinguishable from one nobody corrected.
 */
export default function TripTrackingTab({
  trip,
  canEdit,
}: {
  trip: TripLogInfo;
  canEdit: boolean;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data, isPending, error, refetch } = useTripTracking(trip.id);
  const events = useTripTrackingEvents(trip.id, { pageSize: RECENT_EVENTS });
  const deleteEvent = useDeleteTrackingEvent();
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());

  if (isPending) {
    return <Skeleton active />;
  }

  if (error || !data) {
    return <Alert type="error" showIcon message={t('trips.tracking.unavailable')} />;
  }

  const names = new Map(trip.participants.map((person) => [person.caverId, person.name]));
  const teamTitles = new Map(data.teams.map((team) => [team.id, team.title]));
  const named = (caverId: string) => names.get(caverId) ?? t('trips.tracking.unknownCaver');
  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';

  /**
   * An absence where a position would be, said as strongly as it is actually known.
   *
   * `certain` is for the absence that can only be a withholding — a report whose kind always
   * carries a place, arriving without one. Everything else is the absence this reader genuinely
   * cannot resolve: a caver whose last word was a note may have been placed an hour ago and be
   * having that position kept back, or may never have been placed at all. Saying "this position
   * exists and you may not be told it" there would be a claim about the world made from a gap in
   * what was sent, which is the same mistake as drawing a withheld position as nobody knowing.
   */
  const withheldTag = (certain: boolean) => (
    <Tooltip
      title={t(
        certain
          ? 'trips.tracking.positionWithheldDetail'
          : 'trips.tracking.positionMaybeWithheldDetail',
      )}
    >
      <Tag
        icon={<EyeInvisibleOutlined />}
        data-testid={
          certain ? 'trip-tracking-position-withheld' : 'trip-tracking-position-maybe-withheld'
        }
      >
        {t(certain ? 'trips.tracking.positionWithheld' : 'trips.tracking.positionMaybeWithheld')}
      </Tag>
    </Tooltip>
  );

  const place = (stationName: string | null, depthM: number | null) =>
    [stationName, depthM === null ? null : t('trips.metres', { value: depthM })]
      .filter(Boolean)
      .join(' · ');

  /**
   * One person's last known place.
   *
   * Nothing at all is drawn for somebody nobody has reported yet — there is no position to
   * withhold from anybody, so saying "withheld" there would invent a secret.
   *
   * For everybody else the strength of the claim follows what the last report was. Going in,
   * coming out and a radio note carry no place at all, so they are the ordinary early-trip state
   * and the commonest reason a row has no position — calling those withheld would tell a
   * co-ordinator, five minutes after the party went in, that the page is hiding every position on
   * it. A station or a depth report always carries a place, so an empty one is a withholding and
   * can be nothing else, and that is the only case said as a fact.
   */
  const positionOf = (participant: TrackingParticipant) => {
    if (participant.stationName !== null || participant.depthM !== null) {
      return place(participant.stationName, participant.depthM);
    }
    if (participant.lastRecordedAt === null || !data.positionsWithheld) {
      return '—';
    }
    return withheldTag(participant.lastKind === 'atStation' || participant.lastKind === 'atDepth');
  };

  /**
   * What one report says about a place. A station report and a depth report always carry one, so
   * an empty one on either of those kinds is a withholding and nothing else — there is no second
   * reading of it, unlike the folded position above.
   */
  const eventPlace = (row: TrackingEvent) => {
    if (row.stationName !== null || row.depthEnteredM !== null) {
      return place(row.stationName, row.depthEnteredM);
    }
    return row.kind === 'atStation' || row.kind === 'atDepth' ? withheldTag(true) : '—';
  };

  const onDeleteEvent = async (eventId: string) => {
    try {
      await deleteEvent.mutateAsync({ tripLogId: trip.id, eventId });
      message.success(t('trips.tracking.eventDeleted'));
    } catch (failure) {
      message.error(trackingProblemMessage(failure, t));
    }
  };

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {/* Said once, at the top, as well as marked on every row it applies to: a reader who is
          being shown fewer positions than exist has to learn that from the page rather than from
          the shape of what is missing. */}
      {data.positionsWithheld && (
        <Alert
          type="info"
          showIcon
          message={t('trips.tracking.positionsWithheldTitle')}
          description={t('trips.tracking.positionsWithheldBody')}
          data-testid="trip-tracking-positions-withheld"
        />
      )}

      <TrackingConfigCard
        tripLogId={trip.id}
        caveIds={trip.caveIds}
        tracking={data}
        canEdit={canEdit}
        onStale={() => void refetch()}
      />

      <Table<TrackingParticipant>
        rowKey="caverId"
        size="small"
        pagination={false}
        dataSource={data.participants}
        data-testid="trip-tracking-participants"
        locale={{ emptyText: t('trips.tracking.participantsNone') }}
        rowSelection={
          canEdit
            ? {
                selectedRowKeys: [...selected],
                onChange: (keys) => setSelected(new Set(keys as string[])),
              }
            : undefined
        }
        columns={[
          {
            title: t('trips.tracking.columnCaver'),
            dataIndex: 'caverId',
            render: (caverId: string) => named(caverId),
          },
          {
            title: t('trips.tracking.columnTeam'),
            dataIndex: 'teamId',
            render: (teamId: string | null) =>
              teamId && teamTitles.has(teamId) ? <Tag>{teamTitles.get(teamId)}</Tag> : '—',
          },
          {
            title: t('trips.tracking.columnLastKind'),
            dataIndex: 'lastKind',
            render: (kind: TrackingParticipant['lastKind'], row) =>
              kind ? (
                <Tag color={row.out ? 'default' : 'blue'}>{t(`trips.tracking.kinds.${kind}`)}</Tag>
              ) : (
                '—'
              ),
          },
          {
            title: t('trips.tracking.columnLastRecordedAt'),
            dataIndex: 'lastRecordedAt',
            render: (value: string | null) => when(value),
          },
          {
            title: t('trips.tracking.columnPosition'),
            key: 'position',
            render: (_value, row) => positionOf(row),
          },
        ]}
      />

      {/* The same watch on the survey it is resolved against, for whoever knows the cave well
          enough for a place to mean more than its name. Drawn under the table rather than over it:
          a position that was withheld has no point to put on a model, so the reading that can say
          so in words comes first. */}
      <TrackingModelPanel
        tripLogId={trip.id}
        tracking={data}
        participants={trip.participants}
        events={events.data?.items}
      />

      {canEdit && (
        <TrackingReportForm
          tripLogId={trip.id}
          armed={data.state === 'armed'}
          caverIds={[...selected]}
          teams={data.teams}
          onRecorded={() => setSelected(new Set())}
        />
      )}

      <div>
        <Typography.Text strong>{t('trips.tracking.events')}</Typography.Text>
        <Typography.Paragraph type="secondary" style={{ marginTop: 4 }}>
          {t('trips.tracking.eventsCorrection')}
        </Typography.Paragraph>
        {/* A log that could not be read is not an empty log. Left to the table's own empty text,
            a refused or dropped request would say "nothing has been reported yet" under a
            participants table showing cavers at stations — a failure to learn something drawn as
            a fact about the world, on the one surface that is the record of what came in over the
            radio. The rows already held are kept on screen; what is said about them changes. */}
        {events.error != null && (
          <Alert
            type="error"
            showIcon
            message={t('trips.tracking.eventsUnavailable')}
            style={{ marginBottom: 8 }}
            data-testid="trip-tracking-events-unavailable"
          />
        )}
        <Table<TrackingEvent>
          rowKey="id"
          size="small"
          loading={events.isPending}
          pagination={false}
          dataSource={events.data?.items ?? []}
          data-testid="trip-tracking-events"
          locale={{
            emptyText:
              events.error != null
                ? t('trips.tracking.eventsUnavailable')
                : t('trips.tracking.eventsNone'),
          }}
          columns={[
            {
              title: t('trips.tracking.columnLastRecordedAt'),
              dataIndex: 'recordedAt',
              render: (value: string) => when(value),
            },
            {
              title: t('trips.tracking.columnCaver'),
              dataIndex: 'caverId',
              render: (caverId: string) => named(caverId),
            },
            {
              title: t('trips.tracking.columnLastKind'),
              dataIndex: 'kind',
              render: (kind: TrackingEvent['kind']) => (
                <Tag>{t(`trips.tracking.kinds.${kind}`)}</Tag>
              ),
            },
            {
              title: t('trips.tracking.columnPosition'),
              key: 'position',
              render: (_value, row) => eventPlace(row),
            },
            {
              title: t('trips.tracking.columnNote'),
              dataIndex: 'note',
              render: (note: string | null) => note ?? '—',
            },
            ...(canEdit
              ? [
                  {
                    title: '',
                    key: 'actions',
                    render: (_value: unknown, row: TrackingEvent) => (
                      <Popconfirm
                        title={t('trips.tracking.eventDeleteConfirm')}
                        onConfirm={() => void onDeleteEvent(row.id)}
                      >
                        <DeleteOutlined
                          role="button"
                          aria-label={t('trips.tracking.eventDelete')}
                          data-testid={`trip-tracking-event-delete-${row.id}`}
                        />
                      </Popconfirm>
                    ),
                  },
                ]
              : []),
          ]}
        />
      </div>
    </Space>
  );
}
