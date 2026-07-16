// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { Spin } from 'antd';
import { Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './auth.tsx';

/**
 * Gate for authenticated routes: without tokens it starts the OIDC redirect, which lands
 * on the SPA /login page when there is no server session yet.
 */
export default function RequireAuth() {
  const { user, loading, signIn } = useAuth();
  const location = useLocation();

  useEffect(() => {
    if (!loading && !user) {
      // The hash has to travel with the return URL: tokens live in memory, so opening any
      // deep link cold goes through this redirect, and the map encodes its position in the
      // hash ("#zoom/lat/lon"). Dropping it would silently strip a shared map link of the
      // very position it was shared for.
      void signIn(location.pathname + location.search + location.hash);
    }
  }, [loading, user, signIn, location]);

  if (!user) {
    return <Spin size="large" fullscreen />;
  }

  return <Outlet />;
}
