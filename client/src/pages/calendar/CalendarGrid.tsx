// SPDX-License-Identifier: AGPL-3.0-or-later
import { Calendar } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { CalendarEntry } from '../../api/hooks.ts';
import { calendarDayContentStyle } from '../../theme.ts';
import { countByMonth, entriesByDay, orderedForDay } from './calendarDays.ts';
import EntryChip from './EntryChip.tsx';

interface Props {
  /** Which grid is drawn. The component offers these two and no others. */
  mode: 'month' | 'year';
  /** The month or year on show. */
  value: Dayjs;
  /**
   * Where the reader moved to. The grid draws its own year and month controls and its own
   * month/year switch, so both the day it is showing and which grid it is come back through
   * here — a switch of its own that the page ignored would sit there doing nothing.
   */
  onPanelChange: (value: Dayjs, mode: 'month' | 'year') => void;
  /** The answer's rows, in the order the answer gave them. */
  entries: readonly CalendarEntry[];
  /** The window those rows were read for; a record is drawn in no cell outside it. */
  from: string;
  to: string;
  onOpen: (entry: CalendarEntry) => void;
}

/**
 * The club's dated records drawn as a month of days or a year of months.
 *
 * **The cells are the calendar component's and their contents are this file's.** Only the content
 * of a cell is rendered here, so the cell's border, its day number, its today marker and its
 * selected background all keep coming from the component. What is overridden is one thing: the
 * component sizes a day cell's content at exactly three rows and scrolls the rest away inside
 * itself, so a Saturday carrying four records would show three and hide the fourth behind a
 * scrollbar a few pixels wide. The component accepts a style for that exact element, and the
 * height stated there is what lets a day grow to hold what is on it.
 *
 * Within a day the records are put in the order a day is read — what claims no time first, then
 * what does, earliest first. The answer's own order is by day and breaks ties on an identifier
 * that means nothing, so it carries no order *inside* a day for this to preserve.
 */
export default function CalendarGrid({
  mode,
  value,
  onPanelChange,
  entries,
  from,
  to,
  onOpen,
}: Props) {
  const { t } = useTranslation();

  const byDay = entriesByDay(entries, from, to);
  const byMonth = countByMonth(entries, from, to);

  // The second argument is typed by its one member used here rather than by the calendar's own
  // interface, which lives in a package this project depends on only through the calendar itself.
  const renderCell = (date: Dayjs, info: { type: string }) => {
    if (info.type === 'date') {
      const key = date.format('YYYY-MM-DD');
      const placed = orderedForDay(byDay.get(key) ?? []);
      return (
        // An empty day is an empty cell and not a missing one: the marker is always drawn, so a
        // month with nothing in it reads as a month of empty days rather than as a broken grid.
        <div data-testid={`calendar-day-${key}`}>
          {placed.map((entry) => (
            <EntryChip
              key={`${entry.entry.source}:${entry.entry.id}`}
              placed={entry}
              onOpen={onOpen}
            />
          ))}
        </div>
      );
    }
    const key = date.format('YYYY-MM');
    const count = byMonth.get(key) ?? 0;
    return (
      // A year of twelve cells says how much is in each rather than naming any of it: a month's
      // worth of titles in a cell an inch wide would be unreadable, and the month grid beside this
      // is one click away and does name them.
      <div data-testid={`calendar-month-${key}`}>
        {count > 0 ? (
          <span data-testid="calendar-month-count">{t('calendar.monthCount', { count })}</span>
        ) : null}
      </div>
    );
  };

  return (
    <div data-testid="calendar-grid">
      <Calendar
        value={value}
        mode={mode}
        fullscreen
        onChange={(next) => onPanelChange(next, mode)}
        onPanelChange={(next, nextMode) => onPanelChange(next, nextMode)}
        cellRender={renderCell}
        styles={{ itemContent: calendarDayContentStyle }}
      />
    </div>
  );
}
