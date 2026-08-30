// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Empty, Flex, Typography, theme } from 'antd';
import { LeftOutlined, RightOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { CalendarEntry } from '../../api/hooks.ts';
import { entriesByDay, formatDay, orderedForDay, weekDays } from './calendarDays.ts';
import EntryChip from './EntryChip.tsx';

interface Props {
  /** Any day of the week to draw; the strip covers the whole week that day falls in. */
  value: Dayjs;
  /** Where the reader moved to, so the page can read the week it now has to answer for. */
  onChange: (value: Dayjs) => void;
  /** The answer's rows, in the order the answer gave them. */
  entries: readonly CalendarEntry[];
  /** The window those rows were read for; a record is drawn in no cell outside it. */
  from: string;
  to: string;
  onOpen: (entry: CalendarEntry) => void;
}

/**
 * One week as seven columns of days, each column a stack of what is happening on that day.
 *
 * **There is no hour axis here, and that is what the records are rather than a corner cut.** A
 * trip states the time its party went underground and the time it came out; those are times spent
 * below ground, carrying no day and no time zone, and neither of them is the moment the trip
 * begins. An event states a wall-clock time with no zone either. So there is no instant to place
 * against an hour line, no duration to draw as a block, and nothing that could overlap another
 * thing in a way a reader would believe. A grid of hours would have to invent all three. The
 * columns are therefore stacks in the order a day is read — what has no time first, then what has
 * one, earliest first — and the time is printed on the chip that has it.
 *
 * A record lasting several days appears in every column it covers, marked in the column where it
 * begins and in the column where it ends. It is not drawn as one bar across the week: the columns
 * are laid out independently of each other, so nothing here can promise the same record sits at
 * the same height in the column beside it, and a bar drawn without that promise would join two
 * different records together.
 */
export default function CalendarWeekStrip({
  value,
  onChange,
  entries,
  from,
  to,
  onOpen,
}: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const days = weekDays(value);
  const byDay = entriesByDay(entries, from, to);
  const today = formatDay(new Date());

  const first = dayjs(days[0]);
  const last = dayjs(days[6]);

  return (
    <div data-testid="calendar-week">
      <Flex justify="space-between" align="center" style={{ marginBottom: 8 }}>
        <Flex gap={8} align="center">
          <Button
            aria-label={t('calendar.weekPrevious')}
            data-testid="calendar-week-previous"
            icon={<LeftOutlined />}
            onClick={() => onChange(value.subtract(1, 'week'))}
          />
          <Button
            aria-label={t('calendar.weekNext')}
            data-testid="calendar-week-next"
            icon={<RightOutlined />}
            onClick={() => onChange(value.add(1, 'week'))}
          />
          <Button data-testid="calendar-week-today" onClick={() => onChange(dayjs())}>
            {t('calendar.weekToday')}
          </Button>
        </Flex>
        {/* The week is named by the days it covers rather than by a number: a week number is a
            thing this application has never shown, and a reader looking for a Saturday wants to
            read the date of it. */}
        <Typography.Text strong data-testid="calendar-week-range">
          {`${first.format('D MMM')} – ${last.format('D MMM YYYY')}`}
        </Typography.Text>
      </Flex>
      <Flex gap={8} align="stretch" style={{ overflowX: 'auto' }}>
        {days.map((day) => {
          const date = dayjs(day);
          const placed = orderedForDay(byDay.get(day) ?? []);
          return (
            <div
              key={day}
              data-testid={`calendar-week-day-${day}`}
              style={{
                flex: '1 1 0',
                minWidth: 120,
                border: `1px solid ${token.colorBorderSecondary}`,
                borderRadius: token.borderRadius,
                background: day === today ? token.colorPrimaryBg : token.colorBgContainer,
                padding: token.paddingXS,
              }}
            >
              <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                {date.format('ddd')}
              </Typography.Text>
              <Typography.Title level={5} style={{ margin: '0 0 8px' }}>
                {date.format('D')}
              </Typography.Title>
              {/* A day with nothing on it says so, in the column itself: a blank column and a
                  column whose records were narrowed away look the same, and only one of them is
                  a claim about the day. */}
              {placed.length === 0 ? (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={
                    <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                      {t('calendar.weekDayEmpty')}
                    </Typography.Text>
                  }
                  style={{ margin: 0 }}
                />
              ) : (
                placed.map((entry) => (
                  <EntryChip
                    key={`${entry.entry.source}:${entry.entry.id}`}
                    placed={entry}
                    onOpen={onOpen}
                  />
                ))
              )}
            </div>
          );
        })}
      </Flex>
    </div>
  );
}
