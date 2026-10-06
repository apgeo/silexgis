// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef } from 'react';
import {
  Alert,
  Checkbox,
  DatePicker,
  Empty,
  Flex,
  Segmented,
  Select,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { SorterResult } from 'antd/es/table/interface';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  useCalendar,
  useCavingGroups,
  type CalendarEntry,
  type CalendarSource,
} from '../../api/hooks.ts';
import { EVENT_KINDS } from '../../components/events/eventKinds.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import {
  CALENDAR_SOURCES,
  CALENDAR_VIEWS,
  isCalendarNarrowed,
  kindQuery,
  readCalendarAddress,
  sourceQuery,
  wantsNoSource,
  wantsSource,
  withSource,
  writeCalendarAddress,
  type CalendarAddress,
  type CalendarView,
} from './calendarAddress.ts';
import CalendarGrid from './CalendarGrid.tsx';
import CalendarMapPane from './CalendarMapPane.tsx';
import CalendarWeekStrip from './CalendarWeekStrip.tsx';

const asDay = (value: Dayjs): string => value.format('YYYY-MM-DD');

/**
 * The days a reader is offered when they arrive with no window in mind: the month behind them and
 * the two ahead. A window is required by the answer — both ends of it — because a bounded window
 * is what lets several sources be merged exactly rather than approximately, so the page opens
 * with one rather than asking for everything and being refused.
 */
const openingWindow = (): [Dayjs, Dayjs] => [
  dayjs().subtract(1, 'month').startOf('month'),
  dayjs().add(2, 'month').endOf('month'),
];

/**
 * Which orders this page may ask for, keyed by the column that offers them. The words are the
 * server's and the ordering is done there: a page that sorted the rows it happens to be holding
 * would reorder one page of a merged answer, which is a different and wrong answer as soon as
 * there is more than one page.
 */
const sortableFields: Record<string, string> = { start: 'start', title: 'title' };

/** The order the server puts the record in when nothing is asked for. */
const DefaultSort = 'start';

/**
 * Where each family of row is read. Written as a map over the source union rather than as a
 * chain of tests, so a family added to the answer is a compile error here instead of falling
 * through to whichever address happened to be last — a row that silently opened another
 * record's page would be a dead end on the one surface that exists to lead somewhere.
 */
const detailPath: Record<CalendarSource, (id: string) => string> = {
  tripLog: (id) => `/trip-logs/${id}`,
  expedition: (id) => `/expeditions/${id}`,
  event: (id) => `/events/${id}`,
};

/** The views that are a list of rows rather than a layout of days. */
const isList = (view: CalendarView): boolean => view === 'record' || view === 'agenda';

/**
 * The window each view asks the server for. The record's window is the reader's own, picked and
 * shown; a grid's is decided by the panel it is showing, because a grid drawn over a window that
 * does not cover it would have empty cells that say "nothing happened" about days nobody asked
 * about — the one thing a calendar must not do. The month's window reaches a fortnight either side
 * so that the days of the neighbouring months the grid draws in its corners are answered too.
 */
function windowFor(view: CalendarView, panel: Dayjs, chosen: [Dayjs, Dayjs]): [Dayjs, Dayjs] {
  if (isList(view)) {
    return chosen;
  }
  if (view === 'year') {
    return [panel.startOf('year'), panel.endOf('year')];
  }
  if (view === 'week') {
    // Exactly the week the strip draws. The week a day falls in is the reader's language's
    // week — Monday-first for a Romanian reader, Sunday-first for an English one — and it is
    // settled once, for every date this application draws, by the date library's locale.
    return [panel.startOf('week'), panel.endOf('week')];
  }
  return [panel.startOf('month').subtract(14, 'day'), panel.endOf('month').add(14, 'day')];
}

