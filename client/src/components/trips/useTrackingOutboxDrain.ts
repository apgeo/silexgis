// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { useCallback, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { onServerAnswered } from '../../api/client.ts';
import { useSendHeldTrackingReport } from '../../api/hooks.ts';
import { onSignInRenewed, renewSignIn } from '../../auth/renewSignIn.ts';
import { heldReportsOf } from './trackingOutbox.ts';
import {
  drainHeldReports,
  heldReportsServerAnswered,
  heldReportsSignInLapsed,
  isHeldReportsSignInLapsed,
  wakeHeldReportsWith,
  type HeldReportsRun,
} from './trackingOutboxDrain.ts';

/**
 * Sending an account's held reports from a component, and saying what became of them.
 *
 * <b>What a run did is said once, by whoever started it.</b> Reports sent, reports the server
 * turned out to have already and that were taken off the log since, reports refused: each is one
 * line, with its count. A caller that only joined a run under way says none of that again.
 *
 * <b>Why a run stopped is said only to somebody who asked.</b> A run started by the connection
 * coming back that finds the server still out of reach has nothing new to tell anybody — the
 * reports are held, as they were. A person who pressed the button is owed the reason nothing left.
 *
 * <b>One reason is said to everybody, once: the sign-in is over.</b> That is not the server being
 * away, which mends itself. Reports held for an account that is no longer signed in will not leave
 * however long anybody waits, and their author was told they are sent by themselves — so the first
 * run to find it out says so whoever started it, and the list in the page header goes on saying it,
 * with the way to sign in again, until it is no longer true.
 *
 * `automatic` is for the one mount that lives as long as the sign-in does. It sends when it is
 * mounted; when the browser says the connection is back; when the page comes back into view (a
 * phone that slept does not always announce its connection); when the sign-in is renewed; when the
 * server answers any other request while something is held, which is the only real evidence that
 * it can be reached; when a wait the server asked for is over; and, after a run that got no
 * answer, a little later and then less and less often. With nothing held it makes no request.
 */
export function useTrackingOutboxDrain(
  accountId: string | null,
  { automatic = false }: { automatic?: boolean } = {},
) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const send = useSendHeldTrackingReport();
  // Read through a ref, so that what a run is announced with is never a reason to start one: the
  // effect below sends whenever it is set up again, and it must be set up again only when the
  // account or the way of sending changes — not when the page is read in another language.
  const say = useRef({ t, message });
  say.current = { t, message };

  const drain = useCallback(
    async (asked: boolean): Promise<HeldReportsRun | null> => {
      const { t, message } = say.current;
      // Signed in as nobody this tab can name: nobody's reports to send.
      if (accountId === null) return null;
      const knownLapsed = isHeldReportsSignInLapsed();
      const result = await drainHeldReports(accountId, send, renewSignIn);
      if (!result.joined) {
        if (result.sent > 0) {
          void message.success(t('trips.tracking.outbox.sent', { count: result.sent }));
        }
        if (result.alreadyRemoved > 0) {
          void message.info({
            content: t('trips.tracking.outbox.alreadyRemoved', { count: result.alreadyRemoved }),
            duration: 8,
          });
        }
        if (result.refused > 0) {
          void message.warning({
            content: t('trips.tracking.outbox.refusedSome', { count: result.refused }),
            duration: 8,
          });
        }
      }
      if (result.stopped === 'signedOut') {
        if (asked || (!result.joined && !knownLapsed)) {
          void message.warning({ content: t('trips.tracking.outbox.signedOut'), duration: 10 });
        }
      } else if (asked) {
        if (result.stopped === 'noAnswer') {
          void message.warning(t('trips.tracking.outbox.stillNoAnswer'));
        } else if (result.stopped === 'serverBusy' || result.stopped === 'waiting') {
          void message.warning(t('trips.tracking.outbox.serverBusy'));
        }
      }
      return result;
    },
    [accountId, send],
  );

  useEffect(() => {
    if (!automatic || accountId === null) return;
    const go = () => {
      // Where the browser itself says there is no connection the request can only fail, and the
      // browser will say so again when there is one.
      if (navigator.onLine === false) return;
      void drain(false);
    };
    const onVisible = () => {
      if (document.visibilityState === 'visible') go();
    };
    const onRenewed = () => {
      // Whatever was waiting on a sign-in has one now.
      heldReportsSignInLapsed(false);
      go();
    };
    const onAnswered = () => {
      // Asked of what this tab already remembers, so that an answer costs nothing while nothing is
      // waiting — which is nearly every answer there will ever be.
      const waiting = heldReportsOf(accountId).some(
        (report) => report.state === 'held' && !report.sending,
      );
      if (waiting) heldReportsServerAnswered();
    };
    go();
    window.addEventListener('online', go);
    document.addEventListener('visibilitychange', onVisible);
    wakeHeldReportsWith(go);
    const stopRenewed = onSignInRenewed(onRenewed);
    const stopAnswered = onServerAnswered(onAnswered);
    return () => {
      window.removeEventListener('online', go);
      document.removeEventListener('visibilitychange', onVisible);
      wakeHeldReportsWith(null);
      stopRenewed();
      stopAnswered();
    };
  }, [automatic, accountId, drain]);

  return {
    /** Sends now, because somebody asked: the reason nothing left, if nothing did, is said. */
    sendNow: useCallback(() => drain(true), [drain]),
  };
}
