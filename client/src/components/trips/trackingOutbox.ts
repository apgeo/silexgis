// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { create } from 'zustand';
import type { TripPositionEventKind } from '../../api/hooks.ts';

/**
 * The reports this browser has composed and the server has not yet answered for.
 *
 * <b>This is the application's one exception to holding nothing offline, and it is a narrow one.</b>
 * It is a queue of unsent writes of a single kind — a new tracking report — kept in the browser's
 * own storage. It reads nothing while the server is away, caches no answer and runs nothing in the
 * background: a report waits here until a tab that is open and signed in as the same account sends
 * it. A correction, a deletion, a report put back and a sheet import are never queued.
 *
 * <b>An entry is written before its first send, not after the send failed.</b> A phone that sleeps,
 * a tab the browser discards and a reload all end a request without telling anybody how it ended,
 * so a report that is only kept once a failure has been observed is lost in exactly the cases this
 * exists for. Written first, the entry is what remains whenever no answer was seen, and it is
 * removed the moment an answer is: the report landed, or the server refused it while its author was
 * still looking at the form. Sending it again is safe because every entry carries the key of its
 * act — the server answers a repeated key with what the first send wrote and writes nothing.
 *
 * <b>One storage item per report, never one list.</b> Two tabs of the same browser each hold a copy
 * of what they last read. Were the queue a single stored list, a tab writing its copy back would
 * erase a report another tab had just put there — silently, which is the one thing this must never
 * do. With an item per report a tab can only ever add, change or remove the one report it is
 * handling, and there is no shared value to overwrite.
 *
 * <b>What is kept is somebody's free text about people, on a phone.</b> So every entry names the
 * account that composed it, and every way of reading the queue from outside this module asks for an
 * account and answers only that account's entries: another person signing in on the same browser is
 * neither shown them nor made to send them. There is deliberately no export that lists everything.
 *
 * <b>Storage that throws is an ordinary answer here.</b> A private window and blocked site data
 * throw on access rather than return nothing. Every call is guarded; {@link holdReport} says
 * whether the report was really written, and a report that could not be written is simply not held
 * — its author is then told it was not sent, which is the truth.
 */

/** What every stored item's name starts with; the rest of the name is the report's key. */
export const TRACKING_OUTBOX_PREFIX = 'silexgis.trackingOutbox.';

/** The shape written to storage, so that a later shape can tell its own items from these. */
const ENTRY_VERSION = 1;

/**
 * What a held report says, exactly as it is sent.
 *
 * The moment is never null here, although the form sends null for "now": null asks for the server's
 * clock at the instant the request arrives, and for a report sent an hour after it was composed
 * that would be the hour the signal came back rather than the hour it was about.
 */
export interface HeldReportBody {
  caverIds: string[];
  kind: TripPositionEventKind;
  stationName: string | null;
  depthM: number | null;
  teamId: string | null;
  note: string | null;
  recordedAt: string;
}

/**
 * One composed report that has not been answered for.
 *
 * `held` is waiting for a connection. `refused` was sent again later and the server answered no:
 * it stays, with the server's stable code, until its author sends it again or discards it — it is
 * neither dropped nor retried by itself.
 */
export interface HeldReport {
  /** The key of this act of reporting: minted once, and sent with every attempt. */
  clientKey: string;
  /** The account that composed it — the only account it is shown to or sent by. */
  accountId: string;
  tripLogId: string;
  body: HeldReportBody;
  /** This browser's clock when the report was composed. */
  composedAt: string;
  state: 'held' | 'refused';
  /** The server's code for the refusal; null while held, and for a refusal that named none. */
  problemCode: string | null;
  /** How many times it has been sent, the first send included. */
  attempts: number;
}

/**
 * Who is sending a report right now, in this tab.
 *
 * `first` is the send made as the report is composed: its author is still watching the button, so
 * the report is not yet "held" in any sense they would recognise and is left out of what is listed.
 * `again` is a later send of something already listed as held.
 */
export type HeldReportSend = 'first' | 'again';

/** A held report as a surface lists it. */
export interface ListedHeldReport extends HeldReport {
  /** True while this tab is sending it again. */
  sending: boolean;
}

interface OutboxState {
  entries: readonly HeldReport[];
  /** In memory only: a reload ends every send, and what it leaves behind is simply held. */
  sends: ReadonlyMap<string, HeldReportSend>;
}

const isString = (value: unknown): value is string => typeof value === 'string';
const isNullOr =
  <T>(test: (value: unknown) => value is T) =>
  (value: unknown): value is T | null =>
    value === null || test(value);
const isNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

/**
 * One stored item read back, or null where it is not something this version wrote.
 *
 * Storage is outside the application's control — another version, an extension, a hand in the
 * developer tools — so nothing read from it is believed without being looked at. An item that does
 * not read as a report is left where it is and not sent: destroying what is not understood is worse
 * than ignoring it.
 */
function parseEntry(storageKey: string, raw: string | null): HeldReport | null {
  if (raw === null) return null;
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }
  if (typeof value !== 'object' || value === null) return null;
  const item = value as Record<string, unknown>;
  const body = item.body as Record<string, unknown> | null | undefined;
  if (item.v !== ENTRY_VERSION || typeof body !== 'object' || body === null) return null;
  if (
    !isString(item.clientKey) ||
    storageKey !== TRACKING_OUTBOX_PREFIX + item.clientKey ||
    !isString(item.accountId) ||
    !isString(item.tripLogId) ||
    !isString(item.composedAt) ||
    (item.state !== 'held' && item.state !== 'refused') ||
    !isNullOr(isString)(item.problemCode) ||
    !isNumber(item.attempts) ||
    !Array.isArray(body.caverIds) ||
    !body.caverIds.every(isString) ||
    !isString(body.kind) ||
    !isNullOr(isString)(body.stationName) ||
    !isNullOr(isNumber)(body.depthM) ||
    !isNullOr(isString)(body.teamId) ||
    !isNullOr(isString)(body.note) ||
    !isString(body.recordedAt)
  ) {
    return null;
  }
  return {
    clientKey: item.clientKey,
    accountId: item.accountId,
    tripLogId: item.tripLogId,
    composedAt: item.composedAt,
    state: item.state,
    problemCode: item.problemCode,
    attempts: item.attempts,
    body: {
      caverIds: body.caverIds,
      kind: body.kind as TripPositionEventKind,
      stationName: body.stationName,
      depthM: body.depthM,
      teamId: body.teamId,
      note: body.note,
      recordedAt: body.recordedAt,
    },
  };
}

/** Everything in storage that reads as a held report, oldest composition first. */
function readStored(): HeldReport[] {
  const found: HeldReport[] = [];
  try {
    const storage = window.localStorage;
    for (let i = 0; i < storage.length; i++) {
      const storageKey = storage.key(i);
      if (!storageKey?.startsWith(TRACKING_OUTBOX_PREFIX)) continue;
      const entry = parseEntry(storageKey, storage.getItem(storageKey));
      if (entry) found.push(entry);
    }
  } catch {
    // Private windows and blocked site data throw rather than return nothing. What had been read
    // before the throw is not trusted to be whole, and a queue that cannot be read is an empty one.
    return [];
  }
  // By the moment of composition, so that reports reach the log in the order they were made; the
  // key breaks a tie only so that the order is the same every time it is asked.
  return found.sort(
    (a, b) => a.composedAt.localeCompare(b.composedAt) || a.clientKey.localeCompare(b.clientKey),
  );
}

/** Writes one report, answering whether it is now really in storage. */
function writeStored(entry: HeldReport): boolean {
  try {
    window.localStorage.setItem(
      TRACKING_OUTBOX_PREFIX + entry.clientKey,
      JSON.stringify({ v: ENTRY_VERSION, ...entry }),
    );
    return true;
  } catch {
    return false;
  }
}

function removeStored(clientKey: string): void {
  try {
    window.localStorage.removeItem(TRACKING_OUTBOX_PREFIX + clientKey);
  } catch {
    // Storage that cannot be written to could not have held the report in the first place.
  }
}

// Module-private on purpose: the only ways in are the functions below, each of which names an
// account, so no surface can come to list or send what another account composed.
const useOutbox = create<OutboxState>()(() => ({ entries: readStored(), sends: new Map() }));

/**
 * Reads the queue again from storage.
 *
 * What this tab remembers is a copy; storage is the queue. Another tab of the same browser adds to
 * it and takes from it, and this is how that reaches the tab — it is called on the browser's own
 * notice that storage changed elsewhere, and by anything that is about to act on the whole queue.
 */
export function refreshHeldReports(): void {
  useOutbox.setState({ entries: readStored() });
}

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    // A null key is storage being cleared whole, which empties the queue as much as any removal.
    if (event.key === null || event.key.startsWith(TRACKING_OUTBOX_PREFIX)) refreshHeldReports();
  });
}

/**
 * Keeps a report, answering whether it was really kept.
 *
 * False means storage refused it and nothing is held — not in storage and not in this tab's memory
 * either, because a report shown as held that a reload would lose is a promise the browser has
 * already declined to keep.
 */
