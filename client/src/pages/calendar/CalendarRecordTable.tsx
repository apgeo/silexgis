// SPDX-License-Identifier: AGPL-3.0-or-later
import { memo, useCallback, useMemo, useState, type ReactNode } from 'react';
import { DownOutlined, RightOutlined } from '@ant-design/icons';
import { Button, Empty, Flex, Table, Tag, Typography, theme } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { SorterResult } from 'antd/es/table/interface';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { CalendarEntry, CalendarSource } from '../../api/hooks.ts';
import GroupBySelect from '../../components/GroupBySelect.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import { CALENDAR_SOURCES } from './calendarAddress.ts';
import { RECORD_GROUPINGS, type RecordGrouping } from './recordGrouping.ts';
import { chronologyOf, layoutRecord, type RecordGroup, type RecordRow } from './recordRows.ts';

/**
 * Which orders this list may ask for, keyed by the column that offers them. The words are the
 * server's and the ordering is done there: the rows here are a merged answer, cut off at a cap
 * that keeps its earliest days, and a list that reordered what it happened to be holding would
 * be arranging the wrong rows as soon as that cap had bitten.
 */
const sortableFields: Record<string, string> = { start: 'start', title: 'title' };

/** The order the server puts the record in when nothing is asked for. */
const DefaultSort = 'start';

const NothingFolded: ReadonlySet<string> = new Set();

/**
 * What a cell of an ordinary row asks of the table beyond its contents: nothing. One object,
 * handed back for every such cell, because the table redraws a cell whenever it is handed a
 * different one — and a fresh empty object for each is a different one every time. Frozen, so
 * that if the table ever wrote into it the mistake would be an error rather than every row of
 * the list quietly sharing somebody's span.
 */
const OrdinaryCell: Readonly<Record<string, never>> = Object.freeze({});

interface Props {
  /**
   * Which way a row is drawn: as a set of cells to compare across, or as one entry to read down.
   * They are one list read two ways, so everything else here — the headings, the empty sentence,
   * the click through to the record — is shared and cannot drift between them.
   */
  view: 'record' | 'agenda';
  /** The answer's rows, in the order the answer gave them. */
  entries: readonly CalendarEntry[];
  loading: boolean;
  /** Why there is nothing here, when there is nothing here. */
  emptyText: string;
  /** The order asked of the server, as the address carries it. */
  sort: string | undefined;
  onSortChange: (sort: string | undefined) => void;
  grouping: RecordGrouping;
  onGroupingChange: (grouping: RecordGrouping) => void;
  /** The names of the caving groups the reader can list, by identifier. */
  groupNames: ReadonlyMap<string, string>;
  onOpen: (entry: CalendarEntry) => void;
}

/**
 * The window's rows read as a list, optionally under headings.
 *
 * **One list, not pages.** The window bounds the answer, so every row is already here, and the
 * list draws all of them rather than dealing them out a page at a time. A heading is a row of
 * the list, and a page boundary would part a heading from the rows it counts.
 *
 * **Headings are rows.** The table this application uses can group column headers and cannot
 * group rows, so a heading is an ordinary row spanning every column, holding a button that folds
 * its rows away. Which rows sit under which heading is worked out beside this file, with nothing
 * drawn; this file only draws the answer.
 */
