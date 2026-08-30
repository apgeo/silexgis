// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { CalendarEntry } from '../../api/hooks.ts';
import dayjs from 'dayjs';
import {
  countByMonth,
  coveredDays,
  entriesByDay,
  formatDay,
  orderedForDay,
  weekDays,
} from './calendarDays.ts';

function row(overrides: Partial<CalendarEntry> = {}): CalendarEntry {
  const base: CalendarEntry = {
    source: 'tripLog',
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Coiba Mare recce',
    start: '2026-09-05',
    end: null,
    startTime: null,
    endTime: null,
    kind: null,
    state: 'planned',
    placement: 'ahead',
    cavingGroupId: null,
    hasPosition: false,
  };
  return { ...base, ...overrides };
}

describe('the days a record covers', () => {
  it('is the one day it happened on when it records no end', () => {
    expect(coveredDays('2026-09-05', null, '2026-09-01', '2026-09-30')).toEqual(['2026-09-05']);
  });

  it('is every day from the first to the last, both included', () => {
    expect(coveredDays('2026-09-05', '2026-09-08', '2026-09-01', '2026-09-30')).toEqual([
      '2026-09-05',
      '2026-09-06',
      '2026-09-07',
      '2026-09-08',
    ]);
  });

  /**
   * A record that was already going on when the window opens still fills the window's own days.
   * The alternative — drawing it only from the day it began — would leave a fortnight's camp
   * invisible in the month a reader is looking at, which is the month it is happening in.
   */
  it('starts at the window when the record began before it', () => {
    expect(coveredDays('2026-08-28', '2026-09-02', '2026-09-01', '2026-09-30')).toEqual([
      '2026-09-01',
      '2026-09-02',
    ]);
  });

  it('stops at the window when the record runs on past it', () => {
    expect(coveredDays('2026-09-29', '2026-10-04', '2026-09-01', '2026-09-30')).toEqual([
      '2026-09-29',
      '2026-09-30',
    ]);
  });

  it('covers nothing at all when the record falls outside the window', () => {
    expect(coveredDays('2026-11-01', '2026-11-03', '2026-09-01', '2026-09-30')).toEqual([]);
  });

  /** A month boundary is arithmetic on real dates, not on the day number. */
  it('walks across the end of a month', () => {
    expect(coveredDays('2026-01-30', '2026-02-02', '2026-01-01', '2026-12-31')).toEqual([
      '2026-01-30',
      '2026-01-31',
      '2026-02-01',
      '2026-02-02',
    ]);
  });

  /**
   * The days are written back as the local calendar days they are. Read as UTC instants they
   * would be a day out for every reader east of Greenwich, which is the whole reason the dates
   * on the wire carry no zone.
   */
  it('writes a day back as the day it is, wherever the reader is', () => {
    expect(formatDay(new Date(2026, 0, 1))).toBe('2026-01-01');
    expect(formatDay(new Date(2026, 11, 31))).toBe('2026-12-31');
  });
});

describe('the answer arranged by day', () => {
  it('puts a record in every cell it covers and marks only its first and last', () => {
    const byDay = entriesByDay([row({ start: '2026-09-05', end: '2026-09-08' })], '2026-09-01', '2026-09-30');

    expect([...byDay.keys()]).toEqual(['2026-09-05', '2026-09-06', '2026-09-07', '2026-09-08']);
    expect(byDay.get('2026-09-05')![0]).toMatchObject({ isStart: true, isEnd: false });
    expect(byDay.get('2026-09-06')![0]).toMatchObject({ isStart: false, isEnd: false });
    expect(byDay.get('2026-09-07')![0]).toMatchObject({ isStart: false, isEnd: false });
    expect(byDay.get('2026-09-08')![0]).toMatchObject({ isStart: false, isEnd: true });
  });

  /**
   * A record already under way when the window opened is not marked as starting on the window's
   * first day — it did not start there, and a mark saying it did would be a claim about a day the
   * reader cannot see.
   */
  it('marks no start on a day that is only where the window begins', () => {
    const byDay = entriesByDay([row({ start: '2026-08-28', end: '2026-09-02' })], '2026-09-01', '2026-09-30');

    expect(byDay.get('2026-09-01')![0].isStart).toBe(false);
    expect(byDay.get('2026-09-02')![0].isEnd).toBe(true);
  });

  it('keeps a day of records in the order the answer gave them', () => {
    const byDay = entriesByDay(
      [
        row({ id: 'a', title: 'first' }),
        row({ id: 'b', title: 'second' }),
        row({ id: 'c', title: 'third' }),
      ],
      '2026-09-01',
      '2026-09-30',
    );

    expect(byDay.get('2026-09-05')!.map((placed) => placed.entry.title)).toEqual([
      'first',
      'second',
      'third',
    ]);
  });

  it('leaves a day with nothing on it out of the arrangement entirely', () => {
    const byDay = entriesByDay([row()], '2026-09-01', '2026-09-30');

    expect(byDay.has('2026-09-06')).toBe(false);
  });
});