/**
 * The club's dated records over a window of days — every trip, camp and event the reader may
 * open, read as one list.
 *
 * **What a row carries, and what it does not.** A row is what a thing is called, when it is, and
 * enough to click through to it. It names no cave and carries no count of caves: naming a cave is
 * a read of the cave, decided by a walk of its own, and a row that names none owes neither that
 * walk nor the "and some were held back" count that has to go with it. That absence is the reason
 * a whole quarter can be answered at once. The title is the row's own title as written — the
 * reader has already been shown the row, and rewriting its title for somebody entitled to open it
 * would withhold what its own page hands over.
 *
 * **What the toggles do.** Keeping something off this record is a display decision and never a
 * permission: everything narrowed away here stays exactly as readable as it was, on its own page
 * and on every list that showed it. Rows called off are shown by default and marked, because the
 * person who was going on one is exactly the reader who most needs to find it; narrowing them
 * away is offered rather than assumed.
 *
 * **A family has a toggle of its own, and a kind narrows only the family that has kinds.** Each of
 * the three families is turned on and off by itself, so any combination can be asked for. The
 * kinds are something only an event has: choosing some narrows the events to them and leaves the
 * trips and the camps exactly as their own toggles put them, so nothing leaves the calendar that
 * the reader did not turn off — and the page says so beside the control while that is in force,
 * because a filter that one family in three can answer owes the other two a sentence.
 *
 * **Everything chosen here lives in the address**, so a calendar narrowed and read a particular
 * way is a link: reloading it, opening it in a second tab or sending it to somebody shows the same
 * days the same way. A narrowing is a view somebody chose and is given a history entry; which
 * reading is on, which day a grid stands on, the order and the map are where the reader is
 * standing rather than what they asked to see, and replace the entry they were made on.
 *
 * **Two things this page is asked for and does not yet do, deferred rather than dropped.**
 * *Grouping rows by a column* — the table this application uses has column-header grouping and no
 * row grouping at all, and nothing here hand-rolls one, so it is a component to build rather than
 * a prop to set. *Opening scrolled to the pivot between what has happened and what has not*, with
 * a few rows of each either side — nothing in this application does infinite or anchored
 * scrolling, and paging by offset cannot express "centre on today". Both want a mechanism that
 * does not exist here yet; the window control and the past toggle are what stand in for them.
 */
