// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EyeInvisibleOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Checkbox,
  Popconfirm,
  Skeleton,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { ReactNode } from 'react';
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
import TrackingSharePanel from '../../components/trips/TrackingSharePanel.tsx';
import { trackingProblemMessage } from '../../components/trips/trackingProblems.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import './TripTrackingTab.css';

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
  // Chosen on the pointer, never on the width: a phone held in landscape has a desk's worth of
  // room across and still nothing on it that can hit a fourteen-pixel icon.
  const coarse = useCoarsePointer();
  // And chosen on the width, never on the pointer: how much room there is across is what decides
  // whether five columns can stand side by side, and a tablet with a trackpad has the room.
  const narrow = useIsMobile();
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

  const teamOf = (teamId: string | null) =>
    teamId && teamTitles.has(teamId) ? <Tag>{teamTitles.get(teamId)}</Tag> : '—';

  const kindOf = (kind: TrackingParticipant['lastKind'], out: boolean) =>
    kind ? <Tag color={out ? 'default' : 'blue'}>{t(`trips.tracking.kinds.${kind}`)}</Tag> : '—';

  /**
   * One field of a row, said as a label and an answer stacked under each other.
   *
   * <b>The column headings become these labels, and that is the whole of why this layout exists.</b>
   * Five columns of a watch do not fit across a phone, and a table told to keep its own overflow
   * keeps it by scrolling sideways — which is not the same as showing it. Measured at 412px with
   * both tables at rest: the participants' "Where" began 139px past the right edge and the log's
   * delete control 457px past it, so the answer to "where is everybody", and the only way to take a
   * wrong report off the log, were both reachable only by a horizontal drag inside a table that
   * gives no sign it has more to the right. Stacked, every field of every row is on screen at rest
   * and the page scrolls the way a page scrolls.
   */
  const fact = (label: string, value: ReactNode) => (
    <div className="tracking-stacked-fact" key={label}>
      {/* Said with the library's own secondary text rather than a colour of this stylesheet's own:
          the label has to recede from its answer in both themes, and a colour written here would
          have to be written twice and kept in step by hand. */}
      <Typography.Text type="secondary" className="tracking-stacked-label">
        {label}
      </Typography.Text>
      <span className="tracking-stacked-value">{value}</span>
    </div>
  );

  /**
   * How big everything somebody presses on this surface is drawn.
   *
   * `small` on a desk is what these controls have always been; `large` is where the forty pixels
   * come from, built by antd out of `controlHeightLG` — the touch target the rest of this
   * application uses. Asked for by size rather than set as a height, so the padding, line height
   * and icon inside each control are built for the size the control believes it is.
   */
  const controlSize: 'large' | 'small' = coarse ? 'large' : 'small';
  /** A confirmation is two more things to press, and they are pressed by the same finger. */
  const confirmSizes = { okButtonProps: { size: controlSize }, cancelButtonProps: { size: controlSize } };

  const onDeleteEvent = async (eventId: string) => {
    try {
      await deleteEvent.mutateAsync({ tripLogId: trip.id, eventId });
      message.success(t('trips.tracking.eventDeleted'));
    } catch (failure) {
      message.error(trackingProblemMessage(failure, t));
    }
  };

  /** The one control that takes a report off a log nothing can edit — the same one in both layouts. */
  const deleteControl = (row: TrackingEvent) => (
    <Popconfirm
      title={t('trips.tracking.eventDeleteConfirm')}
      onConfirm={() => void onDeleteEvent(row.id)}
      {...confirmSizes}
    >
      {/* A button rather than a bare icon. The icon on its own was 14px square — the smallest
          thing on the page, and the only way to take a wrong report off a log that cannot be
          edited. */}
      <Button
        type="text"
        size={controlSize}
        icon={<DeleteOutlined />}
        aria-label={t('trips.tracking.eventDelete')}
        data-testid={`trip-tracking-event-delete-${row.id}`}
      />
    </Popconfirm>
  );

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

      {/* Under the setup rather than at the foot of the tab: publishing is a decision about the
          watch as it is configured — which cave, which survey — and the card above it is where
          that configuration is read. A link handed out before a model is chosen is refused. */}
      <TrackingSharePanel tripLogId={trip.id} tripTitle={trip.title} canEdit={canEdit} />

      <div>
        {/* <b>Selecting everybody is a control of this page's own, and not the checkbox antd puts
            in the table's header.</b> Two reasons, and either would be enough on its own.

            The first is that on a phone there is no header to put it in: the rows are stacked, so
            the one act this page starts with would have had nowhere to live. The second is that
            antd's header checkbox cannot be asked to appear only once. A table told to keep its own
            overflow gets a measure row, and the measure row *clones every column's title* into a
            hidden cell — so the header's live "Select all" checkbox was minted a second time inside
            a `height: 0` box marked `aria-hidden`. Measured on a desk at 1600x1000: two checkboxes
            named "Select all", the second reachable by Tab, with no visible focus anywhere on the
            page, and Space on it silently selected the whole party on the surface that decides who
            a report is about. Said here instead, it is one control, it carries its own words, and
            it is large enough to press. */}
        {canEdit && data.participants.length > 0 && (
          <Checkbox
            className="tracking-select-all"
            checked={selected.size === data.participants.length}
            indeterminate={selected.size > 0 && selected.size < data.participants.length}
            onChange={(event) =>
              setSelected(
                event.target.checked
                  ? new Set(data.participants.map((person) => person.caverId))
                  : new Set(),
              )
            }
            data-testid="trip-tracking-select-all"
          >
            {t('trips.tracking.selectEverybody', { count: data.participants.length })}
          </Checkbox>
        )}

        {/* <b>Where there is room across, five columns; where there is not, one row per caver with
            its columns stacked inside it.</b> Wide, the table is told to keep its own overflow:
            without that the inner table simply bursts out of its card — measured at 538px inside a
            364px container — and because nothing clips it the whole page gains that width, so
            reading where somebody is and pressing Save became two views of the page 334px apart.
            Narrow, there is no sideways overflow to keep, because nothing stands side by side. */}
        <Table<TrackingParticipant>
          rowKey="caverId"
          size="small"
          pagination={false}
          showHeader={!narrow}
          scroll={narrow ? undefined : { x: 'max-content' }}
          className={`tracking-table${narrow ? ' tracking-table-stacked' : ''}`}
          dataSource={data.participants}
          data-testid="trip-tracking-participants"
          locale={{ emptyText: t('trips.tracking.participantsNone') }}
          rowSelection={
            canEdit
              ? {
                  selectedRowKeys: [...selected],
                  onChange: (keys) => setSelected(new Set(keys as string[])),
                  // Said above the table instead — see the note on that control.
                  hideSelectAll: true,
                  // The column is what the tap target is made of — see the stylesheet, which gives
                  // the label the whole cell. antd's own 32px would make that cell narrower than the
                  // finger it is for.
                  columnWidth: coarse ? 48 : undefined,
                }
              : undefined
          }
          columns={
            narrow
              ? [
                  {
                    title: t('trips.tracking.columnCaver'),
                    key: 'caver',
                    render: (_value, row) => (
                      <div className="tracking-stacked">
                        <Typography.Text strong>{named(row.caverId)}</Typography.Text>
                        <div className="tracking-stacked-facts">
                          {fact(t('trips.tracking.columnTeam'), teamOf(row.teamId))}
                          {fact(t('trips.tracking.columnLastKind'), kindOf(row.lastKind, row.out))}
                          {fact(t('trips.tracking.columnLastRecordedAt'), when(row.lastRecordedAt))}
                          {fact(t('trips.tracking.columnPosition'), positionOf(row))}
                        </div>
                      </div>
                    ),
                  },
                ]
              : [
                  {
                    title: t('trips.tracking.columnCaver'),
                    dataIndex: 'caverId',
                    render: (caverId: string) => named(caverId),
                  },
                  {
                    title: t('trips.tracking.columnTeam'),
                    dataIndex: 'teamId',
                    render: (teamId: string | null) => teamOf(teamId),
                  },
                  {
                    title: t('trips.tracking.columnLastKind'),
                    dataIndex: 'lastKind',
                    render: (kind: TrackingParticipant['lastKind'], row) => kindOf(kind, row.out),
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
                ]
          }
        />
      </div>

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
          showHeader={!narrow}
          scroll={narrow ? undefined : { x: 'max-content' }}
          className={`tracking-table${narrow ? ' tracking-table-stacked' : ''}`}
          dataSource={events.data?.items ?? []}
          data-testid="trip-tracking-events"
          locale={{
            emptyText:
              events.error != null
                ? t('trips.tracking.eventsUnavailable')
                : t('trips.tracking.eventsNone'),
          }}
          columns={
            narrow
              ? [
                  {
                    title: t('trips.tracking.columnLastRecordedAt'),
                    key: 'report',
                    render: (_value, row) => (
                      <div className="tracking-stacked">
                        <div className="tracking-stacked-head">
                          <Typography.Text strong>{when(row.recordedAt)}</Typography.Text>
                          {/* On the row it corrects rather than in a column of its own. That
                              column was the last of six, so on a phone it began 457px past the
                              right edge of a scroller 364px wide — the only way to take a wrong
                              report off a log nothing can edit, three screens sideways. */}
                          {canEdit && deleteControl(row)}
                        </div>
                        <div className="tracking-stacked-facts">
                          {fact(t('trips.tracking.columnCaver'), named(row.caverId))}
                          {fact(
                            t('trips.tracking.columnLastKind'),
                            <Tag>{t(`trips.tracking.kinds.${row.kind}`)}</Tag>,
                          )}
                          {fact(t('trips.tracking.columnPosition'), eventPlace(row))}
                          {fact(t('trips.tracking.columnNote'), row.note ?? '—')}
                        </div>
                      </div>
                    ),
                  },
                ]
              : [
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
                          render: (_value: unknown, row: TrackingEvent) => deleteControl(row),
                        },
                      ]
                    : []),
                ]
          }
        />
      </div>
    </Space>
  );
}
