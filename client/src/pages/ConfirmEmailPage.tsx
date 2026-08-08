// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router-dom';

type State = 'working' | 'confirmed' | 'failed';

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

  useEffect(() => {
    // React runs effects twice in development, and the second run would consume a token the
    // first has already spent — which would show a failure on a confirmation that worked.
    if (attempted.current) return;
    attempted.current = true;

    if (!user || !token) {
      setState('failed');
      return;
    }

    void fetch('/api/v1/auth/email/confirm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId: user, token }),
    })
      .then((response) => setState(response.ok ? 'confirmed' : 'failed'))
      .catch(() => setState('failed'));
  }, [user, token]);

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 400 }}>
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
