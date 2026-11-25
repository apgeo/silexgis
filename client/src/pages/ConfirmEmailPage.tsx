// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Card, Flex, Spin, Typography } from 'antd';
import Button from '../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router-dom';
import { authTransportProblem, fetchOrNull } from '../auth/authTransportProblem.ts';

/**
 * `failed` is the server's verdict on the link; `undelivered` is no verdict at all (no connection, a
 * full rate window, a failed server), which must not be shown as an invalid link — the link may
 * well be good, and the token is not spent by a request that never completed.
 */
type State = 'working' | 'confirmed' | 'failed' | { undelivered: string };

/**
 * Where a confirmation link lands. Anonymous by design: the link is often opened on a phone that
 * has never signed in, and the token in it is the proof — asking for a password first is how
 * confirmation links go unclicked.
 */
export default function ConfirmEmailPage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const [state, setState] = useState<State>('working');
  const attempted = useRef(false);

  const user = params.get('user');
  const token = params.get('token');

  const confirm = useCallback(() => {
    if (!user || !token) {
      setState('failed');
      return;
    }
    setState('working');
    void fetchOrNull('/api/v1/auth/email/confirm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId: user, token }),
    }).then((response) => {
      const transportProblem = authTransportProblem(response, t);
      if (response === null || transportProblem) {
        setState({ undelivered: transportProblem ?? t('auth.unreachable') });
        return;
      }
      setState(response.ok ? 'confirmed' : 'failed');
    });
  }, [user, token, t]);

  useEffect(() => {
    // React runs effects twice in development, and the second run would consume a token the
    // first has already spent — which would show a failure on a confirmation that worked.
    if (attempted.current) return;
    attempted.current = true;
    confirm();
  }, [confirm]);

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 400, maxWidth: '100%' }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('auth.confirmEmailTitle')}
        </Typography.Title>

        {state === 'working' && (
          <Flex justify="center" style={{ padding: 24 }}>
            <Spin aria-label={t('auth.confirmEmailWorking')} />
          </Flex>
        )}

        {state === 'confirmed' && (
          <Alert type="success" showIcon title={t('auth.confirmEmailDone')} style={{ marginBottom: 16 }} />
        )}

        {state === 'failed' && (
          <Alert type="error" showIcon title={t('auth.confirmEmailFailed')} style={{ marginBottom: 16 }} />
        )}

        {typeof state === 'object' && (
          <Alert
            type="warning"
            showIcon
            title={state.undelivered}
            style={{ marginBottom: 16 }}
            action={
              <Button size="small" onClick={confirm}>
                {t('common.retry')}
              </Button>
            }
          />
        )}

        {state !== 'working' && (
          <Link to="/login">
            <Button type="primary" block>
              {t('auth.signIn')}
            </Button>
          </Link>
        )}
      </Card>
    </Flex>
  );
}
