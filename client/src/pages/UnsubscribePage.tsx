// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router-dom';
import type { UnsubscribeResult } from '../api/hooks.ts';

type State = 'working' | 'done' | 'failed';

/**
 * Where the opt-out link at the foot of a notification lands.
 *
 * Anonymous by design, like the confirmation page: the link is opened from a mail client on
 * whatever device is to hand, often by someone opting out precisely because they do not want to
 * sign in. The signed token in it is the authority, and it authorises only this.
 */
export default function UnsubscribePage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const [state, setState] = useState<State>('working');
  const [result, setResult] = useState<UnsubscribeResult | null>(null);
  const attempted = useRef(false);

  const token = params.get('token');

  useEffect(() => {
    // React runs effects twice in development; without this the second run reports a failure for
    // an opt-out that worked.
    if (attempted.current) return;
    attempted.current = true;

    if (!token) {
      setState('failed');
      return;
    }

    void fetch('/api/v1/notifications/unsubscribe', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
    })
      .then(async (response) => {
        if (!response.ok) {
          setState('failed');
          return;
        }
        setResult((await response.json()) as UnsubscribeResult);
        setState('done');
      })
      .catch(() => setState('failed'));
  }, [token]);

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 420 }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('unsubscribe.title')}
        </Typography.Title>

        {state === 'working' && (
          <Flex justify="center" style={{ padding: 24 }}>
            <Spin aria-label={t('unsubscribe.working')} />
          </Flex>
        )}

        {state === 'done' && (
          <Alert
            type="success"
            showIcon
            style={{ marginBottom: 16 }}
            title={
              // A daily summary is not a category and never names one: it collects everything the
              // reader still hears about, so the link stops the mail rather than one subject.
              result?.kind === 'dailyDigest'
                ? t('unsubscribe.doneDigest')
                : t('unsubscribe.done', {
                    category: result?.category
                      ? t(`settings.notifications.events.${result.category}`)
                      : t('unsubscribe.theseMessages'),
                  })
            }
            description={
              // Which channel this switched off, said out loud. The link was clicked in a mail
              // client, so it speaks for mail and for nothing else — the inbox inside the
              // application keeps every one of these, and somebody who has just stopped the mail
              // is exactly the person who needs telling where they still are.
              result?.kind === 'dailyDigest'
                ? `${t('unsubscribe.inboxUntouched')} ${t('unsubscribe.alertsStillSent')} ${t('unsubscribe.changeAnyTime')}`
                : `${t('unsubscribe.inboxUntouched')} ${t('unsubscribe.changeAnyTime')}`
            }
          />
        )}

        {state === 'failed' && (
          <Alert type="error" showIcon title={t('unsubscribe.failed')} style={{ marginBottom: 16 }} />
        )}

        {state !== 'working' && (
          <Link to="/settings/notifications">
            <Button type="primary" block>
              {t('unsubscribe.manage')}
            </Button>
          </Link>
        )}
      </Card>
    </Flex>
  );
}
