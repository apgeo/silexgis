// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs, { type Dayjs } from 'dayjs';
import type { FilterOp } from './types.ts';

/**
 * Which moment of a picked day a comparison means, written in the picker's own time zone.
 *
 * "After the 3rd" is after the 3rd has ended and "before the 3rd" is before it began, while "at
 * least the 3rd" and "at most the 3rd" each take the whole of the 3rd in — so a day picked here
 * means the day, under every operator.
 *
 * The moment is written with the local offset rather than turned to UTC. A timestamp field
 * compares the moment, which is the same either way; a calendar-date field (the day a trip
 * happened) compares the day the bound names at the offset it was written with. Turned to UTC,
 * midnight of the 5th in Bucharest would name the 4th, and the last moment of the 5th in Lima would
 * name the 6th, each taking in a day nobody picked.
 */
export function dayBound(op: FilterOp, day: Dayjs, end: 'low' | 'high'): string {
  const late = op === 'greaterThan' || op === 'lessOrEqual' || (op === 'between' && end === 'high');
  return (late ? day.endOf('day') : day.startOf('day')).format('YYYY-MM-DDTHH:mm:ss.SSSZ');
}

const WRITTEN_DAY = /^(\d{4}-\d{2}-\d{2})T/;

/**
 * The day a bound was picked as: the calendar day it names as written, not that moment's day in
 * this browser's zone. A filter saved by somebody east of here still shows the day they picked —
 * the day the server compares a calendar date against — rather than the evening before it.
 */
export function pickedDay(value: string): Dayjs | null {
  const written = WRITTEN_DAY.exec(value)?.[1];
  const day = written === undefined ? dayjs(value) : dayjs(written);
  return day.isValid() ? day : null;
}
