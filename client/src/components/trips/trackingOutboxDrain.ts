// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import { ApiError, isSettledRefusal } from '../../api/client.ts';
import type { TrackingReportRequest } from '../../api/hooks.ts';
import type { SignInRenewal } from '../../auth/renewSignIn.ts';
import {
  claimHeldReport,
  dropHeldReport,
  heldReportRefused,
  heldReportsOf,
  heldReportUnanswered,
  refreshHeldReports,
  releaseHeldReport,
} from './trackingOutbox.ts';

/**
 * Sending the reports this browser kept, once there is somebody to send them to.
 *
 * <b>Oldest first, one at a time.</b> The log is read in the order things happened, and a party
 * that reported a station and then its way out must not be shown coming out first because the
 * second request happened to arrive before the first. One request is out at any moment, and the
 * next is not made until the one before it has been answered.
 *
 * <b>What becomes of one report is decided by what the server said, and only by that:</b>
 *
 * - it answered yes — the report is on the log (or was, and has been taken off since: the answer
 *   then lists nothing, and nothing is written again). The kept copy is removed;
 * - it answered no, for good — the kept copy stays, marked refused with the server's code, until
 *   its author sends it again or discards it. It is not sent again by itself: the identical
 *   request cannot be answered differently, so repeating it would be a loop. The reports after it
 *   are still sent, because one refused report says nothing about the next;
 * - it gave no answer at all, or an answer that is not about the report — the sign-in has lapsed,
 *   the server is busy or coming back — the report stays held and <em>nothing after it is
 *   tried</em>: whatever stopped this one would stop those, and each attempt would cost a request.
 *
 * <b>A sign-in that ran out is renewed, once, and the same report sent again.</b> An outage longer
 * than a token lasts is the ordinary case underground, and it leaves the tab holding a token the
 * server no longer takes with nothing set to replace it. So a report refused as coming from nobody
 * is not the end of the run: the sign-in is renewed and the very same request repeated under the
 * same key. Once per run — a second such refusal straight after a renewal is not something another
 * renewal would change. Where the sign-in cannot be renewed it is over, and {@link
 * useHeldReportsSignInLapsed} says so for as long as that is true: these reports cannot leave
 * until their author has signed in again, and nothing else on the page would tell them.
 *
 * <b>A run that got no answer is tried again later, by itself.</b> The browser's own word that the
 * connection is back cannot be waited for alone: it never calls a weak signal, a network with no
 * way out or a restarting server "offline", so it never announces their end. So a run that stops
 * for want of an answer sets one timer — five seconds, then ten, twenty, forty, and a minute from
 * then on — and whoever sees the server answer something else calls {@link
 * heldReportsServerAnswered}. With nothing held there is no timer and no request at all.
 *
 * <b>A wait the server names is kept.</b> A refusal for being asked too often says when to come
 * back, and asking sooner is counted against the very allowance the refusal was about. Until then
 * nothing is sent from this tab, whoever asks — and the one sender that lives for the whole
 * signed-in session is woken when the wait is over.
 *
 * <b>Two tabs doing this at once are harmless, and nothing here tries to prevent it.</b> Every
 * report carries the key of its act; the server answers a second send under a key with what the
 * first one wrote and writes nothing. Both tabs are told yes and both remove the kept copy, which
 * is the same removal twice. Within one tab a second call joins the run already under way rather
 * than starting another, so that its author is not told twice what became of one report.
 */

/** Why a run ended with reports still held, or null where it reached the end of the queue. */
export type HeldReportsStop = 'noAnswer' | 'signedOut' | 'serverBusy' | 'waiting';

/** What one run did. */
export interface HeldReportsRun {
  /** Reports now on the log. */
  sent: number;
  /**
   * Reports the server already had and that were taken off the log since: answered yes, with
   * nothing left to list. No longer kept here, and not written again.
   */
  alreadyRemoved: number;
  /** Reports the server refused for good, kept and marked. */
  refused: number;
  stopped: HeldReportsStop | null;
  /** True for a caller that joined a run somebody else in this tab had started. */
  joined: boolean;
}

/** How one kept report is sent: the answer and the failure exactly as the transport made them. */
export type SendHeldReport = (report: TrackingReportRequest) => Promise<readonly unknown[]>;

/**
 * What a failed send of a kept report means for it.
 *
 * `refused` is the server's answer about the report. The others are about something else — and a
 * lapsed sign-in is told apart from the rest although it is a client error like a refusal, because
 * it is not about the report at all: the same report is taken the moment its author has signed in
 * again. Whether an answer is final is asked of the one rule the rest of the application uses.
 */
