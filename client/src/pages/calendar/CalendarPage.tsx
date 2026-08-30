// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Checkbox, DatePicker, Empty, Flex, Segmented, Select, Table, Tag, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { SorterResult } from 'antd/es/table/interface';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useCalendar,
  useCavingGroups,
  type CalendarEntry,
  type CalendarSource,
} from '../../api/hooks.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
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

/** Every family the answer can hold, so "all of them" can be told from "some of them". */
const CALENDAR_SOURCES = Object.keys(detailPath) as CalendarSource[];

/**
 * The ways the same window of days may be read, offered in the order they narrow: the whole
 * record, then a year, a month and a week of it, then the agenda.
 *
 * The record is the list — sortable, paged, and the only one that carries a column of each thing.
 * The two grids and the week strip are the same rows laid out as the days they fall on. The
 * agenda is the same list again with each row drawn as one entry rather than as a set of cells,
 * which is what a reader wants who is reading forwards rather than looking something up.
 */
const VIEWS = ['record', 'month', 'week', 'year', 'agenda'] as const;
type CalendarView = (typeof VIEWS)[number];

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
 * The club's dated records over a window of days — every trip and every camp the reader may open,
 * read as one list.
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
  const [view, setView] = useState<CalendarView>('record');
  const [panel, setPanel] = useState<Dayjs>(() => dayjs());
  const [range, setRange] = useState<[Dayjs, Dayjs]>(openingWindow);
  const [showTrips, setShowTrips] = useState(true);
  const [showOther, setShowOther] = useState(true);
  const [showPast, setShowPast] = useState(true);
  const [showCancelled, setShowCancelled] = useState(true);
  const [mine, setMine] = useState(false);
  const [cavingGroupId, setCavingGroupId] = useState<string | undefined>(undefined);
  const [sort, setSort] = useState<string | undefined>(undefined);
  // On, because a calendar that has to be asked for its map is a calendar whose map nobody
  // finds. It is a toggle rather than a fixture so a reader working down a long record can put
  // the tiles away.
  const [showMap, setShowMap] = useState(true);
  const { data: cavingGroups } = useCavingGroups();

  // Two toggles over three families of record, so "the rest" names more than one family and the
  // narrowing is written as the list of families wanted rather than as a single word. Wanting all
  // of them is the absence of the parameter; wanting none is not a question anybody can ask, and
  // rather than sending a request that could only come back empty the page says so and asks
  // nothing.
  const wanted: CalendarSource[] = [
    ...(showTrips ? (['tripLog'] as const) : []),
    ...(showOther ? (['expedition', 'event'] as const) : []),
  ];
  const source = wanted.length > 0 && wanted.length < CALENDAR_SOURCES.length
    ? wanted.join(',')
    : undefined;
  const nothingChosen = !showTrips && !showOther;

  const asked = windowFor(view, panel, range);

  const { data, isFetching, isError } = useCalendar(
    {
      from: asDay(asked[0]),
      to: asDay(asked[1]),
      source,
      cavingGroupId,
      mine: mine || undefined,
      includePast: showPast ? undefined : false,
      includeCancelled: showCancelled ? undefined : false,
      sort,
    },
    { enabled: !nothingChosen },
  );

  const onTableChange = (
    _pagination: unknown,
    _filters: unknown,
    sorter: SorterResult<CalendarEntry> | SorterResult<CalendarEntry>[],
  ) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.field ? sortableFields[String(single.field)] : undefined;
    setSort(field && single.order ? `${single.order === 'descend' ? '-' : ''}${field}` : undefined);
  };

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

  const isNarrowed =
    !showTrips || !showOther || !showPast || !showCancelled || mine || cavingGroupId !== undefined;

  // Four situations and four sentences, because only one of them is a fact about the calendar.
  // "Nothing is happening in these days" is a claim: a request that never answered has not earned
  // it, a toggle that excluded everything has not either, and neither has a pair of toggles that
  // between them asked for no kind of record at all.
  const emptyText = nothingChosen
    ? t('calendar.emptyNoKind')
    : isError
      ? t('calendar.failed')
      : isNarrowed
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
          onChange={setView}
          options={VIEWS.map((name) => ({ value: name, label: t(`calendar.views.${name}`) }))}
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
                setRange([next[0], next[1]]);
              }
            }}
          />
        ) : null}
        <Checkbox
          data-testid="calendar-toggle-past"
          checked={showPast}
          onChange={(e) => setShowPast(e.target.checked)}
        >
          {t('calendar.togglePast')}
        </Checkbox>
        <Checkbox
          data-testid="calendar-toggle-trips"
          checked={showTrips}
          onChange={(e) => setShowTrips(e.target.checked)}
        >
          {t('calendar.toggleTrips')}
        </Checkbox>
        <Checkbox
          data-testid="calendar-toggle-other"
          checked={showOther}
          onChange={(e) => setShowOther(e.target.checked)}
        >
          {t('calendar.toggleOther')}
        </Checkbox>
        <Checkbox
          data-testid="calendar-toggle-cancelled"
          checked={showCancelled}
          onChange={(e) => setShowCancelled(e.target.checked)}
        >
          {t('calendar.toggleCancelled')}
        </Checkbox>
        <Checkbox
          data-testid="calendar-toggle-mine"
          checked={mine}
          onChange={(e) => setMine(e.target.checked)}
        >
          {t('calendar.toggleMine')}
        </Checkbox>
        <Checkbox
          data-testid="calendar-toggle-map"
          checked={showMap}
          onChange={(e) => setShowMap(e.target.checked)}
        >
          {t('calendar.mapToggle')}
        </Checkbox>
        <Select<string | undefined>
          allowClear
          placeholder={t('calendar.groupFilter')}
          style={{ minWidth: 200 }}
          data-testid="calendar-group-filter"
          value={cavingGroupId}
          onChange={(value) => setCavingGroupId(value)}
          options={(cavingGroups ?? []).map((group) => ({ value: group.id, label: group.name }))}
        />
      </Flex>
      {data && data.omitted > 0 ? (
        // Said out loud, because an answer quietly short of its last rows reads exactly like a
        // complete one, and a record of a month missing days without saying so is worse than one
        // that refuses.
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="calendar-omitted"
          message={t('calendar.omitted', { count: data.omitted })}
        />
      ) : null}
      {!isList(view) ? (
        nothingChosen || (data?.entries.length ?? 0) === 0 ? (
          // A grid of empty cells cannot say why it is empty — whether nothing was asked for,
          // whether the read failed, or whether these really are days with nothing on them — so
          // the sentence that can say it is drawn above the grid rather than instead of it.
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
          onPanelChange={(next, nextMode) => {
            setPanel(next);
            setView(nextMode);
          }}
          entries={nothingChosen ? [] : (data?.entries ?? [])}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          onOpen={(entry) => void navigate(detailPath[entry.source](entry.id))}
        />
      ) : view === 'week' ? (
        <CalendarWeekStrip
          value={panel}
          onChange={setPanel}
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
            defaultSortOrder: 'ascend',
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
          { title: t('calendar.what'), dataIndex: 'title', sorter: true },
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
      {showMap ? (
        <CalendarMapPane
          entries={nothingChosen ? [] : (data?.entries ?? [])}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          active={showMap}
        />
      ) : null}
    </div>
  );
}