function CalendarRecordTable({
  view,
  entries,
  loading,
  emptyText,
  sort,
  onSortChange,
  grouping,
  onGroupingChange,
  groupNames,
  onOpen,
}: Props) {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const language = i18n.resolvedLanguage;

  // What has been folded away belongs to the grouping it was folded under: "October" means
  // nothing once the list is cut by kind. So a change of grouping starts with everything open,
  // and it is cleared here, while drawing, rather than after — a list drawn once with the old
  // folds under the new headings would fold away whichever of them happened to share a value.
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(NothingFolded);
  const [foldedUnder, setFoldedUnder] = useState(grouping);
  if (foldedUnder !== grouping) {
    setFoldedUnder(grouping);
    setCollapsed(NothingFolded);
  }
  const toggle = useCallback((value: string) => {
    setCollapsed((current) => {
      const next = new Set(current);
      if (!next.delete(value)) {
        next.add(value);
      }
      return next;
    });
  }, []);

  const layout = useMemo(
    () =>
      layoutRecord(entries, {
        grouping,
        order: chronologyOf(sort),
        collapsed,
        groupNames,
      }),
    [entries, grouping, sort, collapsed, groupNames],
  );

  const sorted = sort ?? DefaultSort;

  /**
   * The columns, made once for each thing they depend on and not once for each time the page is
   * drawn. The table redraws every cell of every row whenever it is handed new columns, and this
   * list holds the whole window rather than a page of it — so columns rebuilt on every redraw of
   * the page would make turning a switch somewhere else on it cost a redraw of every row.
   */
  const columns = useMemo<ColumnsType<RecordRow>>(() => {
    const groupLabel = (group: RecordGroup): string => {
      const { value } = group;
      switch (grouping) {
        case 'month':
          return dayjs(`${value}-01`).format('MMMM YYYY');
        case 'week': {
          // Named by the days it covers, as the week reading names a week.
          const first = dayjs(value);
          return `${first.format('D MMM')} – ${first.add(6, 'day').format('D MMM YYYY')}`;
        }
        case 'kind':
          return (CALENDAR_SOURCES as readonly string[]).includes(value)
            ? t(`calendar.sourceValues.${value as CalendarSource}`)
            : t(`events.kindValues.${value}`, { defaultValue: value });
        case 'state':
          return t(`trips.stateValues.${value}`, { defaultValue: value });
        case 'cavingGroup':
          // Three different things, and three different words for them: a row that names no
          // group, a group the reader can list, and a group whose rows they may read while the
          // group itself is not theirs to list.
          return value === ''
            ? t('calendar.grouping.unassigned')
            : (groupNames.get(value) ?? t('calendar.grouping.unlistedGroup'));
        default:
          return value;
      }
    };

    const heading = (row: Extract<RecordRow, { type: 'group' }>): ReactNode => (
      <Button
        type="text"
        size="small"
        // The arrow says the same thing `aria-expanded` does, so it is kept out of the name a
        // screen reader reads: the name is the heading and its count.
        icon={row.collapsed ? <RightOutlined aria-hidden /> : <DownOutlined aria-hidden />}
        aria-expanded={!row.collapsed}
        data-testid="calendar-group"
        data-group={row.group.value}
        onClick={() => toggle(row.group.value)}
      >
        {/* The count travels with the name, always: a heading without one says something was
            there, and a heading with one says how much — folded or not. */}
        <Typography.Text strong>{`${groupLabel(row.group)} (${row.group.entries.length})`}</Typography.Text>
      </Button>
    );

    /** What a line that is not a record draws, in the one cell it is given. */
    const across = (row: Extract<RecordRow, { type: 'group' }>): ReactNode => heading(row);

    const kindOf = (entry: CalendarEntry): string =>
      // The family for the two sources that are exactly one thing, and the row's own kind for
      // the one that is not: "Event" for both a permit deadline and a social evening would make
      // them the same row to somebody scanning a month, which is the reading this column
      // exists for.
      entry.kind ? t(`events.kindValues.${entry.kind}`) : t(`calendar.sourceValues.${entry.source}`);

    const postponed = (entry: CalendarEntry): ReactNode =>
      entry.placement === 'putBack' ? (
        <Tag color="orange" data-testid="calendar-postponed">
          {t('calendar.postponed')}
        </Tag>
      ) : null;

    const columnCount = view === 'agenda' ? 1 : 4;
    /**
     * A heading takes the whole width of the list: the first cell spans every column and the
     * others give theirs up.
     */
    const spanning = (first: boolean) => (row: RecordRow) =>
      row.type === 'entry'
        ? OrdinaryCell
        : {
            colSpan: first ? columnCount : 0,
            style: { paddingBlock: token.paddingXXS, background: token.colorFillQuaternary },
          };

    const orderOf = (key: string) =>
      sorted === key ? ('ascend' as const) : sorted === `-${key}` ? ('descend' as const) : null;

    if (view === 'agenda') {
      /**
       * A row read forwards rather than looked up: one entry to a line, its own words first and
       * the day it falls on under them. The header is dropped with the cells — a single column of
       * whole rows has nothing to head, and the sorting a header offers is the record's job, one
       * click away.
       */
      return [
        {
          title: t('calendar.what'),
          key: 'agenda',
          onCell: spanning(true),
          render: (_, row) =>
            row.type !== 'entry' ? (
              across(row)
            ) : (
              <Flex vertical gap={2} data-testid="calendar-agenda-row">
                <Flex gap={8} align="center" wrap>
                  <Typography.Text strong>{row.entry.title}</Typography.Text>
                  <Tag>{kindOf(row.entry)}</Tag>
                  <TripStateTag state={row.entry.state} />
                  {postponed(row.entry)}
                </Flex>
                <Typography.Text type="secondary">
                  {formatTripDates(row.entry.start, row.entry.end, language)}
                  {/* The time is stated where the row states one, and nothing is filled in where
                      it does not: most of these records carry no time of day at all, and a blank
                      is the truthful reading of that rather than a gap somebody forgot. */}
                  {row.entry.startTime ? ` · ${row.entry.startTime.slice(0, 5)}` : ''}
                  {row.entry.startTime && row.entry.endTime
                    ? `–${row.entry.endTime.slice(0, 5)}`
                    : ''}
                </Typography.Text>
              </Flex>
            ),
        },
      ];
    }

    return [
      {
        title: t('calendar.when'),
        // Named by the field the order is asked by, not only by a key: the table reports which
        // column was sorted by that name, and a column identified only by a key reports nothing
        // — the header would draw an arrow for an order that was never asked for.
        dataIndex: 'start',
        key: 'start',
        width: 240,
        sorter: true,
        // Drawn from the address rather than kept by the table, so a link that asks for an order
        // shows its arrow on the column it is ordered by.
        sortOrder: orderOf('start'),
        onCell: spanning(true),
        render: (_, row) =>
          row.type !== 'entry' ? (
            across(row)
          ) : (
            <Flex gap={8} align="center">
              <span>{formatTripDates(row.entry.start, row.entry.end, language)}</span>
              {/* A date that has been put back is still worth listing and is not a date anybody
                  is going on, so it is marked here rather than beside the state: what is wrong
                  with the row is the day it still carries. */}
              {postponed(row.entry)}
            </Flex>
          ),
      },
      {
        title: t('calendar.what'),
        dataIndex: 'title',
        key: 'title',
        sorter: true,
        sortOrder: orderOf('title'),
        onCell: spanning(false),
        render: (_, row) => (row.type === 'entry' ? row.entry.title : null),
      },
      {
        title: t('calendar.kind'),
        key: 'kind',
        width: 160,
        onCell: spanning(false),
        render: (_, row) => (row.type === 'entry' ? <Tag>{kindOf(row.entry)}</Tag> : null),
      },
      {
        title: t('calendar.state'),
        key: 'state',
        width: 150,
        onCell: spanning(false),
        render: (_, row) =>
          row.type === 'entry' ? <TripStateTag state={row.entry.state} /> : null,
      },
    ];
  }, [
    view,
    grouping,
    groupNames,
    sorted,
    language,
    t,
    toggle,
    token.colorFillQuaternary,
    token.paddingXXS,
  ]);

  const onTableChange = (
    _pagination: unknown,
    _filters: unknown,
    sorter: SorterResult<RecordRow> | SorterResult<RecordRow>[],
  ) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.field ? sortableFields[String(single.field)] : undefined;
    const word = field && single.order ? `${single.order === 'descend' ? '-' : ''}${field}` : undefined;
    onSortChange(word === DefaultSort ? undefined : word);
  };

  // The same care for what the table hands down to its rows: each of these is read by every row,
  // so one that was made anew on each redraw would redraw them all.
  const rowProps = useCallback(
    (row: RecordRow) =>
      row.type === 'entry' ? { onClick: () => onOpen(row.entry), style: { cursor: 'pointer' } } : {},
    [onOpen],
  );
  // Marked by what a line is, so that a record can be told from a heading by anything that has
  // to.
  const rowClass = useCallback((row: RecordRow) => `calendar-row-${row.type}`, []);
  const scroll = useMemo(() => ({ x: 'max-content' }), []);
  const locale = useMemo(
    () => ({
      emptyText: (
        <Empty
          data-testid="calendar-empty"
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={emptyText}
        />
      ),
    }),
    [emptyText],
  );

  return (
    <div data-testid="calendar-record">
      <Flex gap={8} wrap align="center" style={{ marginBottom: 12 }}>
        <GroupBySelect<RecordGrouping>
          label={t('calendar.grouping.groupBy')}
          data-testid="calendar-group-by"
          value={grouping}
          options={RECORD_GROUPINGS.map((word) => ({
            value: word,
            label: t(`calendar.grouping.dimensions.${word}`),
          }))}
          onChange={onGroupingChange}
        />
      </Flex>
      <Table<RecordRow>
        scroll={scroll}
        rowKey="key"
        size="middle"
        loading={loading}
        // No rows are handed over while the first answer is still coming, rather than an empty
        // list of them. The table draws its "nothing here" sentence under any list it is given,
        // spinner or not, and holds it back only when it has been given none — and "nothing is
        // recorded in these days" is a claim a question still in flight has not earned.
        dataSource={loading ? undefined : layout.rows}
        onChange={onTableChange}
        rowClassName={rowClass}
        onRow={rowProps}
        locale={locale}
        pagination={false}
        showHeader={view !== 'agenda'}
        columns={columns}
      />
    </div>
  );
}

/**
 * Redrawn when what it is handed changes and not whenever the page around it is. The list holds
 * the whole window, so being redrawn is the one expensive thing about it; the page redraws for
 * every switch a reader turns, and most of those change nothing here.
 */
export default memo(CalendarRecordTable);
