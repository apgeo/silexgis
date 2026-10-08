// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import type { Rule } from 'antd/es/form';
import type { Dayjs } from 'dayjs';
import { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';
import { useRecordTrackingEvents, type TripPositionEventKind } from '../../api/hooks.ts';
import { signedInAccountId } from '../../auth/accountId.ts';
import { renewSignIn, type SignInRenewal } from '../../auth/renewSignIn.ts';
import {
  claimHeldReport,
  dropHeldReport,
  holdReport,
  releaseHeldReport,
} from './trackingOutbox.ts';
import {
  heldReportAnsweredLater,
  heldReportsSignInLapsed,
  retryHeldReportsLater,
} from './trackingOutboxDrain.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

/**
 * Recording what somebody underground said, wherever it was said from.
 *
 * <b>There are two ways into this and there must be one rule behind them.</b> A report is filled in
 * on the card under the watch, and it is also recorded by pressing a station on the model — which is
 * the faster of the two by a long way, and is the one somebody uses while a voice on the phone is
 * still talking. What must not differ between them is what actually reaches the server: which of
 * the station and the depth travels, what an empty note becomes, how a named moment is written
 * down, and which refusal is shown as a warning rather than an error. Every one of those is a rule
 * about the log rather than about a form, so all of them live here and neither surface holds a
 * second copy.
 *
 * The one thing deliberately *not* here is what happens afterwards. The card clears its fields and
 * lets the table's selection go; the dialog closes. Both are about the surface and not about the
 * report.
 */

/**
 * What became of one report, in the only terms a surface is allowed to act on.
 *
 * <b>The words for a refusal are still not the caller's business</b> — they have been said already,
 * by the one place that words them — and this carries the server's stable code purely so that a
 * surface can change *itself* in answer to a particular refusal. The dialog opened from the model
 * is the case: it states the pressed station as a fact rather than offering it as a field, and the
 * one refusal that makes that statement wrong is the server saying it has no station by that name.
 * Anything else is shown and nothing more.
 *
 * One shape rather than two: `code` is null where the report landed, and also where it was refused
 * by something that named no code — a request nobody answered that the browser could not keep
 * either, or a rule the client applied before asking. A surface that wants to act on a particular refusal tests for that refusal by
 * name, so all three of those cases fall through to "nothing to do about it here", which is what
 * they are.
 *
 * <b>`held` is the third thing that can become of a report, and it is not a refusal.</b> The server
 * gave no answer at all, or answered "later", and the report is kept in this browser to be sent
 * when it can be. For what a surface does with itself that is the same as `recorded` — the report
 * has left the form, so the form clears and the dialog closes; keeping the text on screen as well
 * would invite sending it a second time as a second report. {@link trackingReportLeftTheForm} is
 * that question, asked once. At most one of `recorded` and `held` is true.
 */
export interface TrackingReportOutcome {
  recorded: boolean;
  held: boolean;
  code: string | null;
}

/**
 * Whether the surface that composed a report is done with it: it is on the log, or it is kept to
 * be sent. False is a report still in its author's hands — refused with a reason they can act on,
 * or not sent and not kept — and the surface must then leave what was typed where it is.
 */
export function trackingReportLeftTheForm(outcome: TrackingReportOutcome): boolean {
  return outcome.recorded || outcome.held;
}

/** What either surface has filled in, in the form's own types. */
export interface TrackingReportValues {
  kind: TripPositionEventKind;
  stationName?: string;
  /** The far end of a stretch, where the report says somebody is between two stations. */
  toStationName?: string;
  depthM?: number | null;
  teamId?: string | null;
  note?: string;
  recordedAt?: Dayjs | null;
}

/**
 * The request one report makes, whichever surface filled it in.
 *
 * <b>A station name belongs to a station report and a depth to a depth report.</b> The server
 * refuses a request carrying the other one rather than quietly ignoring it, so what is not being
 * reported is sent as null rather than as whatever was left in a field the reader changed their
 * mind about.
 *
 * <b>The far end of a stretch goes with a station report, and an empty one is no stretch.</b> The
 * field is optional on the form, and a box somebody opened and left blank must read as a report at
 * one station: the server refuses a blank second station rather than guessing that, so blank is
 * turned into absence here, once, for every surface.
 *
 * <b>An empty moment means the server's clock</b>, which is what a report made as it happens wants;
 * a filled one is written as an instant with its offset, because the reader's phone and the server
 * are not in the same place as often as they are.
 */
export function trackingReportBody(
  tripLogId: string,
  caverIds: readonly string[],
  values: TrackingReportValues,
) {
  const note = values.note?.trim();
  const toStation = values.kind === 'atStation' ? values.toStationName?.trim() : undefined;
  return {
    tripLogId,
    caverIds: [...caverIds],
    kind: values.kind,
    stationName: values.kind === 'atStation' ? (values.stationName ?? '').trim() : null,
    toStationName: toStation ? toStation : null,
    depthM: values.kind === 'atDepth' ? (values.depthM ?? null) : null,
    teamId: values.teamId ?? null,
    note: note ? note : null,
    recordedAt: values.recordedAt ? values.recordedAt.toISOString() : null,
  };
}

/**
 * What a station report has to say before it is worth sending.
 *
 * Shared rather than restated so that the dialog opened from the model cannot drift into accepting
 * a report the card would have refused — which is the shape this defect takes: the dialog's station
 * arrives pre-filled from a click, so the field is almost never empty and a missing rule there
 * would go unnoticed until the one time somebody cleared it.
 */
export function trackingStationRules(t: ReturnType<typeof useTranslation>['t']): Rule[] {
  return [{ required: true, message: t('trips.tracking.reportStationRequired') }];
}

/**
 * Whether the server's answer to a report was "later" rather than "no".
 *
 * Two statuses say so in so many words: too many requests, and a service with no room right now.
 * Other server faults are left out on purpose — they do not say the report was not written, and
 * the form deals with those by keeping the text and the key it went under.
 */
function answeredLater(error: ApiError): boolean {
  return error.status === 429 || error.status === 503;
}

/**
 * What the second station of a stretch has to be, where one is given at all.
 *
 * Optional — most reports are at one station — and, where it is filled in, not the station above
 * it. The server refuses the same station twice and says so; asked here as well so that a slip in
 * choosing the far end is shown beside the field that holds it, before anything is sent. It reads
 * the names as typed, so two spellings of one station pass here and are still refused there.
 */
export function trackingToStationRules(t: ReturnType<typeof useTranslation>['t']): Rule[] {
  return [
    ({ getFieldValue }) => ({
      validator: (_rule, value: string | undefined) => {
        const to = (value ?? '').trim();
        const from = String(getFieldValue('stationName') ?? '').trim();
        return to.length > 0 && to === from
          ? Promise.reject(new Error(t('trips.tracking.reportToStationSame')))
          : Promise.resolve();
      },
    }),
  ];
}

/**
 * The one way a report is sent, and the one place its answer is worded.
 *
 * Answers what became of the report, so a surface can decide what to do with itself — the card
 * keeps standing and clears its fields, the dialog closes — without either of them having to know
 * how a refusal is told apart from a success, and without either of them wording one.
 *
 * A concurrent write is said as a warning and everything else as an error, because that one is not
 * a mistake anybody made: somebody else wrote to the same watch in the same moment, and the answer
 * is to look and try again rather than to change anything.
 *
 * <b>A report is never silently lost and never written twice, and both follow from its key.</b>
 * Every send mints the key of its act and, before asking the server anything, writes the report to
 * the browser's own storage under it. Whatever happens next, one of three things is true:
 *
 * - the server answered yes: the kept copy is removed, the report is on the log;
 * - the server answered no: the kept copy is removed and the refusal is shown exactly as it always
 *   was, the form untouched — a refusal its author is looking at is theirs to act on, not something
 *   to queue;
 * - no answer came — no signal, a dropped connection, a request that timed out, a tab the phone put
 *   to sleep: the kept copy stays, and is sent again under the same key. If the first request did
 *   reach the server after all, the repeat is answered with what it wrote and writes nothing.
 *
 * <b>"No answer at all" holds a report, and so do the two answers that say "later".</b> Too many
 * requests and a server with no room are not about the report: the identical request is taken a
 * minute on, and its author — who was shown an error and then reloaded a page that was slow for the
 * same reason — had lost the text by then. So those two are kept exactly like a request nobody
 * answered, and the wait the answer names is respected before the report is sent again. Anything
 * else the server said — a validation error, a refusal of permission, a watch that cannot be
 * written, a fault of its own that names no "later" — is an answer, however unwelcome, and is
 * shown.
 *
 * <b>A sign-in that ran out is not an answer about the report.</b> The request was refused as
 * coming from nobody, which a tab meets after any outage longer than its token lasts. The sign-in
 * is renewed and the same request sent once more under the same key. If it cannot be renewed the
 * kept copy <em>stays</em>: the only way to sign in again is to leave the page, which throws away
 * the form — and the form would otherwise be the report's only copy.
 *
 * <b>The moment is fixed when the report is composed.</b> A report made as it happens goes out with
 * no moment, meaning the server's clock, exactly as before: the phone's clock is not trusted while
 * the server's is to hand. The kept copy is different. It may leave an hour later, so it carries
 * this browser's clock at composition — otherwise it would land at the minute the signal returned.
 *
 * <b>Where the browser will not keep it</b> (a private window, blocked site data) nothing changes
 * from before there was a queue: the report is sent, and if no answer comes its author is told it
 * was not sent and the form keeps what they typed.
 */
export function useTrackingReport() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const record = useRecordTrackingEvents();
  // The last report this surface sent that is still in its author's hands, and the key it went
  // under. Pressing the button again on the very same report is the same act, not a second one, and
  // must carry the same key: an answer that came from something in front of the server — a gateway
  // that gave up waiting — does not say whether the server wrote the report, and a fresh key would
  // then write it twice.
  const unsettled = useRef<{ said: string; clientKey: string } | null>(null);

  const send = async (
    tripLogId: string,
    caverIds: readonly string[],
    values: TrackingReportValues,
  ): Promise<TrackingReportOutcome> => {
    const body = trackingReportBody(tripLogId, caverIds, values);
    const composedAt = new Date().toISOString();
    const said = JSON.stringify(body);
    const clientKey =
      unsettled.current?.said === said ? unsettled.current.clientKey : crypto.randomUUID();
    unsettled.current = { said, clientKey };

    // Claimed before it is written, so that nothing else in this tab finds a held report and sends
    // it while its author is still waiting on this very request.
    claimHeldReport(clientKey, 'first');
    try {
      const accountId = await signedInAccountId();
      const kept =
        accountId !== null &&
        holdReport({
          clientKey,
          accountId,
          tripLogId,
          body: {
            caverIds: body.caverIds,
            kind: body.kind,
            stationName: body.stationName,
            toStationName: body.toStationName,
            depthM: body.depthM,
            teamId: body.teamId,
            note: body.note,
            recordedAt: body.recordedAt ?? composedAt,
          },
          composedAt,
          state: 'held',
          problemCode: null,
          attempts: 1,
        });

      // Written from inside the send below, so held in something that outlives it.
      const renewal: { was: SignInRenewal | null } = { was: null };
      const post = () => record.mutateAsync({ ...body, clientKey });
      try {
        const created = await post().catch(async (refused: unknown) => {
          if (!(refused instanceof ApiError) || refused.status !== 401) throw refused;
          renewal.was = await renewSignIn();
          if (renewal.was !== 'renewed') throw refused;
          return post();
        });
        if (kept) dropHeldReport(accountId, clientKey);
        unsettled.current = null;
        message.success(t('trips.tracking.recorded', { count: created.length }));
        return { recorded: true, held: false, code: null };
      } catch (error) {
        if (!(error instanceof ApiError)) {
          // No answer at all. The transport raises its own error type for anything the server
          // said, whatever the status, so anything else is a request nobody answered.
          if (kept) {
            // Now the queue's: the same text typed again is a new report with a key of its own.
            unsettled.current = null;
            // The browser may never have thought itself offline, and then it will never announce
            // a connection either: the send is tried again a little later whatever it says.
            retryHeldReportsLater();
            message.warning({ content: t('trips.tracking.outbox.held'), duration: 8 });
            return { recorded: false, held: true, code: null };
          }
          message.error({ content: t('trips.tracking.outbox.notKept'), duration: 8 });
          return { recorded: false, held: false, code: null };
        }
        if (error.status === 401 && kept) {
          // Refused as coming from nobody, and the sign-in could not be renewed. Held, like a
          // report nobody answered — and for the same reason handed to the queue.
          unsettled.current = null;
          if (renewal.was === 'noAnswer') retryHeldReportsLater();
          else heldReportsSignInLapsed(true);
          message.warning({ content: t('trips.tracking.outbox.heldSignedOut'), duration: 10 });
          return { recorded: false, held: true, code: null };
        }
        if (answeredLater(error) && kept) {
          // "Later", not "no": held like a report nobody answered, and handed to the queue with
          // the wait the answer named.
          unsettled.current = null;
          heldReportAnsweredLater(error.retryAfterMs);
          message.warning({ content: t('trips.tracking.outbox.heldBusy'), duration: 8 });
          return { recorded: false, held: true, code: null };
        }
        if (kept) dropHeldReport(accountId, clientKey);
        if (isConcurrencyConflict(error)) {
          message.warning(trackingProblemMessage(error, t));
        } else {
          message.error(trackingProblemMessage(error, t));
        }
        return { recorded: false, held: false, code: error.code ?? null };
      }
    } finally {
      releaseHeldReport(clientKey);
    }
  };

  return { send, isPending: record.isPending };
}