export function heldReportFailure(error: unknown): 'refused' | Exclude<HeldReportsStop, 'waiting'> {
  if (!(error instanceof ApiError)) return 'noAnswer';
  if (error.status === 401) return 'signedOut';
  return isSettledRefusal(error) ? 'refused' : 'serverBusy';
}

/** Renews the sign-in for a run, or says it cannot be. */
export type RenewSignIn = () => Promise<SignInRenewal>;

/** For a caller with no way of renewing a sign-in: one that ran out is simply over. */
const cannotRenew: RenewSignIn = () => Promise.resolve('lapsed');

const useSignInLapsed = create<{ lapsed: boolean }>()(() => ({ lapsed: false }));

/**
 * Records that held reports cannot leave because their author's sign-in is over, or that this is
 * no longer so.
 */
export function heldReportsSignInLapsed(lapsed: boolean): void {
  if (useSignInLapsed.getState().lapsed !== lapsed) useSignInLapsed.setState({ lapsed });
}

/** Whether held reports are known to be waiting for their author to sign in again. */
export function isHeldReportsSignInLapsed(): boolean {
  return useSignInLapsed.getState().lapsed;
}

/** {@link isHeldReportsSignInLapsed}, kept current for a component. */
export function useHeldReportsSignInLapsed(): boolean {
  return useSignInLapsed((state) => state.lapsed);
}

/**
 * How long after a run that got no answer the next is started, by how many have failed in a row.
 * The last one is used from then on.
 */
export const HELD_REPORTS_RETRY_MS: readonly number[] = [5_000, 10_000, 20_000, 40_000, 60_000];

let running: Promise<HeldReportsRun> | null = null;
let retryTimer: ReturnType<typeof setTimeout> | undefined;
/** Runs that have ended for want of an answer since one last got through. */
let unanswered = 0;
/** The instant before which a run that failed is not started again on somebody else's answer. */
let quietUntil = 0;
/** The instant before which the server asked not to be sent anything more, on this tab's clock. */
let waitUntil = 0;
let wakeTimer: ReturnType<typeof setTimeout> | undefined;
let wake: (() => void) | null = null;

/**
 * Names the sender to wake when a wait the server asked for is over, or takes it away.
 *
 * One at most, and it is the sender that lives as long as the sign-in does: a surface that sends
 * on a press is not there to be woken, and two wakers would be two sends.
 */
export function wakeHeldReportsWith(sender: (() => void) | null): void {
  wake = sender;
}

/** How much of a wait the server asked for is still to run, in milliseconds; 0 where none is. */
export function heldReportsWaitMs(now = Date.now()): number {
  return Math.max(0, waitUntil - now);
}

function keepWait(ms: number): void {
  waitUntil = Date.now() + ms;
  clearTimeout(wakeTimer);
  // Set only on an answer that named a wait, and in place of the timer a run without an answer
  // sets: with nothing held there is no timer at all.
  wakeTimer = setTimeout(() => wake?.(), ms);
}

/**
 * A report's first send was answered "later", and the report has been kept: arranges when it goes.
 *
 * The same two things a run arranges when a later send meets that answer, for the one send that
 * is not a run. The wait the answer named is kept — asking sooner is counted against the very
 * allowance the answer was about — and where it named none the sender is woken a few seconds on.
 * Either way the next answer to some other request is not taken as a reason to send at once: the
 * page that has just been told the server has no room is still making its other requests.
 */
export function heldReportAnsweredLater(retryAfterMs: number | undefined): void {
  quietUntil = Date.now() + HELD_REPORTS_RETRY_MS[0];
  if (retryAfterMs !== undefined && retryAfterMs > 0) keepWait(retryAfterMs);
  else if (heldReportsWaitMs() === 0) retryHeldReportsLater();
}

/**
 * Has the sender woken once more, later, unless that is already arranged.
 *
 * For a report that has just become held with nothing set to send it: its first send got no
 * answer while the browser went on calling itself online, so no announcement of a connection is
 * coming. A run that ends for want of an answer arranges the same thing for itself.
 */
export function retryHeldReportsLater(): void {
  if (retryTimer !== undefined) return;
  const ms = HELD_REPORTS_RETRY_MS[Math.min(unanswered, HELD_REPORTS_RETRY_MS.length - 1)];
  unanswered += 1;
  retryTimer = setTimeout(() => {
    retryTimer = undefined;
    wake?.();
  }, ms);
}

function forgetRetry(): void {
  clearTimeout(retryTimer);
  retryTimer = undefined;
}

