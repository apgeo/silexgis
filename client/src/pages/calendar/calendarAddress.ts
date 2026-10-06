// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CalendarSource } from '../../api/hooks.ts';
import { parseTripDay } from '../../components/trips/tripDates.ts';
import { formatDay } from './calendarDays.ts';

/**
 * What the calendar page is showing, and its translation to and from the address bar.
 *
 * The address is where the view lives, not a copy of somewhere it lives properly: a month narrowed
 * to the training evenings is a link somebody pastes into a message, and reloading it, opening it
 * in a second tab or walking back to it must all show the same days read the same way. Held in
 * component state instead, every one of those is a different calendar that looks like the same one.
 *
 * So the address is read by people as well as by browsers. Each control is one readable key, a set
 * is a comma-separated list rather than a repeated key or an encoded blob, and the keys that are
 * also questions to the server are spelled the way the server spells them — somebody who can read
 * one can read the other. A default is written as an absent key and never as an explicit one, so
 * the calendar nobody has touched has a bare address and two ways of saying "nothing is chosen"
 * cannot both exist.
 *
 * **A word this application does not know is treated one of two ways, by what ignoring it would
 * do.** Where it would change *which rows* are shown — a family, a kind, an order — it is not
 * corrected here: it is handed to the server, which refuses it, because silently dropping it would
 * draw a calendar that is not the one the link asked for. Where it only decides *how* the same
 * rows are laid out — which reading, which day a grid is standing on — an unknown word falls back
 * to the ordinary one, because nothing is hidden by drawing the same rows the ordinary way.
 */

/**
 * The ways the same window of days may be read, offered in the order they narrow: the whole
 * record, then a month, a week and a year of it, then the agenda.
 *
 * The record is the list — sortable, and the only one that carries a column of each thing. The two
 * grids and the week strip are the same rows laid out as the days they fall on. The agenda is the
 * same list again with each row drawn as one entry rather than as a set of cells, which is what a
 * reader wants who is reading forwards rather than looking something up.
 */
export const CALENDAR_VIEWS = ['record', 'month', 'week', 'year', 'agenda'] as const;
export type CalendarView = (typeof CALENDAR_VIEWS)[number];

/** The reading the page opens on, and so the one an address says nothing to ask for. */
export const DefaultCalendarView: CalendarView = 'record';

/**
 * Every family the answer can hold. Written as a map over the family union rather than as a list
 * of words, so a family added to the answer is a compile error here instead of a family no control
 * on the page can turn off.
 */
const FAMILIES: Record<CalendarSource, true> = { tripLog: true, expedition: true, event: true };
export const CALENDAR_SOURCES = Object.keys(FAMILIES) as CalendarSource[];

/**
 * How an address says that no family at all is wanted. Wanting all of them is the absent key, so
 * wanting none needs a word of its own: a blank value would be indistinguishable from the key
 * somebody deleted half of. It is the page's own word and is never sent to the server — a question
 * that could only be answered with nothing is not asked.
 */
const NoSource = 'none';

export interface CalendarAddress {
  view: CalendarView;
  /**
   * The reader's own window, for the readings that are lists. Both ends or neither: half a window
   * is not a window, and the page then opens on the one it would have chosen itself.
   */
  from?: string;
  to?: string;
  /** The day a grid is standing on. Absent means today, so a bare link to a week is this week. */
  day?: string;
  /**
   * The families wanted, in the words the address carried. Undefined is all of them, which is the
   * ordinary state; an empty list is none.
   */
  sources?: string[];
  /** The kinds of event wanted, in the words the address carried. Empty is every kind. */
  kinds: string[];
  /** One group's calendar. */
  cavingGroupId?: string;
  /** Only what the reader is on. */
  mine: boolean;
  /** Whether days already gone are shown. They are, until somebody turns them off. */
  showPast: boolean;
  /** Whether rows called off are shown. They are, until somebody turns them off. */
  showCancelled: boolean;
  /** Whether the map under the reading is drawn. It is, until somebody puts it away. */
  showMap: boolean;
  /** One of the orders the server knows, a leading minus for descending. Absent is its own. */
  sort?: string;
}

/** The calendar nobody has touched yet. */
export const EmptyCalendarAddress: CalendarAddress = {
  view: DefaultCalendarView,
  kinds: [],
  mine: false,
  showPast: true,
  showCancelled: true,
  showMap: true,
};

const list = (raw: string | null): string[] =>
  raw === null
    ? []
    : raw
        .split(',')
        .map((word) => word.trim())
        .filter((word) => word !== '');

const one = (raw: string | null): string | undefined => (raw === null || raw === '' ? undefined : raw);

/**
 * A calendar day as an address writes one, or nothing. The shape alone is not enough — the
 * thirty-first of February has the right shape — so the day is rebuilt and has to come back as
 * what was written.
 */
function day(raw: string | null): string | undefined {
  if (raw === null || !/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
    return undefined;
  }
  return formatDay(parseTripDay(raw)) === raw ? raw : undefined;
}

