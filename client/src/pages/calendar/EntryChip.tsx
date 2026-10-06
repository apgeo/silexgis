// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CSSProperties } from 'react';
import { theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { CalendarEntry, CalendarSource } from '../../api/hooks.ts';
import { entryKey, spanPosition, type DayEntry } from './calendarDays.ts';
import './EntryChip.css';

/**
 * Which family a record belongs to, as the colour of the bar that closes its chip. Written as a
 * map over the family union rather than as a chain of tests, so a family added to the answer is a
 * compile error here rather than a chip that silently looks like a trip.
 */
const SOURCE_TONE: Record<CalendarSource, 'colorPrimary' | 'purple' | 'magenta'> = {
  tripLog: 'colorPrimary',
  expedition: 'purple',
  event: 'magenta',
};

interface Props {
  placed: DayEntry;
  onOpen: (entry: CalendarEntry) => void;
  /** Whether a reader is pointing at this record, in this day or in another it is drawn in. */
  linked?: boolean;
  /** Told when a reader starts and stops pointing at a record that is drawn in several days. */
  onLink?: (key: string | null) => void;
}

/**
 * A record as one line inside a day, wherever days are drawn as cells or as columns.
 *
 * **A record lasting several days is drawn once in each day it covers, and reads as one record.**
 * Its first day is closed on the leading side by the bar of its family and carries its name; its
 * last day is closed on the trailing side by the same bar; every day between is open at both
 * ends. A rail in the family's colour runs along the chip wherever the record runs on, so the
 * days between draw the rail and nothing else. The name is said again on the first day of a week
 * the record has run on into, because a reader who starts at that row has not seen it — and
 * nowhere else, because a name repeated in every day reads as that many records sharing a title.
 *
 * It is still not a bar: the days are laid out independently of each other, so nothing can
 * promise the record sits at the same height in the day beside this one. The chip therefore
 * stops short of the next day rather than touching it, and what ties its days together across
 * that gap is the rail, the outline every day of it takes while a reader points at any one, and
 * a name that says which day of how many this is.
 *
 * The time is shown only in the day the record begins. A record carries one time of day and it
 * is the time it starts; repeating it would state, four days running, that something began at
 * nine each morning when it began once.
 */
export default function EntryChip({ placed, onOpen, linked = false, onLink }: Props) {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const { entry, isStart, isEnd, dayNumber, dayCount, leadsRow } = placed;
  const position = spanPosition(placed);

  const tone = SOURCE_TONE[entry.source];
  const accent = tone === 'colorPrimary' ? token.colorPrimary : token[tone];
  const background =
    entry.state === 'cancelled'
      ? token.colorErrorBg
      : entry.state === 'delayed' || entry.placement === 'putBack'
        ? token.colorWarningBg
        : token.colorFillQuaternary;

  const familyName = entry.kind
    ? t(`events.kindValues.${entry.kind}`)
    : t(`calendar.sourceValues.${entry.source}`);
  const time = isStart && entry.startTime ? entry.startTime.slice(0, 5) : null;

  // Everything the drawing says, in words: what kind of thing it is and what it is called, when
  // it starts where this is the day it starts, and which of its days this is. On a day that
  // draws only the rail these words are all a screen reader has, and all a hover shows.
  const described = [
    `${familyName} — ${entry.title}`,
    time,
    dayCount > 1 ? t('calendar.spanDay', { day: dayNumber, count: dayCount }) : null,
  ]
    .filter((part) => part !== null)
    .join(', ');

  // Only a record drawn in several days has other days to be tied to.
  const link = dayCount > 1 && onLink ? onLink : undefined;

  return (
    <button
      type="button"
      className="calendar-chip"
      data-testid="calendar-chip"
      data-source={entry.source}
      data-span={position}
      data-span-start={isStart ? 'true' : 'false'}
      data-span-end={isEnd ? 'true' : 'false'}
      data-named={leadsRow ? 'true' : 'false'}
      data-cancelled={entry.state === 'cancelled' ? 'true' : 'false'}
      data-linked={linked ? 'true' : 'false'}
      aria-label={described}
      title={described}
      onClick={(e) => {
        // The cell around this is itself selectable, and a click that both opened a record and
        // moved the grid's selection would leave the reader on a page they did not choose.
        e.stopPropagation();
        onOpen(entry);
      }}
      onMouseEnter={link && (() => link(entryKey(entry)))}
      onMouseLeave={link && (() => link(null))}
      onFocus={link && (() => link(entryKey(entry)))}
      onBlur={link && (() => link(null))}
      style={
        {
          '--calendar-chip-accent': accent,
          '--calendar-chip-background': background,
          '--calendar-chip-text': token.colorText,
          '--calendar-chip-radius': `${token.borderRadius}px`,
          '--calendar-chip-font-size': `${token.fontSizeSM}px`,
          '--calendar-chip-line-height': `${Math.round(token.lineHeightSM * token.fontSizeSM)}px`,
          '--calendar-chip-padding': `${token.paddingXXS}px`,
          '--calendar-chip-gap': `${token.paddingXXS}px`,
          '--calendar-chip-stub': `${token.paddingXS}px`,
          '--calendar-chip-bleed': `${token.paddingXS}px`,
          '--calendar-chip-cap': `${token.lineWidth * 3}px`,
          '--calendar-chip-rail': `${token.lineWidthBold}px`,
          '--calendar-chip-outline': `${token.lineWidth}px`,
        } as CSSProperties
      }
      lang={i18n.resolvedLanguage}
    >
      {leadsRow ? (
        <span className="calendar-chip-label">
          {time ? `${time} ` : ''}
          {entry.title}
        </span>
      ) : null}
    </button>
  );
}