/**
 * The server has just answered something else: a reason to send what is held, now.
 *
 * Not while a run is under way, not during a wait the server asked for, and not within a few
 * seconds of a run that failed — a page that opens makes a dozen requests at once, and each of
 * their answers must not be a fresh attempt at a report that was refused an answer a moment ago.
 * The caller asks only where it knows something is held, so that an answer costs nothing otherwise.
 */
export function heldReportsServerAnswered(): void {
  if (running || Date.now() < quietUntil || heldReportsWaitMs() > 0) return;
  wake?.();
}

/** What a run that has ended leaves arranged for the reports it could not send. */
function settle(result: HeldReportsRun): void {
  forgetRetry();
  if (result.stopped === 'signedOut') {
    heldReportsSignInLapsed(true);
  } else if (result.stopped !== 'waiting' && result.stopped !== 'noAnswer') {
    // The server answered a report, or there was none to send: either way nothing here is known
    // to be waiting on a sign-in.
    heldReportsSignInLapsed(false);
  }
  if (result.stopped === null || result.stopped === 'signedOut') {
    unanswered = 0;
    return;
  }
  quietUntil = Date.now() + HELD_REPORTS_RETRY_MS[0];
  // A wait the server named has its own timer, and is kept instead of this one.
  if (heldReportsWaitMs() === 0) retryHeldReportsLater();
}

async function run(
  accountId: string,
  send: SendHeldReport,
  renew: RenewSignIn,
): Promise<HeldReportsRun> {
  const done: HeldReportsRun = {
    sent: 0,
    alreadyRemoved: 0,
    refused: 0,
    stopped: null,
    joined: false,
  };
  const tried = new Set<string>();
  let renewed = false;
  for (;;) {
    // Read again before every report: another tab may have sent or discarded what this one was
    // about to send, and a report composed while this run was under way is part of it.
    refreshHeldReports();
    const next = heldReportsOf(accountId).find(
      (entry) => entry.state === 'held' && !entry.sending && !tried.has(entry.clientKey),
    );
    if (!next) return done;
    if (heldReportsWaitMs() > 0) return { ...done, stopped: 'waiting' };
    tried.add(next.clientKey);
    if (!claimHeldReport(next.clientKey, 'again')) continue;
    try {
      const written = await send({
        tripLogId: next.tripLogId,
        ...next.body,
        clientKey: next.clientKey,
      });
      dropHeldReport(accountId, next.clientKey);
      if (written.length === 0) done.alreadyRemoved += 1;
      else done.sent += 1;
    } catch (error) {
      let failure = heldReportFailure(error);
      if (failure === 'signedOut') {
        const renewal = renewed ? 'lapsed' : await renew();
        renewed = true;
        if (renewal === 'renewed') {
          // The same report again, under the same key: nothing about it was refused.
          tried.delete(next.clientKey);
          continue;
        }
        // Nobody answered the renewal either, which says nothing about the sign-in.
        if (renewal === 'noAnswer') failure = 'noAnswer';
      }
      if (failure === 'refused') {
        heldReportRefused(accountId, next.clientKey, (error as ApiError).code ?? null);
        done.refused += 1;
        continue;
      }
      heldReportUnanswered(accountId, next.clientKey);
      if (error instanceof ApiError && error.retryAfterMs !== undefined && error.retryAfterMs > 0) {
        keepWait(error.retryAfterMs);
      }
      return { ...done, stopped: failure };
    } finally {
      releaseHeldReport(next.clientKey);
    }
  }
}

/**
 * Sends one account's held reports, oldest first, and answers what became of them.
 *
 * Asks for the account like every way into the queue: what another account composed in this
 * browser is neither read nor sent by this call.
 */
export function drainHeldReports(
  accountId: string,
  send: SendHeldReport,
  renew: RenewSignIn = cannotRenew,
): Promise<HeldReportsRun> {
  if (running) return running.then((result) => ({ ...result, joined: true }));
  const started = run(accountId, send, renew)
    .then((result) => {
      settle(result);
      return result;
    })
    .finally(() => {
      running = null;
    });
  running = started;
  return started;
}

/** Forgets a wait and a waker. For a test that needs this module as a tab just opened has it. */
export function resetHeldReportsDrain(): void {
  clearTimeout(wakeTimer);
  wakeTimer = undefined;
  waitUntil = 0;
  wake = null;
  running = null;
  forgetRetry();
  unanswered = 0;
  quietUntil = 0;
  heldReportsSignInLapsed(false);
}
