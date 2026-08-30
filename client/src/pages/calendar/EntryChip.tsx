// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { CalendarEntry, CalendarSource } from '../../api/hooks.ts';
import type { DayEntry } from './calendarDays.ts';

/**
 * Which family a record belongs to, as the colour of the bar down the side of its chip. Written
 * as a map over the family union rather than as a chain of tests, so a family added to the answer
 * is a compile error here rather than a chip that silently looks like a trip.
 */
const SOURCE_TONE: Record<CalendarSource, 'colorPrimary' | 'purple' | 'magenta'> = {
  tripLog: 'colorPrimary',
  expedition: 'purple',
  event: 'magenta',
};

/**
 * A record as one line inside a day, wherever days are drawn as cells or as columns.
 *
 * A record lasting several days is drawn once in each day it covers, and the corners say which
 * cell is which: the day it begins on is rounded on its leading edge, the day it ends on is
 * rounded on its trailing edge, and the days between are square at both. That is what separates
 * one four-day trip from four separate trips, and it is as much as independently laid-out cells
 * can express — there is no bar running across the week and no guarantee the chip sits at the same
 * height in the cell beside it.
 *
 * The time is shown only in the cell where the record begins. A record carries one time of day and
 * it is the time it starts; repeating it in every later day would state, four days running, that
 * something began at nine each morning when it began once.
 */
export default function EntryChip({
  placed,
  onOpen,
}: {
  placed: DayEntry;
  onOpen: (entry: CalendarEntry) => void;
}) {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const { entry, isStart, isEnd } = placed;

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

  return (
    <button
      type="button"
      data-testid="calendar-chip"
      data-source={entry.source}
      data-span-start={isStart ? 'true' : 'false'}
      data-span-end={isEnd ? 'true' : 'false'}
      title={`${familyName} — ${entry.title}`}
      onClick={(e) => {
        // The cell around this is itself selectable, and a click that both opened a record and
        // moved the grid's selection would leave the reader on a page they did not choose.
        e.stopPropagation();
        onOpen(entry);
      }}
      style={{
        display: 'block',
        width: '100%',
        border: 0,
        borderInlineStart: `3px solid ${accent}`,
        borderStartStartRadius: isStart ? token.borderRadius : 0,
        borderEndStartRadius: isStart ? token.borderRadius : 0,
        borderStartEndRadius: isEnd ? token.borderRadius : 0,
        borderEndEndRadius: isEnd ? token.borderRadius : 0,
        background,
        color: token.colorText,
        cursor: 'pointer',
        font: 'inherit',
        fontSize: token.fontSizeSM,
        lineHeight: `${Math.round(token.lineHeightSM * token.fontSizeSM)}px`,
        marginBlockEnd: 2,
        overflow: 'hidden',
        padding: `0 ${token.paddingXXS}px`,
        textAlign: 'start',
        textDecoration: entry.state === 'cancelled' ? 'line-through' : undefined,
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
      }}
      lang={i18n.resolvedLanguage}
    >
      {isStart && entry.startTime ? `${entry.startTime.slice(0, 5)} ` : ''}
      {entry.title}
    </button>
  );
}