export function holdReport(entry: HeldReport): boolean {
  if (!writeStored(entry)) return false;
  refreshHeldReports();
  return true;
}

/** The report under a key, if this account composed it. */
function ownEntry(accountId: string, clientKey: string): HeldReport | undefined {
  return useOutbox
    .getState()
    .entries.find((entry) => entry.clientKey === clientKey && entry.accountId === accountId);
}

/**
 * Takes a report out of the queue: it was sent, or its author discarded it.
 *
 * Asks for the account like everything else here, and does nothing for a report that is somebody
 * else's — one account can no more discard another's report than send it.
 */
export function dropHeldReport(accountId: string, clientKey: string): void {
  refreshHeldReports();
  if (!ownEntry(accountId, clientKey)) return;
  removeStored(clientKey);
  refreshHeldReports();
}

function changeHeldReport(
  accountId: string,
  clientKey: string,
  change: (entry: HeldReport) => HeldReport,
): void {
  // Read first: the report may have been sent and removed by another tab since this one last
  // looked, and writing a changed copy back would put a sent report into the queue again.
  refreshHeldReports();
  const entry = ownEntry(accountId, clientKey);
  if (!entry) return;
  writeStored(change(entry));
  refreshHeldReports();
}

/** A later send got no answer at all: still held, one more attempt made. */
export function heldReportUnanswered(accountId: string, clientKey: string): void {
  changeHeldReport(accountId, clientKey, (entry) => ({ ...entry, attempts: entry.attempts + 1 }));
}

/**
 * A later send was answered with a refusal: kept, marked, and left for its author.
 *
 * `problemCode` is the server's stable code, or null where the answer named none. The sentence for
 * it is not stored: it is worded when shown, in whichever language is being read then.
 */
export function heldReportRefused(
  accountId: string,
  clientKey: string,
  problemCode: string | null,
): void {
  changeHeldReport(accountId, clientKey, (entry) => ({
    ...entry,
    state: 'refused',
    problemCode,
    attempts: entry.attempts + 1,
  }));
}

/** Its author asked for a refused report to be sent again: it waits like any other. */
export function heldReportQueuedAgain(accountId: string, clientKey: string): void {
  changeHeldReport(accountId, clientKey, (entry) => ({
    ...entry,
    state: 'held',
    problemCode: null,
  }));
}

/**
 * Takes the right to send one report, in this tab. False means it is already being sent here.
 *
 * Two things in one tab can decide to send the same report in the same moment — its first send and
 * whatever sends held reports when the connection returns, or that sender started twice. The key
 * makes the second request harmless to the log, but its author would still be told twice, and in
 * two different ways, what became of one report. Another tab is not excluded and need not be.
 */
export function claimHeldReport(clientKey: string, as: HeldReportSend): boolean {
  const { sends } = useOutbox.getState();
  if (sends.has(clientKey)) return false;
  useOutbox.setState({ sends: new Map(sends).set(clientKey, as) });
  return true;
}

/** Gives the right back, whatever became of the send. */
export function releaseHeldReport(clientKey: string): void {
  const { sends } = useOutbox.getState();
  if (!sends.has(clientKey)) return;
  const left = new Map(sends);
  left.delete(clientKey);
  useOutbox.setState({ sends: left });
}

function listed(
  { entries, sends }: OutboxState,
  accountId: string | null,
  tripLogId?: string,
): ListedHeldReport[] {
  // Nobody signed in is nobody's queue, not everybody's.
  if (accountId === null) return [];
  return entries
    .filter(
      (entry) =>
        entry.accountId === accountId &&
        (tripLogId === undefined || entry.tripLogId === tripLogId) &&
        sends.get(entry.clientKey) !== 'first',
    )
    .map((entry) => ({ ...entry, sending: sends.get(entry.clientKey) === 'again' }));
}

/**
 * One account's held reports, oldest composition first — on one trip, or on every trip.
 *
 * A report whose first send is still under way is not among them: it is not held yet.
 */
export function heldReportsOf(accountId: string | null, tripLogId?: string): ListedHeldReport[] {
  return listed(useOutbox.getState(), accountId, tripLogId);
}

/** {@link heldReportsOf}, kept current for a component. */
export function useHeldReports(accountId: string | null, tripLogId?: string): ListedHeldReport[] {
  const entries = useOutbox((state) => state.entries);
  const sends = useOutbox((state) => state.sends);
  return useMemo(
    () => listed({ entries, sends }, accountId, tripLogId),
    [entries, sends, accountId, tripLogId],
  );
}