describe('the answer counted by month', () => {
  it('counts a record once in each month it touches, however long it lasts', () => {
    const byMonth = countByMonth(
      [
        row({ id: 'a', start: '2026-07-18', end: '2026-08-01' }),
        row({ id: 'b', start: '2026-07-20' }),
      ],
      '2026-01-01',
      '2026-12-31',
    );

    expect(byMonth.get('2026-07')).toBe(2);
    expect(byMonth.get('2026-08')).toBe(1);
    expect(byMonth.get('2026-09')).toBeUndefined();
  });
});

describe('one day in the order it is read', () => {
  /**
   * Most records here carry no time of day at all — a trip states the times its party was under
   * ground, which is not when it begins, and a camp states days. A day ordered only on the times
   * it happens to hold would have nowhere to put the majority of what is on it, so the ones with
   * no time are ordered first rather than dropped or left wherever they fell.
   */
  it('keeps a record that states no time, and puts it before the ones that do', () => {
    const day = '2026-09-05';
    const ordered = orderedForDay([
      { entry: row({ id: 'a', title: 'Evening meeting', start: day, startTime: '19:00:00' }), isStart: true, isEnd: true },
      { entry: row({ id: 'b', title: 'Ponorul camp', start: day }), isStart: true, isEnd: true },
    ]);

    expect(ordered.map((placed) => placed.entry.title)).toEqual(['Ponorul camp', 'Evening meeting']);
  });

  it('orders the records that state a time by it, earliest first', () => {
    const day = '2026-09-05';
    const ordered = orderedForDay([
      { entry: row({ id: 'a', title: 'Zulu', start: day, startTime: '18:00:00' }), isStart: true, isEnd: true },
      { entry: row({ id: 'b', title: 'Alpha', start: day, startTime: '07:00:00' }), isStart: true, isEnd: true },
      { entry: row({ id: 'c', title: 'Mike', start: day, startTime: '12:30:00' }), isStart: true, isEnd: true },
    ]);

    expect(ordered.map((placed) => placed.entry.title)).toEqual(['Alpha', 'Mike', 'Zulu']);
  });

  /**
   * A record states one time and it is the time it begins. In the later days of a span it is
   * something already going on, and ordering it by that first morning's time would claim, four
   * days running, that it started at nine each day.
   */
  it('claims no time in the days a record is merely continuing through', () => {
    const ordered = orderedForDay([
      { entry: row({ id: 'a', title: 'Evening meeting', start: '2026-09-06', startTime: '19:00:00' }), isStart: true, isEnd: true },
      { entry: row({ id: 'b', title: 'Ponorul camp', start: '2026-09-05', end: '2026-09-08', startTime: '09:00:00' }), isStart: false, isEnd: false },
    ]);

    expect(ordered.map((placed) => placed.entry.title)).toEqual(['Ponorul camp', 'Evening meeting']);
  });

  /**
   * Records that claim the same minute, and records that claim none, keep the order the answer
   * merged them in — the server merged three families of record into one sequence, and a day that
   * reshuffled the ones it could not tell apart would show a different answer on every render.
   */
  it('leaves records it cannot tell apart in the order the answer gave them', () => {
    const day = '2026-09-05';
    const ordered = orderedForDay([
      { entry: row({ id: 'a', title: 'First', start: day, startTime: '09:00:00' }), isStart: true, isEnd: true },
      { entry: row({ id: 'b', title: 'Second', start: day, startTime: '09:00:00' }), isStart: true, isEnd: true },
    ]);

    expect(ordered.map((placed) => placed.entry.title)).toEqual(['First', 'Second']);
  });
});

describe('the week a day falls in', () => {
  it('is seven days, from the week that day belongs to and no other', () => {
    // A Wednesday, asked for by any of the days around it.
    const days = weekDays(dayjs('2026-09-09'));

    expect(days).toHaveLength(7);
    expect(days).toEqual(weekDays(dayjs('2026-09-09').startOf('week')));
    expect(days[0]).toBe(dayjs('2026-09-09').startOf('week').format('YYYY-MM-DD'));
    expect(days[6]).toBe(dayjs('2026-09-09').endOf('week').format('YYYY-MM-DD'));
    expect(days.includes('2026-09-09')).toBe(true);
  });

  it('runs forwards without a gap or a repeat', () => {
    const days = weekDays(dayjs('2026-12-30'));

    expect(new Set(days).size).toBe(7);
    days.forEach((day, index) => {
      expect(day).toBe(dayjs(days[0]).add(index, 'day').format('YYYY-MM-DD'));
    });
  });
});