export default function CalendarPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: cavingGroups } = useCavingGroups();

  // The address is the view, not a copy of it.
  const address = useMemo(() => readCalendarAddress(searchParams), [searchParams]);

  // What the next change is made to: the address as last drawn, or as last changed if that is
  // newer. The two differ because the router is free to defer the redraw that follows a
  // navigation, and on a busy machine a reader turns a second switch before the first has come
  // back. A change worked out from what the page last drew would then be worked out from before
  // the first one and would quietly undo it — so each change is made to the one before it, and
  // the router's own answer is taken up again whenever it arrives.
  const latest = useRef(address);
  useEffect(() => {
    latest.current = address;
  }, [address]);

  /**
   * Every change goes through here, so there is one place that decides what the back button walks
   * through: a narrowing is worth a history entry and a rearrangement of the same rows is not.
   * A change that depends on what is already chosen is given as a function of it, so that it too
   * is worked out from the newest address rather than from the one this render was drawn with.
   */
  const apply = (
    change: Partial<CalendarAddress> | ((current: CalendarAddress) => Partial<CalendarAddress>),
    replace = false,
  ) => {
    const current = latest.current;
    const next = { ...current, ...(typeof change === 'function' ? change(current) : change) };
    latest.current = next;
    setSearchParams(writeCalendarAddress(next), { replace });
  };

  const view = address.view;
  // A grid with no day named stands on today, read when the page is drawn rather than kept, so a
  // bare link to the week is this week whenever it is opened.
  const panel = address.day ? dayjs(address.day) : dayjs();
  const range: [Dayjs, Dayjs] =
    address.from && address.to ? [dayjs(address.from), dayjs(address.to)] : openingWindow();

  /** Where a grid moved to; today is written as the absence of a day, which is what it means. */
  const standOn = (next: Dayjs): string | undefined =>
    asDay(next) === asDay(dayjs()) ? undefined : asDay(next);

  // Wanting every family is the absence of the narrowing; wanting none is not a question anybody
  // can ask, and rather than sending a request that could only come back empty the page says so
  // and asks nothing.
  const nothingChosen = wantsNoSource(address);
  const eventsWanted = wantsSource(address, 'event');

  const asked = windowFor(view, panel, range);

  const { data, isFetching, isError } = useCalendar(
    {
      from: asDay(asked[0]),
      to: asDay(asked[1]),
      source: sourceQuery(address),
      kind: kindQuery(address),
      cavingGroupId: address.cavingGroupId,
      mine: address.mine || undefined,
      includePast: address.showPast ? undefined : false,
      includeCancelled: address.showCancelled ? undefined : false,
      sort: address.sort,
    },
    { enabled: !nothingChosen },
  );

  const sort = address.sort ?? DefaultSort;
  const orderOf = (key: string) =>
    sort === key ? ('ascend' as const) : sort === `-${key}` ? ('descend' as const) : null;

  const onTableChange = (
    _pagination: unknown,
    _filters: unknown,
    sorter: SorterResult<CalendarEntry> | SorterResult<CalendarEntry>[],
  ) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.field ? sortableFields[String(single.field)] : undefined;
    const word = field && single.order ? `${single.order === 'descend' ? '-' : ''}${field}` : undefined;
    apply({ sort: word === DefaultSort ? undefined : word }, true);
  };

  // One toggle to a family, written as a map over the family union so a family added to the
  // answer has to be given a toggle before this compiles.
  const familyToggles: Record<CalendarSource, { testId: string; label: string }> = {
    tripLog: { testId: 'calendar-toggle-trips', label: t('calendar.toggleTrips') },
    expedition: { testId: 'calendar-toggle-camps', label: t('calendar.toggleCamps') },
    event: { testId: 'calendar-toggle-events', label: t('calendar.toggleEvents') },
  };

  const kindOptions = [
    ...EVENT_KINDS.map((kind) => ({ value: kind as string, label: t(`events.kindValues.${kind}`) })),
    // A word a hand-written address carried that names no kind is still offered, as itself. The
    // server refuses it, and a control that did not show it would leave the reader with a
    // calendar that cannot be read and nothing on the page to let go of.
    ...address.kinds
      .filter((word) => !(EVENT_KINDS as readonly string[]).includes(word))
      .map((word) => ({ value: word, label: word })),
  ];
  // Said while it is true and not otherwise: a kind is in force, and a family it cannot narrow is
  // still on the calendar beside the events it did narrow.
  const kindLeavesOthers =
    kindQuery(address) !== undefined &&
    (wantsSource(address, 'tripLog') || wantsSource(address, 'expedition'));

  /**
   * A row read forwards rather than looked up: one entry to a line, its own words first and the
   * day it falls on under them.
   *
   * **It is the same table, given a different renderer for its rows.** The list and the agenda are
   * one answer read two ways, so they share the paging, the empty sentence that says why there is
   * nothing, the click that opens the record and the shortfall warning above them all; what
   * differs is whether a row is a set of cells to compare across or a line to read down. Written
   * as a second set of columns rather than as a second component, because a second component
   * would be a second place for all of that to drift out of step.
   *
   * The header is dropped with it: a single column of whole rows has nothing to head, and the
   * sorting a header offers is the record's job, which is one click away.
   */
  const agendaColumns: ColumnsType<CalendarEntry> = [
    {
      title: t('calendar.what'),
      key: 'agenda',
      render: (_, entry) => (
        <Flex vertical gap={2} data-testid="calendar-agenda-row">
          <Flex gap={8} align="center" wrap>
            <Typography.Text strong>{entry.title}</Typography.Text>
            <Tag>
              {entry.kind
                ? t(`events.kindValues.${entry.kind}`)
                : t(`calendar.sourceValues.${entry.source}`)}
            </Tag>
            <TripStateTag state={entry.state} />
            {entry.placement === 'putBack' ? (
              <Tag color="orange" data-testid="calendar-postponed">
                {t('calendar.postponed')}
              </Tag>
            ) : null}
          </Flex>
          <Typography.Text type="secondary">
            {formatTripDates(entry.start, entry.end, i18n.resolvedLanguage)}
            {/* The time is stated where the row states one, and nothing is filled in where it
                does not: most of these records carry no time of day at all, and a blank is the
                truthful reading of that rather than a gap somebody forgot. */}
            {entry.startTime ? ` · ${entry.startTime.slice(0, 5)}` : ''}
            {entry.startTime && entry.endTime ? `–${entry.endTime.slice(0, 5)}` : ''}
          </Typography.Text>
        </Flex>
      ),
    },
  ];

  // Four situations and four sentences, because only one of them is a fact about the calendar.
  // "Nothing is happening in these days" is a claim: a request that never answered has not earned
  // it, a toggle that excluded everything has not either, and neither has a set of toggles that
  // between them asked for no kind of record at all.
  const emptyText = nothingChosen
    ? t('calendar.emptyNoKind')
    : isError
      ? t('calendar.failed')
      : isCalendarNarrowed(address)
        ? t('calendar.emptyFiltered')
        : t('calendar.empty');

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('calendar.title')}
        </Typography.Title>
        <Segmented<CalendarView>
          data-testid="calendar-view"
          value={view}
          onChange={(next) => apply({ view: next }, true)}
          options={CALENDAR_VIEWS.map((name) => ({ value: name, label: t(`calendar.views.${name}`) }))}
        />
      </Flex>
      <Flex gap={12} wrap align="center" style={{ marginBottom: 12 }}>
        {/* The window is the reader's to pick only where it is theirs to pick: a grid is drawn
            over the month or the year it is showing, and offering a range control beside it would
            be offering a second answer to a question the grid has already answered. */}
        {isList(view) ? (
          <DatePicker.RangePicker
            allowClear={false}
            data-testid="calendar-window"
            value={range}
            onChange={(next) => {
              if (next?.[0] && next[1]) {
                apply({ from: asDay(next[0]), to: asDay(next[1]) });
              }
            }}
          />
        ) : null}
        <Checkbox
          data-testid="calendar-toggle-past"
          checked={address.showPast}
          onChange={(e) => apply({ showPast: e.target.checked })}
        >
          {t('calendar.togglePast')}
        </Checkbox>
        {CALENDAR_SOURCES.map((family) => (
          <Checkbox
            key={family}
            data-testid={familyToggles[family].testId}
            checked={wantsSource(address, family)}
            onChange={(e) =>
              apply((current) => ({ sources: withSource(current, family, e.target.checked) }))
            }
          >
            {familyToggles[family].label}
          </Checkbox>
        ))}
        {/* Beside the family it narrows. Disabled rather than hidden while that family is off: a
            control that came and went with a checkbox would move everything after it along the
            bar, and the reason it cannot be used is one worth being told. */}
        <Tooltip title={eventsWanted ? undefined : t('calendar.kindFilterOff')}>
          <Select<string[]>
            mode="multiple"
            allowClear
            showSearch
            optionFilterProp="label"
            maxTagCount="responsive"
            placeholder={t('calendar.kindFilter')}
            style={{ minWidth: 200, maxWidth: 320 }}
            data-testid="calendar-kind-filter"
            disabled={!eventsWanted}
            value={address.kinds}
            options={kindOptions}
            onChange={(values) => apply({ kinds: values })}
          />
        </Tooltip>
        <Checkbox
          data-testid="calendar-toggle-cancelled"
          checked={address.showCancelled}
          onChange={(e) => apply({ showCancelled: e.target.checked })}
        >
          {t('calendar.toggleCancelled')}
        </Checkbox>
        <Checkbox
          data-testid="calendar-toggle-mine"
          checked={address.mine}
          onChange={(e) => apply({ mine: e.target.checked })}
        >
          {t('calendar.toggleMine')}
        </Checkbox>
        {/* On, because a calendar that has to be asked for its map is a calendar whose map nobody
            finds. It is a toggle rather than a fixture so a reader working down a long record can
            put the tiles away. */}
        <Checkbox
          data-testid="calendar-toggle-map"
          checked={address.showMap}
          onChange={(e) => apply({ showMap: e.target.checked }, true)}
        >
          {t('calendar.mapToggle')}
        </Checkbox>
        <Select<string | undefined>
          allowClear
          placeholder={t('calendar.groupFilter')}
          style={{ minWidth: 200 }}
          data-testid="calendar-group-filter"
          value={address.cavingGroupId}
          onChange={(value) => apply({ cavingGroupId: value })}
          options={(cavingGroups ?? []).map((group) => ({ value: group.id, label: group.name }))}
        />
      </Flex>
      {kindLeavesOthers ? (
        <Typography.Paragraph type="secondary" data-testid="calendar-kind-note">
          {t('calendar.kindFilterNote')}
        </Typography.Paragraph>
      ) : null}
      {data && data.omitted > 0 ? (
        // Said out loud, because an answer quietly short of its last rows reads exactly like a
        // complete one, and a record of a month missing days without saying so is worse than one
        // that refuses.
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="calendar-omitted"
          title={t('calendar.omitted', { count: data.omitted })}
        />
      ) : null}
      {!isList(view) ? (
        nothingChosen || isError || data?.entries.length === 0 ? (
          // A grid of empty cells cannot say why it is empty — whether nothing was asked for,
          // whether the read failed, or whether these really are days with nothing on them — so
          // the sentence that can say it is drawn above the grid rather than instead of it. It
          // is drawn once there is something to say: while the first answer is still coming
          // there is neither an answer nor a failure, and "nothing is recorded in these days"
          // is a claim a question still in flight has not earned.
          <Empty
            data-testid="calendar-empty"
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={emptyText}
            style={{ marginBottom: 12 }}
          />
        ) : null
      ) : null}
      {view === 'month' || view === 'year' ? (
        <CalendarGrid
          mode={view}
          value={panel}
          onPanelChange={(next, nextMode) => apply({ day: standOn(next), view: nextMode }, true)}
          entries={nothingChosen ? [] : (data?.entries ?? [])}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          onOpen={(entry) => void navigate(detailPath[entry.source](entry.id))}
        />
      ) : view === 'week' ? (
        <CalendarWeekStrip
          value={panel}
          onChange={(next) => apply({ day: standOn(next) }, true)}
          entries={nothingChosen ? [] : (data?.entries ?? [])}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          onOpen={(entry) => void navigate(detailPath[entry.source](entry.id))}
        />
      ) : (
      <Table<CalendarEntry>
        scroll={{ x: 'max-content' }}
        rowKey={(row) => `${row.source}:${row.id}`}
        size="middle"
        loading={isFetching && !data}
        dataSource={nothingChosen ? [] : data?.entries}
        onChange={onTableChange}
        onRow={(row) => ({
          onClick: () => void navigate(detailPath[row.source](row.id)),
          style: { cursor: 'pointer' },
        })}
        locale={{
          emptyText: (
            <Empty
              data-testid="calendar-empty"
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={emptyText}
            />
          ),
        }}
        // The window bounds the answer, so the rows are all here and the paging is of what is
        // already in hand. Paging that spanned the sources would make "row forty of the combined
        // record" mean nothing, which is the one thing this record is for.
        pagination={{ pageSize: 25, showSizeChanger: true }}
        showHeader={view !== 'agenda'}
        columns={view === 'agenda' ? agendaColumns : [
          {
            title: t('calendar.when'),
            // Named by the field the row carries, not only by a key: the table reports which
            // column was sorted by that name, and a column identified only by a key reports
            // nothing — the header would draw an arrow for an order that was never asked for.
            dataIndex: 'start',
            key: 'start',
            width: 240,
            sorter: true,
            // Drawn from the address rather than kept by the table, so a link that asks for an
            // order shows its arrow on the column it is ordered by.
            sortOrder: orderOf('start'),
            render: (_, row) => (
              <Flex gap={8} align="center">
                <span>{formatTripDates(row.start, row.end, i18n.resolvedLanguage)}</span>
                {/* A date that has been put back is still worth listing and is not a date
                    anybody is going on, so it is marked here rather than beside the state:
                    what is wrong with the row is the day it still carries. */}
                {row.placement === 'putBack' ? (
                  <Tag color="orange" data-testid="calendar-postponed">
                    {t('calendar.postponed')}
                  </Tag>
                ) : null}
              </Flex>
            ),
          },
          {
            title: t('calendar.what'),
            dataIndex: 'title',
            key: 'title',
            sorter: true,
            sortOrder: orderOf('title'),
          },
          {
            title: t('calendar.kind'),
            dataIndex: 'source',
            width: 160,
            // The family for the two sources that are exactly one thing, and the row's own kind
            // for the one that is not: "Event" for both a permit deadline and a social evening
            // would make them the same row to somebody scanning a month, which is the reading
            // this column exists for.
            render: (value: CalendarSource, row) => (
              <Tag>
                {row.kind
                  ? t(`events.kindValues.${row.kind}`)
                  : t(`calendar.sourceValues.${value}`)}
              </Tag>
            ),
          },
          {
            title: t('calendar.state'),
            dataIndex: 'state',
            width: 150,
            render: (value: CalendarEntry['state']) => <TripStateTag state={value} />,
          },
        ]}
      />
      )}
      {/* One pane, under whichever way the same rows are being read, because it answers the same
          question about the same rows: it is handed what is on screen and matches the shapes to
          it, so it narrows with every toggle above without knowing what any of them mean. */}
      {address.showMap ? (
        <CalendarMapPane
          entries={nothingChosen ? [] : (data?.entries ?? [])}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          active={address.showMap}
        />
      ) : null}
    </div>
  );
}