/** The view an address describes. An absent key is the default, never an error. */
export function readCalendarAddress(params: URLSearchParams): CalendarAddress {
  const view = params.get('view');
  const from = day(params.get('from'));
  const to = day(params.get('to'));
  // A window the page could not have written — one end missing, or the ends the wrong way round —
  // is no window, and the page opens on its own. The range control shows which days those are, so
  // nothing about the fallback is hidden from whoever followed the link.
  const windowed = from !== undefined && to !== undefined && from <= to;
  const source = params.get('source');
  return {
    view: (CALENDAR_VIEWS as readonly string[]).includes(view ?? '')
      ? (view as CalendarView)
      : DefaultCalendarView,
    from: windowed ? from : undefined,
    to: windowed ? to : undefined,
    day: day(params.get('day')),
    sources:
      source === null || source.trim() === ''
        ? undefined
        : list(source).filter((word) => word !== NoSource),
    kinds: list(params.get('kind')),
    cavingGroupId: one(params.get('cavingGroupId')),
    // Only the one word that departs from the ordinary state means anything. Anything else is
    // read as the ordinary state, so a mistyped address shows more than was asked for and never
    // less.
    mine: params.get('mine') === 'true',
    showPast: params.get('includePast') !== 'false',
    showCancelled: params.get('includeCancelled') !== 'false',
    showMap: params.get('map') !== 'false',
    sort: one(params.get('sort')),
  };
}

/** The address a view describes: every default absent, so the untouched calendar has a bare one. */
export function writeCalendarAddress(address: CalendarAddress): URLSearchParams {
  const params = new URLSearchParams();
  if (address.view !== DefaultCalendarView) {
    params.set('view', address.view);
  }
  if (address.from !== undefined && address.to !== undefined) {
    params.set('from', address.from);
    params.set('to', address.to);
  }
  if (address.day !== undefined) {
    params.set('day', address.day);
  }
  if (address.sources !== undefined) {
    params.set('source', address.sources.length > 0 ? address.sources.join(',') : NoSource);
  }
  if (address.kinds.length > 0) {
    params.set('kind', address.kinds.join(','));
  }
  if (address.cavingGroupId !== undefined) {
    params.set('cavingGroupId', address.cavingGroupId);
  }
  if (address.mine) {
    params.set('mine', 'true');
  }
  if (!address.showPast) {
    params.set('includePast', 'false');
  }
  if (!address.showCancelled) {
    params.set('includeCancelled', 'false');
  }
  if (address.sort !== undefined) {
    params.set('sort', address.sort);
  }
  if (!address.showMap) {
    params.set('map', 'false');
  }
  return params;
}

/** Whether a family is among the ones wanted. */
export function wantsSource(address: CalendarAddress, source: CalendarSource): boolean {
  return address.sources === undefined || address.sources.includes(source);
}

/**
 * Whether no family at all is wanted, which is the one question the page does not ask. A list
 * holding only a word this application does not know is not that: it is a question, and the
 * server's refusal is its answer.
 */
export function wantsNoSource(address: CalendarAddress): boolean {
  return address.sources !== undefined && address.sources.length === 0;
}

/**
 * The families wanted once one of them has been turned on or off.
 *
 * Rebuilt from the families this application knows rather than edited in place, so a word a
 * hand-written address carried that names no family is let go of the first time a toggle is
 * touched, instead of riding along in every address the page writes afterwards. All of them
 * wanted is written as the absence of the narrowing, which is what it is.
 */
export function withSource(
  address: CalendarAddress,
  source: CalendarSource,
  wanted: boolean,
): string[] | undefined {
  const next = CALENDAR_SOURCES.filter((family) =>
    family === source ? wanted : wantsSource(address, family),
  );
  return next.length === CALENDAR_SOURCES.length ? undefined : next;
}

/**
 * What the server is asked for the families: nothing when all are wanted, and otherwise the words
 * the address carries, unknown ones included — a link naming a family this application does not
 * have is refused by the server rather than quietly answered as though the word were not there.
 */
export function sourceQuery(address: CalendarAddress): string | undefined {
  return address.sources === undefined || address.sources.length === 0
    ? undefined
    : address.sources.join(',');
}

/**
 * What the server is asked for the kinds of event: the chosen words, and only while events are
 * among the families wanted.
 *
 * A kind is something only an event has, so with the events turned off there is nothing for a kind
 * to narrow and the choice is not sent — it stays in the address, and comes back into force when
 * the events do. The trips and the camps are never narrowed by it: they are governed by their own
 * toggles, so nothing leaves the calendar that the reader did not turn off.
 */
export function kindQuery(address: CalendarAddress): string | undefined {
  return address.kinds.length > 0 && wantsSource(address, 'event')
    ? address.kinds.join(',')
    : undefined;
}

/**
 * Whether anything is narrowing the answer, which decides what an empty calendar says: "nothing is
 * recorded in these days" is a claim about the club, and a calendar emptied by its own filters has
 * not earned it. The window is not a narrowing — it is the question — and neither is how the rows
 * are read or ordered.
 */
export function isCalendarNarrowed(address: CalendarAddress): boolean {
  return (
    address.sources !== undefined ||
    kindQuery(address) !== undefined ||
    address.cavingGroupId !== undefined ||
    address.mine ||
    !address.showPast ||
    !address.showCancelled
  );
}
