// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { Spin } from 'antd';
import { useNavigate } from 'react-router-dom';
import { userManager } from '../auth/auth.tsx';

/** Completes the OIDC redirect: exchanges the code (PKCE) and stores tokens in memory. */
export default function CallbackPage() {
  const navigate = useNavigate();
  // The code is single-use; guard against StrictMode double-effects.
  const handled = useRef(false);

  useEffect(() => {
    if (handled.current) {
      return;
    }
    handled.current = true;

    void userManager
      .signinRedirectCallback()
      .then((user) => navigate(typeof user.state === 'string' ? user.state : '/', { replace: true }))
      .catch(() => navigate('/', { replace: true }));
  }, [navigate]);

  return <Spin size="large" fullscreen />;
}
