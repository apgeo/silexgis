// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Button, Result, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './auth.tsx';

/**
 * Gate for authenticated routes: without tokens it starts the OIDC redirect, which lands
 * on the SPA /login page when there is no server session yet.
 */
export default function RequireAuth() {
  const { user, loading, signIn } = useAuth();
  const location = useLocation();
  const { t } = useTranslation();
  const [unreachable, setUnreachable] = useState(false);

  // The hash has to travel with the return URL: tokens live in memory, so opening any
  // deep link cold goes through this redirect, and the map encodes its position in the
  // hash ("#zoom/lat/lon"). Dropping it would silently strip a shared map link of the
  // very position it was shared for.
  const returnTo = location.pathname + location.search + location.hash;

  useEffect(() => {
    if (loading || user || unreachable) {
      return;
    }
    // Starting the redirect first fetches the identity server's discovery document, so it
    // rejects whenever that server is down — and a reverse proxy answers with an HTML error
    // page, which fails as a content-type mismatch rather than as a network error. Landing
    // on a state that says so is the only way out: nothing else retries, so an unhandled
    // rejection here would leave the spinner below on screen for good.
    signIn(returnTo).catch(() => setUnreachable(true));
  }, [loading, user, unreachable, signIn, returnTo]);

  if (unreachable) {
    return (
      <Result
        status="warning"
        title={t('auth.serverUnreachable')}
        subTitle={t('auth.serverUnreachableHint')}
        extra={
          <Button type="primary" onClick={() => setUnreachable(false)}>
            {t('common.retry')}
          </Button>
        }
      />
    );
  }

  if (!user) {
    return <Spin size="large" fullscreen />;
  }

  return <Outlet />;
}
