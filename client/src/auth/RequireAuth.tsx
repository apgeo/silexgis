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
      void signIn(location.pathname + location.search);
    }
  }, [loading, user, signIn, location]);

  if (!user) {
    return <Spin size="large" fullscreen />;
  }

  return <Outlet />;
}
