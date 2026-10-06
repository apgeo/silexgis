// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef } from 'react';
import {
  Alert,
  Checkbox,
  DatePicker,
  Empty,
  Flex,
  Segmented,
  Select,
  Tooltip,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  useCalendar,
  useCavingGroups,
  type CalendarEntry,
  type CalendarParams,
  type CalendarSource,
} from '../../api/hooks.ts';
import { EVENT_KINDS } from '../../components/events/eventKinds.ts';
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
import CalendarRecordTable from './CalendarRecordTable.tsx';
import CalendarWeekStrip from './CalendarWeekStrip.tsx';
import type { RecordGrouping } from './recordGrouping.ts';

const asDay = (value: Dayjs): string => value.format('YYYY-MM-DD');

/** No rows, as the one same list each time, so that "still none" is not a change to anything. */
const NoEntries: readonly CalendarEntry[] = [];

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
 * reading is on, which day a grid stands on, the order, the grouping and the map are where the
 * reader is standing rather than what they asked to see, and replace the entry they were made on.
 *
 * **Read as a list, the window is one run through the days.** The record and the agenda are the
 * same list drawn two ways, and the list is its own file: it puts the rows under headings when
 * asked to, draws a line where the run crosses today, and opens with that line a few rows down.
 * This page tells it what the rows in hand are the answer to, which is how it knows a new
 * question from the same one answered again — it moves itself to today for the first and leaves
 * the reader alone for the second.
 */
export default function CalendarPage() {
  const { t } = useTranslation();
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
  // The router's way of writing the address, as last handed over. Reached through here so that
  // the function below can stay the same function for as long as the page is mounted.
  const write = useRef(setSearchParams);
  useEffect(() => {
    write.current = setSearchParams;
  }, [setSearchParams]);

  /**
   * Every change goes through here, so there is one place that decides what the back button walks
   * through: a narrowing is worth a history entry and a rearrangement of the same rows is not.
   * A change that depends on what is already chosen is given as a function of it, so that it too
   * is worked out from the newest address rather than from the one this render was drawn with.
   *
   * It is one function for the life of the page. The list below is handed ways of changing the
   * address, and redraws every row it holds when it is handed different ones.
   */
  const apply = useCallback(
    (
      change: Partial<CalendarAddress> | ((current: CalendarAddress) => Partial<CalendarAddress>),
      replace = false,
    ) => {
      const current = latest.current;
      const next = { ...current, ...(typeof change === 'function' ? change(current) : change) };
      latest.current = next;
      write.current(writeCalendarAddress(next), { replace });
    },
    [],
  );

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

  const asking: CalendarParams = {
    from: asDay(asked[0]),
    to: asDay(asked[1]),
    source: sourceQuery(address),
    kind: kindQuery(address),
    cavingGroupId: address.cavingGroupId,
    mine: address.mine || undefined,
    includePast: address.showPast ? undefined : false,
    includeCancelled: address.showCancelled ? undefined : false,
    sort: address.sort,
  };
  const { data, isFetching, isError, isPlaceholderData } = useCalendar(asking, {
    enabled: !nothingChosen,
  });

  // What the rows in hand are the answer to, once they are. While a new question is in flight
  // the rows of the one before stay on screen, so that the record does not empty under whoever
  // is reading it — and for as long as they do, the rows are not this question's answer. The
  // list opens on today once per question, and must not do it over rows about to be replaced.
  const answered =
    data !== undefined && !isPlaceholderData && !nothingChosen ? JSON.stringify(asking) : null;

  // Opening a record, as one function for as long as the router gives the same way of going
  // anywhere: the list hands it to every row, and a new one on each redraw of this page would
  // have every row redrawn with it. The three beside it are the list's ways of changing the
  // address, kept the same for the same reason. The order and the grouping rearrange the same
  // rows, so neither is a step the back button should walk through; asking for today lets go of
  // the reader's own window, because the window the page opens on is the one built round today.
  const open = useCallback(
    (entry: CalendarEntry) => void navigate(detailPath[entry.source](entry.id)),
    [navigate],
  );
  const orderBy = useCallback((next: string | undefined) => apply({ sort: next }, true), [apply]);
  const groupBy = useCallback((next: RecordGrouping) => apply({ groupBy: next }, true), [apply]);
  const showToday = useCallback(() => apply({ from: undefined, to: undefined }), [apply]);

  // The caving groups by name, for the headings of a list grouped by them. The same directory
  // the group filter beside it offers, so a group named in one is named in the other.
  const groupNames = useMemo(
    () => new Map((cavingGroups ?? []).map((group) => [group.id, group.name])),
    [cavingGroups],
  );

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
          onOpen={open}
        />
      ) : view === 'week' ? (
        <CalendarWeekStrip
          value={panel}
          onChange={(next) => apply({ day: standOn(next) }, true)}
          entries={nothingChosen ? [] : (data?.entries ?? [])}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          onOpen={open}
        />
      ) : (
        <CalendarRecordTable
          view={view === 'agenda' ? 'agenda' : 'record'}
          entries={nothingChosen ? NoEntries : (data?.entries ?? NoEntries)}
          loading={isFetching && !data}
          emptyText={emptyText}
          from={asDay(asked[0])}
          to={asDay(asked[1])}
          sort={address.sort}
          onSortChange={orderBy}
          grouping={address.groupBy}
          onGroupingChange={groupBy}
          groupNames={groupNames}
          question={answered}
          onOpen={open}
          onShowToday={showToday}
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
